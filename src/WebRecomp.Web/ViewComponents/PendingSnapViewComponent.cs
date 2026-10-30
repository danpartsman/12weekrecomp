using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;
using WebRecomp.Web.Services.Coaching;
namespace WebRecomp.Web.ViewComponents;
public sealed class PendingSnapViewComponent(AppUserContextService users, CoachAccountabilityStore store, IConfiguration configuration,SimpleCoachingService? simple=null) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!AccountabilityPreview.Enabled(configuration) || User.Identity?.IsAuthenticated != true) return Content("");
        var ct = HttpContext.RequestAborted;
        var context = await users.GetAsync(HttpContext, ct);
        if (context.Client is null) return Content("");
        try
        {
            var row = SimpleCoaching.Enabled(configuration)?await simple!.ReadAsync(context.User.UserId,context.Client.ClientId,false,ct):await store.ReadAsync(context.User.UserId, context.Client.ClientId, false, ct);
            if (row is null) return Content("");
            var state = AccountabilityDocument.Parse(row.StateJson);
            var pending = state.SnapWorkouts.Where(w => (!SimpleCoaching.Enabled(configuration)||w.Simple)&&!(SimpleCoaching.Enabled(configuration)?state.SnapResults.GetValueOrDefault(w.Id)?.Finalized==true:SnapWorkoutLibrary.Completed(w,state.SnapResults.GetValueOrDefault(w.Id)))).OrderBy(w => w.Date).ToList();
            if (pending.Count == 0) return Content("");
            ViewData["ClientId"] = context.Client.ClientId;
            return View("~/Views/Shared/Components/PendingSnap/Default.cshtml", pending);
        }
        catch (UnauthorizedAccessException) { return Content(""); }
    }
}
