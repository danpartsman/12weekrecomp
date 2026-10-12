using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.WorkoutTargeting;

namespace WebRecomp.Web.ViewComponents;

public sealed class WorkoutRecommendationsViewComponent(AppUserContextService users, WorkoutTargetService targets,
    ILogger<WorkoutRecommendationsViewComponent> logger) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(DateOnly date)
    {
        var ct = HttpContext.RequestAborted;
        var context = await users.GetAsync(HttpContext, ct);
        if (context.Client is null || context.Program is null || date != AppUserContextService.GetClientToday(context.Client)) return Content("");
        try { return View(await targets.BuildDashboardAsync(context, ct)); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Dashboard workout recommendation unavailable");
            var model = new TodayWorkoutViewModel { Date=date };
            model.Notices.Add("Workout recommendations are temporarily unavailable. Your saved plan and workouts are still available.");
            return View(model);
        }
    }
}
