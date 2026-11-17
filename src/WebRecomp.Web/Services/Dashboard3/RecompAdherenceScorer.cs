using WebRecomp.Web.Services.Dashboard2;

namespace WebRecomp.Web.Services.Dashboard3;

public sealed record RecompDayEvidence(
    DateOnly Date, decimal ProteinTarget, decimal CarbsTarget, decimal FatTarget,
    decimal EnergyTarget, decimal? Protein, decimal? Carbs, decimal? Fat,
    decimal? Energy, int? FlexibleItems, string TargetSource)
{
    // Financial assessment retains the baseline prescription, never an experimental fuel correction.
    public decimal? AgreementCarbsTarget { get; init; }
}

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
    public decimal? CompletedEnergyKcal { get; init; }
    public decimal? TargetEnergyKcal { get; init; }
    public decimal ProjectedFraction => TargetG <= 0 ? 1m
        : Math.Clamp((CompletedG + RemainingPlannedG) / TargetG, 0m, 1m);
}

public sealed record RecompScoreComponent(string Label, decimal Weight, decimal Earned, string Explanation, bool Included = true);
public sealed class RecompScoreResult
{
    public const string Version = "RECOMP_SCORE_V8";
    public int Score { get; init; }
    public string Band => !HasEvidence ? "gray" : Score >= 80 ? "green" : Score >= 60 ? "yellow" : "red";
    public string Status => !HasEvidence ? "Awaiting completed evidence" : Score >= 80 ? "On course" : Score >= 60 ? "Build your momentum" : "Time to reset the plan";
    public List<RecompScoreComponent> Components { get; init; } = [];
    public bool HasEvidence => Components.Any(x => x.Included);
    public int? DisplayScore => HasEvidence ? Score : null;
    public int? NutritionScore => ComponentScore(Components.Take(3));
    public RecompWorkoutReview? WorkoutReview { get; init; }
    public int? NutritionBaseScore { get; init; }
    public int? WorkoutScore => WorkoutReview is not null ? WorkoutReview.Score : ComponentScore(Components.Skip(3).SkipLast(1));
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
        IReadOnlyList<RecompTrainingGauge> training, RecompGlycogenWork? glycogen = null, RecompWorkoutReview? workoutReview = null)
    {
        var components = new List<RecompScoreComponent>();
        var complete = days.Where(x => x.Date < today).ToArray();
        var protein = complete.Where(x => x.Protein.HasValue && x.ProteinTarget > 0).Select(x =>
            MacroDayPlanner.Bands(x.ProteinTarget, x.CarbsTarget, x.FatTarget, x.Protein, x.Carbs, x.Fat, x.EnergyTarget)[0]).ToArray();
        components.Add(new("Protein consistency", 25m, protein.Length == 0 ? 0m : 25m * protein.Average(x => x.Quality),
            "Protein green range is 80–100% of the configured upper end (approximately 0.8–1 g/lb when that upper end is 1 g/lb). Meal planning aims at 90%; no top-up to the upper end is required. Unknown days are excluded.", protein.Length > 0));
        components.Add(new("Rolling carbohydrate context", 20m, 0m,
            "Daily carbohydrate deviations do not deduct points. The experimental four-day glycogen range informs bounded recommendations, not a validated compliance or financial threshold. Review sustained energy intake separately.", false));
        var fats = complete.Where(x => x.Fat.HasValue && x.EnergyTarget > 0).ToArray();
        var fatBand = fats.Length == 0 ? null : MacroDayPlanner.Bands(1, 1, fats.Average(x => x.FatTarget),
            actualFat: fats.Average(x => x.Fat!.Value), energy: fats.Average(x => x.EnergyTarget))[2];
        components.Add(new("Fat balance", 10m, fatBand is null ? 0m : fatBand.Quality * 10m,
            "Completed-period average fat is compared with 20–35% of the average prescribed energy allocation. This is broad adult guidance, not an exact individual requirement; isolated higher or lower days are not penalized separately.", fatBand is not null));
        if (workoutReview is not null)
        {
            foreach (var requirement in workoutReview.Requirements)
                components.Add(new(requirement.Label, 5m, requirement.Met ? 5m : 0m, requirement.Evidence + ". " + requirement.NextAction, workoutReview.FullWindow));
        }
        else foreach (var gauge in training)
        {
            var due = Math.Max(0, gauge.Target - gauge.RemainingPlanned);
            var included = due > 0 || gauge.Completed > 0;
            var fraction = due <= 0 ? 1m : Math.Min(1m, (decimal)gauge.Completed / due);
            var label = gauge.Label;
            var explanation = $"{gauge.Completed}/{due} completed-period goal. Comparable substitutions and moved sessions are allowed; extra work adds no bonus points.";
            if (gauge.Key == "resistance" && glycogen is not null)
            {
                var target = Math.Max(0, glycogen.TargetG - glycogen.RemainingPlannedG);
                included = target > 0;
                fraction = target <= 0 ? 1m : Math.Min(1m, glycogen.CompletedG / target);
                if (glycogen.TargetEnergyKcal is > 0m && glycogen.CompletedEnergyKcal.HasValue)
                    fraction = Math.Min(fraction, Math.Min(1m, glycogen.CompletedEnergyKcal.Value / glycogen.TargetEnergyKcal.Value));
                label = "Weekly workload";
                explanation = $"{glycogen.CompletedG:0}/{target:0} g modeled glycogen work across the completed period. Calorie work is also checked when available. No day-by-day matching or workout-name penalty. Resistance coverage is checked separately; calorie burn alone cannot replace muscle-building work.";
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
        var nutritionBase = RecompScoreResult.ComponentScore(components.Take(3).Append(components.Last()));
        return new()
        {
            WorkoutReview = workoutReview, NutritionBaseScore = workoutReview is null ? null : nutritionBase,
            Score = workoutReview is null ? RecompScoreResult.ComponentScore(components) ?? 0
                : Math.Max(0, (nutritionBase ?? workoutReview.Score ?? 0) - (nutritionBase.HasValue ? workoutReview.Deductions : 0)),
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
