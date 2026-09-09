using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30ActivityProfileCatalogTests
{
    [Fact]
    public void OccupationCatalog_HasExpectedOrderedCalibrationRange()
    {
        var options = Engine30ActivityProfileCatalog.Occupations;

        Assert.Equal(7, options.Count);
        Assert.Equal("HIGHLY_SEDENTARY", options[0].Code);
        Assert.Equal(0.30m, options[0].EffectiveOccupationalActivityFactor);
        Assert.Equal("HEAVY_PHYSICAL", options[^1].Code);
        Assert.Equal(1.00m, options[^1].EffectiveOccupationalActivityFactor);

        Assert.True(options
            .Zip(options.Skip(1), (a, b) => b.EffectiveOccupationalActivityFactor > a.EffectiveOccupationalActivityFactor)
            .All(x => x));
    }

    [Fact]
    public void Lifestyle_BlankFallsBackToMinimal()
    {
        var result = Engine30ActivityProfileCatalog.ResolveLifestyle(null);
        Assert.Equal("MINIMAL", result.Code);
    }

    [Fact]
    public void OccupationClassification_CanFeedHabitualActivityCalculator()
    {
        var bus = Engine30ActivityProfileCatalog.ResolveOccupation("SEDENTARY_MIXED");
        var it = Engine30ActivityProfileCatalog.ResolveOccupation("HIGHLY_SEDENTARY");

        var result = new Engine30HabitualActivityCalculator().Calculate(
            baseLivingEnergyKcal: 1800m,
            roles:
            [
                new Engine30OccupationalRoleInput("School Bus Driver", bus.Code, 20m, bus.EffectiveOccupationalActivityFactor),
                new Engine30OccupationalRoleInput("IT Consultant", it.Code, 10m, it.EffectiveOccupationalActivityFactor)
            ],
            lifestyleActivityCode: "LIGHT");

        Assert.Equal(30m, result.TotalOccupationalHoursPerWeek);
        Assert.Equal(10m, result.HomeEquivalentHoursPerWeek);
        Assert.True(result.AverageDailyHabitualActivityEnergyKcal > 0m);
        Assert.Equal("LIGHT", result.LifestyleActivityCode);
    }
}
