using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30WeeklyFuelProjectionServiceTests
{
    [Fact]
    public void Projection_RollsFuelStateSequentially_WithoutAddingRescueCalories()
    {
        var fuel = new Engine30FuelStateService();
        var projection = new Engine30WeeklyFuelProjectionService(fuel);
        var prescriptionService = new Engine30PrescriptionService();

        var historicalStart = new DateOnly(2026, 8, 28);
        var historical = fuel.BuildHistoricalWindow(
        [
            new(historicalStart, 80m, 120m, 120m),
            new(historicalStart.AddDays(1), 80m, 120m, 120m),
            new(historicalStart.AddDays(2), 80m, 120m, 120m),
            new(historicalStart.AddDays(3), 80m, 120m, 120m)
        ]);

        var weekStart = historicalStart.AddDays(4);

        var prescription = prescriptionService.Calculate(
            "PLAN_2",
            200m,
            72m,
            Enumerable.Range(0, 7)
                .Select(i => new Engine30PrescriptionDayInput(
                    weekStart.AddDays(i),
                    2500m,
                    i is 0 or 2 or 4
                        ? Engine30PrescriptionCategoryCodes.MajorStrength
                        : Engine30PrescriptionCategoryCodes.RestRecoveryOther,
                    i is 0 or 2 or 4 ? 100m : 0m))
                .ToArray());

        var planned = Enumerable.Range(0, 7)
            .Select(i => new Engine30PlannedFuelDay(
                weekStart.AddDays(i),
                i is 0 or 2 or 4 ? 100m : 0m,
                0.80m,
                i is 0 or 2 or 4 ? 45m : 0m))
            .ToArray();

        var result = projection.Calculate(historical, prescription, planned);

        Assert.Equal(7, result.Days.Count);
        Assert.Equal(historical.Days[1].Date, result.Days[0].OutgoingWindow.Days[0].Date);
        Assert.Equal(result.Days[0].OutgoingWindow, result.Days[1].IncomingWindow);
        Assert.Equal(result.Days[^1].OutgoingWindow, result.FinalProjectedWindow);

        foreach (var day in result.Days)
        {
            Assert.True(day.PreWorkoutCarbohydrateAllocationG <= day.PrescribedCarbohydrateG);
            Assert.Equal(day.PrescribedCarbohydrateG, day.ProjectedLedgerDay.ActualCarbohydrateG is null
                ? day.PrescribedCarbohydrateG
                : day.ProjectedLedgerDay.ActualCarbohydrateG.Value);
        }
    }
}
