namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Converts the baseline Engine 3.0 weekly prescription into a goal-specific
/// RECOMP prescription.
///
/// The baseline weekly energy budget remains authoritative unless either:
/// - physiological macro floors, or
/// - administrator-configured category minimums
/// require more energy.
///
/// The allocator starts every day at the stronger of:
/// - its macro-energy floor, and
/// - the category minimum maintenance fraction.
///
/// Remaining weekly calories are then spent in descending protection priority:
/// first toward each category TARGET, then toward each category MAXIMUM.
///
/// This deliberately makes important Major Strength days calorie-protected
/// rather than merely giving them a relative 1.10 weighting.
/// </summary>
public sealed class Engine30RecompEnergyPolicyAllocator
{
    private const decimal EnergyIncrementKcal = 10m;
    private const decimal CarbIncrementG = 5m;

    public Engine30WeeklyPrescriptionResult Apply(
        Engine30WeeklyPrescriptionResult baseline,
        Engine30GoalEnergyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(policy);

        Engine30GoalEnergyPolicyService.Validate(
            policy);

        var normalizedPlan =
            NormalizePlan(
                baseline.PlanCode);

        if (!policy.PlanCode.Equals(
                normalizedPlan,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Energy policy '{policy.PlanCode}' does not match prescription plan '{normalizedPlan}'.");
        }

        if (baseline.Days.Count != 7)
        {
            throw new ArgumentException(
                "Engine 3.0 recomp policy allocation requires exactly seven prescription days.");
        }

        var reasons =
            baseline.ReasonCodes.ToList();

        reasons.Add(
            "ADMIN_GOAL_CATEGORY_ENERGY_POLICY_APPLIED");

        var rows =
            baseline.Days
                .Select((day, index) =>
                {
                    var category =
                        policy.Category(
                            day.WorkoutCategoryCode);

                    var macroFloorKcal =
                        CeilingToIncrement(
                            baseline.StableProteinG * 4m
                            + baseline.StableFatG * 9m
                            + day.PhysiologicalCarbFloorG * 4m,
                            EnergyIncrementKcal);

                    var categoryMinimum =
                        CeilingToIncrement(
                            day.MaintenanceEnergyKcal
                            * category.MinimumMaintenanceFraction,
                            EnergyIncrementKcal);

                    var minimum =
                        Math.Max(
                            macroFloorKcal,
                            categoryMinimum);

                    var target =
                        RoundToIncrement(
                            day.MaintenanceEnergyKcal
                            * category.TargetMaintenanceFraction,
                            EnergyIncrementKcal);

                    var maximum =
                        RoundToIncrement(
                            day.MaintenanceEnergyKcal
                            * category.MaximumMaintenanceFraction,
                            EnergyIncrementKcal);

                    target =
                        Math.Clamp(
                            target,
                            minimum,
                            Math.Max(
                                minimum,
                                maximum));

                    maximum =
                        Math.Max(
                            maximum,
                            target);

                    return new Row(
                        Index:
                            index,
                        Day:
                            day,
                        Policy:
                            category,
                        Minimum:
                            minimum,
                        Target:
                            target,
                        Maximum:
                            maximum);
                })
                .ToArray();

        var weeklyTarget =
            baseline.WeeklyPrescribedEnergyKcal;

        var minimumWeekly =
            rows.Sum(x => x.Minimum);

        if (minimumWeekly > weeklyTarget)
        {
            weeklyTarget =
                minimumWeekly;

            reasons.Add(
                "DEFICIT_REDUCED_TO_PROTECT_CATEGORY_ENERGY_FLOORS");
        }

        var allocated =
            rows
                .Select(x => x.Minimum)
                .ToArray();

        var remaining =
            weeklyTarget
            - allocated.Sum();

        remaining =
            FillInPriorityOrder(
                rows,
                allocated,
                remaining,
                x => x.Target);

        remaining =
            FillInPriorityOrder(
                rows,
                allocated,
                remaining,
                x => x.Maximum);

        if (remaining > 0m)
        {
            // A configuration can theoretically set maximums too low to hold
            // the already-approved weekly energy budget. We never throw away
            // legitimate prescribed energy. Put the unavoidable remainder on
            // the highest-protection day and make the relaxation auditable.
            var highestPriority =
                rows
                    .OrderByDescending(
                        x => x.Policy.ProtectionPriority)
                    .ThenByDescending(
                        x => x.Day.MaintenanceEnergyKcal)
                    .ThenBy(x => x.Index)
                    .First();

            allocated[highestPriority.Index] +=
                remaining;

            remaining = 0m;

            reasons.Add(
                "CATEGORY_MAX_RELAXED_TO_PRESERVE_WEEKLY_ENERGY");
        }

        var results =
            new List<Engine30PrescriptionDayResult>(
                rows.Length);

        foreach (var row in rows)
        {
            var day =
                row.Day;

            var energyTarget =
                allocated[row.Index];

            var fixedKcal =
                baseline.StableProteinG * 4m
                + baseline.StableFatG * 9m;

            var energyBalancedCarbs =
                RoundToIncrement(
                    Math.Max(
                        0m,
                        (energyTarget - fixedKcal) / 4m),
                    CarbIncrementG);

            decimal carbs;
            string governing;

            if (
                day.PhysiologicalCarbFloorG
                > day.StandardCarbUpperBoundaryG
            ) {
                carbs =
                    day.PhysiologicalCarbFloorG;

                governing =
                    "PHYSIOLOGICAL_FLOOR_EXCEEDS_STANDARD_MAX";
            }
            else if (
                energyBalancedCarbs
                < day.PhysiologicalCarbFloorG
            ) {
                carbs =
                    day.PhysiologicalCarbFloorG;

                governing =
                    "PHYSIOLOGICAL_FLOOR";
            }
            else if (
                energyBalancedCarbs
                > day.StandardCarbUpperBoundaryG
            ) {
                carbs =
                    day.StandardCarbUpperBoundaryG;

                governing =
                    "STANDARD_USEFUL_MAX";
            }
            else
            {
                carbs =
                    energyBalancedCarbs;

                governing =
                    "ENERGY_BALANCE";
            }

            var macroKcal =
                Math.Round(
                    baseline.StableProteinG * 4m
                    + carbs * 4m
                    + baseline.StableFatG * 9m,
                    0,
                    MidpointRounding.AwayFromZero);

            results.Add(
                new Engine30PrescriptionDayResult(
                    Date:
                        day.Date,
                    WorkoutCategoryCode:
                        NormalizeCategory(
                            day.WorkoutCategoryCode),
                    CategoryWeight:
                        day.CategoryWeight,
                    MaintenanceEnergyKcal:
                        day.MaintenanceEnergyKcal,
                    PrescribedEnergyKcal:
                        energyTarget,
                    ProteinG:
                        baseline.StableProteinG,
                    FatG:
                        baseline.StableFatG,
                    PhysiologicalCarbFloorG:
                        day.PhysiologicalCarbFloorG,
                    EnergyBalancedCarbsG:
                        energyBalancedCarbs,
                    StandardCarbUpperBoundaryG:
                        day.StandardCarbUpperBoundaryG,
                    RecommendedCarbsG:
                        carbs,
                    MacroDerivedEnergyKcal:
                        macroKcal,
                    MacroEnergyVarianceKcal:
                        Math.Round(
                            macroKcal - energyTarget,
                            2,
                            MidpointRounding.AwayFromZero),
                    CarbGoverningRuleCode:
                        governing));
        }

        var weeklyMaintenance =
            baseline.WeeklyMaintenanceEnergyKcal;

        var effectivePct =
            weeklyMaintenance > 0m
                ? (weeklyMaintenance - weeklyTarget)
                    / weeklyMaintenance
                : 0m;

        return new Engine30WeeklyPrescriptionResult(
            PlanCode:
                baseline.PlanCode,
            WeightLb:
                baseline.WeightLb,
            Bmi:
                baseline.Bmi,
            RequestedDeficitPct:
                baseline.RequestedDeficitPct,
            EffectiveDeficitPct:
                Math.Round(
                    effectivePct,
                    6,
                    MidpointRounding.AwayFromZero),
            WeeklyMaintenanceEnergyKcal:
                weeklyMaintenance,
            WeeklyDeficitKcal:
                Math.Round(
                    weeklyMaintenance - weeklyTarget,
                    2,
                    MidpointRounding.AwayFromZero),
            WeeklyPrescribedEnergyKcal:
                weeklyTarget,
            AverageDailyPrescribedEnergyKcal:
                Math.Round(
                    weeklyTarget / 7m,
                    2,
                    MidpointRounding.AwayFromZero),
            StableProteinG:
                baseline.StableProteinG,
            StableFatG:
                baseline.StableFatG,
            BaseRestCarbsG:
                baseline.BaseRestCarbsG,
            Days:
                results,
            ReasonCodes:
                reasons
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray());
    }

    private static decimal FillInPriorityOrder(
        IReadOnlyCollection<Row> rows,
        decimal[] allocated,
        decimal remaining,
        Func<Row, decimal> capSelector)
    {
        if (remaining <= 0m)
            return 0m;

        foreach (
            var row in
            rows
                .OrderByDescending(
                    x => x.Policy.ProtectionPriority)
                .ThenByDescending(
                    x => x.Day.MaintenanceEnergyKcal)
                .ThenBy(x => x.Index)
        ) {
            if (remaining <= 0m)
                break;

            var cap =
                capSelector(row);

            var headroom =
                Math.Max(
                    0m,
                    cap - allocated[row.Index]);

            if (headroom <= 0m)
                continue;

            var add =
                Math.Min(
                    remaining,
                    headroom);

            allocated[row.Index] +=
                add;

            remaining -=
                add;
        }

        return remaining;
    }

    public static string NormalizeCategory(
        string categoryCode)
        => (categoryCode ?? "")
            .Trim()
            .ToUpperInvariant() switch
        {
            Engine30PrescriptionCategoryCodes.MajorStrength
                => Engine30PrescriptionCategoryCodes.MajorStrength,

            Engine30PrescriptionCategoryCodes.AccessoryStrength
                => Engine30PrescriptionCategoryCodes.AccessoryStrength,

            Engine30PrescriptionCategoryCodes.ConditioningCardio
                => Engine30PrescriptionCategoryCodes.ConditioningCardio,

            Engine30PrescriptionCategoryCodes.RestRecoveryOther
                => Engine30PrescriptionCategoryCodes.RestRecoveryOther,

            _ => throw new ArgumentException(
                $"Unsupported Engine 3.0 prescription category '{categoryCode}'.")
        };

    public static string NormalizePlan(
        string planCode)
    {
        var normalized =
            (planCode ?? "")
                .Trim()
                .ToUpperInvariant()
                .Replace("-", "_")
                .Replace(" ", "_");

        return normalized switch
        {
            "P1" or "PLAN1" or "PLAN_1" => "PLAN_1",
            "P2" or "PLAN2" or "PLAN_2" => "PLAN_2",
            "P3" or "PLAN3" or "PLAN_3" => "PLAN_3",
            "P4" or "PLAN4" or "PLAN_4" => "PLAN_4",
            "P5" or "PLAN5" or "PLAN_5" => "PLAN_5",

            _ => throw new ArgumentException(
                $"Unsupported Engine 3.0 plan '{planCode}'.")
        };
    }

    private static decimal RoundToIncrement(
        decimal value,
        decimal increment)
    {
        if (increment <= 0m)
            return value;

        return Math.Round(
                   value / increment,
                   0,
                   MidpointRounding.AwayFromZero)
               * increment;
    }

    private static decimal CeilingToIncrement(
        decimal value,
        decimal increment)
    {
        if (increment <= 0m)
            return value;

        return Math.Ceiling(
                   value / increment)
               * increment;
    }

    private sealed record Row(
        int Index,
        Engine30PrescriptionDayResult Day,
        Engine30CategoryEnergyPolicy Policy,
        decimal Minimum,
        decimal Target,
        decimal Maximum);
}
