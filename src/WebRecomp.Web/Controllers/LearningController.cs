using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Learning;

namespace WebRecomp.Web.Controllers;

public sealed class LearningController(
    AppUserContextService userContext,
    LearningOrchestratorService learning,
    WebRecomp.Web.Data.RecompDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Coaches(
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        ViewData["PreferredCoachCode"] =
            CoachCatalog.Resolve(
                context.User.PreferredCoachCode).Code;

        return View(
            CoachCatalog.All);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChooseCoach(
        string coachCode,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (!CoachCatalog.IsValid(coachCode))
        {
            TempData["StatusMessage"] =
                "Choose one of the available coaches.";

            return RedirectToAction(
                nameof(Coaches));
        }

        context.User.PreferredCoachCode =
            CoachCatalog.Resolve(coachCode).Code;

        context.User.UpdatedAtUtc =
            DateTime.UtcNow;

        await db.SaveChangesAsync(
            cancellationToken);

        TempData["StatusMessage"] =
            $"{CoachCatalog.Resolve(coachCode).Name} is now your coach.";

        if (!string.IsNullOrWhiteSpace(returnUrl)
            && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction(
            nameof(Coaches));
    }

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

        if (string.Equals(
                Request.Headers["X-Requested-With"].ToString(),
                "XMLHttpRequest",
                StringComparison.OrdinalIgnoreCase))
        {
            return NoContent();
        }

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
