using WebRecomp.Web.Services.Dashboard2;

namespace WebRecomp.Web.Services.Dashboard3;

public sealed record RecompDayEvidence(
    DateOnly Date, decimal ProteinTarget, decimal CarbsTarget, decimal FatTarget,
    decimal EnergyTarget, decimal? Protein, decimal? Carbs, decimal? Fat,
    decimal? Energy, int? FlexibleItems, string TargetSource);

public sealed record RecompTrainingGauge(
    string Key, string Label, int Completed, int Target, int RemainingPlanned, decimal Weight)
{
    public decimal ProjectedFraction => Target == 0 ? 1m : Math.Min(1m, (decimal)(Completed + RemainingPlanned) / Target);
    public string Status => Target == 0 ? "Not in this week's plan" : Completed >= Target ? "Weekly goal reached"
        : ProjectedFraction < 1m ? "Behind plan · recoverable" : "Still on schedule";
}

public sealed record RecompScoreComponent(string Label, decimal Weight, decimal Earned, string Explanation);
public sealed class RecompScoreResult
{
    public const string Version = "RECOMP_SCORE_V1";
    public int Score { get; init; }
    public string Band => Score >= 80 ? "green" : Score >= 60 ? "yellow" : "red";
    public string Status => Score >= 80 ? "On course" : Score >= 60 ? "Build your momentum" : "Time to reset the plan";
    public List<RecompScoreComponent> Components { get; init; } = [];
    public int CompletedNutritionDays { get; init; }
    public int KnownNutritionDays { get; init; }
    public int? FlexibleItems { get; init; }
    public int FlexibleConfirmedDays { get; init; }
    public bool FlexibleDayOverMacros { get; init; }
    public string FlexibleBand => FlexibleDayOverMacros || FlexibleItems >= 5 ? "red"
        : FlexibleItems >= 3 ? "yellow" : "green";
}

/// <summary>
/// Versioned product adherence rubric, not a physiological or outcome measurement.
/// Unknown intake never means zero intake or loses points. Unfinished/future days retain
/// potential, so the visible score MUST travel with its evidence coverage when evaluated.
/// </summary>
public static class RecompAdherenceScorer
{
    public static RecompScoreResult Score(DateOnly today, IReadOnlyList<RecompDayEvidence> days,
        IReadOnlyList<RecompTrainingGauge> training)
    {
        var components = new List<RecompScoreComponent>();
        foreach (var (key, label, weight) in new[] { ("protein", "Protein consistency", 25m),
            ("carbs", "Training-day carbohydrate", 20m), ("fat", "Fat balance", 10m) })
        {
            var qualities = days.Select(d =>
            {
                var band = MacroDayPlanner.Bands(d.ProteinTarget, d.CarbsTarget, d.FatTarget,
                    d.Protein, d.Carbs, d.Fat).Single(x => x.Key == key);
                return d.Date > today ? 1m : Quality(band.Eaten, band.Low, band.High, d.Date == today);
            }).ToArray();
            var fraction = qualities.Length == 0 ? 1m : qualities.Average();
            components.Add(new(label, weight, weight * fraction,
                fraction == 1m ? "No known misses; unfinished days retain their potential."
                    : "Completed-day shortfalls and known overages lower this part; future days can still be followed."));
        }
        foreach (var gauge in training)
            components.Add(new(gauge.Label, gauge.Weight, gauge.Weight * gauge.ProjectedFraction,
                $"{gauge.Completed}/{gauge.Target} completed; {gauge.RemainingPlanned} scheduled opportunities remain. {gauge.Status}."));

        var knownFlex = days.Where(x => x.Date <= today && x.FlexibleItems.HasValue).ToArray();
        int? flex = knownFlex.Length == 0 ? null : knownFlex.Sum(x => x.FlexibleItems!.Value);
        var flexOver = knownFlex.Any(x => x.FlexibleItems > 0 && MacroDayPlanner.Bands(
            x.ProteinTarget, x.CarbsTarget, x.FatTarget, x.Protein, x.Carbs, x.Fat).Any(b => b.Eaten > b.High));
        var flexQuality = flexOver || flex >= 5 ? 0m : flex >= 3 ? .6m : 1m;
        components.Add(new("Planned flexibility", 5m, 5m * flexQuality,
            flex is null ? "Not confirmed yet; no unreported items have been assumed."
            : flexOver ? "A day with flexible items is over a macro range. Review the combined intake."
            : $"{flex} reported items this week. 0–2 green; 3–4 yellow; 5+ red."));
        return new()
        {
            Score = (int)Math.Round(components.Sum(x => x.Earned), MidpointRounding.AwayFromZero),
            Components = components, FlexibleItems = flex, FlexibleDayOverMacros = flexOver,
            FlexibleConfirmedDays = knownFlex.Length,
            CompletedNutritionDays = days.Count(x => x.Date < today),
            KnownNutritionDays = days.Count(x => x.Date < today && x.Protein.HasValue && x.Carbs.HasValue && x.Fat.HasValue)
        };
    }

    public static decimal Quality(decimal? actual, decimal low, decimal high, bool unfinished)
    {
        if (!actual.HasValue || low <= 0m || high <= 0m) return 1m;
        if (actual > high) return Math.Clamp(high / actual.Value, 0m, 1m);
        if (unfinished || actual >= low) return 1m;
        return Math.Clamp(actual.Value / low, 0m, 1m);
    }
}
