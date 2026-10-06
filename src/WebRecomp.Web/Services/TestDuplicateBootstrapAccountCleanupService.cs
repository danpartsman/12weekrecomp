using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

/// <summary>
/// TEST-only one-time safety cleanup for a duplicate bootstrap administrator row.
///
/// The service is inert everywhere except the Azure App Service named
/// "recomp-test". It identifies the configured bootstrap administrator by the
/// SHA-256 email hash already used by TEST, then compares every direct foreign
/// key reference to app.UserAccounts.
///
/// It deletes a duplicate only when:
/// - exactly two matching UserAccount rows exist;
/// - one row has zero application-data references (excluding the local
///   credential row itself); and
/// - the other row has one or more application-data references.
///
/// This intentionally refuses ambiguous cases.
/// </summary>
public sealed class TestDuplicateBootstrapAccountCleanupService(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<TestDuplicateBootstrapAccountCleanupService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
        if (!string.Equals(siteName, "recomp-test", StringComparison.OrdinalIgnoreCase))
            return;

        var configuredHash = configuration["WebRecomp:BootstrapAdministratorEmailSha256"]?
            .Trim()
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(configuredHash))
            return;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecompDbContext>();

        var users = await db.UserAccounts
            .Where(x => x.EmailNormalized != null)
            .Take(2000)
            .ToListAsync(cancellationToken);

        var matches = users
            .Where(x => FixedTimeHexEquals(
                Sha256Hex(x.EmailNormalized!.Trim().ToLowerInvariant()),
                configuredHash))
            .ToArray();

        if (matches.Length < 2)
            return;

        var ranked = new List<(UserAccount User, long References)>();
        foreach (var user in matches)
        {
            ranked.Add((
                user,
                await CountApplicationReferencesAsync(
                    db,
                    user.UserId,
                    cancellationToken)));
        }

        var keeper = ranked
            .OrderByDescending(x => x.References)
            .ThenBy(x => x.User.CreatedAtUtc)
            .First()
            .User;

        var duplicates = ranked
            .Where(x => x.User.UserId != keeper.UserId)
            .ToArray();

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(cancellationToken);

            foreach (var duplicate in duplicates)
            {
                duplicate.User.EmailNormalized =
                    $"duplicate-{duplicate.User.UserId:N}@invalid.local";
                duplicate.User.ContactEmail = null;
                duplicate.User.AccountStatusCode = "SUSPENDED";
                duplicate.User.UpdatedAtUtc = DateTime.UtcNow;
            }

            keeper.AccountStatusCode = "ACTIVE";
            keeper.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        logger.LogWarning(
            "TEST bootstrap administrator collision repaired. Preserved richer account {KeeperUserId}; quarantined {DuplicateCount} duplicate row(s).",
            keeper.UserId,
            duplicates.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    private static async Task<long> CountApplicationReferencesAsync(
        RecompDbContext db,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;

        if (closeWhenDone)
            await connection.OpenAsync(cancellationToken);

        try
        {
            var references = new List<(string Schema, string Table, string Column)>();

            await using (var metadata = connection.CreateCommand())
            {
                metadata.CommandText = """
                    SELECT
                        OBJECT_SCHEMA_NAME(fkc.parent_object_id) AS schema_name,
                        OBJECT_NAME(fkc.parent_object_id) AS table_name,
                        COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS column_name
                    FROM sys.foreign_key_columns AS fkc
                    INNER JOIN sys.foreign_keys AS fk
                        ON fk.object_id = fkc.constraint_object_id
                    WHERE fkc.referenced_object_id = OBJECT_ID(N'app.UserAccounts')
                      AND COL_NAME(
                            fkc.referenced_object_id,
                            fkc.referenced_column_id) = N'user_id'
                      AND NOT (
                            OBJECT_SCHEMA_NAME(fkc.parent_object_id) = N'app'
                            AND OBJECT_NAME(fkc.parent_object_id) = N'LocalAuthCredentials'
                      );
                    """;

                await using var reader =
                    await metadata.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    references.Add((
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2)));
                }
            }

            long total = 0;

            foreach (var reference in references)
            {
                await using var count = connection.CreateCommand();
                count.CommandText =
                    $"SELECT COUNT_BIG(*) FROM {Quote(reference.Schema)}.{Quote(reference.Table)} WHERE {Quote(reference.Column)} = @userId;";

                var parameter = count.CreateParameter();
                parameter.ParameterName = "@userId";
                parameter.Value = userId;
                count.Parameters.Add(parameter);

                var scalar = await count.ExecuteScalarAsync(cancellationToken);
                total += scalar is null || scalar is DBNull
                    ? 0
                    : Convert.ToInt64(scalar);
            }

            return total;
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }
    }

    private static string Quote(string identifier)
        => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string Sha256Hex(string value)
        => Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool FixedTimeHexEquals(string left, string right)
    {
        try
        {
            var leftBytes = Convert.FromHexString(left);
            var rightBytes = Convert.FromHexString(right);

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
}
