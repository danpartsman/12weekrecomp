using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30StableFatTargetTests
{
    [Fact]
    public void NormalWeek_PreservesEnergySafetyAfterRecoveryTaper()
    {
        var days =
            Enumerable.Range(0, 7)
                .Select(i =>
                    new Engine30PrescriptionDayInput(
                        new DateOnly(2026, 9, 7).AddDays(i),
                        2250m,
                        i is 0 or 3
                            ? Engine30PrescriptionCategoryCodes.MajorStrength
                            : i == 6
                                ? Engine30PrescriptionCategoryCodes.RestRecoveryOther
                                : Engine30PrescriptionCategoryCodes.AccessoryStrength,
                        i is 0 or 3 ? 100m : 50m))
                .ToArray();

        var result =
            new Engine30PrescriptionService()
                .Calculate(
                    "PLAN_2",
                    weightLb: 208m,
                    heightIn: 73m,
                    days: days);

        Assert.Equal(55m, result.StableFatG);
        Assert.All(result.Days, day => Assert.Equal(55m, day.FatG));
        Assert.Contains(
            "STABLE_FAT_RAISED_FOR_ENERGY_SAFETY_GUARDRAIL",
            result.ReasonCodes);
    }

    [Fact]
    public void ExtremeEnergyGap_RaisesFatRatherThanBackfillingCarbohydrate()
    {
        var days =
            Enumerable.Range(0, 7)
                .Select(i =>
                    new Engine30PrescriptionDayInput(
                        new DateOnly(2026, 9, 7).AddDays(i),
                        4000m,
                        i is 0 or 3
                            ? Engine30PrescriptionCategoryCodes.MajorStrength
                            : i == 6
                                ? Engine30PrescriptionCategoryCodes.RestRecoveryOther
                                : Engine30PrescriptionCategoryCodes.AccessoryStrength,
                        i is 0 or 3 ? 100m : 25m))
                .ToArray();

        var result =
            new Engine30PrescriptionService()
                .Calculate(
                    "PLAN_2",
                    weightLb: 208m,
                    heightIn: 73m,
                    days: days);

        Assert.True(result.StableFatG > 50m);
        Assert.All(
            result.Days,
            day => Assert.Equal(result.StableFatG, day.FatG));

        Assert.Equal(100m, result.BaseRestCarbsG);
        Assert.Equal(170m, result.Days[0].RecommendedCarbsG);
        Assert.Equal(120m, result.Days[1].RecommendedCarbsG);

        Assert.Contains(
            "STABLE_FAT_RAISED_FOR_ENERGY_SAFETY_GUARDRAIL",
            result.ReasonCodes);
    }
}
