using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

/// <summary>
/// Sequential Engine 2.1 macro projection.
///
/// Starts with a real four-day historical fuel window, calculates each
/// planned day's macro prescription, then rolls that prescribed day's
/// modeled glycogen demand/restoration into the next day's fuel window.
///
/// Pure/in-memory. No database reads or writes.
/// </summary>
public sealed record Engine21WeeklyMacroDayInput(
    DateOnly Date,
    PlanDefinition Plan,
    WorkoutTypeDefinition Workout,
    decimal IntendedEnergyTargetKcal);

public sealed record Engine21WeeklyMacroDayResult(
    DateOnly Date,
    string WorkoutTypeCode,
    decimal IntendedEnergyTargetKcal,
    Engine21FuelWindow IncomingFuelWindow,
    Engine21DailyMacroResult Macro,
    Engine21FuelLedgerDay ProjectedFuelDay,
    Engine21FuelWindow OutgoingFuelWindow);

public sealed class Engine21WeeklyMacroProjectionService(
    Engine21DailyMacroService dailyMacro,
    Engine21ProjectedFuelWindowService projectedFuel)
{
    public IReadOnlyList<Engine21WeeklyMacroDayResult> Calculate(
        decimal weightLb,
        decimal proteinG,
        decimal fatG,
        Engine21FuelWindow initialFuelWindow,
        IReadOnlyCollection<Engine21WeeklyMacroDayInput> plannedDays,
        IReadOnlyDictionary<string, decimal> settings)
    {
        Validate(
            weightLb,
            proteinG,
            fatG,
            initialFuelWindow,
            plannedDays);

        var orderedDays =
            plannedDays
                .OrderBy(x => x.Date)
                .ToList();

        if (orderedDays
            .Select(x => x.Date)
            .Distinct()
            .Count() != orderedDays.Count)
        {
            throw new InvalidOperationException(
                "Projected macro days contain duplicate dates.");
        }

        //
        // The projected sequence must be continuous because the fuel
        // ledger advances exactly one calendar day at a time.
        //
        for (var i = 1; i < orderedDays.Count; i++)
        {
            var expected =
                orderedDays[i - 1].Date.AddDays(1);

            if (orderedDays[i].Date != expected)
            {
                throw new InvalidOperationException(
                    $"Projected macro days must be consecutive. " +
                    $"Expected {expected:yyyy-MM-dd}; " +
                    $"received {orderedDays[i].Date:yyyy-MM-dd}.");
            }
        }

        var results =
            new List<Engine21WeeklyMacroDayResult>(
                capacity: orderedDays.Count);

        var currentFuelWindow =
            initialFuelWindow;

        foreach (var day in orderedDays)
        {
            //
            // Calculate this day's nutrition using the fuel state that
            // exists BEFORE this day's planned workout/prescription.
            //
            var macro =
                dailyMacro.Calculate(
                    new Engine21DailyMacroInput(
                        WeightLb:
                            weightLb,

                        Plan:
                            day.Plan,

                        Workout:
                            day.Workout,

                        FuelDemandG:
                            currentFuelWindow.DemandG,

                        FuelRestorationCreditG:
                            currentFuelWindow.RestorationCreditG,

                        FuelAbsoluteRestorationRatio:
                            currentFuelWindow.AbsoluteRestorationRatio,

                        ModerateHardDays:
                            currentFuelWindow.ModerateHardDays,

                        HardDays:
                            currentFuelWindow.HardDays,

                        ProteinG:
                            proteinG,

                        FatG:
                            fatG,

                        IntendedEnergyTargetKcal:
                            day.IntendedEnergyTargetKcal),
                    settings);

            //
            // Convert this day's planned workout demand and prescribed
            // carbohydrate into the projected ledger day that will
            // influence tomorrow.
            //
            var projectedDay =
                projectedFuel.BuildProjectedDay(
                    new Engine21ProjectedFuelDayInput(
                        Date:
                            day.Date,

                        DemandG:
                            macro.CarbRequirement.PlannedGlycogenBurnG,

                        RecommendedCarbsG:
                            macro.Reconciliation.RecommendedCarbsG,

                        BaseRestCarbsG:
                            day.Plan.BaseRestCarbsG,

                        FuelScore:
                            day.Workout.FuelScore),
                    settings);

            var outgoingFuelWindow =
                projectedFuel.RollForward(
                    currentFuelWindow,
                    projectedDay);

            results.Add(
                new Engine21WeeklyMacroDayResult(
                    Date:
                        day.Date,

                    WorkoutTypeCode:
                        day.Workout.WorkoutTypeCode,

                    IntendedEnergyTargetKcal:
                        day.IntendedEnergyTargetKcal,

                    IncomingFuelWindow:
                        currentFuelWindow,

                    Macro:
                        macro,

                    ProjectedFuelDay:
                        projectedDay,

                    OutgoingFuelWindow:
                        outgoingFuelWindow));

            currentFuelWindow =
                outgoingFuelWindow;
        }

        return results;
    }

    private static void Validate(
        decimal weightLb,
        decimal proteinG,
        decimal fatG,
        Engine21FuelWindow initialFuelWindow,
        IReadOnlyCollection<Engine21WeeklyMacroDayInput> plannedDays)
    {
        if (weightLb <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weightLb));
        }

        if (proteinG < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(proteinG));
        }

        if (fatG < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fatG));
        }

        if (initialFuelWindow.Days.Count != 4)
        {
            throw new InvalidOperationException(
                "Sequential macro projection requires exactly four " +
                "historical fuel-ledger days.");
        }

        if (plannedDays.Count == 0)
        {
            throw new InvalidOperationException(
                "Sequential macro projection requires at least one planned day.");
        }

        //
        // The first projected date must immediately follow the last
        // historical ledger date.
        //
        var lastHistoricalDate =
            initialFuelWindow.Days
                .Max(x => x.Date);

        var firstProjectedDate =
            plannedDays
                .Min(x => x.Date);

        var expectedFirstDate =
            lastHistoricalDate.AddDays(1);

        if (firstProjectedDate != expectedFirstDate)
        {
            throw new InvalidOperationException(
                $"First projected macro day must be " +
                $"{expectedFirstDate:yyyy-MM-dd}; " +
                $"received {firstProjectedDate:yyyy-MM-dd}.");
        }
    }
}
