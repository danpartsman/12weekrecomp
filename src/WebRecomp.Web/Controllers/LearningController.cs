using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Learning;

namespace WebRecomp.Web.Controllers;

public sealed class LearningController(
    AppUserContextService userContext,
    LearningOrchestratorService learning) : Controller
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
            await learning.GetPreferredCoachCodeAsync(
                context.User.UserId,
                cancellationToken);

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

        await learning.SetPreferredCoachAsync(
            context.User.UserId,
            coachCode,
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
    public async Task<IActionResult> Read(string surfaceCode, string displayMode,
        string? contentCode, string? returnUrl, CancellationToken cancellationToken)
    {
        if (LearningContentCatalog.ForSurface(surfaceCode ?? "").Count == 0
            || (displayMode != "full" && displayMode != "thumbnail")) return BadRequest();
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        var presentation = await learning.ReadAsync(context.User, surfaceCode, displayMode,
            contentCode, cancellationToken);
        if (presentation is null) return StatusCode(503);
        ViewData["ReturnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        return PartialView("~/Views/Shared/Components/CoachLearning/Default.cshtml", presentation);
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

