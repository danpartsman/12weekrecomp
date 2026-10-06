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
            return Json(new
            {
                configured = true,
                matchCount = matches.Length,
                userExists = matches.Length > 0
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
