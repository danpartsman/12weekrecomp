using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

public sealed class HomeController(
    AppUserContextService userContext) : Controller
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

            if (context.Client is null || context.Program is null)
                return RedirectToAction("Index", "Onboarding");

            return RedirectToAction("Index", "Dashboard");
        }

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

        // New customers enter onboarding.
        // Existing customers return directly to Dashboard.
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Onboarding");

        return RedirectToAction("Index", "Dashboard");
    }

    [AllowAnonymous]
    public IActionResult Error() => View();
}