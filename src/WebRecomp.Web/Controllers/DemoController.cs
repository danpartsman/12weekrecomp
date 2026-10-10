using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

[Route("demo")]
public sealed class DemoController(
    DemoSessionService demoSessions,
    GuidedDemoSeederService guidedSeeder) : Controller
{
    [AllowAnonymous]
    [HttpGet("{code}")]
    public async Task<IActionResult> Redeem(
        string code,
        CancellationToken cancellationToken)
    {
        // Do not silently mix an authenticated permanent Entra account with
        // a temporary demo workspace in the same browser session.
        if (User.Identity?.IsAuthenticated == true
            && !DemoSessionService.IsDemoPrincipal(User))
        {
            ViewBag.Message =
                "You are already signed in to a Recomp customer account. " +
                "Sign out before opening a demo invitation.";

            return View("Unavailable");
        }

        // If this browser already has a demo, replace its browser session
        // cleanly before redeeming another invitation.
        if (DemoSessionService.IsDemoPrincipal(User))
        {
            await demoSessions.RevokeCurrentSessionAsync(
                HttpContext,
                cancellationToken);
        }

        var metadata =
            DemoSessionService.CaptureRequestMetadata(HttpContext);

        var result = await demoSessions.RedeemAsync(
            code,
            metadata,
            cancellationToken);

        if (!result.Success
            || result.Workspace is null
            || string.IsNullOrWhiteSpace(result.RawSessionToken)
            || result.SessionExpiresAtUtc is null)
        {
            ViewBag.Message =
                result.Message
                ?? "This demo invitation could not be opened.";

            return View("Unavailable");
        }

        // Fresh Demo stays deliberately blank and uses the existing onboarding
        // path after the next request restores the temporary demo principal.
        if (string.Equals(
                result.Workspace.DemoTypeCode,
                "FRESH",
                StringComparison.OrdinalIgnoreCase))
        {
            demoSessions.WriteBrowserCookie(
                Response,
                result.RawSessionToken,
                result.SessionExpiresAtUtc.Value);

            return Redirect("/");
        }

        // Guided Demo is preloaded with normal Recomp records and calculations.
        // No parallel or canned Dashboard engine is used.
        var seeded =
            await guidedSeeder.SeedAsync(
                result.Workspace,
                cancellationToken);

        demoSessions.WriteBrowserCookie(
            Response,
            result.RawSessionToken,
            result.SessionExpiresAtUtc.Value);

        return RedirectToAction(
            "CommandCenter",
            "Dashboard",
            new
            {
                date =
                    seeded.ActionDate.ToString(
                        "yyyy-MM-dd")
            });
    }

    [Authorize]
    [HttpGet("guided")]
    public IActionResult Guided()
    {
        if (!DemoSessionService.IsDemoPrincipal(User))
            return Redirect("/");

        // Kept as a compatibility route for the first v1006 validation build.
        // A seeded Guided Demo now lands on Dashboard.
        return RedirectToAction(
            "CommandCenter",
            "Dashboard");
    }

    [Authorize]
    [HttpPost("exit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Exit(
        CancellationToken cancellationToken)
    {
        if (DemoSessionService.IsDemoPrincipal(User))
        {
            await demoSessions.RevokeCurrentSessionAsync(
                HttpContext,
                cancellationToken);
        }

        return Redirect("/");
    }
}
