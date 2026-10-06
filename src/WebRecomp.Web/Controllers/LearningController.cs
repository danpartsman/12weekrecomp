using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Learning;

namespace WebRecomp.Web.Controllers;

public sealed class LearningController(
    AppUserContextService userContext,
    LearningOrchestratorService learning) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Interact(
        string contentCode,
        string surfaceCode,
        string interactionCode,
        string? returnUrl,
        string? targetUrl,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        await learning.RecordInteractionAsync(
            context.User,
            contentCode,
            surfaceCode,
            interactionCode,
            targetUrl,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(targetUrl)
            && Url.IsLocalUrl(targetUrl)
            && interactionCode.Equals(
                LearningInteractionCodes.LinkClick,
                StringComparison.OrdinalIgnoreCase))
        {
            return LocalRedirect(targetUrl);
        }

        if (!string.IsNullOrWhiteSpace(returnUrl)
            && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction(
            "Index",
            "Dashboard");
    }
}
