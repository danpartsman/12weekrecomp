using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30SameDayGlycogenCalculatorTests
{
    [Fact]
    public void OverlappingSecondWorkout_UsesRemainingRegionalPool()
    {
        var first =
            Session(
                "LOWER_BODY",
                regionBaselineG: 100m,
                regionalDepletionFraction: 0.50m);

        var second =
            Session(
                "LOWER_BODY",
                regionBaselineG: 100m,
                regionalDepletionFraction: 0.50m);

        var result =
            new Engine30SameDayGlycogenCalculator()
                .Calculate([first, second]);

        Assert.Equal(100m, result.FreshPoolModeledDepletionG);
        Assert.Equal(75m, result.SequentialDepletionG);
        Assert.Equal(75m, result.SequentialCarbohydrateReloadG);

        Assert.Equal(50m, result.Sessions[0].SequentialDepletionG);
        Assert.Equal(25m, result.Sessions[1].SequentialDepletionG);

        Assert.Contains(
            "OVERLAPPING_MUSCLE_POOL_REDUCTION_APPLIED",
            result.ReasonCodes);
    }

    [Fact]
    public void SeparateUpperAndLowerWorkouts_DoNotArtificiallySuppressEachOther()
    {
        var lower =
            Session(
                "LOWER_BODY",
                regionBaselineG: 100m,
                regionalDepletionFraction: 0.50m);

        var upper =
            Session(
                "UPPER_BODY",
                regionBaselineG: 80m,
                regionalDepletionFraction: 0.50m);

        var result =
            new Engine30SameDayGlycogenCalculator()
                .Calculate([lower, upper]);

        Assert.Equal(90m, result.FreshPoolModeledDepletionG);
        Assert.Equal(90m, result.SequentialDepletionG);
    }

    [Fact]
    public void ThreeOverlappingSessions_ShowProgressiveDiminishingAvailablePool()
    {
        var session =
            Session(
                "LOWER_BODY",
                regionBaselineG: 120m,
                regionalDepletionFraction: 0.40m);

        var result =
            new Engine30SameDayGlycogenCalculator()
                .Calculate([session, session, session]);

        Assert.Equal(48m, result.Sessions[0].SequentialDepletionG);
        Assert.Equal(28.80m, result.Sessions[1].SequentialDepletionG);
        Assert.Equal(17.28m, result.Sessions[2].SequentialDepletionG);

        Assert.Equal(94.08m, result.SequentialDepletionG);
        Assert.True(
            result.SequentialDepletionG
            < result.FreshPoolModeledDepletionG);
    }

    private static Engine30WorkoutGlycogenAdapterResult Session(
        string region,
        decimal regionBaselineG,
        decimal regionalDepletionFraction)
    {
        var depleted =
            regionBaselineG
            * regionalDepletionFraction;

        return new Engine30WorkoutGlycogenAdapterResult(
            ModelCode: "TEST",
            EstimatedGlycogenDepletedG: depleted,
            TargetGlycogenRestorationG: depleted,
            CarbohydrateReloadRequirementG: depleted,
            CarbohydrateReloadEnergyKcal: depleted * 4m,
            Regions:
            [
                new Engine30WorkoutGlycogenRegionResult(
                    MuscleGroupCode: region,
                    WholeBodySkeletalMuscleFraction: 0.50m,
                    RegionBaselineGlycogenG: regionBaselineG,
                    EffectiveRegionalDepletionFraction:
                        regionalDepletionFraction,
                    EstimatedGlycogenDepletedG: depleted)
            ]);
    }
}
