using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30StableFatTargetTests
{
    [Fact]
    public void HighEnergyWeek_DoesNotInflateStableFatAboveWeightBasedTarget()
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

        Assert.Equal(50m, result.StableFatG);
        Assert.All(result.Days, day => Assert.Equal(50m, day.FatG));
    }
}
