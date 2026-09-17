using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30HabitualActivityAgeWindowTests
{
    [Theory]
    [InlineData(30, 60)]
    [InlineData(40, 60)]
    [InlineData(50, 60)]
    [InlineData(55, 55)]
    [InlineData(60, 50)]
    [InlineData(65, 45)]
    [InlineData(70, 40)]
    [InlineData(75, 35)]
    [InlineData(80, 30)]
    [InlineData(90, 30)]
    public void AgePolicy_ReturnsAgreedWeeklyWindow(
        int ageYears,
        int expectedHours)
    {
        var result =
            Engine30HabitualActivityWindowPolicy.Resolve(
                ageYears);

        Assert.Equal(
            (decimal)expectedHours,
            result.HoursPerWeek);

        Assert.Equal(
            Engine30HabitualActivityWindowPolicy.ModelCode,
            result.ModelCode);
    }

    [Fact]
    public void Age40_40HourOfficeWeek_Leaves20HomeEquivalentHours()
    {
        var calculator =
            new Engine30HabitualActivityCalculator();

        var roles =
            new[]
            {
                new Engine30OccupationalRoleInput(
                    OccupationName: "Office",
                    OccupationActivityCode: "SEDENTARY",
                    TypicalHoursPerWeek: 40m,
                    EffectiveOccupationalActivityFactor: 0.20m)
            };

        var result =
            calculator.Calculate(
                baseLivingEnergyKcal: 1680m,
                roles: roles,
                lifestyleActivityCode: "MINIMAL",
                habitualWindowHoursPerWeek:
                    Engine30HabitualActivityWindowPolicy
                        .Resolve(40)
                        .HoursPerWeek);

        Assert.Equal(
            60m,
            result.StandardHabitualHoursPerWeek);

        Assert.Equal(
            40m,
            result.TotalOccupationalHoursPerWeek);

        Assert.Equal(
            20m,
            result.HomeEquivalentHoursPerWeek);

        // 1680/24 = 70 kcal/h.
        // Work: 70 * .20 * 40 / 7 = 80 kcal/day.
        // Home: 70 * .60 * 20 / 7 = 120 kcal/day.
        // Total Bucket #2 = 200 kcal/day.
        Assert.Equal(
            80m,
            result.AverageDailyOccupationalActivityEnergyKcal);

        Assert.Equal(
            120m,
            result.AverageDailyHomeActivityEnergyKcal);

        Assert.Equal(
            200m,
            result.AverageDailyHabitualActivityEnergyKcal);
    }

    [Fact]
    public void ActualWorkHoursAboveAgeWindow_AreAlwaysCountedInFull()
    {
        var calculator =
            new Engine30HabitualActivityCalculator();

        var roles =
            new[]
            {
                new Engine30OccupationalRoleInput(
                    OccupationName: "Long Work Week",
                    OccupationActivityCode: "TEST",
                    TypicalHoursPerWeek: 60m,
                    EffectiveOccupationalActivityFactor: 0.50m)
            };

        var result =
            calculator.Calculate(
                baseLivingEnergyKcal: 1680m,
                roles: roles,
                lifestyleActivityCode: "MINIMAL",
                habitualWindowHoursPerWeek:
                    Engine30HabitualActivityWindowPolicy
                        .Resolve(80)
                        .HoursPerWeek);

        Assert.Equal(
            30m,
            result.StandardHabitualHoursPerWeek);

        Assert.Equal(
            60m,
            result.TotalOccupationalHoursPerWeek);

        Assert.Equal(
            0m,
            result.HomeEquivalentHoursPerWeek);

        // All 60 actual work hours count:
        // 70 kcal/h * .50 * 60 / 7 = 300 kcal/day.
        Assert.Equal(
            300m,
            result.AverageDailyHabitualActivityEnergyKcal);
    }

    [Fact]
    public void LifestylePremium_RemainsSeparateFromWorkHomeWindow()
    {
        var calculator =
            new Engine30HabitualActivityCalculator();

        var roles =
            new[]
            {
                new Engine30OccupationalRoleInput(
                    OccupationName: "Office",
                    OccupationActivityCode: "SEDENTARY",
                    TypicalHoursPerWeek: 40m,
                    EffectiveOccupationalActivityFactor: 0.20m)
            };

        var window =
            Engine30HabitualActivityWindowPolicy
                .Resolve(40)
                .HoursPerWeek;

        var minimal =
            calculator.Calculate(
                baseLivingEnergyKcal: 1680m,
                roles: roles,
                lifestyleActivityCode: "MINIMAL",
                habitualWindowHoursPerWeek: window);

        var light =
            calculator.Calculate(
                baseLivingEnergyKcal: 1680m,
                roles: roles,
                lifestyleActivityCode: "LIGHT",
                habitualWindowHoursPerWeek: window);

        // Existing LIGHT premium remains +4 equivalent hours:
        // 70 * .60 * 4 / 7 = 24 kcal/day.
        Assert.Equal(
            24m,
            light.AverageDailyHabitualActivityEnergyKcal
            - minimal.AverageDailyHabitualActivityEnergyKcal);
    }

    [Fact]
    public void LegacyDirectCalculatorCall_RemainsFortyHourCompatible()
    {
        var calculator =
            new Engine30HabitualActivityCalculator();

        var result =
            calculator.Calculate(
                baseLivingEnergyKcal: 1680m,
                roles:
                [
                    new Engine30OccupationalRoleInput(
                        OccupationName: "Office",
                        OccupationActivityCode: "SEDENTARY",
                        TypicalHoursPerWeek: 40m,
                        EffectiveOccupationalActivityFactor: 0.20m)
                ],
                lifestyleActivityCode: "MINIMAL");

        Assert.Equal(
            40m,
            result.StandardHabitualHoursPerWeek);

        Assert.Equal(
            0m,
            result.HomeEquivalentHoursPerWeek);

        Assert.Equal(
            80m,
            result.AverageDailyHabitualActivityEnergyKcal);
    }

    [Fact]
    public void Policy_IsMonotonicAfter50_AndStopsAtThirtyHourFloor()
    {
        var prior =
            Engine30HabitualActivityWindowPolicy
                .Resolve(50)
                .HoursPerWeek;

        for (var age = 51; age <= 100; age++)
        {
            var current =
                Engine30HabitualActivityWindowPolicy
                    .Resolve(age)
                    .HoursPerWeek;

            Assert.True(current <= prior);
            Assert.True(current >= 30m);

            prior = current;
        }

        Assert.Equal(
            30m,
            Engine30HabitualActivityWindowPolicy
                .Resolve(100)
                .HoursPerWeek);
    }
}