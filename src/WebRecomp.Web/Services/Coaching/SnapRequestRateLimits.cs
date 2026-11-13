using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace WebRecomp.Web.Services.Coaching;

public static class SnapRequestRateLimits
{
    public const string Submissions = "snap-submissions";
    public const string Estimates = "snap-estimates";

    public static void Configure(RateLimiterOptions options)
    {
        foreach (var policy in new[] { Submissions, Estimates })
            options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst("oid")?.Value ?? context.User.FindFirst("sub")?.Value
                    ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = policy == Estimates ? 120 : 60,
                    Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        options.OnRejected = ExplainAsync;
    }

    private static async ValueTask ExplainAsync(OnRejectedContext rejected, CancellationToken ct)
    {
        var http = rejected.HttpContext;
        var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        if (policy is not (Submissions or Estimates)) return;
        var seconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)) : 60;
        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        http.Response.Headers.CacheControl = "no-store";
        if (policy == Estimates)
        {
            await http.Response.WriteAsJsonAsync(new { message = $"Estimate requests paused. Wait {seconds} seconds; you can still submit the workout." }, ct);
            return;
        }
        static string Encode(string value) => HtmlEncoder.Default.Encode(value);
        var fields = new System.Text.StringBuilder();
        // Retain the rejected POST for an explicit retry; never run it automatically.
        // Bound form reads here because rate limiting runs before action request-size filters.
        if (HttpMethods.IsPost(http.Request.Method) && http.Request.HasFormContentType
            && http.Request.ContentLength is > 0 and <= 65536)
        {
            var form = await http.Request.ReadFormAsync(ct);
            foreach (var field in form)
                foreach (var value in field.Value)
                    fields.Append($"<input type=\"hidden\" name=\"{Encode(field.Key)}\" value=\"{Encode(value ?? "")}\" />");
        }
        http.Response.ContentType = "text/html; charset=utf-8";
        var retry = fields.Length == 0 ? "" : $"<form method=\"post\" action=\"{Encode(http.Request.PathBase.Add(http.Request.Path).Value ?? "/")}\">{fields}<button type=\"submit\" data-snap-retry disabled>Try submission again</button></form>";
        await http.Response.WriteAsync($$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Snap Workout · Please wait</title>
            <link rel="stylesheet" href="/css/snap-request-help.css"></head><body><main>
            <p>12 Week Recomp · Snap Workout</p><h1>Your submission was paused</h1>
            <p>This submission was not processed. Too many Snap requests were received in a short time, so the site temporarily paused submissions (HTTP 429).</p>
            <h2>How to fix it</h2><ol><li>Wait <strong data-snap-wait>{{seconds}}</strong> seconds for the limit to reset.</li>
            <li>Then click <strong>Try submission again</strong> once.{{(fields.Length > 0 ? " Your submitted entries are retained on this page." : " Use Go back to return to your form.")}}</li>
            <li>If it happens again, stop submitting for one minute and try once more. If it continues, send support the time and this error code: HTTP 429.</li></ol>
            <p>You do not need to change phones or computers. The limit applies to your signed-in account.</p>
            <div class="actions" data-snap-retry-seconds="{{seconds}}">{{retry}}<button type="button" data-snap-back>Go back to form</button></div>
            </main><script src="/js/snap-request-help.js" defer></script></body></html>
            """, ct);
    }
}
