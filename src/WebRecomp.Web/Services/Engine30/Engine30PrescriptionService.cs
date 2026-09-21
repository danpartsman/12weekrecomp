namespace WebRecomp.Web.Services.Engine30;

public static class Engine30PrescriptionCategoryCodes
{
    public const string MajorStrength = "MAJOR_STRENGTH";
    public const string AccessoryStrength = "ACCESSORY_STRENGTH";
    public const string ConditioningCardio = "CONDITIONING_CARDIO";
    public const string RestRecoveryOther = "REST_RECOVERY_OTHER";
}

public static class Engine30BackgroundCarbohydrateDefaults
{
    /*
     * Adult background carbohydrate EAR.
     *
     * The Institute of Medicine / National Academies carbohydrate DRI uses
     * 100 g/day as the adult Estimated Average Requirement based on brain
     * glucose utilization. The 130 g/day RDA includes the population-variance
     * margin; Engine 3.0 is calculating an individual physiological floor, so
     * it uses the 100 g EAR as the background requirement.
     *
     * This is deliberately NOT labeled muscle-glycogen depletion. Ordinary
     * low-intensity living is substantially fat-fueled. Workout muscle
     * glycogen depletion is modeled separately and added explicitly below.
     */
    public const decimal AdultBackgroundCarbohydrateEarG = 100m;
}

public sealed record Engine30PrescriptionDayInput(
    DateOnly Date,
    decimal MaintenanceEnergyKcal,
    string WorkoutCategoryCode,
    decimal GlycogenReloadRequirementG);

public sealed record Engine30PrescriptionDayResult(
    DateOnly Date,
    string WorkoutCategoryCode,
    decimal CategoryWeight,
    decimal MaintenanceEnergyKcal,
    decimal PrescribedEnergyKcal,
    decimal ProteinG,
    decimal FatG,
    decimal PhysiologicalCarbFloorG,
    decimal EnergyBalancedCarbsG,
    decimal StandardCarbUpperBoundaryG,
    decimal RecommendedCarbsG,
    decimal MacroDerivedEnergyKcal,
    decimal MacroEnergyVarianceKcal,
    string CarbGoverningRuleCode);

public sealed record Engine30WeeklyPrescriptionResult(
    string PlanCode,
    decimal WeightLb,
    decimal Bmi,
    decimal RequestedDeficitPct,
    decimal EffectiveDeficitPct,
    decimal WeeklyMaintenanceEnergyKcal,
    decimal WeeklyDeficitKcal,
    decimal WeeklyPrescribedEnergyKcal,
    decimal AverageDailyPrescribedEnergyKcal,
    decimal StableProteinG,
    decimal StableFatG,
    decimal BaseRestCarbsG,
    IReadOnlyList<Engine30PrescriptionDayResult> Days,
    IReadOnlyList<string> ReasonCodes);

/// <summary>
/// Engine 3.0 physiology-first prescription layer.
///
/// Energy architecture remains authoritative for expenditure diagnostics, but
/// expenditure no longer back-fills carbohydrate. Nutrition is constructed in
/// physiological order:
///
///   1. Stable protein.
///   2. Stable weight-based fat target.
///   3. Adult background carbohydrate requirement.
///   4. Actual Engine 3.0 glycogen reload requirement for the day's workout.
///   5. Calories are the consequence of those macros.
///
/// Maintenance and plan deficit remain diagnostic guardrails. They do not
/// manufacture carbohydrate merely to make a calorie target reconcile.
/// </summary>
public sealed class Engine30PrescriptionService
{
    private const decimal PoundsPerKg = 2.2046226218m;

    public Engine30WeeklyPrescriptionResult Calculate(
        string planCode,
        decimal weightLb,
        decimal heightIn,
        IReadOnlyCollection<Engine30PrescriptionDayInput> days)
    {
        if (weightLb <= 0m)
            throw new ArgumentOutOfRangeException(nameof(weightLb));

        if (heightIn <= 0m)
            throw new ArgumentOutOfRangeException(nameof(heightIn));

        ArgumentNullException.ThrowIfNull(days);

        if (days.Count != 7)
        {
            throw new ArgumentException(
                "Engine 3.0 weekly prescription requires exactly seven days.",
                nameof(days));
        }

        if (days.Any(x => x.MaintenanceEnergyKcal <= 0m))
        {
            throw new ArgumentOutOfRangeException(
                nameof(days),
                "Every day requires positive maintenance energy.");
        }

        if (days.Any(x => x.GlycogenReloadRequirementG < 0m))
        {
            throw new ArgumentOutOfRangeException(
                nameof(days),
                "Glycogen reload cannot be negative.");
        }

        var normalizedPlan = NormalizePlan(planCode);
        var bmi = CalculateBmi(weightLb, heightIn);
        var requestedPct = PlanDeficitPct(normalizedPlan);

        var reasons = new List<string>
        {
            PlanReason(normalizedPlan),
            "PHYSIOLOGY_FIRST_CARBOHYDRATE",
            "BACKGROUND_CARB_EAR_100G",
            "CALORIES_DERIVED_FROM_MACROS_NOT_CARB_BACKFILLED"
        };

        if (bmi < 18.5m)
            reasons.Add("BMI_BELOW_DEFICIT_THRESHOLD_REVIEW");

        var weeklyMaintenance =
            days.Sum(x => x.MaintenanceEnergyKcal);

        var weightKg =
            weightLb / PoundsPerKg;

        var protein =
            RoundToIncrement(
                weightLb * 0.88m,
                5m);

        /*
         * Stable dietary fat remains a minimum weight-based target. It is not
         * used as a residual calorie bucket.
         */
        var fat =
            CeilingToIncrement(
                weightKg * 0.50m,
                5m);

        /*
         * Compatibility name: BaseRestCarbsG now means the physiological
         * background glucose/carbohydrate requirement, not a plan-owned carb
         * allowance. It is intentionally identical across plans.
         */
        var baseRestCarbs =
            Engine30BackgroundCarbohydrateDefaults
                .AdultBackgroundCarbohydrateEarG;

        var standardCarbUpper =
            RoundToIncrement(
                weightKg * 5.0m,
                5m);

        var results =
            new List<Engine30PrescriptionDayResult>(7);

        foreach (var day in days.OrderBy(x => x.Date))
        {
            var carbFloor =
                CeilingToIncrement(
                    baseRestCarbs
                    + day.GlycogenReloadRequirementG,
                    5m);

            /*
             * Do NOT use maintenance or a requested calorie target to fill
             * carbohydrate. The carbohydrate prescription is exactly the
             * background requirement plus calculated glycogen replacement.
             */
            var carbs = carbFloor;

            var macroKcal =
                Math.Round(
                    protein * 4m
                    + fat * 9m
                    + carbs * 4m,
                    0,
                    MidpointRounding.AwayFromZero);

            var governing =
                carbFloor > standardCarbUpper
                    ? "PHYSIOLOGICAL_FLOOR_EXCEEDS_STANDARD_MAX"
                    : "BACKGROUND_PLUS_GLYCOGEN_RELOAD";

            results.Add(
                new Engine30PrescriptionDayResult(
                    Date: day.Date,
                    WorkoutCategoryCode:
                        NormalizeCategory(day.WorkoutCategoryCode),
                    CategoryWeight:
                        CategoryWeight(day.WorkoutCategoryCode),
                    MaintenanceEnergyKcal:
                        Round(day.MaintenanceEnergyKcal, 2),
                    PrescribedEnergyKcal:
                        macroKcal,
                    ProteinG:
                        protein,
                    FatG:
                        fat,
                    PhysiologicalCarbFloorG:
                        carbFloor,
                    // Legacy diagnostic field retained for contract stability.
                    // It intentionally equals the physiology-governed target.
                    EnergyBalancedCarbsG:
                        carbs,
                    StandardCarbUpperBoundaryG:
                        standardCarbUpper,
                    RecommendedCarbsG:
                        carbs,
                    MacroDerivedEnergyKcal:
                        macroKcal,
                    MacroEnergyVarianceKcal:
                        0m,
                    CarbGoverningRuleCode:
                        governing));
        }

        var weeklyPrescribed =
            results.Sum(x => x.PrescribedEnergyKcal);

        var weeklyDeficit =
            weeklyMaintenance - weeklyPrescribed;

        var effectivePct =
            weeklyMaintenance > 0m
                ? weeklyDeficit / weeklyMaintenance
                : 0m;

        var absoluteDeficitCap = 500m * 7m;
        var percentageDeficitCap = weeklyMaintenance * 0.20m;
        var safetyDeficitCap = Math.Min(
            absoluteDeficitCap,
            percentageDeficitCap);

        if (weeklyDeficit > safetyDeficitCap + 0.001m)
        {
            reasons.Add(
                "MACRO_DERIVED_DEFICIT_EXCEEDS_EXISTING_SAFETY_GUARDRAIL_REVIEW");
        }

        if (weeklyPrescribed > weeklyMaintenance + 0.001m)
        {
            reasons.Add(
                "PHYSIOLOGICAL_MACROS_EXCEED_MAINTENANCE_REVIEW");
        }

        if (effectivePct > requestedPct + 0.001m)
        {
            reasons.Add(
                "MACRO_DERIVED_DEFICIT_EXCEEDS_PLAN_TARGET");
        }
        else if (effectivePct < requestedPct - 0.001m)
        {
            reasons.Add(
                "PHYSIOLOGICAL_MACROS_LIMIT_PLAN_DEFICIT");
        }

        return new Engine30WeeklyPrescriptionResult(
            PlanCode:
                normalizedPlan,
            WeightLb:
                Round(weightLb, 2),
            Bmi:
                Round(bmi, 2),
            RequestedDeficitPct:
                Round(requestedPct, 4),
            EffectiveDeficitPct:
                Round(effectivePct, 6),
            WeeklyMaintenanceEnergyKcal:
                Round(weeklyMaintenance, 2),
            WeeklyDeficitKcal:
                Round(weeklyDeficit, 2),
            WeeklyPrescribedEnergyKcal:
                weeklyPrescribed,
            AverageDailyPrescribedEnergyKcal:
                Round(weeklyPrescribed / 7m, 2),
            StableProteinG:
                protein,
            StableFatG:
                fat,
            BaseRestCarbsG:
                baseRestCarbs,
            Days:
                results,
            ReasonCodes:
                reasons
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
    }

    private static decimal CategoryWeight(
        string categoryCode)
        => NormalizeCategory(categoryCode) switch
        {
            Engine30PrescriptionCategoryCodes.MajorStrength
                => 1.10m,

            Engine30PrescriptionCategoryCodes.AccessoryStrength
                => 1.00m,

            Engine30PrescriptionCategoryCodes.ConditioningCardio
                => 0.95m,

            Engine30PrescriptionCategoryCodes.RestRecoveryOther
                => 0.80m,

            _ => throw new ArgumentOutOfRangeException(
                nameof(categoryCode))
        };

    private static string NormalizeCategory(
        string categoryCode)
    {
        if (string.IsNullOrWhiteSpace(categoryCode))
        {
            throw new ArgumentException(
                "Workout category is required.",
                nameof(categoryCode));
        }

        var normalized =
            categoryCode
                .Trim()
                .ToUpperInvariant();

        return normalized switch
        {
            Engine30PrescriptionCategoryCodes.MajorStrength
                => Engine30PrescriptionCategoryCodes.MajorStrength,
            Engine30PrescriptionCategoryCodes.AccessoryStrength
                => Engine30PrescriptionCategoryCodes.AccessoryStrength,
            Engine30PrescriptionCategoryCodes.ConditioningCardio
                => Engine30PrescriptionCategoryCodes.ConditioningCardio,
            Engine30PrescriptionCategoryCodes.RestRecoveryOther
                => Engine30PrescriptionCategoryCodes.RestRecoveryOther,
            _ => throw new ArgumentOutOfRangeException(
                nameof(categoryCode),
                $"Unsupported prescription category '{categoryCode}'.")
        };
    }

    private static string NormalizePlan(
        string planCode)
    {
        if (string.IsNullOrWhiteSpace(planCode))
        {
            throw new ArgumentException(
                "Plan is required.",
                nameof(planCode));
        }

        return planCode
            .Trim()
            .ToUpperInvariant();
    }

    private static decimal PlanDeficitPct(
        string planCode)
        => planCode switch
        {
            "PLAN_1" => 0.10m,
            "PLAN_2" => 0.15m,
            "PLAN_3" => 0.075m,
            "PLAN_4" => 0.10m,
            "PLAN_5" => 0.10m,

            _ => throw new ArgumentException(
                $"Unsupported Recomp plan '{planCode}'.",
                nameof(planCode))
        };

    private static string PlanReason(
        string planCode)
        => planCode switch
        {
            "PLAN_1" => "PLAN_HEALTH_RESET",
            "PLAN_2" => "PLAN_FAT_LOSS_RECOMP",
            "PLAN_3" => "PLAN_MUSCLE_RECOMP",
            "PLAN_4" => "PLAN_ATHLETIC_RECOMP",
            "PLAN_5" => "PLAN_PAIN_LIMITED",
            _ => "PLAN_UNKNOWN"
        };

    private static decimal CalculateBmi(
        decimal weightLb,
        decimal heightIn)
        =>
            weightLb
            / (heightIn * heightIn)
            * 703m;

    private static decimal RoundToIncrement(
        decimal value,
        decimal increment)
        =>
            increment <= 0m
                ? value
                : Math.Round(
                    value / increment,
                    0,
                    MidpointRounding.AwayFromZero)
                    * increment;

    private static decimal CeilingToIncrement(
        decimal value,
        decimal increment)
    {
        if (increment <= 0m)
            return value;

        return
            Math.Ceiling(
                value / increment)
            * increment;
    }

    private static decimal Round(
        decimal value,
        int decimals)
        =>
            Math.Round(
                value,
                decimals,
                MidpointRounding.AwayFromZero);
}
