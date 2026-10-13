using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.WorkoutHistory;

namespace WebRecomp.Web.Controllers;

[Authorize]
public sealed class ExerciseHistoryController(RecompDbContext db, AppUserContextService users) : Controller
{
    [HttpGet("/DetailedWorkout/ExerciseHistory")]
    public async Task<IActionResult> Index(Guid exerciseId, int offset = 0, CancellationToken cancellationToken = default)
    {
        Response.Headers["Cache-Control"] = "no-store";
        if (exerciseId == Guid.Empty || offset < 0) return BadRequest();
        var context = await users.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null) return NotFound();
        return Json(await ExerciseHistoryReader.ReadAsync(db, context.Client.ClientId, exerciseId,
            AppUserContextService.GetClientToday(context.Client), offset, cancellationToken));
    }
}
