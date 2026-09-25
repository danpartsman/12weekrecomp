using System.Data;
using System.Data.Common;
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
    EmailConflict
}

public sealed record LocalRegistrationResult(
    LocalRegistrationStatus Status,
    UserAccount? User = null,
    string RoleCode = "CLIENT");

public sealed record LocalSignInResult(
    bool Success,
    UserAccount? User = null,
    string RoleCode = "CLIENT",
    bool LockedOut = false);

public sealed class LocalAuthService(
    RecompDbContext db,
    IPasswordHasher<UserAccount> passwordHasher,
    FirstSignInWelcomeEmailService welcomeEmail)
{
    public async Task<LocalRegistrationResult> RegisterAsync(
        string displayName,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);

        var matching = await db.UserAccounts
            .Where(x => x.EmailNormalized == normalizedEmail)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matching.Count > 1)
            return new(LocalRegistrationStatus.EmailConflict);

        if (matching.Count == 1)
        {
            var hasLocalCredential = await CredentialExistsAsync(
                matching[0].UserId,
                cancellationToken);

            return new(
                hasLocalCredential
                    ? LocalRegistrationStatus.ExistingLocalAccount
                    : LocalRegistrationStatus.ExistingExternalAccount,
                matching[0]);
        }

        var now = DateTime.UtcNow;
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

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

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
        if (credential.LockoutEndUtc is > var lockoutEnd && lockoutEnd > now)
            return new(false, LockedOut: true);

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

public sealed class LocalAuthSchemaHostedService(
    IServiceProvider services,
    ILogger<LocalAuthSchemaHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecompDbContext>();

        const string sql = """
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
                    FOREIGN KEY (UserId) REFERENCES app.UserAccounts(UserId)
            );
        END;
        """;

        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        logger.LogInformation("12WR local-auth database schema is ready.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
