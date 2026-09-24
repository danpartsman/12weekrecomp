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
        var context = await userContext.GetAsync(HttpContext, cancellationToken);

        // The Dashboard is an Engine 3.0 calculation surface, not an onboarding
        // form. Never let a new or interrupted account reach the engine until
        // the minimum real physiology + plan inputs exist.
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Onboarding");

        var client = context.Client;
        var program = context.Program;
        var today = AppUserContextService.GetClientToday(client);

        var profileReady =
            client.DateOfBirth.HasValue
            && !string.IsNullOrWhiteSpace(client.SexCode)
            && client.HeightIn >= 48m
            && client.HeightIn <= 90m
            && !string.IsNullOrWhiteSpace(program.GoalCode)
            && program.AvailableTrainingDaysWeek is >= 1 and <= 7;

        var hasUsableWeight = await db.Measurements
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.ClientId == client.ClientId
                    && x.ProgramId == program.ProgramId
                    && !x.IsDeleted
                    && x.WeightLb.HasValue
                    && x.WeightLb.Value > 0m
                    && x.LocalMeasurementDate <= today,
                cancellationToken);

        if (!profileReady || !hasUsableWeight)
        {
            TempData["StatusMessage"] =
                "Finish the short setup first. Recomp needs your real weight, age, calculation sex, height and plan direction before Engine 3.0 can safely calculate a Dashboard prescription.";

            return RedirectToAction("Index", "Onboarding");
        }

        var vm = await dashboard.BuildAsync(context, today, cancellationToken);
        return View(vm);
    }
}
