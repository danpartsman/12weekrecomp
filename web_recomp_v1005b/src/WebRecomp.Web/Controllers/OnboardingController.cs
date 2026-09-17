using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

public sealed class OnboardingController(AppUserContextService userContext) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // Calling GetAsync guarantees that this is a real authenticated Recomp account.
        // Existing members may revisit this page from the authenticated navigation.
        _ = await userContext.GetAsync(HttpContext, cancellationToken);
        ViewData["OnboardingPage"] = true;
        return View();
    }
}
