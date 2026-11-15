namespace WebRecomp.Web.Services.Engine30;

public sealed record RollingGlycogenDay(DateOnly Date, decimal DemandG, decimal BackgroundCarbsG, decimal? CarbsG, bool HasTrainingEvidence = true);
public sealed record RollingGlycogenEstimate(decimal CapacityG, decimal LowG, decimal HighG,
    decimal TargetLowG, decimal TargetHighG, int EvidenceDays, int WindowDays,
    decimal CarbAdjustmentG, IReadOnlyList<RollingGlycogenDay> Days)
{
    public string Confidence => EvidenceDays < WindowDays ? "Incomplete food/training logs · adjustment paused" : "Low · modeled estimate";
    public string Method => "Experimental four-day fuel model, not a glycogen measurement. Background carbohydrate use is approximated by the engine base-rest carbohydrate reference, not directly measured oxidation. Assumptions: capacity = 80 g + 2.5 g per lb estimated fat-free mass; initial stores 25–75% of capacity; intake ±10%, background use ±20%, exercise demand ±25%. These are modeling assumptions, not validated individual error bounds. Muscle and liver are simplified into one pool; local muscle depletion is not resolved. Fat is not credited as glycogen. No midnight reset or calorie debt; no score or financial penalty is based on this estimate.";
}

/// <summary>Bounded scenario model. The range is not a statistical confidence interval.
/// Recomputed from source evidence so corrected/deleted entries cannot leave stale stores.
/// </summary>
public static class Engine30RollingGlycogen
{
    public static RollingGlycogenEstimate Calculate(decimal fatFreeMassLb,
        IReadOnlyList<RollingGlycogenDay> days, decimal upcomingDemandG,
        decimal baselineCarbsG, decimal carbFloorG, decimal energyBudgetKcal,
        decimal proteinUpperG, decimal fatG)
    {
        if (fatFreeMassLb <= 0 || days.Count != 4 || baselineCarbsG < 0 || upcomingDemandG < 0
            || days.Any(x => x.DemandG < 0 || x.BackgroundCarbsG < 0 || x.CarbsG < 0))
            throw new ArgumentException("Four consecutive days and nonnegative fuel inputs are required.");
        var ordered = days.OrderBy(x => x.Date).ToArray();
        for (var i = 1; i < ordered.Length; i++)
            if (ordered[i].Date != ordered[i - 1].Date.AddDays(1)) throw new ArgumentException("Fuel dates must be consecutive.");
        var capacity = Math.Clamp(80m + fatFreeMassLb * 2.5m, 200m, 800m);
        var low = capacity * .25m; var high = capacity * .75m;
        foreach (var d in ordered)
        {
            if (!d.CarbsG.HasValue || !d.HasTrainingEvidence) { low = 0; high = capacity; continue; }
            low = Math.Clamp(low + d.CarbsG.Value * .9m - d.BackgroundCarbsG * 1.2m - d.DemandG * 1.25m, 0m, capacity);
            high = Math.Clamp(high + d.CarbsG.Value * 1.1m - d.BackgroundCarbsG * .8m - d.DemandG * .75m, 0m, capacity);
        }
        // A readiness band, not a mandate to keep stores full. Upcoming demand
        // increases the reserve; the energy budget and physiological floor win.
        var targetLow = Math.Min(capacity * .8m, capacity * .4m + upcomingDemandG);
        var targetHigh = Math.Min(capacity, capacity * .7m + upcomingDemandG);
        var known = ordered.Count(x => x.CarbsG.HasValue && x.HasTrainingEvidence);
        var correction = known < 4 ? 0m : high < targetLow ? targetLow - high
            : low > targetHigh ? targetHigh - low : 0m;
        // Product guardrail: at most 25% of the baseline prescription. It is not
        // a scientifically proven allowance or a carry-forward debt.
        correction = Math.Clamp(correction, -baselineCarbsG * .25m, baselineCarbsG * .25m);
        var maximum = Math.Max(baselineCarbsG, (energyBudgetKcal - proteinUpperG * .9m * 4m - fatG * 9m) / 4m);
        var adjusted = Math.Clamp(baselineCarbsG + correction, Math.Min(baselineCarbsG, carbFloorG), maximum);
        adjusted = Math.Round(adjusted / 5m, 0, MidpointRounding.AwayFromZero) * 5m;
        adjusted = Math.Max(Math.Min(baselineCarbsG, carbFloorG), Math.Min(maximum, adjusted));
        return new(capacity, low, high, targetLow, targetHigh, known, 4,
            known < 4 ? 0m : adjusted - baselineCarbsG, ordered);
    }
}
