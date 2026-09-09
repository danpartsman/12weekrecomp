using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30LegacyWorkoutCompatibilityTests
{
    [Fact]
    public void AllTwentySevenEngine30WorkoutCodesHaveExplicitLegacyMappings()
    {
        var expected =
            new[]
            {
                "REST",
                "MOBILITY_RECOVERY",
                "GENERAL_CARDIO",
                "EASY_CARDIO",
                "ZONE2_CARDIO",
                "WALKING",
                "RUNNING",
                "TREADMILL",
                "CYCLING",
                "ROWING",
                "ELLIPTICAL",
                "STAIR_CLIMBER",
                "SWIMMING",
                "MAJOR_LEGS",
                "MAJOR_PUSH",
                "MAJOR_PULL",
                "FULL_BODY_STRENGTH",
                "ACCESSORY",
                "RESISTANCE_CIRCUIT",
                "RUNNING_INTERVALS",
                "CYCLING_INTERVALS",
                "ROWING_INTERVALS",
                "MIXED_CONDITIONING",
                "TRUE_HIIT",
                "ORANGETHEORY",
                "MIXED_GYM_SESSION",
                "CUSTOM_COMPOSITE"
            };

        Assert.Equal(
            27,
            Engine30LegacyWorkoutCompatibility.Engine30Codes.Count);

        Assert.Empty(
            expected.Except(
                Engine30LegacyWorkoutCompatibility.Engine30Codes,
                StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("REST", "RECOVERY_0_RPE2")]
    [InlineData("MOBILITY_RECOVERY", "MOBILITY__POSTURE")]
    [InlineData("ZONE2_CARDIO", "Z2_40_RPE4")]
    [InlineData("RUNNING", "PURE_CARDIO")]
    [InlineData("MAJOR_LEGS", "MAJOR_LEGS")]
    [InlineData("MAJOR_PUSH", "MAJOR_PUSH")]
    [InlineData("MAJOR_PULL", "MAJOR_PULL")]
    [InlineData("FULL_BODY_STRENGTH", "MAJOR_MIXED")]
    [InlineData("ACCESSORY", "ACCESSORY_MIXED")]
    [InlineData("RUNNING_INTERVALS", "HIIT_45_RPE9")]
    [InlineData("ORANGETHEORY", "OTF_STANDARD")]
    [InlineData("MIXED_GYM_SESSION", "ACCESSORY_MIXED_PLUS_CARDIO")]
    [InlineData("CUSTOM_COMPOSITE", "ACC_PLUS_Z2_75_RPE6")]
    public void RepresentativeMappingsAreDeterministic(
        string engine30,
        string expectedEngine2)
    {
        Assert.Equal(
            expectedEngine2,
            Engine30LegacyWorkoutCompatibility.ToEngine2(
                engine30));
    }

    [Fact]
    public void ExistingLegacyWorkoutCodesPassThroughUnchanged()
    {
        Assert.Equal(
            "RECOVERY_0_RPE2",
            Engine30LegacyWorkoutCompatibility.ToEngine2(
                "RECOVERY_0_RPE2"));

        Assert.Equal(
            "OTF_STANDARD",
            Engine30LegacyWorkoutCompatibility.ToEngine2(
                "OTF_STANDARD"));
    }
}
