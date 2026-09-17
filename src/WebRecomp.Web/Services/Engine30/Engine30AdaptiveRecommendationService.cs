namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// 12WR v1009 adaptive recommendation layer.
///
/// This service interprets already-calculated Engine 3.0 fuel/readiness,
/// energy-carryover, training-load, and current recovery evidence.
///
/// It does NOT:
/// - change energy expenditure math;
/// - change glycogen math;
/// - add calories for exercise;
/// - bank under-target calories;
/// - persist or rewrite the Weekly Plan.
///
/// Decision order:
/// 1. hard safety;
/// 2. rescue-before-downgrade for high-demand training;
/// 3. optional light week-balancing activity after a material prior overage;
/// 4. otherwise keep the plan.
///
/// A workout recommendation is advisory. The planned workout remains historical
/// truth until the user explicitly changes/records what they perform.
/// </summary>
public sealed record Engine30AdaptiveRecommendationInput(
    bool HasPlannedWorkout,
    string? PlannedWorkoutTypeCode,
    string PlannedWorkoutDisplayName,
    string PlannedWorkoutCategoryCode,
    decimal PlannedGlycogenDemandG,

    decimal PreWorkoutRescueG,
    string IncomingReadinessBandCode,
    string ProjectedReadinessBandCode,

    decimal? PreviousTargetCalories,
    decimal? PreviousEnergyVarianceKcal,
    decimal? PreviousEligibleEnergyCarryoverKcal,

    short FourDayHardDays,
    short FourDayModerateHardDays,

    decimal? SleepHours,
    byte? EnergyScore,
    byte? SorenessScore,
    bool PainFlag,
    bool IllnessFlag);

public static class Engine30AdaptiveDecisionCodes
{
    public const string KeepPlan = "KEEP_PLAN";
    public const string FuelRescueKeepPlan = "FUEL_RESCUE_KEEP_PLAN";
    public const string LowFuelDowngrade = "LOW_FUEL_DOWNGRADE";
    public const string SafetyRecovery = "SAFETY_RECOVERY";
    public const string AddLightActivityAfterOverage = "ADD_LIGHT_ACTIVITY_AFTER_OVERAGE";
    public const string PreserveRestForRecovery = "PRESERVE_REST_FOR_RECOVERY";
}

public static class Engine30AdaptiveNutritionActionCodes
{
    public const string FollowCurrentTarget = "FOLLOW_CURRENT_TARGET";
    public const string UsePreWorkoutCarbsWithinTarget = "USE_PREWORKOUT_CARBS_WITHIN_TARGET";
    public const string RecoveryCarbsKeepCurrentTarget = "RECOVERY_CARBS_KEEP_CURRENT_TARGET";
    public const string FollowCurrentTargetNoEatBack = "FOLLOW_CURRENT_TARGET_NO_EATBACK";
    public const string RecoveryPriority = "RECOVERY_PRIORITY";
}

public sealed record Engine30AdaptiveRecommendationResult(
    bool ShowOnDashboard,
    string DecisionCode,
    bool WorkoutChangeRecommended,

    string? PlannedWorkoutTypeCode,
    string PlannedWorkoutDisplayName,
    string? RecommendedWorkoutTypeCode,
    string RecommendedWorkoutDisplayName,
    decimal? RecommendedDurationMinutes,

    string NutritionActionCode,
    string Title,
    string Summary,
    string NutritionSummary,

    decimal? MaterialEnergyOverageKcal,
    decimal? MaterialEnergyOverageThresholdKcal,

    IReadOnlyList<string> ReasonCodes);

public sealed class Engine30AdaptiveRecommendationService
{
    public const decimal MinimumFixedMaterialOverageKcal = 200m;
    public const decimal MaterialOverageFractionOfTarget = 0.10m;
    public const decimal MinimumEligibleCarryoverForActivityKcal = 100m;
    public const decimal ActiveRecoveryMinutes = 35m;
    public const decimal FuelDowngradeZone2Minutes = 40m;

    public Engine30AdaptiveRecommendationResult Evaluate(
        Engine30AdaptiveRecommendationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var reasons =
            new List<string>();

        var plannedCode =
            Normalize(
                input.PlannedWorkoutTypeCode);

        var plannedName =
            string.IsNullOrWhiteSpace(
                input.PlannedWorkoutDisplayName)
                    ? plannedCode ?? "Rest day"
                    : input.PlannedWorkoutDisplayName.Trim();

        var isRest =
            !input.HasPlannedWorkout
            || plannedCode is null
            || plannedCode == "REST";

        // ------------------------------------------------------------
        // 1. Hard safety always wins.
        // ------------------------------------------------------------

        if (input.PainFlag || input.IllnessFlag)
        {
            if (input.PainFlag)
                reasons.Add("PAIN_SAFETY_OVERRIDE");

            if (input.IllnessFlag)
                reasons.Add("ILLNESS_SAFETY_OVERRIDE");

            return new Engine30AdaptiveRecommendationResult(
                ShowOnDashboard:
                    true,
                DecisionCode:
                    Engine30AdaptiveDecisionCodes.SafetyRecovery,
                WorkoutChangeRecommended:
                    !isRest,
                PlannedWorkoutTypeCode:
                    plannedCode,
                PlannedWorkoutDisplayName:
                    plannedName,
                RecommendedWorkoutTypeCode:
                    "REST",
                RecommendedWorkoutDisplayName:
                    "Rest / Recovery",
                RecommendedDurationMinutes:
                    null,
                NutritionActionCode:
                    Engine30AdaptiveNutritionActionCodes.RecoveryPriority,
                Title:
                    "Recovery takes priority today",
                Summary:
                    "A pain/injury or illness flag is active, so Engine 3.0 recommends recovery instead of adding or forcing training.",
                NutritionSummary:
                    "Follow the current nutrition target unless your clinician or coach advises otherwise. Do not use exercise as calorie compensation.",
                MaterialEnergyOverageKcal:
                    Positive(
                        input.PreviousEnergyVarianceKcal),
                MaterialEnergyOverageThresholdKcal:
                    MaterialOverageThreshold(
                        input.PreviousTargetCalories),
                ReasonCodes:
                    reasons);
        }

        var highDemand =
            IsHighDemand(
                plannedCode,
                input.PlannedWorkoutCategoryCode,
                input.PlannedGlycogenDemandG);

        var projectedLow =
            IsLowOrVeryLow(
                input.ProjectedReadinessBandCode);

        var rescueUsed =
            input.PreWorkoutRescueG > 0m;

        var rescueReachedAdequate =
            IsAdequate(
                input.ProjectedReadinessBandCode);

        // ------------------------------------------------------------
        // 2. Rescue before downgrade.
        //
        // If today's bounded carb rescue brings a hard session to ADEQUATE,
        // preserve the planned workout.
        // ------------------------------------------------------------

        if (highDemand
            && rescueUsed
            && rescueReachedAdequate)
        {
            reasons.Add("FUEL_RESCUE_SUFFICIENT");
            reasons.Add("PLANNED_HARD_WORKOUT_PRESERVED");

            return new Engine30AdaptiveRecommendationResult(
                ShowOnDashboard:
                    true,
                DecisionCode:
                    Engine30AdaptiveDecisionCodes.FuelRescueKeepPlan,
                WorkoutChangeRecommended:
                    false,
                PlannedWorkoutTypeCode:
                    plannedCode,
                PlannedWorkoutDisplayName:
                    plannedName,
                RecommendedWorkoutTypeCode:
                    plannedCode,
                RecommendedWorkoutDisplayName:
                    plannedName,
                RecommendedDurationMinutes:
                    null,
                NutritionActionCode:
                    Engine30AdaptiveNutritionActionCodes.UsePreWorkoutCarbsWithinTarget,
                Title:
                    "Fuel first, then keep the plan",
                Summary:
                    $"Fuel was limited coming into {plannedName}, but the modeled {input.PreWorkoutRescueG:0} g pre-workout carbohydrate allocation raises projected readiness to adequate.",
                NutritionSummary:
                    $"Use about {input.PreWorkoutRescueG:0} g of today's already-prescribed carbohydrate before training. This is timing inside today's target, not extra calories.",
                MaterialEnergyOverageKcal:
                    Positive(
                        input.PreviousEnergyVarianceKcal),
                MaterialEnergyOverageThresholdKcal:
                    MaterialOverageThreshold(
                        input.PreviousTargetCalories),
                ReasonCodes:
                    reasons);
        }

        // ------------------------------------------------------------
        // 3. Persistent LOW/VERY_LOW after rescue: reduce demand.
        //
        // Important nutrition behavior:
        // keep the current day's recovery carbohydrate prescription.
        // A lower workout demand is not used as an excuse to remove the
        // carbohydrate needed to repair the existing historical shortfall.
        // ------------------------------------------------------------

        if (highDemand
            && projectedLow)
        {
            reasons.Add("LOW_FUEL_AFTER_RESCUE");
            reasons.Add("TRAINING_DEMAND_REDUCED");
            reasons.Add("RECOVERY_CARBOHYDRATE_PRESERVED");

            return new Engine30AdaptiveRecommendationResult(
                ShowOnDashboard:
                    true,
                DecisionCode:
                    Engine30AdaptiveDecisionCodes.LowFuelDowngrade,
                WorkoutChangeRecommended:
                    true,
                PlannedWorkoutTypeCode:
                    plannedCode,
                PlannedWorkoutDisplayName:
                    plannedName,
                RecommendedWorkoutTypeCode:
                    "ZONE2_CARDIO",
                RecommendedWorkoutDisplayName:
                    "Zone 2 Cardio",
                RecommendedDurationMinutes:
                    FuelDowngradeZone2Minutes,
                NutritionActionCode:
                    Engine30AdaptiveNutritionActionCodes.RecoveryCarbsKeepCurrentTarget,
                Title:
                    "Reduce today's training demand",
                Summary:
                    $"Projected fuel readiness remains {Humanize(input.ProjectedReadinessBandCode).ToLowerInvariant()} even after the practical pre-workout allocation. Use about {FuelDowngradeZone2Minutes:0} minutes of Zone 2 instead of forcing {plannedName}.",
                NutritionSummary:
                    input.PreWorkoutRescueG > 0m
                        ? $"Keep today's Engine 3.0 nutrition target and use about {input.PreWorkoutRescueG:0} g of the prescribed carbohydrate before activity if practical. The remaining carbohydrate supports recovery; do not add calories for the Zone 2 session."
                        : "Keep today's Engine 3.0 nutrition target so the historical fuel shortfall can recover. Do not add exercise calories or cut recovery carbohydrate because the workout was downgraded.",
                MaterialEnergyOverageKcal:
                    Positive(
                        input.PreviousEnergyVarianceKcal),
                MaterialEnergyOverageThresholdKcal:
                    MaterialOverageThreshold(
                        input.PreviousTargetCalories),
                ReasonCodes:
                    reasons);
        }

        // ------------------------------------------------------------
        // 4. Material prior overage going into a true REST day.
        //
        // Activity is deliberately light and only recommended when today's
        // recovery evidence does not argue for preserving full rest.
        // ------------------------------------------------------------

        if (isRest
            && IsMaterialPriorOverage(
                input,
                out var overage,
                out var threshold))
        {
            reasons.Add("MATERIAL_PRIOR_ENERGY_OVERAGE");

            if (RecoverySupportsOptionalLightActivity(
                    input,
                    reasons))
            {
                reasons.Add("OPTIONAL_WEEK_BALANCING_ACTIVITY");
                reasons.Add("NO_EXERCISE_CALORIE_EATBACK");

                return new Engine30AdaptiveRecommendationResult(
                    ShowOnDashboard:
                        true,
                    DecisionCode:
                        Engine30AdaptiveDecisionCodes.AddLightActivityAfterOverage,
                    WorkoutChangeRecommended:
                        true,
                    PlannedWorkoutTypeCode:
                        plannedCode,
                    PlannedWorkoutDisplayName:
                        plannedName,
                    RecommendedWorkoutTypeCode:
                        "WALKING",
                    RecommendedWorkoutDisplayName:
                        "Active Recovery Walk / Easy Zone 2",
                    RecommendedDurationMinutes:
                        ActiveRecoveryMinutes,
                    NutritionActionCode:
                        Engine30AdaptiveNutritionActionCodes.FollowCurrentTargetNoEatBack,
                    Title:
                        "A little extra movement fits today",
                    Summary:
                        $"Yesterday finished about {overage:0} kcal above target. Because today is a rest day and current recovery/load signals do not require complete rest, add about {ActiveRecoveryMinutes:0} minutes of easy walking or equivalent active recovery.",
                    NutritionSummary:
                        "Keep today's existing Engine 3.0 nutrition/carryover target. Do not eat back the activity and do not try to burn off yesterday calorie-for-calorie.",
                    MaterialEnergyOverageKcal:
                        overage,
                    MaterialEnergyOverageThresholdKcal:
                        threshold,
                    ReasonCodes:
                        reasons);
            }

            reasons.Add("REST_DAY_RECOVERY_PRESERVED");

            return new Engine30AdaptiveRecommendationResult(
                ShowOnDashboard:
                    true,
                DecisionCode:
                    Engine30AdaptiveDecisionCodes.PreserveRestForRecovery,
                WorkoutChangeRecommended:
                    false,
                PlannedWorkoutTypeCode:
                    plannedCode,
                PlannedWorkoutDisplayName:
                    plannedName,
                RecommendedWorkoutTypeCode:
                    plannedCode ?? "REST",
                RecommendedWorkoutDisplayName:
                    "Rest / Recovery",
                RecommendedDurationMinutes:
                    null,
                NutritionActionCode:
                    Engine30AdaptiveNutritionActionCodes.FollowCurrentTarget,
                Title:
                    "Keep the rest day",
                Summary:
                    $"Yesterday was about {overage:0} kcal above target, but recovery/training-load evidence says the rest day is more valuable than adding activity.",
                NutritionSummary:
                    "Use the existing bounded Engine 3.0 energy-carryover target. Do not add a punishment workout or attempt calorie-for-calorie compensation.",
                MaterialEnergyOverageKcal:
                    overage,
                MaterialEnergyOverageThresholdKcal:
                    threshold,
                ReasonCodes:
                    reasons);
        }

        // ------------------------------------------------------------
        // 5. No adaptive change.
        // ------------------------------------------------------------

        return new Engine30AdaptiveRecommendationResult(
            ShowOnDashboard:
                false,
            DecisionCode:
                Engine30AdaptiveDecisionCodes.KeepPlan,
            WorkoutChangeRecommended:
                false,
            PlannedWorkoutTypeCode:
                plannedCode,
            PlannedWorkoutDisplayName:
                plannedName,
            RecommendedWorkoutTypeCode:
                plannedCode,
            RecommendedWorkoutDisplayName:
                plannedName,
            RecommendedDurationMinutes:
                null,
            NutritionActionCode:
                Engine30AdaptiveNutritionActionCodes.FollowCurrentTarget,
            Title:
                "Follow today's plan",
            Summary:
                "No adaptive workout change is indicated by current Engine 3.0 fuel, energy, load, or safety evidence.",
            NutritionSummary:
                "Follow today's Engine 3.0 nutrition target.",
            MaterialEnergyOverageKcal:
                Positive(
                    input.PreviousEnergyVarianceKcal),
            MaterialEnergyOverageThresholdKcal:
                MaterialOverageThreshold(
                    input.PreviousTargetCalories),
            ReasonCodes:
                reasons);
    }

    private static bool IsHighDemand(
        string? workoutCode,
        string categoryCode,
        decimal glycogenDemandG)
    {
        if (string.Equals(
                categoryCode,
                "MAJOR_STRENGTH",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (glycogenDemandG >= 75m)
            return true;

        return workoutCode is
            "TRUE_HIIT"
            or "RUNNING_INTERVALS"
            or "CYCLING_INTERVALS"
            or "ROWING_INTERVALS"
            or "MIXED_CONDITIONING"
            or "ORANGETHEORY"
            or "MIXED_GYM_SESSION"
            or "CUSTOM_COMPOSITE";
    }

    private static bool RecoverySupportsOptionalLightActivity(
        Engine30AdaptiveRecommendationInput input,
        List<string> reasons)
    {
        var blocked =
            false;

        if (input.SleepHours is decimal sleep
            && sleep < 5.5m)
        {
            reasons.Add("LOW_SLEEP_PRESERVES_REST");
            blocked = true;
        }

        if (input.EnergyScore is byte energy
            && energy <= 3)
        {
            reasons.Add("LOW_ENERGY_PRESERVES_REST");
            blocked = true;
        }

        if (input.SorenessScore is byte soreness
            && soreness >= 8)
        {
            reasons.Add("HIGH_SORENESS_PRESERVES_REST");
            blocked = true;
        }

        if (input.FourDayHardDays >= 3)
        {
            reasons.Add("RECENT_HARD_LOAD_PRESERVES_REST");
            blocked = true;
        }
        else if (input.FourDayModerateHardDays >= 4)
        {
            reasons.Add("RECENT_MODERATE_HARD_LOAD_PRESERVES_REST");
            blocked = true;
        }

        return !blocked;
    }

    private static bool IsMaterialPriorOverage(
        Engine30AdaptiveRecommendationInput input,
        out decimal overage,
        out decimal threshold)
    {
        overage =
            Positive(
                input.PreviousEnergyVarianceKcal)
            ?? 0m;

        threshold =
            MaterialOverageThreshold(
                input.PreviousTargetCalories);

        var eligible =
            Positive(
                input.PreviousEligibleEnergyCarryoverKcal)
            ?? 0m;

        return overage >= threshold
            && eligible >= MinimumEligibleCarryoverForActivityKcal;
    }

    private static decimal MaterialOverageThreshold(
        decimal? priorTargetCalories)
    {
        var proportional =
            priorTargetCalories is > 0m
                ? priorTargetCalories.Value
                  * MaterialOverageFractionOfTarget
                : 0m;

        return Math.Max(
            MinimumFixedMaterialOverageKcal,
            proportional);
    }

    private static decimal? Positive(
        decimal? value)
        => value is > 0m
            ? value
            : null;

    private static string? Normalize(
        string? code)
        => string.IsNullOrWhiteSpace(code)
            ? null
            : code.Trim().ToUpperInvariant();

    private static bool IsAdequate(
        string code)
        => string.Equals(
            Normalize(code),
            "ADEQUATE",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsLowOrVeryLow(
        string code)
    {
        var normalized =
            Normalize(code);

        return normalized is
            "LOW"
            or "VERY_LOW";
    }

    private static string Humanize(
        string code)
        => (Normalize(code) ?? "UNKNOWN")
            .Replace(
                "_",
                " ");
}