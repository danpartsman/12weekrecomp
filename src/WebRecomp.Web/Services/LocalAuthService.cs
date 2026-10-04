using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public enum LocalRegistrationStatus
{
    Created,
    ExistingLocalAccount,
    ExistingExternalAccount,
    EmailConflict,
    Unavailable
}

public sealed record LocalRegistrationResult(
    LocalRegistrationStatus Status,
    UserAccount? User = null,
    string RoleCode = "CLIENT");

public sealed record LocalSignInResult(
    bool Success,
    UserAccount? User = null,
    string RoleCode = "CLIENT",
    bool LockedOut = false,
    bool Unavailable = false);

internal static class LocalAuthSchemaReadiness
{
    private static int _isReady;

    public static bool IsReady
    {
        get => Volatile.Read(ref _isReady) == 1;
        set => Volatile.Write(ref _isReady, value ? 1 : 0);
    }
}

public sealed class LocalAuthService(
    RecompDbContext db,
    IPasswordHasher<UserAccount> passwordHasher,
    FirstSignInWelcomeEmailService welcomeEmail,
    IConfiguration configuration)
{
    public bool IsAvailable => LocalAuthSchemaReadiness.IsReady;

    public async Task<LocalRegistrationResult> RegisterAsync(
        string displayName,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return new(LocalRegistrationStatus.Unavailable);

        var normalizedEmail = NormalizeEmail(email);

        var matching = await db.UserAccounts
            .Where(x => x.EmailNormalized == normalizedEmail)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matching.Count > 1)
            return new(LocalRegistrationStatus.EmailConflict);

        var now = DateTime.UtcNow;

        if (matching.Count == 1)
        {
            var existingUser = matching[0];
            var hasLocalCredential = await CredentialExistsAsync(
                existingUser.UserId,
                cancellationToken);

            if (hasLocalCredential)
                return new(LocalRegistrationStatus.ExistingLocalAccount, existingUser);

            var existingRoleCode = ResolveInitialRole(existingUser, normalizedEmail);
            var existingPasswordHash = passwordHasher.HashPassword(existingUser, password);

            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction =
                    await db.Database.BeginTransactionAsync(cancellationToken);

                existingUser.DisplayName = displayName.Trim();
                existingUser.PreferredDisplayName = displayName.Trim();
                existingUser.ContactEmail = normalizedEmail;
                existingUser.AccountStatusCode = "ACTIVE";
                existingUser.UpdatedAtUtc = now;

                await db.SaveChangesAsync(cancellationToken);

                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO app.LocalAuthCredentials
                        (UserId, PasswordHash, GlobalRoleCode, FailedSignInCount,
                         LockoutEndUtc, LastSignInAtUtc, CreatedAtUtc, UpdatedAtUtc)
                    VALUES
                        ({existingUser.UserId}, {existingPasswordHash}, {existingRoleCode}, 0,
                         NULL, NULL, {now}, {now});
                    """,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            });

            await welcomeEmail.TrySendAsync(existingUser, cancellationToken);
            return new(LocalRegistrationStatus.Created, existingUser, existingRoleCode);
        }

        var user = new UserAccount
        {
            UserId = Guid.NewGuid(),
            ExternalSubject = $"local:{Guid.NewGuid():N}",
            EmailNormalized = normalizedEmail,
            DisplayName = displayName.Trim(),
            PreferredDisplayName = displayName.Trim(),
            ContactEmail = normalizedEmail,
            ThemeCode = "SYSTEM",
            AccountStatusCode = "ACTIVE",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var passwordHash = passwordHasher.HashPassword(user, password);
        const string roleCode = "CLIENT";

        // RecompDbContext uses SqlServerRetryingExecutionStrategy. A manually
        // started transaction must therefore run inside that strategy or EF
        // can reject the unit of work and roll it back. Keep the user row and
        // credential row atomic, while allowing SQL Azure transient retries.
        var strategyNewUser = db.Database.CreateExecutionStrategy();
        await strategyNewUser.ExecuteAsync(async () =>
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(cancellationToken);

            // Mark Added on every execution-strategy attempt. If SQL Server
            // retries after a transient failure, a prior SaveChanges may have
            // changed the tracked state even though its transaction rolled back.
            db.UserAccounts.Add(user);
            await db.SaveChangesAsync(cancellationToken);

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO app.LocalAuthCredentials
                    (UserId, PasswordHash, GlobalRoleCode, FailedSignInCount,
                     LockoutEndUtc, LastSignInAtUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ({user.UserId}, {passwordHash}, {roleCode}, 0,
                     NULL, NULL, {now}, {now});
                """,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        });

        // Account creation must not depend on mail delivery. The existing
        // welcome service already treats transport failures as non-fatal.
        await welcomeEmail.TrySendAsync(user, cancellationToken);

        return new(LocalRegistrationStatus.Created, user, roleCode);
    }

    public async Task<LocalSignInResult> ValidateCredentialsAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return new(false, Unavailable: true);

        var normalizedEmail = NormalizeEmail(email);

        var matching = await db.UserAccounts
            .Where(x => x.EmailNormalized == normalizedEmail)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matching.Count != 1)
            return new(false);

        var user = matching[0];
        if (!string.Equals(
                user.AccountStatusCode,
                "ACTIVE",
                StringComparison.OrdinalIgnoreCase))
        {
            return new(false);
        }

        var credential = await ReadCredentialAsync(
            user.UserId,
            cancellationToken);

        if (credential is null)
            return new(false);

        var now = DateTime.UtcNow;
        if (credential.LockoutEndUtc is DateTime lockoutEnd
            && lockoutEnd > now)
        {
            return new(false, LockedOut: true);
        }

        if (credential.LockoutEndUtc.HasValue
            && credential.LockoutEndUtc.Value <= now)
        {
            await ResetFailuresAsync(user.UserId, cancellationToken);
            credential = credential with
            {
                FailedSignInCount = 0,
                LockoutEndUtc = null
            };
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user,
            credential.PasswordHash,
            password);

        if (verification == PasswordVerificationResult.Failed)
        {
            await RecordFailureAsync(user.UserId, cancellationToken);
            return new(false);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var replacementHash = passwordHasher.HashPassword(user, password);
            await ReplacePasswordHashAsync(
                user.UserId,
                replacementHash,
                cancellationToken);
        }

        await RecordSuccessAsync(user.UserId, cancellationToken);

        return new(
            true,
            user,
            string.IsNullOrWhiteSpace(credential.GlobalRoleCode)
                ? "CLIENT"
                : credential.GlobalRoleCode.Trim().ToUpperInvariant());
    }

    private async Task<bool> CredentialExistsAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => await ReadCredentialAsync(userId, cancellationToken) is not null;

    private async Task<CredentialRow?> ReadCredentialAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;

        if (closeWhenDone)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT PasswordHash,
                       GlobalRoleCode,
                       FailedSignInCount,
                       LockoutEndUtc
                FROM app.LocalAuthCredentials
                WHERE UserId = @userId;
                """;

            AddParameter(command, "@userId", userId);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                return null;

            return new CredentialRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3));
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }
    }

    private Task ResetFailuresAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE app.LocalAuthCredentials
            SET FailedSignInCount = 0,
                LockoutEndUtc = NULL,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE UserId = {userId};
            """,
            cancellationToken);

    private Task RecordFailureAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE app.LocalAuthCredentials
            SET FailedSignInCount = FailedSignInCount + 1,
                LockoutEndUtc = CASE
                    WHEN FailedSignInCount + 1 >= 5
                    THEN DATEADD(MINUTE, 15, SYSUTCDATETIME())
                    ELSE LockoutEndUtc
                END,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE UserId = {userId};
            """,
            cancellationToken);

    private Task RecordSuccessAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE app.LocalAuthCredentials
            SET FailedSignInCount = 0,
                LockoutEndUtc = NULL,
                LastSignInAtUtc = SYSUTCDATETIME(),
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE UserId = {userId};
            """,
            cancellationToken);

    private Task ReplacePasswordHashAsync(
        Guid userId,
        string passwordHash,
        CancellationToken cancellationToken)
        => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE app.LocalAuthCredentials
            SET PasswordHash = {passwordHash},
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE UserId = {userId};
            """,
            cancellationToken);

    private string ResolveInitialRole(
        UserAccount user,
        string normalizedEmail)
        => BootstrapAdministratorMatcher.IsMatch(
            user,
            normalizedEmail,
            configuration)
                ? "ADMINISTRATOR"
                : "CLIENT";

    private static string NormalizeEmail(string email)
        => email.Trim().ToLowerInvariant();

    private static void AddParameter(
        DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record CredentialRow(
        string PasswordHash,
        string GlobalRoleCode,
        int FailedSignInCount,
        DateTime? LockoutEndUtc);
}

internal static class BootstrapAdministratorMatcher
{
    public static bool IsMatch(
        UserAccount user,
        string normalizedEmail,
        IConfiguration configuration)
    {
        var configuredHash =
            configuration["WebRecomp:BootstrapAdministratorEmailSha256"]?
                .Trim()
                .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(configuredHash))
            return false;

        if (string.IsNullOrWhiteSpace(user.ExternalSubject)
            || user.ExternalSubject.StartsWith(
                "local:",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = normalizedEmail.Trim().ToLowerInvariant();
        var actualHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();

        return string.Equals(
            configuredHash,
            actualHash,
            StringComparison.Ordinal);
    }
}

public sealed class LocalAuthSchemaHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<LocalAuthSchemaHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LocalAuthSchemaReadiness.IsReady = false;

        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RecompDbContext>();
            var connection = db.Database.GetDbConnection();
            var closeWhenDone = connection.State != ConnectionState.Open;

            if (closeWhenDone)
                await connection.OpenAsync(cancellationToken);

            try
            {
                if (!await CredentialTableExistsAsync(connection, cancellationToken))
                {
                    try
                    {
                        await using var create = connection.CreateCommand();
                        create.CommandText = """
                            IF OBJECT_ID(N'app.LocalAuthCredentials', N'U') IS NULL
                            BEGIN
                                CREATE TABLE app.LocalAuthCredentials
                                (
                                    UserId uniqueidentifier NOT NULL,
                                    PasswordHash nvarchar(512) NOT NULL,
                                    GlobalRoleCode varchar(20) NOT NULL
                                        CONSTRAINT DF_LocalAuthCredentials_GlobalRoleCode DEFAULT ('CLIENT'),
                                    FailedSignInCount int NOT NULL
                                        CONSTRAINT DF_LocalAuthCredentials_FailedSignInCount DEFAULT (0),
                                    LockoutEndUtc datetime2(3) NULL,
                                    LastSignInAtUtc datetime2(3) NULL,
                                    CreatedAtUtc datetime2(3) NOT NULL,
                                    UpdatedAtUtc datetime2(3) NOT NULL,
                                    CONSTRAINT PK_LocalAuthCredentials PRIMARY KEY (UserId),
                                    CONSTRAINT FK_LocalAuthCredentials_UserAccounts
                                        FOREIGN KEY (UserId) REFERENCES app.UserAccounts(user_id)
                                );
                            END;
                            """;

                        await create.ExecuteNonQueryAsync(cancellationToken);
                    }
                    catch (DbException exception)
                    {
                        logger.LogWarning(
                            exception,
                            "12WR local-auth schema is missing and the application identity could not create it.");
                    }
                }

                LocalAuthSchemaReadiness.IsReady =
                    await CredentialTableExistsAsync(connection, cancellationToken);
            }
            finally
            {
                if (closeWhenDone)
                    await connection.CloseAsync();
            }

            if (LocalAuthSchemaReadiness.IsReady)
            {
                await PromoteConfiguredLegacyAdministratorAsync(
                    db,
                    cancellationToken);

                logger.LogInformation(
                    "12WR local-auth database schema is ready; native auth is enabled.");
            }
            else
            {
                logger.LogWarning(
                    "12WR local-auth database schema is not present. Native auth is disabled, but application startup will continue.");
            }
        }
        catch (Exception exception) when (
            exception is DbException or InvalidOperationException)
        {
            LocalAuthSchemaReadiness.IsReady = false;
            logger.LogError(
                exception,
                "Could not verify the 12WR local-auth schema. Native auth is disabled, but application startup will continue.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    private static async Task<bool> CredentialTableExistsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE
                WHEN OBJECT_ID(N'app.LocalAuthCredentials', N'U') IS NULL THEN 0
                ELSE 1
            END;
            """;

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is not null
            && scalar is not DBNull
            && Convert.ToInt32(scalar) == 1;
    }

    private async Task PromoteConfiguredLegacyAdministratorAsync(
        RecompDbContext db,
        CancellationToken cancellationToken)
    {
        var configuredHash =
            configuration["WebRecomp:BootstrapAdministratorEmailSha256"]?
                .Trim()
                .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(configuredHash))
            return;

        var candidates = await db.UserAccounts
            .Where(x => x.EmailNormalized != null)
            .ToListAsync(cancellationToken);

        var users = candidates
            .Where(x => BootstrapAdministratorMatcher.IsMatch(
                x,
                x.EmailNormalized!,
                configuration))
            .Take(2)
            .ToList();

        if (users.Count != 1)
            return;

        var user = users[0];

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE app.LocalAuthCredentials
            SET GlobalRoleCode = {"ADMINISTRATOR"},
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE UserId = {user.UserId};
            """,
            cancellationToken);
    }
}
