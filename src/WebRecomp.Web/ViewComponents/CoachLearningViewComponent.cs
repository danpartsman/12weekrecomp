using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Learning;

namespace WebRecomp.Web.ViewComponents;

public sealed class CoachLearningViewComponent(
    AppUserContextService userContext,
    LearningOrchestratorService learning) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(
        string surfaceCode,
        string displayMode = "thumbnail")
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                HttpContext.RequestAborted);

        var presentation =
            await learning.ResolveAsync(
                context.User,
                surfaceCode,
                displayMode,
                HttpContext.RequestAborted);

        ViewData["ReturnUrl"] =
            $"{HttpContext.Request.Path}{HttpContext.Request.QueryString}";

        return View(
            "~/Views/Shared/Components/CoachLearning/Default.cshtml",
            presentation);
    }
}
