using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30WorkoutUiCatalogServiceTests
{
    [Theory]
    [InlineData("RECOVERY_0_RPE2", "REST")]
    [InlineData("MOBILITY__POSTURE", "MOBILITY_RECOVERY")]
    [InlineData("Z2_40_RPE4", "ZONE2_CARDIO")]
    [InlineData("ZONE_2", "ZONE2_CARDIO")]
    [InlineData("PURE_CARDIO", "GENERAL_CARDIO")]
    [InlineData("MAJOR_60_RPE8", "FULL_BODY_STRENGTH")]
    [InlineData("MAJOR_MIXED", "FULL_BODY_STRENGTH")]
    [InlineData("ACCESSORY_45_RPE7", "ACCESSORY")]
    [InlineData("ACCESSORY_MIXED", "ACCESSORY")]
    [InlineData("HIIT_45_RPE9", "TRUE_HIIT")]
    [InlineData("OTF_STANDARD", "ORANGETHEORY")]
    [InlineData("ACCESSORY_MIXED_PLUS_CARDIO", "MIXED_GYM_SESSION")]
    [InlineData("LONG_MIXED_75_RPE9", "MIXED_CONDITIONING")]
    [InlineData("PICKLEBALL_120_RPE6", "GENERAL_CARDIO")]
    [InlineData("MAJOR_PUSH", "MAJOR_PUSH")]
    [InlineData("RUNNING_INTERVALS", "RUNNING_INTERVALS")]
    public void NormalizeCode_MapsLegacyTaxonomyIntoEngine30(
        string source,
        string expected)
    {
        Assert.Equal(
            expected,
            Engine30WorkoutUiCatalogService.NormalizeCode(source));
    }

    [Theory]
    [InlineData("REST")]
    [InlineData("ZONE2_CARDIO")]
    [InlineData("GENERAL_CARDIO")]
    [InlineData("FULL_BODY_STRENGTH")]
    [InlineData("ACCESSORY")]
    [InlineData("TRUE_HIIT")]
    [InlineData("ORANGETHEORY")]
    public void NormalizeCode_CurrentEngine30CodesPassThrough(
        string engine30)
    {
        Assert.Equal(
            engine30,
            Engine30WorkoutUiCatalogService.NormalizeCode(engine30));
    }
}
