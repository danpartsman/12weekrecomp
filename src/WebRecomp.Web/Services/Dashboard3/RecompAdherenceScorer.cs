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
    public const string Version = "RECOMP_SCORE_V9";
    public int Score { get; init; }
    public string Band => !HasEvidence ? "gray" : Score >= 80 ? "green" : Score >= 60 ? "yellow" : "red";
    public string Status => !HasEvidence ? "Awaiting completed evidence" : Score >= 80 ? "On course" : Score >= 60 ? "Build your momentum" : "Time to reset the plan";
    public List<RecompScoreComponent> Components { get; init; } = [];
    public bool HasEvidence => Components.Any(x => x.Included);
    public int? DisplayScore => HasEvidence ? Score : null;
    public int? NutritionScore => ComponentScore(Components.Take(3));
    public int? WorkoutScore => ComponentScore(Components.Where(x => x.Label is "Upper push" or "Upper pull" or "Legs" or "Cardio / 2G / 3G / HIIT" or "Rest day"));
    internal static int? ComponentScore(IEnumerable<RecompScoreComponent> components)
    {
        var parts = components.Where(x => x.Included).ToArray();
        var total = parts.Sum(x => x.Weight);
        return total <= 0 ? null : (int)Math.Round(parts.Sum(x => x.Earned) / total * 100m,
            MidpointRounding.AwayFromZero);
    }
    public int? OutcomeScore { get; init; }
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
        IReadOnlyList<RecompTrainingGauge> training, RecompGlycogenWork? glycogen = null,
        RecompBodyCompositionTrends? composition = null)
    {
        var components = new List<RecompScoreComponent>();
        var complete = days.Where(x => x.Date < today).ToArray();
        var protein = complete.Where(x => x.Protein.HasValue && x.ProteinTarget > 0).Select(x =>
            MacroDayPlanner.Bands(x.ProteinTarget, x.CarbsTarget, x.FatTarget, x.Protein, x.Carbs, x.Fat, x.EnergyTarget)[0]).ToArray();
        components.Add(new("Protein consistency", 25m, protein.Length == 0 ? 0m : 25m * protein.Average(x => x.Quality),
            "Protein green range is 80–100% of the configured upper end (approximately 0.8–1 g/lb when that upper end is 1 g/lb). Meal planning aims at 90%; no top-up to the upper end is required. Unknown days are excluded.", protein.Length > 0));
        // Carb adherence is assessed across completed days, not as a single-day fuel ceiling.
        // A modest deviation is tolerated, while sustained excess and under-fueling reduce points.
        var carbDays = complete.Where(x => x.Carbs.HasValue && x.CarbsTarget > 0).ToArray();
        var carbQuality = carbDays.Length == 0 ? 0m : carbDays.Average(x =>
        {
            var ratio = x.Carbs!.Value / x.CarbsTarget;
            // Product tolerance: +/-20% is full credit; beyond this, gradually reduce
            // credit instead of imposing an abrupt all-or-nothing deduction.
            var deviation = Math.Max(0m, Math.Abs(ratio - 1m) - .20m);
            return Math.Clamp(1m - deviation / .60m, 0m, 1m);
        });
        var carbOver = carbDays.Count(x => x.Carbs!.Value > x.CarbsTarget * 1.20m);
        var carbUnder = carbDays.Count(x => x.Carbs!.Value < x.CarbsTarget * .80m);
        components.Add(new("Rolling carbohydrate adherence", 20m, 20m * carbQuality,
            carbDays.Length == 0 ? "No completed carbohydrate logs: score excluded, not zero."
            : $"{carbDays.Length} recorded days: {carbOver} above the +20% planning band and {carbUnder} below the -20% planning band. A deviation beyond that band gradually reduces points. Review food choices and training demand; do not eat extra merely to reach a calorie number. This is a product adherence metric, not a validated glycogen or fee threshold.", carbDays.Length > 0));
        var fats = complete.Where(x => x.Fat.HasValue && x.EnergyTarget > 0).ToArray();
        var fatBand = fats.Length == 0 ? null : MacroDayPlanner.Bands(1, 1, fats.Average(x => x.FatTarget),
            actualFat: fats.Average(x => x.Fat!.Value), energy: fats.Average(x => x.EnergyTarget))[2];
        components.Add(new("Fat balance", 10m, fatBand is null ? 0m : fatBand.Quality * 10m,
            "Completed-period average fat is compared with 20–35% of the average prescribed energy allocation. This is broad adult guidance, not an exact individual requirement; isolated higher or lower days are not penalized separately.", fatBand is not null));
        foreach (var gauge in training)
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

        // Report observed combinations only; food names and motives cannot be inferred
        // from daily totals, so never claim that a specific meal caused an overage.
        var overlap = complete.Where(x => x.Protein.HasValue && x.Carbs.HasValue && x.Fat.HasValue
            && x.ProteinTarget > 0 && x.CarbsTarget > 0 && x.FatTarget > 0).ToArray();
        var highCarbHighFat = overlap.Count(x => x.Carbs!.Value > x.CarbsTarget * 1.20m
            && x.Fat!.Value > x.FatTarget * 1.10m);
        var lowProtein = overlap.Count(x => x.Protein!.Value < x.ProteinTarget * .80m);
        var lowProteinHighCarb = overlap.Count(x => x.Protein!.Value < x.ProteinTarget * .80m
            && x.Carbs!.Value > x.CarbsTarget * 1.20m);
        if (overlap.Length > 0)
        {
            var advice = new List<string>();
            if (highCarbHighFat > 0)
                advice.Add($"{highCarbHighFat} day(s) had both high carbohydrate and high fat; review logged portions and food choices");
            if (lowProtein > 0)
                advice.Add($"{lowProtein} day(s) were below the protein planning range; consider spreading protein through meals");
            if (lowProteinHighCarb > 0)
                advice.Add($"{lowProteinHighCarb} day(s) combined excess carbohydrate with below-range protein; consider protein-first meal planning without topping up calories");
            if (advice.Count > 0)
                components.Add(new("Nutrition pattern analysis", 0m, 0m,
                    string.Join(". ", advice) + ". These observations do not identify particular cheat meals or prove why choices were made.", false));
        }
        var knownFlex = days.Where(x => x.Date < today && x.FlexibleItems.HasValue).ToArray();
        int? flex = knownFlex.Length == 0 ? null : knownFlex.Sum(x => x.FlexibleItems!.Value);
        var flexOver = false;
        var flexPenalty = flex.HasValue ? FlexiblePenalty(flex.Value) : 0m;
        var flexEarned = Math.Max(0m, 5m - flexPenalty);
        components.Add(new("Planned flexibility", 5m, flexEarned,
            flex is null ? "Pending: not confirmed on a completed day; excluded from the score."
            : $"{flex} treat portions in the completed scoring period. First 2 free; portions 3–4 each cost 0.5 point; each additional portion costs 1 point. {flexPenalty:0.#} penalty points (5-point component floors at zero). Macro penalties are separate.", flex.HasValue));
        var outcomes = composition?.ThirtyDay;
        var fat = outcomes?.Fat;
        var muscle = outcomes?.Muscle;
        bool enoughFat = fat is not null && !fat.Sparse && fat.ChangeLb.HasValue;
        bool enoughMuscle = muscle is not null && !muscle.Sparse && muscle.ChangeLb.HasValue;
        // These are descriptive estimates from the scale, not confirmed changes in tissue.
        decimal OutcomePoints(RecompCompositionChange change) =>
            change.Band == "yellow" ? 5m : change.Band == "green" ? 10m : 0m;
        var outcomeEarned = (enoughFat ? OutcomePoints(fat!) : 0m)
            + (enoughMuscle ? OutcomePoints(muscle!) : 0m);
        var outcomeWeight = (enoughFat ? 10m : 0m) + (enoughMuscle ? 10m : 0m);
        components.Add(new("Body composition trend", 20m,
            outcomeWeight == 0 ? 0 : 20m * outcomeEarned / outcomeWeight,
            outcomeWeight == 0 ? "Insufficient comparable scale measurements: outcomes are unscored, not perfect."
            : $"30-day scale estimates: fat {fat?.DisplayChangeLb:+0.0;-0.0;0.0} lb; muscle {muscle?.DisplayChangeLb:+0.0;-0.0;0.0} lb. Small/noisy change earns neutral credit. This is not proof of tissue gain or loss.", outcomeWeight > 0));
        var nutritionParts = components.Take(3).Concat(components.Where(x => x.Label == "Planned flexibility")).ToArray();
        var workoutParts = components.Where(x => x.Label is "Upper push" or "Upper pull" or "Legs" or "Cardio / 2G / 3G / HIIT" or "Rest day").ToArray();
        var outcomeParts = components.Where(x => x.Label == "Body composition trend").ToArray();
        var groups = new[] { (Score: RecompScoreResult.ComponentScore(nutritionParts), Weight: 45m),
            (Score: RecompScoreResult.ComponentScore(workoutParts), Weight: 35m),
            (Score: RecompScoreResult.ComponentScore(outcomeParts), Weight: 20m) };
        var availableWeight = groups.Where(x => x.Score.HasValue).Sum(x => x.Weight);
        var combinedScore = availableWeight == 0 ? 0 :
            (int)Math.Round(groups.Where(x => x.Score.HasValue).Sum(x => x.Score!.Value * x.Weight)
                / availableWeight, MidpointRounding.AwayFromZero);
        return new()
        {
            Score = combinedScore,
            OutcomeScore = RecompScoreResult.ComponentScore(outcomeParts),
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
