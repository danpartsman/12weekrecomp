using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

/// <summary>
/// Stable public entry point for the Guided Demo.
/// The actual invite code stays in environment configuration so test and
/// production can use different demo invitations without changing source.
/// </summary>
[Route("guided-demo")]
public sealed class GuidedDemoController(
    IConfiguration configuration) : Controller
{
    [AllowAnonymous]
    [HttpGet("")]
    public IActionResult Index()
    {
        // If a Guided Demo is already active, this top-nav link simply returns
        // the tester to the demo Dashboard instead of consuming another invite.
        if (DemoSessionService.IsDemoPrincipal(User)
            && string.Equals(
                User.FindFirst("recomp_demo_type")?.Value,
                "GUIDED",
                StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction("CommandCentral", "Dashboard");
        }

        var code =
            configuration["WebRecomp:DefaultGuidedDemoCode"];

        if (string.IsNullOrWhiteSpace(code))
            return View("Unavailable");

        var query = new List<string>();

        foreach (var key in new[]
        {
            "utm_source",
            "utm_medium",
            "utm_campaign",
            "utm_content"
        })
        {
            if (Request.Query.TryGetValue(key, out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                query.Add(
                    $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value.ToString())}");
            }
        }

        if (Uri.TryCreate(
                Request.Headers.Referer.ToString(),
                UriKind.Absolute,
                out var referrer))
        {
            query.Add(
                $"wr_referrer_host={Uri.EscapeDataString(referrer.Host)}");

            query.Add(
                $"wr_referrer_path={Uri.EscapeDataString(referrer.AbsolutePath)}");
        }

        var destination =
            $"/demo/{Uri.EscapeDataString(code.Trim())}";

        if (query.Count > 0)
            destination += "?" + string.Join("&", query);

        return Redirect(destination);
    }
}
