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
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(HttpContext, cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var raw = await db.Measurements
            .Where(x =>
                x.ClientId == context.Client.ClientId
                && !x.IsDeleted)
            .OrderBy(x => x.LocalMeasurementDate)
            .ThenBy(x => x.MeasurementAtUtc)
            .ToListAsync(cancellationToken);

        // Keep the latest measurement for each local day.
        var measurements = raw
            .GroupBy(x => x.LocalMeasurementDate)
            .Select(g =>
                g.OrderByDescending(x => x.MeasurementAtUtc)
                    .First())
            .OrderBy(x => x.LocalMeasurementDate)
            .ToList();

        // Do not mix generic "muscle mass" and skeletal-muscle estimates in
        // one trend line. Prefer skeletal muscle when there are enough values
        // to establish a trend; otherwise fall back to the scale's generic
        // muscle-mass field for legacy/provider data.
        var skeletalCount =
            measurements.Count(x =>
                x.SkeletalMuscleMassLb.HasValue);

        var genericMuscleCount =
            measurements.Count(x =>
                x.MuscleMassLb.HasValue);

        var useSkeletal =
            skeletalCount >= 2
            || (skeletalCount > 0 && genericMuscleCount < 2);

        decimal? MuscleValue(WebRecomp.Web.Models.Measurement x) =>
            useSkeletal
                ? x.SkeletalMuscleMassLb
                : x.MuscleMassLb;

        var points = measurements
            .Select(x =>
            {
                var fatMass =
                    x.WeightLb.HasValue
                    && x.BodyFatPct.HasValue
                        ? x.WeightLb.Value
                          * x.BodyFatPct.Value
                        : (decimal?)null;

                return new RecompProgressPoint(
                    x.LocalMeasurementDate,
                    x.WeightLb,
                    x.BodyFatPct.HasValue
                        ? x.BodyFatPct.Value * 100m
                        : null,
                    fatMass,
                    MuscleValue(x),
                    x.SubcutaneousFatPct.HasValue
                        ? x.SubcutaneousFatPct.Value * 100m
                        : null,
                    x.VisceralFatRating,
                    x.BodyWaterLb,
                    x.WaistIn);
            })
            .ToList();

        var firstFat =
            points.FirstOrDefault(x => x.FatMassLb.HasValue);

        var latestFat =
            points.LastOrDefault(x => x.FatMassLb.HasValue);

        var firstMuscle =
            points.FirstOrDefault(x => x.MuscleLb.HasValue);

        var latestMuscle =
            points.LastOrDefault(x => x.MuscleLb.HasValue);

        var firstWeight =
            points.FirstOrDefault(x => x.WeightLb.HasValue);

        var latestWeight =
            points.LastOrDefault(x => x.WeightLb.HasValue);

        var firstBodyFat =
            points.FirstOrDefault(x => x.BodyFatPercent.HasValue);

        var latestBodyFat =
            points.LastOrDefault(x => x.BodyFatPercent.HasValue);

        var firstSubcutaneous =
            points.FirstOrDefault(x =>
                x.SubcutaneousFatPercent.HasValue);

        var latestSubcutaneous =
            points.LastOrDefault(x =>
                x.SubcutaneousFatPercent.HasValue);

        var firstVisceral =
            points.FirstOrDefault(x =>
                x.VisceralFatRating.HasValue);

        var latestVisceral =
            points.LastOrDefault(x =>
                x.VisceralFatRating.HasValue);

        var latestAny = points.LastOrDefault();

        var model = new RecompProgressViewModel
        {
            ClientName = context.Client.ClientName,

            UsingSkeletalMuscle = useSkeletal,

            MuscleMetricLabel =
                useSkeletal
                    ? "Skeletal Muscle"
                    : "Muscle Mass (scale estimate)",

            CurrentFatMassLb = latestFat?.FatMassLb,
            FatMassChangeLb =
                Delta(
                    latestFat?.FatMassLb,
                    firstFat?.FatMassLb),

            CurrentMuscleLb = latestMuscle?.MuscleLb,
            MuscleChangeLb =
                Delta(
                    latestMuscle?.MuscleLb,
                    firstMuscle?.MuscleLb),

            CurrentWeightLb = latestWeight?.WeightLb,
            WeightChangeLb =
                Delta(
                    latestWeight?.WeightLb,
                    firstWeight?.WeightLb),

            CurrentBodyFatPercent =
                latestBodyFat?.BodyFatPercent,

            BodyFatPercentChange =
                Delta(
                    latestBodyFat?.BodyFatPercent,
                    firstBodyFat?.BodyFatPercent),

            CurrentSubcutaneousFatPercent =
                latestSubcutaneous?.SubcutaneousFatPercent,

            SubcutaneousFatPercentChange =
                Delta(
                    latestSubcutaneous?.SubcutaneousFatPercent,
                    firstSubcutaneous?.SubcutaneousFatPercent),

            CurrentVisceralFatRating =
                latestVisceral?.VisceralFatRating,

            VisceralFatChange =
                Delta(
                    latestVisceral?.VisceralFatRating,
                    firstVisceral?.VisceralFatRating),

            CurrentBodyWaterLb = latestAny?.BodyWaterLb,
            CurrentWaistIn = latestAny?.WaistIn,

            Points = points
        };

        return View(model);
    }

    private static decimal? Delta(
        decimal? current,
        decimal? first)
    {
        if (!current.HasValue || !first.HasValue)
            return null;

        return current.Value - first.Value;
    }
}
