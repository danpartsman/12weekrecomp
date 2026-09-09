using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

public sealed class OnboardingController(
    AppUserContextService userContext,
    AccessEntitlementService entitlements) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
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

        ViewData["OnboardingPage"] = true;
        return View();
    }
}
