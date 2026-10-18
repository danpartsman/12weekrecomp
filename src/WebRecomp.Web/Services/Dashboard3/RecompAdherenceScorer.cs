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
    public string Status => Target == 0 ? "Not in this period's plan" : Completed >= Target ? "Period goal reached"
        : ProjectedFraction < 1m ? "Behind plan · recoverable" : "Still on schedule";
}

public sealed record RecompGlycogenDay(DateOnly Date, decimal ActualG, decimal TargetG, bool Rest, bool HasWorkout)
{
    public decimal Quality => Rest ? 1m : !HasWorkout || ActualG < Math.Max(1m, TargetG * .10m) ? 0m
        : ActualG < TargetG ? .5m : 1m;
}
public sealed record RecompGlycogenWork(decimal CompletedG, decimal TargetG, decimal RemainingPlannedG,
    IReadOnlyList<RecompGlycogenDay>? Days = null)
{
    public decimal ProjectedFraction => TargetG <= 0 ? 1m
        : Math.Clamp((CompletedG + RemainingPlannedG) / TargetG, 0m, 1m);
}

public sealed record RecompScoreComponent(string Label, decimal Weight, decimal Earned, string Explanation, bool Included = true);
public sealed class RecompScoreResult
{
    public const string Version = "RECOMP_SCORE_V6";
    public int Score { get; init; }
    public string Band => !HasEvidence ? "gray" : Score >= 80 ? "green" : Score >= 60 ? "yellow" : "red";
    public string Status => !HasEvidence ? "Awaiting completed evidence" : Score >= 80 ? "On course" : Score >= 60 ? "Build your momentum" : "Time to reset the plan";
    public List<RecompScoreComponent> Components { get; init; } = [];
    public bool HasEvidence => Components.Any(x => x.Included);
    public int? DisplayScore => HasEvidence ? Score : null;
    public int? NutritionScore => ComponentScore(Components.Take(3));
    public int? WorkoutScore => ComponentScore(Components.Skip(3).SkipLast(1));
    internal static int? ComponentScore(IEnumerable<RecompScoreComponent> components)
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
    public string FlexibleBand => !FlexibleItems.HasValue ? "gray" : FlexibleItems >= 5 ? "red"
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
            var hits = bands.Count(b => b.Quality == 1m);
            var half = bands.Count(b => b.Quality == .5m);
            var included = bands.Length > 0;
            var fraction = included ? bands.Sum(b => b.Quality) / bands.Length : 0m;
            components.Add(new(label, weight, weight * fraction,
                included ? $"{hits}/{bands.Length} green days; {half} yellow days. Over-target: existing yellow/red penalties; under-target: yellow/red penalties are halved."
                    : "Pending: unfinished and unknown days are excluded from the score.", included));
        }
        foreach (var gauge in training)
        {
            var due = Math.Max(gauge.Completed, Math.Max(0, gauge.Target - gauge.RemainingPlanned));
            var included = due > 0;
            var missed = Math.Max(0, due - gauge.Completed);
            var fraction = included ? Math.Max(0m, 1m - .5m * missed / due) : 0m;
            var label = gauge.Label;
            var explanation = included
                ? $"{gauge.Completed}/{due} prescribed category days completed. Each missing day adds half a daily penalty. Future sessions earn no points."
                : "Pending: no completed or due sessions in this category yet; excluded from the score.";
            if (gauge.Key == "resistance" && glycogen is not null)
            {
                var dueWork = Math.Max(glycogen.CompletedG, Math.Max(0m, glycogen.TargetG - glycogen.RemainingPlannedG));
                var workDays = glycogen.Days?.Where(x => x.Date < today && !x.Rest).ToArray();
                if (workDays?.Length > 0)
                {
                    included = true;
                    fraction = Math.Max(0m, workDays.Average(x => x.Quality) - (due > 0 ? .5m * missed / due : 0m));
                }
                else if (included && dueWork > 0m)
                {
                    var workQuality = glycogen.CompletedG < Math.Max(1m, dueWork * .10m) ? 0m
                        : glycogen.CompletedG < dueWork ? .5m : 1m;
                    fraction = Math.Max(0m, workQuality - .5m * missed / due);
                }
                label = "Glycogen work + resistance";
                explanation += $" Modeled work: {glycogen.CompletedG:0} g completed, {dueWork:0} g due so far. No workout or burn below 10% of prescription: full daily penalty. Other below-prescription workouts: half. Rest days are exempt. Category shortfalls add half a daily penalty each.";
            }
            components.Add(new(label, gauge.Weight, gauge.Weight * fraction, explanation, included));
        }

        var knownFlex = days.Where(x => x.Date < today && x.FlexibleItems.HasValue).ToArray();
        int? flex = knownFlex.Length == 0 ? null : knownFlex.Sum(x => x.FlexibleItems!.Value);
        var flexOver = false;
        var flexPenalty = flex.HasValue ? FlexiblePenalty(flex.Value) : 0m;
        var flexEarned = Math.Max(0m, 5m - flexPenalty);
        components.Add(new("Planned flexibility", 5m, flexEarned,
            flex is null ? "Pending: not confirmed on a completed day; excluded from the score."
            : $"{flex} treat portions in the completed scoring period. First 2 free; portions 3–4 each cost 0.5 point; each additional portion costs 1 point. {flexPenalty:0.#} penalty points (5-point component floors at zero). Macro penalties are separate.", flex.HasValue));
        return new()
        {
            Score = RecompScoreResult.ComponentScore(components) ?? 0,
            Components = components, FlexibleItems = flex, FlexibleDayOverMacros = flexOver,
            FlexibleConfirmedDays = knownFlex.Length,
            CompletedNutritionDays = days.Count(x => x.Date < today),
            KnownNutritionDays = days.Count(x => x.Date < today && x.Protein.HasValue && x.Carbs.HasValue && x.Fat.HasValue)
        };
    }

    public static decimal FlexiblePenalty(int count) => Math.Min(2, Math.Max(0, count - 2)) * .5m + Math.Max(0, count - 4);

    public static decimal Quality(decimal? actual, decimal low, decimal high, bool unfinished)
    {
        if (!actual.HasValue || unfinished || low <= 0m || high <= 0m) return 0m;
        return actual >= low && actual <= high ? 1m : 0m;
    }
}
