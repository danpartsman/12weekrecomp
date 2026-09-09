using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30DashboardLegacyHistoryMappingTests
{
    [Theory]
    [InlineData("RECOVERY_0_RPE2", "REST")]
    [InlineData("MOBILITY__POSTURE", "MOBILITY_RECOVERY")]
    [InlineData("ZONE_2", "ZONE2_CARDIO")]
    [InlineData("Z2_40_RPE4", "ZONE2_CARDIO")]
    [InlineData("PURE_CARDIO", "GENERAL_CARDIO")]

    [InlineData("MAJOR_60_RPE8", "FULL_BODY_STRENGTH")]
    [InlineData("MAJOR_MIXED", "FULL_BODY_STRENGTH")]
    [InlineData("MAJOR_LEGS", "MAJOR_LEGS")]
    [InlineData("MAJOR_PUSH", "MAJOR_PUSH")]
    [InlineData("MAJOR_PULL", "MAJOR_PULL")]

    [InlineData("ACCESSORY_45_RPE7", "ACCESSORY")]
    [InlineData("ACCESSORY_UPPER", "ACCESSORY")]
    [InlineData("ACCESSORY_LOWER", "ACCESSORY")]
    [InlineData("ACCESSORY_MIXED", "ACCESSORY")]
    [InlineData("OTF_STRENGTH_UPPER", "ACCESSORY")]
    [InlineData("OTF_STRENGTH_LOWER", "ACCESSORY")]
    [InlineData("OTF_STRENGTH_TOTAL", "ACCESSORY")]

    [InlineData("ACCESSORY_MIXED_PLUS_CARDIO", "MIXED_GYM_SESSION")]
    [InlineData("ACC_PLUS_Z2_75_RPE6", "CUSTOM_COMPOSITE")]

    [InlineData("HIIT_45_RPE9", "TRUE_HIIT")]
    [InlineData("LONG_MIXED_75_RPE9", "MIXED_CONDITIONING")]
    [InlineData("OTF_STANDARD", "ORANGETHEORY")]
    [InlineData("PICKLEBALL_120_RPE6", "MIXED_CONDITIONING")]

    [InlineData("ORANGETHEORY", "ORANGETHEORY")]
    [InlineData("FULL_BODY_STRENGTH", "FULL_BODY_STRENGTH")]
    [InlineData("ACCESSORY", "ACCESSORY")]
    public void KnownHistoricalWorkoutCodesResolveDeterministically(
        string historicalCode,
        string expectedEngine30Code)
    {
        Assert.Equal(
            expectedEngine30Code,
            Engine30DashboardProjectionService.NormalizeWorkoutCode(
                historicalCode));
    }

    [Fact]
    public void UnknownHistoricalCodePassesThroughForSafeRuntimeHandling()
    {
        Assert.Equal(
            "LEGACY_UNKNOWN_EXAMPLE",
            Engine30DashboardProjectionService.NormalizeWorkoutCode(
                "legacy_unknown_example"));
    }
}
