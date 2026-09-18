using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

public sealed class HomeController(
    AppUserContextService userContext,
    AccessEntitlementService entitlements,
    PendingAccessCodeService pendingCodes) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        // Welcome remains public. Authenticated customers opening the root
        // are routed into their Recomp experience.
        if (User.Identity?.IsAuthenticated == true)
        {
            var context =
                await userContext.GetAsync(HttpContext, cancellationToken);

            if (entitlements.IsEntitlementRequired)
            {
                var hasAccess =
                    await entitlements.HasApplicationAccessAsync(
                        context.User.UserId,
                        cancellationToken);

                if (!hasAccess)
                    return RedirectToAction("Index", "Access");
            }

            if (context.Client is null || context.Program is null)
                return RedirectToAction("Index", "Onboarding");

            return RedirectToAction("Index", "Dashboard");
        }

        ViewData["PublicPage"] = true;

        var pendingCode = pendingCodes.Get(HttpContext);

        if (!string.IsNullOrWhiteSpace(pendingCode))
        {
            var validation = await entitlements.ValidateCodeAsync(
                pendingCode,
                cancellationToken);

            if (validation.Success)
            {
                ViewData["PendingAccessMessage"] =
                    validation.PromotionName is not null
                        ? $"{validation.PromotionName} is saved. " +
                          $"{validation.AccessDurationDays} days of access " +
                          "will be applied when you create or sign in to your account."
                        : $"Your access code is saved. " +
                          $"{validation.AccessDurationDays} days of access " +
                          "will be applied when you create or sign in to your account.";
            }
            else
            {
                pendingCodes.Clear(HttpContext);
            }
        }

        return View();
    }

    [AllowAnonymous]
    [HttpGet("/welcome-next")]
    public IActionResult WelcomeNext()
    {
        ViewData["PublicPage"] = true;
        return View();
    }

    // High-energy marketing preview. Kept separate from the live welcome page
    // until the design is explicitly approved for promotion.
    [AllowAnonymous]
    [HttpGet("/welcome-ignite")]
    public IActionResult WelcomeIgnite()
    {
        ViewData["PublicPage"] = true;
        return View();
    }

    // Experimental welcome concept lab. All routes are TEST-safe previews and
    // intentionally remain separate from the live root welcome page.
    [AllowAnonymous]
    [HttpGet("/welcome-lab")]
    public IActionResult WelcomeLab()
    {
        ViewData["PublicPage"] = true;
        return View();
    }

    [AllowAnonymous]
    [HttpGet("/welcome-lab/break-free")]
    public IActionResult WelcomeLabBreakFree()
    {
        ViewData["PublicPage"] = true;
        return View();
    }

    [AllowAnonymous]
    [HttpGet("/welcome-lab/hotspots")]
    public IActionResult WelcomeLabHotspots()
    {
        ViewData["PublicPage"] = true;
        return View();
    }

    [AllowAnonymous]
    [HttpGet("/welcome-lab/cards")]
    public IActionResult WelcomeLabCards()
    {
        ViewData["PublicPage"] = true;
        return View();
    }

    [AllowAnonymous]
    [HttpGet("/welcome-lab/ascend")]
    public IActionResult WelcomeLabAscend()
    {
        ViewData["PublicPage"] = true;
        return View();
    }

    // Simple public guide pages used by the experimental welcome concepts.
    [AllowAnonymous]
    [HttpGet("/recomp/what")]
    public IActionResult RecompWhat()
    {
        ViewData["PublicPage"] = true;
        return View();
    }

    [AllowAnonymous]
    [HttpGet("/recomp/how")]
    public IActionResult RecompHow()
    {
        ViewData["PublicPage"] = true;
        return View();
    }

    [AllowAnonymous]
    [HttpGet("/recomp/evidence")]
    public IActionResult RecompEvidence()
    {
        ViewData["PublicPage"] = true;
        return View();
    }

    [AllowAnonymous]
    [HttpGet("/recomp/start")]
    public IActionResult RecompStart()
    {
        ViewData["PublicPage"] = true;
        return View();
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Evidence()
    {
        ViewData["PublicPage"] = true;
        return View();
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> AfterSignIn(
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(HttpContext, cancellationToken);

        if (entitlements.IsEntitlementRequired)
        {
            if (entitlements.IsEntitlementRequired)
            {
                var hasAccess =
                    await entitlements.HasApplicationAccessAsync(
                        context.User.UserId,
                        cancellationToken);

                if (!hasAccess)
                    return RedirectToAction("Index", "Access");
            }
        }

        // Entitled new customers enter onboarding.
        // Existing customers return directly to Dashboard.
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Onboarding");

        return RedirectToAction("Index", "Dashboard");
    }

    [AllowAnonymous]
    public IActionResult Error() => View();
}
