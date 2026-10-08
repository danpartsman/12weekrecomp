using System.Data;
using System.Data.Common;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record LocalPasswordResetTicket(
    string Token,
    string DestinationEmail,
    bool ConvertsLegacyAccount);

public enum LocalPasswordResetStatus
{
    Success,
    InvalidOrExpired,
    Unavailable
}

public sealed record LocalPasswordResetResult(
    LocalPasswordResetStatus Status,
    bool ConvertedLegacyAccount = false);

public sealed record LocalPasswordResetTokenPayload(
    Guid UserId,
    string NormalizedEmail,
    string CredentialFingerprint,
    long ExpiresUnixSeconds);

/// <summary>
/// Creates short-lived, tamper-protected password-reset tokens.
///
/// Tokens carry a fingerprint of the CURRENT credential state. Once a password
/// is changed, or a legacy account is converted to local credentials, the old
/// link no longer matches the account state and cannot be reused.
/// </summary>
public sealed class LocalPasswordResetTokenService(
    IDataProtectionProvider dataProtectionProvider)
{
    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector(
            "12WR.LocalPasswordReset.v1");

    public string Create(
        Guid userId,
        string normalizedEmail,
        string credentialFingerprint,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        var payload =
            new LocalPasswordResetTokenPayload(
                userId,
                normalizedEmail,
                credentialFingerprint,
                now.Add(lifetime).ToUnixTimeSeconds());

        return _protector.Protect(
            JsonSerializer.Serialize(payload));
    }

    public bool TryRead(
        string token,
        DateTimeOffset now,
        out LocalPasswordResetTokenPayload? payload)
    {
        payload = null;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var json =
                _protector.Unprotect(token);

            payload =
                JsonSerializer.Deserialize<LocalPasswordResetTokenPayload>(
                    json);

            if (payload is null)
                return false;

            if (payload.UserId == Guid.Empty
                || string.IsNullOrWhiteSpace(payload.NormalizedEmail)
                || string.IsNullOrWhiteSpace(payload.CredentialFingerprint))
            {
                payload = null;
                return false;
            }

            if (payload.ExpiresUnixSeconds
                < now.ToUnixTimeSeconds())
            {
                payload = null;
                return false;
            }

            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>
/// Self-service password reset for native 12WR accounts.
///
/// Existing legacy Microsoft/Entra user rows are deliberately supported:
/// if the user exists but has no app.LocalAuthCredentials row, a valid reset
/// link creates a local credential IN PLACE and preserves the existing UserId,
/// client access, program history, nutrition, workouts, and measurements.
///
/// Security properties:
/// - requests do not reveal whether an email exists;
/// - reset links expire after one hour;
/// - reset links are invalidated by any password/credential-state change;
/// - legacy conversion never creates a duplicate UserAccount;
/// - local password hashes continue to use ASP.NET Core PasswordHasher;
/// - successful reset clears lockout/failure state.
/// </summary>
public sealed class LocalPasswordResetService(
    RecompDbContext db,
    IPasswordHasher<UserAccount> passwordHasher,
    LocalPasswordResetTokenService tokens,
    IEmailService emailService,
    IConfiguration configuration,
    ILogger<LocalPasswordResetService> logger)
{
    public static readonly TimeSpan TokenLifetime =
        TimeSpan.FromHours(1);

    public async Task<LocalPasswordResetTicket?> CreateTicketAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        if (!LocalAuthSchemaReadiness.IsReady
            || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalizedEmail =
            NormalizeEmail(email);

        var users =
            await db.UserAccounts
                .Where(x =>
                    x.EmailNormalized == normalizedEmail
                    && x.AccountStatusCode == "ACTIVE")
                .Take(2)
                .ToListAsync(cancellationToken);

        if (users.Count != 1)
            return null;

        var user =
            users[0];

        var credential =
            await ReadCredentialAsync(
                user.UserId,
                cancellationToken);

        var fingerprint =
            CredentialFingerprint(
                credential?.PasswordHash,
                user.ExternalSubject);

        var token =
            tokens.Create(
                user.UserId,
                normalizedEmail,
                fingerprint,
                DateTimeOffset.UtcNow,
                TokenLifetime);

        var destination =
            FirstUsableEmail(
                user.ContactEmail,
                user.EmailNormalized);

        if (destination is null)
            return null;

        return new LocalPasswordResetTicket(
            token,
            destination,
            ConvertsLegacyAccount: credential is null);
    }

    public async Task<bool> SendResetEmailAsync(
        LocalPasswordResetTicket ticket,
        string resetUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        if (string.IsNullOrWhiteSpace(resetUrl))
            return false;

        var legacyLine =
            ticket.ConvertsLegacyAccount
                ? Environment.NewLine
                  + "This link will create a 12WR password for your existing account while preserving your current 12WR data."
                  + Environment.NewLine
                : "";

        var body =
            string.Join(
                Environment.NewLine,
                new[]
                {
                    "A password reset was requested for your 12 Week Recomp account.",
                    legacyLine,
                    "Use this link within 60 minutes:",
                    resetUrl,
                    "",
                    "If you did not request this, you can ignore this email. Your password will not change.",
                    "",
                    "12 Week Recomp"
                });

        var result =
            await emailService.SendAsync(
                new OutboundEmail(
                    ToAddress: ticket.DestinationEmail,
                    Subject: "Reset your 12 Week Recomp password",
                    Body: body),
                cancellationToken);

        if (!result.Success)
        {
            logger.LogWarning(
                "12WR password-reset email transport did not accept the message. Error: {Error}",
                result.Error);
        }

        return result.Success;
    }

    public async Task<LocalPasswordResetResult> ResetAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (!LocalAuthSchemaReadiness.IsReady)
        {
            return new(
                LocalPasswordResetStatus.Unavailable);
        }

        if (!tokens.TryRead(
                token,
                DateTimeOffset.UtcNow,
                out var payload)
            || payload is null)
        {
            return new(
                LocalPasswordResetStatus.InvalidOrExpired);
        }

        var users =
            await db.UserAccounts
                .Where(x =>
                    x.UserId == payload.UserId
                    && x.EmailNormalized == payload.NormalizedEmail
                    && x.AccountStatusCode == "ACTIVE")
                .Take(2)
                .ToListAsync(cancellationToken);

        if (users.Count != 1)
        {
            return new(
                LocalPasswordResetStatus.InvalidOrExpired);
        }

        var user =
            users[0];

        var credential =
            await ReadCredentialAsync(
                user.UserId,
                cancellationToken);

        var currentFingerprint =
            CredentialFingerprint(
                credential?.PasswordHash,
                user.ExternalSubject);

        if (!FixedTimeEquals(
                currentFingerprint,
                payload.CredentialFingerprint))
        {
            return new(
                LocalPasswordResetStatus.InvalidOrExpired);
        }

        var replacementHash =
            passwordHasher.HashPassword(
                user,
                newPassword);

        if (credential is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE app.LocalAuthCredentials
                SET PasswordHash = {replacementHash},
                    FailedSignInCount = 0,
                    LockoutEndUtc = NULL,
                    UpdatedAtUtc = SYSUTCDATETIME()
                WHERE UserId = {user.UserId};
                """,
                cancellationToken);

            return new(
                LocalPasswordResetStatus.Success);
        }

        var roleCode =
            ResolveLegacyRole(
                user);

        var now =
            DateTime.UtcNow;

        var newLocalSubject =
            $"local:{Guid.NewGuid():N}";

        var strategy =
            db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    cancellationToken);

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO app.LocalAuthCredentials
                    (UserId, PasswordHash, GlobalRoleCode, FailedSignInCount,
                     LockoutEndUtc, LastSignInAtUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ({user.UserId}, {replacementHash}, {roleCode}, 0,
                     NULL, NULL, {now}, {now});
                """,
                cancellationToken);

            user.ExternalSubject =
                newLocalSubject;

            user.AccountStatusCode =
                "ACTIVE";

            user.UpdatedAtUtc =
                now;

            await db.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        });

        logger.LogInformation(
            "Converted legacy 12WR account {UserId} to local credentials through verified password reset.",
            user.UserId);

        return new(
            LocalPasswordResetStatus.Success,
            ConvertedLegacyAccount: true);
    }

    private string ResolveLegacyRole(
        UserAccount user)
    {
        var configuredHash =
            configuration[
                "WebRecomp:BootstrapAdministratorEmailSha256"]?
                .Trim()
                .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(configuredHash)
            || string.IsNullOrWhiteSpace(user.EmailNormalized))
        {
            return "CLIENT";
        }

        var actualHash =
            Sha256Hex(
                NormalizeEmail(
                    user.EmailNormalized));

        return FixedTimeEquals(
                actualHash,
                configuredHash)
            ? "ADMINISTRATOR"
            : "CLIENT";
    }

    private async Task<CredentialRow?> ReadCredentialAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var connection =
            db.Database.GetDbConnection();

        var closeWhenDone =
            connection.State
            != ConnectionState.Open;

        if (closeWhenDone)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                SELECT PasswordHash,
                       GlobalRoleCode
                FROM app.LocalAuthCredentials
                WHERE UserId = @userId;
                """;

            AddParameter(
                command,
                "@userId",
                userId);

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                return null;

            return new CredentialRow(
                reader.GetString(0),
                reader.GetString(1));
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }
    }

    private static string CredentialFingerprint(
        string? passwordHash,
        string externalSubject)
    {
        var state =
            !string.IsNullOrWhiteSpace(passwordHash)
                ? $"LOCAL|{passwordHash}"
                : $"LEGACY|{externalSubject}";

        return Sha256Hex(state);
    }

    private static string Sha256Hex(
        string value)
        => Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        value)))
            .ToLowerInvariant();

    private static bool FixedTimeEquals(
        string left,
        string right)
    {
        try
        {
            var leftBytes =
                Convert.FromHexString(left);

            var rightBytes =
                Convert.FromHexString(right);

            return leftBytes.Length == rightBytes.Length
                && CryptographicOperations.FixedTimeEquals(
                    leftBytes,
                    rightBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string NormalizeEmail(
        string email)
        => email.Trim().ToLowerInvariant();

    private static string? FirstUsableEmail(
        params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            try
            {
                return new MailAddress(
                    candidate.Trim())
                    .Address;
            }
            catch
            {
                // Try the next candidate.
            }
        }

        return null;
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object value)
    {
        var parameter =
            command.CreateParameter();

        parameter.ParameterName =
            name;

        parameter.Value =
            value;

        command.Parameters.Add(
            parameter);
    }

    private sealed record CredentialRow(
        string PasswordHash,
        string GlobalRoleCode);
}
