namespace WebRecomp.Web.Services.Engine30;

public static class Engine30PrescriptionCategoryCodes
{
    public const string MajorStrength = "MAJOR_STRENGTH";
    public const string AccessoryStrength = "ACCESSORY_STRENGTH";
    public const string ConditioningCardio = "CONDITIONING_CARDIO";
    public const string RestRecoveryOther = "REST_RECOVERY_OTHER";
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
/// Engine 3.0 shadow prescription layer.
///
/// This consumes already-modeled maintenance and glycogen demand. It does not
/// recalculate expenditure, does not add glycogen-reload kcal to maintenance,
/// and does not modify workout configuration.
///
/// Hierarchy:
///   1. Protect stable protein.
///   2. Protect an adequate/stable fat floor.
///   3. Protect the physiological carbohydrate/fuel floor.
///   4. Periodize the remaining weekly energy across workout demand.
///   5. If the requested deficit cannot fit those physiological floors,
///      reduce the deficit transparently rather than reporting a calorie
///      target that the macro prescription itself exceeds.
///
/// DRAFT calibration values remain explicit/versionable until promoted into
/// CFG-3000.x configuration.
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
            PlanReason(normalizedPlan)
        };

        var weeklyMaintenance =
            days.Sum(x => x.MaintenanceEnergyKcal);

        decimal effectiveRequestedPct = requestedPct;

        if (bmi < 18.5m)
        {
            effectiveRequestedPct = 0m;
            reasons.Add("BMI_BELOW_DEFICIT_THRESHOLD");
        }

        var boundedPct =
            Math.Clamp(
                effectiveRequestedPct,
                0m,
                0.20m);

        if (boundedPct < effectiveRequestedPct)
            reasons.Add("DEFICIT_PERCENT_CAP_APPLIED");

        var percentageDeficit =
            weeklyMaintenance * boundedPct;

        var requestedWeeklyDeficit =
            Math.Min(
                percentageDeficit,
                500m * 7m);

        if (requestedWeeklyDeficit < percentageDeficit)
            reasons.Add("DEFICIT_ABSOLUTE_CAP_APPLIED");

        var requestedWeeklyPrescribed =
            RoundToIncrement(
                weeklyMaintenance - requestedWeeklyDeficit,
                10m);

        var weightKg =
            weightLb / PoundsPerKg;

        var protein =
            RoundToIncrement(
                weightLb * 0.88m,
                5m);

        var baseRestCarbs =
            BaseRestCarbs(normalizedPlan);

        var standardCarbUpper =
            RoundToIncrement(
                weightKg * 5.0m,
                5m);

        var allocationRows =
            days.Select(x => new AllocationRow(
                    Day: x,
                    Weight:
                        CategoryWeight(
                            x.WorkoutCategoryCode),
                    Score:
                        x.MaintenanceEnergyKcal
                        * CategoryWeight(
                            x.WorkoutCategoryCode)))
                .ToArray();

        if (allocationRows.Sum(x => x.Score) <= 0m)
        {
            throw new InvalidOperationException(
                "Weekly allocation score must be positive.");
        }

        /*
         * Determine stable fat and the minimum daily macro-energy targets.
         *
         * Fat depends partly on average prescribed energy. If physiological
         * macro floors force the weekly prescription upward, fat may change
         * slightly too. Iterate until the stable-fat / weekly-budget pair
         * converges. With 5g/10kcal rounding this normally settles in one or
         * two passes.
         */
        var weeklyPrescribed =
            requestedWeeklyPrescribed;

        decimal fat = 0m;
        decimal[] carbFloors = [];
        decimal[] minimumDailyTargets = [];

        for (var iteration = 0; iteration < 10; iteration++)
        {
            var averagePrescribed =
                weeklyPrescribed / 7m;

            var fatFromEnergy =
                averagePrescribed
                * 0.20m
                / 9m;

            var unroundedFatFloor =
                weightKg * 0.50m;

            /*
             * This is a FLOOR. Normal rounding can move 35.15g down to 35g,
             * which is exactly what the plausibility matrix caught.
             */
            var roundedFatFloor =
                CeilingToIncrement(
                    unroundedFatFloor,
                    5m);

            var roundedFatFromEnergy =
                RoundToIncrement(
                    fatFromEnergy,
                    5m);

            fat =
                Math.Max(
                    roundedFatFromEnergy,
                    roundedFatFloor);

            var fixedKcal =
                protein * 4m
                + fat * 9m;

            carbFloors =
                allocationRows
                    .Select(x =>
                        /*
                         * A physiological carbohydrate FLOOR is also rounded
                         * upward, never downward.
                         */
                        CeilingToIncrement(
                            baseRestCarbs
                            + x.Day.GlycogenReloadRequirementG,
                            5m))
                    .ToArray();

            minimumDailyTargets =
                carbFloors
                    .Select(carbFloor =>
                        /*
                         * Daily calorie target must be high enough to contain
                         * stable protein + stable fat + the carb floor.
                         *
                         * The calorie target remains a 10-kcal value.
                         */
                        CeilingToIncrement(
                            fixedKcal
                            + carbFloor * 4m,
                            10m))
                    .ToArray();

            var minimumWeeklyEnergy =
                minimumDailyTargets.Sum();

            if (minimumWeeklyEnergy
                <= weeklyPrescribed + 0.001m)
            {
                break;
            }

            /*
             * The requested deficit cannot coexist with the configured
             * physiological macro floors. Reduce the deficit instead of
             * allowing macro-derived calories to silently exceed the target.
             */
            weeklyPrescribed =
                CeilingToIncrement(
                    minimumWeeklyEnergy,
                    10m);

            if (!reasons.Contains(
                    "DEFICIT_REDUCED_TO_PROTECT_MACRO_FLOORS"))
            {
                reasons.Add(
                    "DEFICIT_REDUCED_TO_PROTECT_MACRO_FLOORS");
            }

            if (iteration == 9)
            {
                throw new InvalidOperationException(
                    "Engine 3.0 macro-floor reconciliation did not converge.");
            }
        }

        if (weeklyPrescribed
            > weeklyMaintenance + 0.001m)
        {
            /*
             * Do not hide an impossible calibration. This means the current
             * protein/fat/carb floors require more energy than modeled
             * maintenance across the week.
             */
            reasons.Add(
                "PHYSIOLOGICAL_MACRO_MINIMUM_EXCEEDS_MAINTENANCE");
        }

        /*
         * Allocate the authoritative weekly energy with lower bounds.
         *
         * This is a constrained proportional allocation:
         * - start with maintenance x category-weight shares;
         * - lock any day whose proportional share falls below its macro floor;
         * - reallocate the remaining energy over the remaining days;
         * - preserve the exact weekly total.
         */
        var roundedTargets =
            AllocateTargetsWithFloors(
                allocationRows.Select(x => x.Score).ToArray(),
                minimumDailyTargets,
                weeklyPrescribed);

        var results =
            new List<Engine30PrescriptionDayResult>(7);

        for (var i = 0; i < allocationRows.Length; i++)
        {
            var row =
                allocationRows[i];

            var energyTarget =
                roundedTargets[i];

            var carbFloor =
                carbFloors[i];

            var fixedKcal =
                protein * 4m
                + fat * 9m;

            var energyBalancedCarbs =
                RoundToIncrement(
                    Math.Max(
                        0m,
                        (energyTarget - fixedKcal) / 4m),
                    5m);

            decimal carbs;
            string governing;

            if (carbFloor > standardCarbUpper)
            {
                carbs = carbFloor;
                governing =
                    "PHYSIOLOGICAL_FLOOR_EXCEEDS_STANDARD_MAX";
            }
            else if (energyBalancedCarbs < carbFloor)
            {
                carbs = carbFloor;
                governing =
                    "PHYSIOLOGICAL_FLOOR";
            }
            else if (energyBalancedCarbs > standardCarbUpper)
            {
                carbs = standardCarbUpper;
                governing =
                    "STANDARD_USEFUL_MAX";
            }
            else
            {
                carbs = energyBalancedCarbs;
                governing =
                    "ENERGY_BALANCE";
            }

            var macroKcal =
                Math.Round(
                    protein * 4m
                    + carbs * 4m
                    + fat * 9m,
                    0,
                    MidpointRounding.AwayFromZero);

            results.Add(
                new Engine30PrescriptionDayResult(
                    row.Day.Date,
                    NormalizeCategory(
                        row.Day.WorkoutCategoryCode),
                    row.Weight,
                    Round(
                        row.Day.MaintenanceEnergyKcal,
                        2),
                    energyTarget,
                    protein,
                    fat,
                    carbFloor,
                    energyBalancedCarbs,
                    standardCarbUpper,
                    carbs,
                    macroKcal,
                    Round(
                        macroKcal - energyTarget,
                        2),
                    governing));
        }

        var effectivePct =
            weeklyMaintenance > 0m
                ? (weeklyMaintenance - weeklyPrescribed)
                    / weeklyMaintenance
                : 0m;

        return new Engine30WeeklyPrescriptionResult(
            normalizedPlan,
            Round(weightLb, 2),
            Round(bmi, 2),
            Round(requestedPct, 4),
            Round(effectivePct, 6),
            Round(weeklyMaintenance, 2),
            Round(
                weeklyMaintenance - weeklyPrescribed,
                2),
            weeklyPrescribed,
            Round(
                weeklyPrescribed / 7m,
                2),
            protein,
            fat,
            baseRestCarbs,
            results,
            reasons);
    }

    private static decimal[] AllocateTargetsWithFloors(
        decimal[] scores,
        decimal[] minimumTargets,
        decimal weeklyTarget)
    {
        if (scores.Length != minimumTargets.Length)
        {
            throw new ArgumentException(
                "Allocation score and minimum-target arrays must have equal length.");
        }

        if (scores.Length == 0)
            return [];

        if (minimumTargets.Sum()
            > weeklyTarget + 0.001m)
        {
            throw new InvalidOperationException(
                "Weekly prescribed energy is below the sum of the daily physiological macro floors.");
        }

        var raw =
            new decimal[scores.Length];

        var locked =
            new bool[scores.Length];

        var remainingTarget =
            weeklyTarget;

        var remainingScore =
            scores.Sum();

        while (true)
        {
            var unlocked =
                Enumerable.Range(
                        0,
                        scores.Length)
                    .Where(i => !locked[i])
                    .ToArray();

            if (unlocked.Length == 0)
                break;

            if (remainingScore <= 0m)
            {
                throw new InvalidOperationException(
                    "Remaining allocation score must stay positive.");
            }

            var proposals =
                unlocked
                    .Select(i => new
                    {
                        Index = i,
                        Proposed =
                            remainingTarget
                            * scores[i]
                            / remainingScore,
                        Deficit =
                            remainingTarget
                            * scores[i]
                            / remainingScore
                            - minimumTargets[i]
                    })
                    .OrderBy(x => x.Deficit)
                    .ThenBy(x => x.Index)
                    .ToArray();

            var worst =
                proposals[0];

            if (worst.Deficit >= -0.001m)
            {
                foreach (var proposal in proposals)
                {
                    raw[proposal.Index] =
                        proposal.Proposed;
                }

                break;
            }

            /*
             * Lock only the most constrained day, then recompute. Locking all
             * provisional violations at once can over-constrain the solution
             * because removing one floor-bound day increases the share
             * available to all remaining days.
             */
            raw[worst.Index] =
                minimumTargets[worst.Index];

            locked[worst.Index] = true;

            remainingTarget -=
                raw[worst.Index];

            remainingScore -=
                scores[worst.Index];
        }

        var rounded =
            raw.Select(
                    (value, i) =>
                        Math.Max(
                            minimumTargets[i],
                            RoundToIncrement(
                                value,
                                10m)))
                .ToArray();

        var difference =
            weeklyTarget
            - rounded.Sum();

        while (difference >= 9.999m)
        {
            var index =
                raw.Select(
                        (value, i) => new
                        {
                            Index = i,
                            Residual =
                                value - rounded[i]
                        })
                    .OrderByDescending(
                        x => x.Residual)
                    .ThenBy(
                        x => x.Index)
                    .First()
                    .Index;

            rounded[index] += 10m;
            difference -= 10m;
        }

        while (difference <= -9.999m)
        {
            var candidate =
                raw.Select(
                        (value, i) => new
                        {
                            Index = i,
                            Residual =
                                rounded[i] - value,
                            CanReduce =
                                rounded[i] - 10m
                                >= minimumTargets[i]
                                - 0.001m
                        })
                    .Where(x => x.CanReduce)
                    .OrderByDescending(
                        x => x.Residual)
                    .ThenBy(
                        x => x.Index)
                    .FirstOrDefault();

            if (candidate is null)
            {
                throw new InvalidOperationException(
                    "Rounded weekly targets could not be reconciled without violating a physiological macro floor.");
            }

            rounded[candidate.Index] -= 10m;
            difference += 10m;
        }

        /*
         * Every meaningful value is 10-kcal rounded here. Any remaining
         * fraction is decimal noise.
         */
        if (Math.Abs(difference) > 0.01m)
        {
            throw new InvalidOperationException(
                $"Weekly target reconciliation left an unexpected {difference:0.###} kcal residual.");
        }

        return rounded;
    }

    private static decimal CategoryWeight(
        string categoryCode)
        => NormalizeCategory(
            categoryCode) switch
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
        if (string.IsNullOrWhiteSpace(
                categoryCode))
        {
            throw new ArgumentException(
                "Workout category is required.",
                nameof(categoryCode));
        }

        return categoryCode
            .Trim()
            .ToUpperInvariant();
    }

    private static string NormalizePlan(
        string planCode)
    {
        if (string.IsNullOrWhiteSpace(
                planCode))
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

    private static decimal BaseRestCarbs(
        string planCode)
        => planCode switch
        {
            "PLAN_1" => 100m,
            "PLAN_2" => 120m,
            "PLAN_3" => 140m,
            "PLAN_4" => 130m,
            "PLAN_5" => 90m,

            _ => throw new ArgumentException(
                $"Unsupported Recomp plan '{planCode}'.",
                nameof(planCode))
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

    private sealed record AllocationRow(
        Engine30PrescriptionDayInput Day,
        decimal Weight,
        decimal Score);
}

