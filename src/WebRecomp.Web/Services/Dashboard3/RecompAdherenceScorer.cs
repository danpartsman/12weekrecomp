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

public sealed record RecompGlycogenWork(decimal CompletedG, decimal TargetG, decimal RemainingPlannedG)
{
    public decimal ProjectedFraction => TargetG <= 0 ? 1m
        : Math.Clamp((CompletedG + RemainingPlannedG) / TargetG, 0m, 1m);
}

public sealed record RecompScoreComponent(string Label, decimal Weight, decimal Earned, string Explanation, bool Included = true);
public sealed class RecompScoreResult
{
    public const string Version = "RECOMP_SCORE_V3";
    public int Score { get; init; }
    public string Band => !HasEvidence ? "gray" : Score >= 80 ? "green" : Score >= 60 ? "yellow" : "red";
    public string Status => !HasEvidence ? "Awaiting completed evidence" : Score >= 80 ? "On course" : Score >= 60 ? "Build your momentum" : "Time to reset the plan";
    public List<RecompScoreComponent> Components { get; init; } = [];
    public bool HasEvidence => Components.Any(x => x.Included);
    public int? DisplayScore => HasEvidence ? Score : null;
    public int? NutritionScore => ComponentScore(Components.Take(3));
    public int? WorkoutScore => ComponentScore(Components.Skip(3).SkipLast(1));
    private static int? ComponentScore(IEnumerable<RecompScoreComponent> components)
    {
        var parts = components.Where(x => x.Included).ToArray();
        var total = parts.Sum(x => x.Weight);
        return total <= 0 ? null : (int)Math.Round(parts.Sum(x => x.Earned) / total * 100m,
            MidpointRounding.AwayFromZero);
    }
    public int CompletedNutritionDays { get; init; }
    public int KnownNutritionDays { get; init; }
    public int? FlexibleItems { get; init; }
    public int FlexibleConfirmedDays { get; init; }
    public bool FlexibleDayOverMacros { get; init; }
    public string FlexibleBand => !FlexibleItems.HasValue ? "gray" : FlexibleDayOverMacros || FlexibleItems >= 5 ? "red"
        : FlexibleItems >= 3 ? "yellow" : "green";
}

/// <summary>
/// Versioned product adherence rubric, not a physiological or outcome measurement.
/// Unknown or unfinished categories are excluded from the denominator. Nutrition is
/// scored as daily category hits/misses, using the same bands as the dashboard.
/// </summary>
public static class RecompAdherenceScorer
{
    public static RecompScoreResult Score(DateOnly today, IReadOnlyList<RecompDayEvidence> days,
        IReadOnlyList<RecompTrainingGauge> training, RecompGlycogenWork? glycogen = null)
    {
        var components = new List<RecompScoreComponent>();
        foreach (var (key, label, weight) in new[] { ("protein", "Protein consistency", 25m),
            ("carbs", "Training-day carbohydrate", 20m), ("fat", "Fat balance", 10m) })
        {
            var bands = days.Where(d => d.Date < today).Select(d =>
                MacroDayPlanner.Bands(d.ProteinTarget, d.CarbsTarget, d.FatTarget,
                    d.Protein, d.Carbs, d.Fat).Single(x => x.Key == key))
                .Where(b => b.Eaten.HasValue && b.Target > 0m).ToArray();
            var hits = bands.Count(b => Quality(b.Eaten, b.Low, b.High, false) == 1m);
            var included = bands.Length > 0;
            var fraction = included ? (decimal)hits / bands.Length : 0m;
            components.Add(new(label, weight, weight * fraction,
                included ? $"{hits}/{bands.Length} completed, known days within range. Each out-of-range day is a full miss for this category."
                    : "Pending: unfinished and unknown days are excluded from the score.", included));
        }
        foreach (var gauge in training)
        {
            var due = Math.Max(gauge.Completed, Math.Max(0, gauge.Target - gauge.RemainingPlanned));
            var included = due > 0;
            var fraction = included ? Math.Min(1m, (decimal)gauge.Completed / due) : 0m;
            var label = gauge.Label;
            var explanation = included
                ? $"{gauge.Completed}/{due} sessions completed against commitments due so far. Future sessions earn no points."
                : "Pending: no completed or due sessions in this category yet; excluded from the score.";
            if (gauge.Key == "resistance" && glycogen is not null)
            {
                var dueWork = Math.Max(glycogen.CompletedG, Math.Max(0m, glycogen.TargetG - glycogen.RemainingPlannedG));
                if (included && dueWork > 0m) fraction = Math.Min(fraction, glycogen.CompletedG / dueWork);
                label = "Glycogen work + resistance";
                explanation += $" Modeled work: {glycogen.CompletedG:0} g completed, {dueWork:0} g due so far. Extra burn cannot replace missed resistance.";
            }
            components.Add(new(label, gauge.Weight, gauge.Weight * fraction, explanation, included));
        }

        var knownFlex = days.Where(x => x.Date < today && x.FlexibleItems.HasValue).ToArray();
        int? flex = knownFlex.Length == 0 ? null : knownFlex.Sum(x => x.FlexibleItems!.Value);
        var flexOver = knownFlex.Any(x => x.FlexibleItems > 0 && MacroDayPlanner.Bands(
            x.ProteinTarget, x.CarbsTarget, x.FatTarget, x.Protein, x.Carbs, x.Fat).Any(b => b.Eaten > b.High));
        var flexQuality = flex is null || flexOver || flex >= 5 ? 0m : flex >= 3 ? .6m : 1m;
        components.Add(new("Planned flexibility", 5m, 5m * flexQuality,
            flex is null ? "Pending: not confirmed on a completed day; excluded from the score."
            : flexOver ? "A day with flexible items is over a macro range. Review the combined intake."
            : $"{flex} reported items across completed days. 0–2 green; 3–4 yellow; 5+ red.", flex.HasValue));
        return new()
        {
            Score = ComponentScore(components) ?? 0,
            Components = components, FlexibleItems = flex, FlexibleDayOverMacros = flexOver,
            FlexibleConfirmedDays = knownFlex.Length,
            CompletedNutritionDays = days.Count(x => x.Date < today),
            KnownNutritionDays = days.Count(x => x.Date < today && x.Protein.HasValue && x.Carbs.HasValue && x.Fat.HasValue)
        };
    }

    public static decimal Quality(decimal? actual, decimal low, decimal high, bool unfinished)
    {
        if (!actual.HasValue || unfinished || low <= 0m || high <= 0m) return 0m;
        return actual >= low && actual <= high ? 1m : 0m;
    }
}
