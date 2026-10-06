using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30BackgroundCarbohydrateCalculatorTests
{
    private readonly Engine30HabitualActivityCalculator _habitual = new();
    private readonly Engine30BackgroundCarbohydrateCalculator _carbs = new();

    [Fact]
    public void NoHabitualActivity_ReturnsOnlyObligatoryGlucoseAnchor()
    {
        var habitual =
            _habitual.Calculate(
                baseLivingEnergyKcal: 1800m,
                roles: Array.Empty<Engine30OccupationalRoleInput>(),
                homeActivityFactor: 0m,
                habitualWindowHoursPerWeek: 0m);

        var result = _carbs.Calculate(habitual);

        Assert.Equal(100m, result.ObligatoryGlucoseRequirementG);
        Assert.Equal(0m, result.HabitualActivityCarbohydrateG);
        Assert.Equal(100m, result.TotalBackgroundCarbohydrateRequirementG);
    }

    [Fact]
    public void BusDriverAndItProfile_ProducesCalculatedBackground_NotUniversalHundred()
    {
        // Existing Engine 3.0 synthetic profile: 65M, 210 lb equivalent
        // Base Living 1,791.42; Bus 20 h at factor .50; IT 10 h at .30;
        // remaining 10 h of the habitual window at home factor .60.
        var habitual =
            _habitual.Calculate(
                1791.42m,
                [
                    new Engine30OccupationalRoleInput(
                        "School Bus Driver",
                        "SEDENTARY_LIGHT",
                        20m,
                        0.50m),
                    new Engine30OccupationalRoleInput(
                        "IT Consultant",
                        "HIGHLY_SEDENTARY",
                        10m,
                        0.30m)
                ]);

        var result = _carbs.Calculate(habitual);

        Assert.Equal(100m, result.ObligatoryGlucoseRequirementG);
        Assert.Equal(9.13m, result.OccupationalActivityCarbohydrateG);
        Assert.Equal(4.80m, result.HomeActivityCarbohydrateG);
        Assert.Equal(0m, result.LifestyleActivityCarbohydrateG);
        Assert.Equal(13.93m, result.HabitualActivityCarbohydrateG);
        Assert.Equal(100m, result.TotalBackgroundCarbohydrateRequirementG);
    }

    [Fact]
    public void MoreHabitualWork_ChangesOxidationDiagnostic_NotDietaryEatBack()
    {
        var sedentary =
            _habitual.Calculate(
                1800m,
                [
                    new Engine30OccupationalRoleInput(
                        "Desk",
                        "HIGHLY_SEDENTARY",
                        40m,
                        0.30m)
                ]);

        var physical =
            _habitual.Calculate(
                1800m,
                [
                    new Engine30OccupationalRoleInput(
                        "Physical Work",
                        "HEAVY_PHYSICAL",
                        40m,
                        1.00m)
                ]);

        var sedentaryResult = _carbs.Calculate(sedentary);
        var physicalResult = _carbs.Calculate(physical);

        Assert.Equal(
            sedentaryResult.TotalBackgroundCarbohydrateRequirementG,
            physicalResult.TotalBackgroundCarbohydrateRequirementG);

        Assert.Equal(100m, physicalResult.TotalBackgroundCarbohydrateRequirementG);

        Assert.True(
            physicalResult.HabitualActivityCarbohydrateG
            > sedentaryResult.HabitualActivityCarbohydrateG);
    }
    [Fact]
    public void Prescription_AddsCalculatedBackgroundToWorkoutReload_NotCalories()
    {
        var days =
            Enumerable.Range(0, 7)
                .Select(i =>
                    new Engine30PrescriptionDayInput(
                        new DateOnly(2026, 9, 7).AddDays(i),
                        MaintenanceEnergyKcal: 2400m,
                        WorkoutCategoryCode:
                            Engine30PrescriptionCategoryCodes.ConditioningCardio,
                        GlycogenReloadRequirementG: 78m,
                        BackgroundCarbohydrateRequirementG: 100m))
                .ToArray();

        var result =
            new Engine30PrescriptionService()
                .Calculate(
                    "PLAN_2",
                    weightLb: 208m,
                    heightIn: 73m,
                    days: days);

        Assert.Equal(150m, result.Days[0].RecommendedCarbsG);

        Assert.All(
            result.Days.Skip(1).Take(5),
            day => Assert.Equal(180m, day.RecommendedCarbsG));

        Assert.Equal(
            175m,
            result.Days[^1].RecommendedCarbsG);

        Assert.Equal(100m, result.BaseRestCarbsG);
        Assert.DoesNotContain(
            result.Days,
            day => day.CarbGoverningRuleCode == "ENERGY_BALANCE");
    }


    [Theory]
    [InlineData(0.30, 0.225)]
    [InlineData(0.50, 0.275)]
    [InlineData(0.60, 0.300)]
    [InlineData(1.00, 0.400)]
    [InlineData(2.00, 0.400)]
    public void HabitualSubstrateFraction_IsBoundedAndIntensityResponsive(
        double activityFactor,
        double expectedFraction)
    {
        var actual =
            Engine30BackgroundCarbohydrateCalculator
                .ResolveIncrementalCarbohydrateFraction(
                    (decimal)activityFactor);

        Assert.Equal((decimal)expectedFraction, actual);
    }
}
