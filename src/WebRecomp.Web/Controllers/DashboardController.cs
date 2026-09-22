using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;

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

        // TEST v1012 onboarding recovery:
        // Engine 3.0 requires a real body weight for energy and prescription
        // calculations. A client/program can occasionally exist without a
        // usable Measurement row (for example, an interrupted or legacy
        // onboarding flow). That is recoverable user input, not a server error.
        // Never invent a weight. Send the user to today's Daily Entry so they
        // can supply it, then the Dashboard can calculate normally.
        var hasUsableWeight =
            await db.Measurements
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ClientId == context.Client.ClientId
                        && !x.IsDeleted
                        && x.WeightLb.HasValue
                        && x.WeightLb.Value > 0m
                        && x.LocalMeasurementDate <= today,
                    cancellationToken);

        if (!hasUsableWeight)
        {
            TempData["StatusMessage"] =
                "Enter your current weight below to finish setup. Recomp needs a real weight before Engine 3.0 can calculate your Dashboard.";

            return RedirectToAction(
                "Index",
                "DailyEntry",
                new
                {
                    date = today.ToString("yyyy-MM-dd")
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
