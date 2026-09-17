using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

public sealed class ProgressController(
    RecompDbContext db,
    AppUserContextService userContext) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var all = await db.Measurements
            .Where(x => x.ClientId == context.Client.ClientId && !x.IsDeleted)
            .OrderByDescending(x => x.LocalMeasurementDate)
            .ThenByDescending(x => x.MeasurementAtUtc)
            .ToListAsync(cancellationToken);

        // Daily entry can create more than one measurement snapshot on a date.
        // Progress shows the latest snapshot for each calendar day so graphs and
        // trend tables are not distorted by same-day edits.
        var daily = all
            .GroupBy(x => x.LocalMeasurementDate)
            .Select(g => g.OrderByDescending(x => x.MeasurementAtUtc).First())
            .OrderByDescending(x => x.LocalMeasurementDate)
            .ToList();

        var rows = daily.Select(x =>
        {
            var fatMass = x.WeightLb.HasValue && x.BodyFatPct.HasValue
                ? x.WeightLb.Value * x.BodyFatPct.Value
                : (decimal?)null;
            var leanMass = x.WeightLb.HasValue && fatMass.HasValue
                ? x.WeightLb.Value - fatMass.Value
                : (decimal?)null;

            return new ProgressMeasurementRowViewModel
            {
                Date = x.LocalMeasurementDate,
                WeightLb = x.WeightLb,
                BodyFatPercent = x.BodyFatPct * 100m,
                FatMassLb = fatMass,
                LeanMassLb = leanMass,
                MuscleMassLb = x.MuscleMassLb,
                BodyWaterLb = x.BodyWaterLb,
                VisceralFatRating = x.VisceralFatRating,
                WaistIn = x.WaistIn,
                HipsIn = x.HipsIn,
                Notes = x.Notes
            };
        }).ToList();

        var latest = rows.FirstOrDefault();
        var first = rows.LastOrDefault();

        return View(new ProgressViewModel
        {
            ClientName = context.Client.ClientName,
            Today = AppUserContextService.GetClientToday(context.Client),
            MeasurementDays = rows.Count,
            FirstMeasurementDate = first?.Date,
            LatestMeasurementDate = latest?.Date,
            WeightChangeLb = Delta(latest?.WeightLb, first?.WeightLb),
            BodyFatChangePoints = Delta(latest?.BodyFatPercent, first?.BodyFatPercent),
            WaistChangeIn = Delta(latest?.WaistIn, first?.WaistIn),
            Measurements = rows
        });
    }

    private static decimal? Delta(decimal? latest, decimal? first)
        => latest.HasValue && first.HasValue ? latest.Value - first.Value : null;
}
