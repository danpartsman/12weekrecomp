using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

public sealed class DashboardController(
    RecompDbContext db,
    AppUserContextService userContext,
    DashboardService dashboard) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var today =
            AppUserContextService.GetClientToday(
                context.Client);

        var hasTodayWeight = await db.Measurements.AnyAsync(
            x => x.ClientId == context.Client.ClientId
                 && x.ProgramId == context.Program.ProgramId
                 && !x.IsDeleted
                 && x.LocalMeasurementDate == today
                 && x.WeightLb != null,
            cancellationToken);

        if (!hasTodayWeight)
        {
            return View(new DashboardViewModel
            {
                ActionDate = today,
                ClientName = context.Client.ClientName,
                ProgramWeek = Math.Max(
                    1,
                    (today.DayNumber - context.Program.StartDate.DayNumber) / 7 + 1),
                PlanCode = context.Program.SelectedPlanCode,
                NeedsCurrentWeight = true,
                TodayStatusLabel = "Weight needed",
                TodayStatusClass = "warning"
            });
        }

        var vm =
            await dashboard.BuildAsync(
                context,
                today,
                cancellationToken);

        return View(vm);
    }
}
