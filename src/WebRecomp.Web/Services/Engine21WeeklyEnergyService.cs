namespace WebRecomp.Web.Services;

/// <summary>
/// One planned day supplied to the Engine 2.1 weekly energy allocator.
/// Workout calories are the existing 12WR physiological workout estimate.
/// </summary>
public sealed record Engine21PlannedEnergyDay(
    DateOnly Date,
    string WorkoutCategoryCode,
    decimal PlannedWorkoutCalories,
    decimal PlannedWorkoutMinutes);

/// <summary>
/// Engine 2.1 intended energy-management target for one day.
///
/// This is NOT a command that macros must exactly equal.
/// Protein, adequate fat and justified carbohydrate are reconciled afterward.
/// </summary>
public sealed record Engine21DailyEnergyBudget(
    DateOnly Date,
    string WorkoutCategoryCode,
    decimal GrossWorkoutEnergyKcal,
    decimal NetWorkoutEnergyKcal,
    decimal EstimatedMaintenanceEnergyKcal,
    decimal AllocationWeight,
    decimal AllocationScore,
    decimal IntendedEnergyTargetKcal);

/// <summary>
/// Weekly Engine 2.1 energy-budget result.
///
/// The weekly budget establishes the energy-management framework.
/// The AveragePrescribedEnergyKcal is the reference used for stable
/// person-level dietary-fat calculation.
/// </summary>
public sealed record Engine21WeeklyEnergyBudget(
    decimal ProvisionalNonWorkoutEnergyKcal,
    decimal RestingEnergyKcal,
    decimal TargetDeficitPct,
    decimal WeeklyMaintenanceEnergyKcal,
    decimal WeeklyPrescribedEnergyKcal,
    decimal AveragePrescribedEnergyKcal,
    IReadOnlyList<Engine21DailyEnergyBudget> Days);

public sealed class Engine21WeeklyEnergyService
{
    public Engine21WeeklyEnergyBudget Calculate(
        decimal provisionalNonWorkoutEnergyKcal,
        decimal restingEnergyKcal,
        decimal targetDeficitPct,
        IReadOnlyCollection<Engine21PlannedEnergyDay> plannedDays,
        IReadOnlyDictionary<string, decimal> settings)
    {
        if (provisionalNonWorkoutEnergyKcal <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(provisionalNonWorkoutEnergyKcal));
        }

        if (restingEnergyKcal <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(restingEnergyKcal));
        }

        if (targetDeficitPct < 0m || targetDeficitPct >= 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetDeficitPct),
                "Target deficit must be >= 0 and < 1.");
        }

        if (plannedDays.Count != 7)
        {
            throw new ArgumentException(
                "Exactly seven planned days are required.",
                nameof(plannedDays));
        }

        var orderedDays =
            plannedDays
                .OrderBy(x => x.Date)
                .ToArray();

        if (orderedDays
            .Select(x => x.Date)
            .Distinct()
            .Count() != 7)
        {
            throw new ArgumentException(
                "The weekly energy plan must contain seven unique dates.",
                nameof(plannedDays));
        }

        var energyRounding =
            Setting(
                settings,
                "ENERGY_ROUNDING_KCAL",
                10m);

        var restingEnergyPerMinute =
            restingEnergyKcal / 1440m;

        var drafts =
            orderedDays
                .Select(day =>
                {
                    if (day.PlannedWorkoutCalories < 0m)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(plannedDays),
                            "Workout calories cannot be negative.");
                    }

                    if (day.PlannedWorkoutMinutes < 0m)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(plannedDays),
                            "Workout minutes cannot be negative.");
                    }

                    var allocationWeight =
                        CategoryWeight(
                            day.WorkoutCategoryCode,
                            settings);

                    // The daily non-workout energy reference already covers
                    // the entire 24-hour day.
                    //
                    // Therefore only workout energy ABOVE the resting energy
                    // that would have occurred during those workout minutes
                    // is added to estimated daily expenditure.
                    var restingEnergyDuringWorkout =
                        restingEnergyPerMinute
                        * day.PlannedWorkoutMinutes;

                    var netWorkoutEnergy =
                        Math.Max(
                            0m,
                            day.PlannedWorkoutCalories
                            - restingEnergyDuringWorkout);

                    // The non-workout reference covers the full 24-hour day,
                    // so add only workout energy above the resting cost that
                    // would already have occurred during the workout minutes.
                    var maintenanceEnergy =
                        provisionalNonWorkoutEnergyKcal
                        + netWorkoutEnergy;

                    // Category weight is NOT multiplied directly into TDEE
                    // as the final calorie prescription.
                    //
                    // It creates a normalized allocation score. The total
                    // weekly energy budget remains authoritative.
                    var allocationScore =
                        maintenanceEnergy
                        * allocationWeight;

                    return new
                    {
                        Day = day,
                        NetWorkoutEnergy = netWorkoutEnergy,
                        MaintenanceEnergy = maintenanceEnergy,
                        AllocationWeight = allocationWeight,
                        AllocationScore = allocationScore
                    };
                })
                .ToArray();

        var weeklyMaintenance =
            drafts.Sum(x => x.MaintenanceEnergy);

        var unroundedWeeklyPrescription =
            weeklyMaintenance
            * (1m - targetDeficitPct);

        var weeklyPrescription =
            RoundToIncrement(
                unroundedWeeklyPrescription,
                energyRounding);

        var totalAllocationScore =
            drafts.Sum(x => x.AllocationScore);

        if (totalAllocationScore <= 0m)
        {
            throw new InvalidOperationException(
                "Weekly allocation score must be greater than zero.");
        }

        var unroundedTargets =
            drafts
                .Select(x =>
                    weeklyPrescription
                    * x.AllocationScore
                    / totalAllocationScore)
                .ToArray();

        var roundedTargets =
            unroundedTargets
                .Select(x =>
                    RoundToIncrement(
                        x,
                        energyRounding))
                .ToArray();

        // Independent daily rounding can create a small weekly discrepancy.
        // Preserve the authoritative weekly budget by placing that tiny
        // rounding remainder onto the highest-allocation day.
        var roundingDifference =
            weeklyPrescription
            - roundedTargets.Sum();

        if (roundingDifference != 0m)
        {
            var highestAllocationIndex =
                drafts
                    .Select(
                        (x, index) =>
                            new
                            {
                                x.AllocationScore,
                                Index = index
                            })
                    .OrderByDescending(x => x.AllocationScore)
                    .First()
                    .Index;

            roundedTargets[highestAllocationIndex] +=
                roundingDifference;
        }

        var dailyBudgets =
            drafts
                .Select(
                    (x, index) =>
                        new Engine21DailyEnergyBudget(
                            Date:
                                x.Day.Date,

                            WorkoutCategoryCode:
                                x.Day.WorkoutCategoryCode,

                            GrossWorkoutEnergyKcal:
                                Round(
                                    x.Day.PlannedWorkoutCalories,
                                    2),

                            NetWorkoutEnergyKcal:
                                Round(
                                    x.NetWorkoutEnergy,
                                    2),

                            EstimatedMaintenanceEnergyKcal:
                                Round(
                                    x.MaintenanceEnergy,
                                    2),

                            AllocationWeight:
                                x.AllocationWeight,

                            AllocationScore:
                                Round(
                                    x.AllocationScore,
                                    2),

                            IntendedEnergyTargetKcal:
                                roundedTargets[index]))
                .ToArray();

        return new Engine21WeeklyEnergyBudget(
            ProvisionalNonWorkoutEnergyKcal:
                Round(
                    provisionalNonWorkoutEnergyKcal,
                    2),

            RestingEnergyKcal:
                Round(
                    restingEnergyKcal,
                    2),

            TargetDeficitPct:
                Round(
                    targetDeficitPct,
                    4),

            WeeklyMaintenanceEnergyKcal:
                Round(
                    weeklyMaintenance,
                    2),

            WeeklyPrescribedEnergyKcal:
                weeklyPrescription,

            AveragePrescribedEnergyKcal:
                Round(
                    weeklyPrescription / 7m,
                    2),

            Days:
                dailyBudgets);
    }

    private static decimal CategoryWeight(
        string categoryCode,
        IReadOnlyDictionary<string, decimal> settings)
    {
        if (string.IsNullOrWhiteSpace(categoryCode))
        {
            throw new ArgumentException(
                "Workout category is required.",
                nameof(categoryCode));
        }

        return categoryCode
            .Trim()
            .ToUpperInvariant() switch
        {
            "MAJOR_STRENGTH" =>
                Setting(
                    settings,
                    "ENERGY_WEIGHT_MAJOR_STRENGTH",
                    1.10m),

            "ACCESSORY_STRENGTH" =>
                Setting(
                    settings,
                    "ENERGY_WEIGHT_ACCESSORY_STRENGTH",
                    1.00m),

            "CONDITIONING_CARDIO" =>
                Setting(
                    settings,
                    "ENERGY_WEIGHT_CONDITIONING_CARDIO",
                    0.95m),

            "REST_RECOVERY_OTHER" =>
                Setting(
                    settings,
                    "ENERGY_WEIGHT_REST_RECOVERY_OTHER",
                    0.80m),

            _ =>
                throw new ArgumentException(
                    $"Unsupported workout category '{categoryCode}'.",
                    nameof(categoryCode))
        };
    }

    private static decimal Setting(
        IReadOnlyDictionary<string, decimal> settings,
        string code,
        decimal fallback)
        => settings.TryGetValue(
            code,
            out var value)
                ? value
                : fallback;

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

    private static decimal Round(
        decimal value,
        int decimals)
        => Math.Round(
            value,
            decimals,
            MidpointRounding.AwayFromZero);
}

