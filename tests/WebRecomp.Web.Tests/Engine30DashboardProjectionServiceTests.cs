using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30DashboardProjectionServiceTests
{
    [Fact]
    public void WeekStart_UsesMondayBoundary()
    {
        Assert.Equal(
            new DateOnly(2026, 8, 31),
            Engine30DashboardProjectionService.WeekStart(
                new DateOnly(2026, 9, 4)));

        Assert.Equal(
            new DateOnly(2026, 8, 31),
            Engine30DashboardProjectionService.WeekStart(
                new DateOnly(2026, 9, 6)));

        Assert.Equal(
            new DateOnly(2026, 9, 7),
            Engine30DashboardProjectionService.WeekStart(
                new DateOnly(2026, 9, 7)));
    }

    [Theory]
    [InlineData("ORANGETHEORY", "ORANGETHEORY")]
    [InlineData("OTF_STANDARD", "ORANGETHEORY")]
    [InlineData("ZONE_2", "ZONE2_CARDIO")]
    [InlineData("Z2_40_RPE4", "ZONE2_CARDIO")]
    [InlineData("RECOVERY_0_RPE2", "REST")]
    [InlineData("MAJOR_MIXED", "FULL_BODY_STRENGTH")]
    [InlineData("HIIT_45_RPE9", "TRUE_HIIT")]
    public void NormalizeWorkoutCode_IsDeterministic(
        string source,
        string expected)
    {
        Assert.Equal(
            expected,
            Engine30DashboardProjectionService.NormalizeWorkoutCode(source));
    }

    [Theory]
    [InlineData(25.0, 0.25)]
    [InlineData(0.25, 0.25)]
    public void BodyFat_IsNormalizedToFraction(
        decimal stored,
        decimal expected)
    {
        Assert.Equal(
            expected,
            Engine30DashboardProjectionService.NormalizeBodyFatFraction(stored));
    }

    [Theory]
    [InlineData("ADEQUATE", "Adequate")]
    [InlineData("SLIGHTLY_REDUCED", "Slightly reduced")]
    [InlineData("MODERATELY_LOW", "Moderately low")]
    [InlineData("LOW", "Low")]
    [InlineData("VERY_LOW", "Very low")]
    public void ReadinessLabels_AreHumanReadable(
        string code,
        string expected)
    {
        Assert.Equal(
            expected,
            Engine30DashboardProjectionService.HumanizeReadiness(code));
    }

    [Fact]
    public void DashboardFuelTarget_UsesSettledDraftValue()
    {
        Assert.Equal(
            0.80m,
            Engine30DashboardProjectionService.DashboardPlannedRestorationFraction);
    }

    [Fact]
    public void VersionIdentity_IsEngine30()
    {
        Assert.Equal(
            "3000.0",
            Engine30DashboardProjectionService.EngineVersionCode);

        Assert.Equal(
            "CFG-3000.0",
            Engine30DashboardProjectionService.ConfigVersionCode);
    }
}
