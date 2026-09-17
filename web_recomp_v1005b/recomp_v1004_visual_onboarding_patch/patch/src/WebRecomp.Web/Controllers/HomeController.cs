using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

public sealed class HomeController(AppUserContextService userContext) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        ViewData["PublicPage"] = true;
        return View();
    }

    [HttpGet]
    public IActionResult Evidence()
    {
        ViewData["PublicPage"] = true;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> AfterSignIn(CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);

        // A newly authenticated account sees the short Recomp explanation first.
        // Once setup exists, normal sign-in returns directly to the Dashboard.
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Onboarding");

        return RedirectToAction("Index", "Dashboard");
    }

    public IActionResult Error() => View();
}
