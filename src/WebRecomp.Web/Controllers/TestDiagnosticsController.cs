using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace WebRecomp.Web.Controllers;

// TEST ONLY. This diagnostic surface is intentionally useful for v1010 testing.
// Do not promote this controller/view or the TestDiagnostics exception-handler
// route into LIVE.
public sealed class TestDiagnosticsController(
    ILogger<TestDiagnosticsController> logger) : Controller
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
