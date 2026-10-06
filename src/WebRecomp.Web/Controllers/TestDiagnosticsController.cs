using System.Diagnostics;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.Controllers;

// TEST ONLY. This diagnostic surface is intentionally useful for v1010 testing.
// Do not promote this controller/view or the TestDiagnostics exception-handler
// route into LIVE.
public sealed class TestDiagnosticsController(
    ILogger<TestDiagnosticsController> logger,
    RecompDbContext db,
    IConfiguration configuration) : Controller
{
    private static readonly Regex SensitiveAssignmentRegex =
        new(
            @"(?i)\b(password|pwd|client_secret|clientsecret|access_token|accesstoken|authorization|cookie|set-cookie|connectionstring|connection string)\b\s*[:=]\s*[^\s,;]+",
            RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(100));

    private static readonly Regex ConnectionPartRegex =
        new(
            @"(?i)\b(server|data source|initial catalog|database|user id|uid|password|pwd)\s*=\s*[^;]+",
            RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(100));

    private static readonly Regex BearerRegex =
        new(
            @"(?i)\bBearer\s+[A-Za-z0-9\-._~+/]+=*",
            RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(100));

    [AllowAnonymous]
    [HttpGet("/TestDiagnostics/BootstrapAuthState")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> BootstrapAuthState(CancellationToken cancellationToken)
    {
        var host = Request.Host.Host.Trim().ToLowerInvariant();
        if (host is not ("test.12weekrecomp.com" or "recomp-test.azurewebsites.net"))
            return NotFound();

        var configuredHash = configuration["WebRecomp:BootstrapAdministratorEmailSha256"]?
            .Trim()
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(configuredHash))
            return Json(new { configured = false });

        var users = await db.UserAccounts
            .AsNoTracking()
            .Where(x => x.EmailNormalized != null)
            .Take(2000)
            .ToListAsync(cancellationToken);

        var matches = users
            .Where(x => FixedTimeHexEquals(
                Sha256Hex(x.EmailNormalized!.Trim().ToLowerInvariant()),
                configuredHash))
            .Take(2)
            .ToArray();

        if (matches.Length != 1)
        {
            var candidates = new List<object>();
            var candidateStates = new List<(UserAccount User, bool HasCredential, long References)>();

            for (var i = 0; i < matches.Length; i++)
            {
                var candidate = matches[i];
                var hasCredential = await LocalCredentialExistsAsync(
                    candidate.UserId,
                    cancellationToken);

                var candidateSubject = candidate.ExternalSubject ?? string.Empty;
                var candidateSubjectType = candidateSubject.StartsWith(
                    "local:",
                    StringComparison.OrdinalIgnoreCase)
                    ? "LOCAL"
                    : "LEGACY_EXTERNAL";

                var applicationReferenceCount =
                    await CountApplicationReferencesAsync(
                        candidate.UserId,
                        cancellationToken);

                candidateStates.Add((candidate, hasCredential, applicationReferenceCount));

                candidates.Add(new
                {
                    slot = i + 1,
                    accountStatus = candidate.AccountStatusCode,
                    subjectType = candidateSubjectType,
                    hasLocalCredential = hasCredential,
                    applicationReferenceCount,
                    createdAtUtc = candidate.CreatedAtUtc,
                    updatedAtUtc = candidate.UpdatedAtUtc,
                    contactEmailPresent = !string.IsNullOrWhiteSpace(candidate.ContactEmail)
                });
            }

            if (candidateStates.Count == 2)
            {
                var empty = candidateStates.SingleOrDefault(x =>
                    x.References == 0
                    && !x.HasCredential);

                var keeper = candidateStates.SingleOrDefault(x =>
                    x.References > 0
                    && x.HasCredential);

                if (empty.User is not null && keeper.User is not null)
                {
                    var strategy = db.Database.CreateExecutionStrategy();

                    await strategy.ExecuteAsync(async () =>
                    {
                        await using var transaction =
                            await db.Database.BeginTransactionAsync(cancellationToken);

                        await db.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            DELETE FROM app.LocalAuthCredentials
                            WHERE UserId = {empty.User.UserId};
                            """,
                            cancellationToken);

                        var deletedRows = await db.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            DELETE FROM app.UserAccounts
                            WHERE user_id = {empty.User.UserId};
                            """,
                            cancellationToken);

                        if (deletedRows != 1)
                            throw new InvalidOperationException(
                                $"Expected one duplicate UserAccount row to be deleted, but deleted {deletedRows}.");

                        await transaction.CommitAsync(cancellationToken);
                    });

                    return Json(new
                    {
                        configured = true,
                        matchCount = 1,
                        userExists = true,
                        cleanupPerformed = true,
                        accountStatus = keeper.User.AccountStatusCode,
                        subjectType = "LOCAL",
                        hasLocalCredential = keeper.HasCredential,
                        applicationReferenceCount = keeper.References,
                        resetEligible = string.Equals(
                            keeper.User.AccountStatusCode,
                            "ACTIVE",
                            StringComparison.OrdinalIgnoreCase),
                        contactEmailPresent = !string.IsNullOrWhiteSpace(keeper.User.ContactEmail)
                    });
                }
            }

            return Json(new
            {
                configured = true,
                matchCount = matches.Length,
                userExists = matches.Length > 0,
                cleanupPerformed = false,
                candidates
            });
        }

        var user = matches[0];
        var hasLocalCredential = await LocalCredentialExistsAsync(
            user.UserId,
            cancellationToken);

        var subject = user.ExternalSubject ?? string.Empty;
        var subjectType = subject.StartsWith("local:", StringComparison.OrdinalIgnoreCase)
            ? "LOCAL"
            : "LEGACY_EXTERNAL";

        return Json(new
        {
            configured = true,
            matchCount = 1,
            userExists = true,
            accountStatus = user.AccountStatusCode,
            subjectType,
            hasLocalCredential,
            resetEligible = string.Equals(user.AccountStatusCode, "ACTIVE", StringComparison.OrdinalIgnoreCase),
            contactEmailPresent = !string.IsNullOrWhiteSpace(user.ContactEmail)
        });
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("/TestDiagnostics/DeleteEmptyBootstrapDuplicate")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> DeleteEmptyBootstrapDuplicate(
        CancellationToken cancellationToken)
    {
        var host = Request.Host.Host.Trim().ToLowerInvariant();
        if (host is not ("test.12weekrecomp.com" or "recomp-test.azurewebsites.net"))
            return NotFound();

        var configuredHash = configuration["WebRecomp:BootstrapAdministratorEmailSha256"]?
            .Trim()
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(configuredHash))
            return Json(new { deleted = false, reason = "not_configured" });

        var users = await db.UserAccounts
            .Where(x => x.EmailNormalized != null)
            .Take(2000)
            .ToListAsync(cancellationToken);

        var matches = users
            .Where(x => FixedTimeHexEquals(
                Sha256Hex(x.EmailNormalized!.Trim().ToLowerInvariant()),
                configuredHash))
            .ToArray();

        if (matches.Length != 2)
            return Json(new { deleted = false, reason = "match_count", matchCount = matches.Length });

        var firstRefs = await CountApplicationReferencesAsync(matches[0].UserId, cancellationToken);
        var secondRefs = await CountApplicationReferencesAsync(matches[1].UserId, cancellationToken);
        var firstCredential = await LocalCredentialExistsAsync(matches[0].UserId, cancellationToken);
        var secondCredential = await LocalCredentialExistsAsync(matches[1].UserId, cancellationToken);

        UserAccount? duplicate = null;

        if (firstRefs == 0 && !firstCredential && secondRefs > 0 && secondCredential)
            duplicate = matches[0];
        else if (secondRefs == 0 && !secondCredential && firstRefs > 0 && firstCredential)
            duplicate = matches[1];

        if (duplicate is null)
        {
            return Json(new
            {
                deleted = false,
                reason = "ambiguous",
                firstReferences = firstRefs,
                secondReferences = secondRefs,
                firstHasCredential = firstCredential,
                secondHasCredential = secondCredential
            });
        }

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(cancellationToken);

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM app.LocalAuthCredentials
                WHERE UserId = {duplicate.UserId};
                """,
                cancellationToken);

            var deletedRows = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM app.UserAccounts
                WHERE user_id = {duplicate.UserId};
                """,
                cancellationToken);

            if (deletedRows != 1)
                throw new InvalidOperationException(
                    $"Expected one duplicate UserAccount row to be deleted, but deleted {deletedRows}.");

            await transaction.CommitAsync(cancellationToken);
        });

        return Json(new
        {
            deleted = true,
            preservedAccountReferences = Math.Max(firstRefs, secondRefs)
        });
    }

    private async Task<long> CountApplicationReferencesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        long total = 0;

        var accessRows = await db.UserClientAccess
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        total += accessRows.Count;

        var clientIds = accessRows
            .Select(x => x.ClientId)
            .Distinct()
            .ToArray();

        if (clientIds.Length > 0)
        {
            total += await db.Programs.AsNoTracking()
                .LongCountAsync(x => clientIds.Contains(x.ClientId), cancellationToken);
            total += await db.Measurements.AsNoTracking()
                .LongCountAsync(x => clientIds.Contains(x.ClientId), cancellationToken);
            total += await db.DailyCheckins.AsNoTracking()
                .LongCountAsync(x => clientIds.Contains(x.ClientId), cancellationToken);
            total += await db.PlannedWorkouts.AsNoTracking()
                .LongCountAsync(x => clientIds.Contains(x.ClientId), cancellationToken);
            total += await db.WorkoutSessions.AsNoTracking()
                .LongCountAsync(x => clientIds.Contains(x.ClientId), cancellationToken);
            total += await db.NutritionDays.AsNoTracking()
                .LongCountAsync(x => clientIds.Contains(x.ClientId), cancellationToken);
            total += await db.AnalysisRuns.AsNoTracking()
                .LongCountAsync(x => clientIds.Contains(x.ClientId), cancellationToken);
            total += await db.Recommendations.AsNoTracking()
                .LongCountAsync(x => clientIds.Contains(x.ClientId), cancellationToken);
            total += await db.WorkoutTemplates.AsNoTracking()
                .LongCountAsync(x => clientIds.Contains(x.ClientId), cancellationToken);
            total += await db.ClientExercisePreferences.AsNoTracking()
                .LongCountAsync(x => clientIds.Contains(x.ClientId), cancellationToken);
            total += await db.ClientOccupationalActivities.AsNoTracking()
                .LongCountAsync(x => clientIds.Contains(x.ClientId), cancellationToken);
        }

        total += await db.ProgramPlanChanges.AsNoTracking()
            .LongCountAsync(x => x.ChangedByUserId == userId, cancellationToken);
        total += await db.UserEntitlements.AsNoTracking()
            .LongCountAsync(x => x.UserId == userId, cancellationToken);
        total += await db.PromoCodeRedemptions.AsNoTracking()
            .LongCountAsync(x => x.UserId == userId, cancellationToken);
        total += await db.DemoWorkspaces.AsNoTracking()
            .LongCountAsync(x => x.UserId == userId || x.ConvertedToUserId == userId, cancellationToken);
        total += await db.DemoInvites.AsNoTracking()
            .LongCountAsync(x => x.CreatedByUserId == userId, cancellationToken);
        total += await db.GiveawayCodeBatches.AsNoTracking()
            .LongCountAsync(x => x.CreatedByUserId == userId, cancellationToken);
        total += await db.GiveawayCodes.AsNoTracking()
            .LongCountAsync(x => x.RedeemedByUserId == userId || x.RevokedByUserId == userId, cancellationToken);
        total += await db.PromoCodes.AsNoTracking()
            .LongCountAsync(x => x.CreatedByUserId == userId, cancellationToken);
        total += await db.ContactSettings.AsNoTracking()
            .LongCountAsync(x => x.UpdatedByUserId == userId, cancellationToken);
        total += await db.ContactMessages.AsNoTracking()
            .LongCountAsync(x => x.ReadByUserId == userId || x.ArchivedByUserId == userId, cancellationToken);

        return total;
    }

    private async Task<bool> LocalCredentialExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;
        if (closeWhenDone)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM app.LocalAuthCredentials
                        WHERE UserId = @userId
                    ) THEN 1
                    ELSE 0
                END;
                """;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@userId";
            parameter.Value = userId;
            command.Parameters.Add(parameter);

            var scalar = await command.ExecuteScalarAsync(cancellationToken);
            return scalar is not null
                && scalar is not DBNull
                && Convert.ToInt32(scalar) == 1;
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }
    }

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
                && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    [HttpGet]
    [ResponseCache(
        Duration = 0,
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public IActionResult Error()
    {
        var feature =
            HttpContext.Features
                .Get<IExceptionHandlerPathFeature>();

        var error = feature?.Error;
        var errorId =
            Activity.Current?.TraceId.ToString()
            ?? HttpContext.TraceIdentifier;

        var endpointDisplayName =
            ReadFeatureProperty(feature, "Endpoint")
                is Endpoint endpoint
                    ? endpoint.DisplayName
                    : null;

        string? controller = null;
        string? action = null;

        if (ReadFeatureProperty(feature, "RouteValues")
            is RouteValueDictionary routeValues)
        {
            controller =
                routeValues.TryGetValue(
                    "controller",
                    out var controllerValue)
                    ? controllerValue?.ToString()
                    : null;

            action =
                routeValues.TryGetValue(
                    "action",
                    out var actionValue)
                    ? actionValue?.ToString()
                    : null;
        }

        var model =
            new TestDiagnosticErrorViewModel
            {
                ErrorId = errorId,
                TimestampUtc = DateTime.UtcNow,
                RequestMethod = Request.Method,
                RequestPath =
                    string.IsNullOrWhiteSpace(feature?.Path)
                        ? Request.Path.Value ?? "(unknown)"
                        : feature.Path,
                Endpoint = endpointDisplayName,
                Controller = controller,
                Action = action,
                ExceptionType =
                    error?.GetType().FullName
                    ?? "(exception feature unavailable)",
                Message = SafeMessage(error?.Message),
                InnerExceptionType =
                    error?.InnerException?.GetType().FullName,
                InnerMessage =
                    SafeMessage(error?.InnerException?.Message),
                SqlErrorNumber =
                    FindSqlErrorNumber(error)
            };

        if (error is not null)
        {
            logger.LogError(
                error,
                "12WR TEST diagnostic error {ErrorId} at {Method} {Path}. Endpoint={Endpoint}; Controller={Controller}; Action={Action}; SqlErrorNumber={SqlErrorNumber}",
                model.ErrorId,
                model.RequestMethod,
                model.RequestPath,
                model.Endpoint,
                model.Controller,
                model.Action,
                model.SqlErrorNumber);
        }
        else
        {
            logger.LogWarning(
                "12WR TEST diagnostic error page was reached without an exception feature. ErrorId={ErrorId}; Method={Method}; Path={Path}",
                model.ErrorId,
                model.RequestMethod,
                model.RequestPath);
        }

        Response.StatusCode =
            StatusCodes.Status500InternalServerError;

        return View(model);
    }

    private static object? ReadFeatureProperty(
        IExceptionHandlerPathFeature? feature,
        string propertyName)
    {
        if (feature is null)
            return null;

        return feature
            .GetType()
            .GetProperty(propertyName)
            ?.GetValue(feature);
    }

    private static int? FindSqlErrorNumber(
        Exception? exception)
    {
        for (var current = exception;
             current is not null;
             current = current.InnerException)
        {
            var type = current.GetType();

            if (!type.Name.Contains(
                    "SqlException",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var number =
                type.GetProperty("Number")
                    ?.GetValue(current);

            if (number is int value)
                return value;
        }

        return null;
    }

    private static string? SafeMessage(
        string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var safe = message.Trim();

        safe =
            SensitiveAssignmentRegex.Replace(
                safe,
                "$1=[REDACTED]");

        safe =
            ConnectionPartRegex.Replace(
                safe,
                "$1=[REDACTED]");

        safe =
            BearerRegex.Replace(
                safe,
                "Bearer [REDACTED]");

        const int maxLength = 4000;

        return safe.Length <= maxLength
            ? safe
            : safe[..maxLength]
              + " ... [TRUNCATED]";
    }
}

public sealed class TestDiagnosticErrorViewModel
{
    public required string ErrorId { get; init; }
    public DateTime TimestampUtc { get; init; }
    public required string RequestMethod { get; init; }
    public required string RequestPath { get; init; }
    public string? Endpoint { get; init; }
    public string? Controller { get; init; }
    public string? Action { get; init; }
    public required string ExceptionType { get; init; }
    public string? Message { get; init; }
    public string? InnerExceptionType { get; init; }
    public string? InnerMessage { get; init; }
    public int? SqlErrorNumber { get; init; }
}
