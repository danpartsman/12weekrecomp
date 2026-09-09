namespace WebRecomp.Web.Services.Engine30;

public sealed record Engine30PlannedFuelDay(
    DateOnly Date,
    decimal PlannedGlycogenDemandG,
    decimal PlannedRestorationFraction,
    decimal? MaxCarbohydrateAvailableBeforeWorkoutG = null);

public sealed record Engine30ProjectedFuelStateDay(
    DateOnly Date,
    Engine30FuelWindow IncomingWindow,
    Engine30FuelReadinessResult Readiness,
    decimal PrescribedCarbohydrateG,
    decimal PreWorkoutCarbohydrateAllocationG,
    Engine30FuelLedgerDay ProjectedLedgerDay,
    Engine30FuelWindow OutgoingWindow);

public sealed record Engine30WeeklyFuelProjectionResult(
    string PlanCode,
    Engine30FuelWindow HistoricalStartingWindow,
    IReadOnlyList<Engine30ProjectedFuelStateDay> Days,
    Engine30FuelWindow FinalProjectedWindow);

/// <summary>
/// Sequential Engine 3.0 fuel projection:
///
/// 4 real historical days -> today's prescription -> outgoing fuel state ->
/// tomorrow's incoming state -> repeat.
///
/// Rescue does not create additional calories. It identifies how much of the
/// already-prescribed carbohydrate should be available before training when
/// fuel is the limiting factor.
/// </summary>
public sealed class Engine30WeeklyFuelProjectionService(
    Engine30FuelStateService fuelStateService)
{
    public Engine30WeeklyFuelProjectionResult Calculate(
        Engine30FuelWindow historicalWindow,
        Engine30WeeklyPrescriptionResult prescription,
        IReadOnlyCollection<Engine30PlannedFuelDay> plannedFuelDays)
    {
        ArgumentNullException.ThrowIfNull(historicalWindow);
        ArgumentNullException.ThrowIfNull(prescription);
        ArgumentNullException.ThrowIfNull(plannedFuelDays);

        if (prescription.Days.Count != 7)
            throw new ArgumentException("Prescription must contain seven days.", nameof(prescription));

        if (plannedFuelDays.Count != 7)
            throw new ArgumentException("Fuel projection requires seven planned days.", nameof(plannedFuelDays));

        var plannedByDate = plannedFuelDays.ToDictionary(x => x.Date);
        var orderedPrescription = prescription.Days.OrderBy(x => x.Date).ToArray();

        foreach (var day in orderedPrescription)
        {
            if (!plannedByDate.ContainsKey(day.Date))
                throw new InvalidOperationException($"Missing planned fuel input for {day.Date:yyyy-MM-dd}.");
        }

        var current = historicalWindow;
        var results = new List<Engine30ProjectedFuelStateDay>(7);

        foreach (var day in orderedPrescription)
        {
            var planned = plannedByDate[day.Date];

            var readiness = fuelStateService.CalculateReadiness(
                current,
                planned.PlannedGlycogenDemandG,
                planned.PlannedRestorationFraction,
                planned.MaxCarbohydrateAvailableBeforeWorkoutG);

            // Rescue is a timing allocation inside the day's prescribed carbs.
            var preWorkoutAllocation = Math.Min(
                day.RecommendedCarbsG,
                readiness.PreWorkoutRescueG);

            var projectedDay = fuelStateService.BuildProjectedDay(
                new Engine30ProjectedFuelDayInput(
                    Date: day.Date,
                    GlycogenDemandG: planned.PlannedGlycogenDemandG,
                    PrescribedCarbohydrateG: day.RecommendedCarbsG,
                    BaseRestCarbohydrateG: prescription.BaseRestCarbsG));

            var outgoing = fuelStateService.RollForward(current, projectedDay);

            results.Add(new Engine30ProjectedFuelStateDay(
                Date: day.Date,
                IncomingWindow: current,
                Readiness: readiness,
                PrescribedCarbohydrateG: day.RecommendedCarbsG,
                PreWorkoutCarbohydrateAllocationG: preWorkoutAllocation,
                ProjectedLedgerDay: projectedDay,
                OutgoingWindow: outgoing));

            current = outgoing;
        }

        return new Engine30WeeklyFuelProjectionResult(
            PlanCode: prescription.PlanCode,
            HistoricalStartingWindow: historicalWindow,
            Days: results,
            FinalProjectedWindow: current);
    }
}
