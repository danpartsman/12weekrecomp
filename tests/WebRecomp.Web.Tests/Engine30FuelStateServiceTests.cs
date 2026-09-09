using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30FuelStateServiceTests
{
    private readonly Engine30FuelStateService _service = new();

    [Fact]
    public void HistoricalWindow_PreservesFrozenRestorationEquation()
    {
        var start = new DateOnly(2026, 9, 1);

        var window = _service.BuildHistoricalWindow(
        [
            new(start, 100m, 120m, 180m),
            new(start.AddDays(1), 80m, 120m, 120m),
            new(start.AddDays(2), 60m, 120m, 200m),
            new(start.AddDays(3), 0m, 120m, 100m)
        ]);

        // Day 1: min(100, 60 + 24) = 84 -> 85 after 5g rounding.
        // Day 2: min(80, 0 + 24) = 24 -> 25.
        // Day 3: min(60, 80 + 24) = 60.
        // Day 4: no demand -> 0.
        Assert.Equal(240m, window.TotalGlycogenDemandG);
        Assert.Equal(170m, window.TotalRestorationCreditG);
        Assert.Equal(0.7083m, window.AbsoluteRestorationRatio);
        Assert.Equal(3, window.ModerateHardDays);
        Assert.Equal(2, window.HardDays);
    }

    [Fact]
    public void HistoricalWindow_MissingCarbohydrate_RemainsMissingEvidence()
    {
        var start = new DateOnly(2026, 9, 1);

        var window = _service.BuildHistoricalWindow(
        [
            new(start, 50m, 120m, null),
            new(start.AddDays(1), 0m, 120m, null),
            new(start.AddDays(2), 0m, 120m, null),
            new(start.AddDays(3), 0m, 120m, null)
        ]);

        Assert.False(window.Days[0].HasCarbohydrateEvidence);
        Assert.Equal(0m, window.Days[0].RestorationCreditG);
        Assert.Equal(50m, window.TotalGlycogenDemandG);
    }

    [Fact]
    public void FunctionalTarget_AppliesAccumulatedDemandBonus_AndCapsAtPointNine()
    {
        var start = new DateOnly(2026, 9, 1);
        var window = _service.BuildHistoricalWindow(
        [
            new(start, 70m, 120m, 120m),
            new(start.AddDays(1), 70m, 120m, 120m),
            new(start.AddDays(2), 70m, 120m, 120m),
            new(start.AddDays(3), 70m, 120m, 120m)
        ]);

        var readiness = _service.CalculateReadiness(
            window,
            plannedGlycogenDemandG: 100m,
            plannedRestorationFraction: 0.85m);

        Assert.Equal(0.90m, readiness.FunctionalTargetFraction);
        Assert.Equal(Engine30FuelDemandClassCodes.VeryHigh, readiness.DemandClassCode);
        Assert.Equal(75m, readiness.RecoveryAllocationCapG);
        Assert.Equal(45m, readiness.PreWorkoutRescueCapG);
    }

    [Theory]
    [InlineData(1.00, Engine30FuelReadinessBandCodes.Adequate)]
    [InlineData(0.85, Engine30FuelReadinessBandCodes.SlightlyReduced)]
    [InlineData(0.70, Engine30FuelReadinessBandCodes.ModeratelyLow)]
    [InlineData(0.50, Engine30FuelReadinessBandCodes.Low)]
    [InlineData(0.49, Engine30FuelReadinessBandCodes.VeryLow)]
    public void ReadinessBands_AreStable(decimal ratio, string expected)
        => Assert.Equal(expected, Engine30FuelStateService.ReadinessBand(ratio));

    [Fact]
    public void Rescue_IsBoundedByPreWorkoutAvailability()
    {
        var start = new DateOnly(2026, 9, 1);
        var window = _service.BuildHistoricalWindow(
        [
            new(start, 100m, 120m, 100m),
            new(start.AddDays(1), 100m, 120m, 100m),
            new(start.AddDays(2), 100m, 120m, 100m),
            new(start.AddDays(3), 100m, 120m, 100m)
        ]);

        var readiness = _service.CalculateReadiness(
            window,
            plannedGlycogenDemandG: 150m,
            plannedRestorationFraction: 0.80m,
            maxCarbohydrateAvailableBeforeWorkoutG: 30m);

        Assert.Equal(30m, readiness.PreWorkoutRescueCapG);
        Assert.Equal(30m, readiness.PreWorkoutRescueG);
        Assert.Contains("PREWORKOUT_TIMING_LIMIT_APPLIED", readiness.ReasonCodes);
    }

    [Fact]
    public void RollForward_DropsOldestDay_AndKeepsFourDays()
    {
        var start = new DateOnly(2026, 9, 1);
        var window = _service.BuildHistoricalWindow(
        [
            new(start, 20m, 120m, 150m),
            new(start.AddDays(1), 30m, 120m, 150m),
            new(start.AddDays(2), 40m, 120m, 150m),
            new(start.AddDays(3), 50m, 120m, 150m)
        ]);

        var projected = _service.BuildProjectedDay(
            new Engine30ProjectedFuelDayInput(
                start.AddDays(4),
                60m,
                180m,
                120m));

        var rolled = _service.RollForward(window, projected);

        Assert.Equal(4, rolled.Days.Count);
        Assert.Equal(start.AddDays(1), rolled.Days[0].Date);
        Assert.Equal(start.AddDays(4), rolled.Days[^1].Date);
        Assert.True(rolled.Days[^1].IsProjected);
    }
}
