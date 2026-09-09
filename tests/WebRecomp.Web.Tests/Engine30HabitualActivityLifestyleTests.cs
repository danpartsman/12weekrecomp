using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30HabitualActivityLifestyleTests
{
    private readonly Engine30BaseLivingCalculator _baseLiving = new();
    private readonly Engine30HabitualActivityCalculator _activity = new();
    private readonly ITestOutputHelper _output;

    public Engine30HabitualActivityLifestyleTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TenPersonPopulation_WithLifestylePremium_PrintsPlausibilityTable()
    {
        var actionDate =
            new DateOnly(2026, 9, 2);

        var cases = new[]
        {
            new TestCase(
                1, 25, "MALE", 68m, 155m,
                "Developer 40h",
                "MODERATE",
                8m,
                1662.57m,
                118.76m,
                0.00m,
                47.50m,
                166.26m,
                1828.83m,
                [
                    new Role(
                        "Software Developer",
                        "HIGHLY_SEDENTARY",
                        40m,
                        0.30m)
                ]),

            new TestCase(
                2, 35, "FEMALE", 64m, 135m,
                "Accountant 40h",
                "MINIMAL",
                0m,
                1292.35m,
                123.08m,
                0.00m,
                0.00m,
                123.08m,
                1415.43m,
                [
                    new Role(
                        "Accountant",
                        "SEDENTARY_LIGHT",
                        40m,
                        0.40m)
                ]),

            new TestCase(
                3, 45, "MALE", 70m, 190m,
                "Teacher 40h",
                "LIGHT",
                4m,
                1753.08m,
                250.44m,
                0.00m,
                25.04m,
                275.48m,
                2028.56m,
                [
                    new Role(
                        "Teacher",
                        "TYPICAL_MIXED",
                        40m,
                        0.60m)
                ]),

            new TestCase(
                4, 55, "FEMALE", 66m, 175m,
                "Retail 30h",
                "MODERATE",
                8m,
                1405.54m,
                188.24m,
                50.20m,
                40.16m,
                278.60m,
                1684.14m,
                [
                    new Role(
                        "Retail Sales",
                        "ACTIVE_LIGHT",
                        30m,
                        0.75m)
                ]),

            new TestCase(
                5, 65, "MALE", 73m, 210m,
                "Bus 20h + IT 10h",
                "LIGHT",
                4m,
                1791.42m,
                138.62m,
                63.98m,
                25.59m,
                228.19m,
                2019.61m,
                [
                    new Role(
                        "School Bus Driver",
                        "SEDENTARY_LIGHT",
                        20m,
                        0.50m),

                    new Role(
                        "IT Consultant",
                        "HIGHLY_SEDENTARY",
                        10m,
                        0.30m)
                ]),

            new TestCase(
                6, 30, "FEMALE", 69m, 155m,
                "Hospital RN 36h",
                "MINIMAL",
                0m,
                1487.44m,
                302.80m,
                21.25m,
                0.00m,
                324.05m,
                1811.49m,
                [
                    new Role(
                        "Hospital Floor RN",
                        "VERY_ACTIVE",
                        36m,
                        0.95m)
                ]),

            new TestCase(
                7, 40, "MALE", 76m, 250m,
                "Construction 40h",
                "MINIMAL",
                0m,
                2145.48m,
                510.83m,
                0.00m,
                0.00m,
                510.83m,
                2656.31m,
                [
                    new Role(
                        "Construction",
                        "HEAVY_PHYSICAL",
                        40m,
                        1.00m)
                ]),

            new TestCase(
                8, 60, "FEMALE", 62m, 190m,
                "No Current Work",
                "VERY_ACTIVE",
                16m,
                1385.08m,
                0.00m,
                197.87m,
                79.15m,
                277.02m,
                1662.10m,
                []),

            new TestCase(
                9, 50, "MALE", 66m, 165m,
                "Delivery 40h",
                "MODERATE",
                8m,
                1551.18m,
                295.46m,
                0.00m,
                44.32m,
                339.78m,
                1890.96m,
                [
                    new Role(
                        "Delivery Driver",
                        "ACTIVE",
                        40m,
                        0.80m)
                ]),

            new TestCase(
                10, 70, "FEMALE", 67m, 145m,
                "Office 15h",
                "VERY_ACTIVE",
                16m,
                1210.33m,
                37.82m,
                108.07m,
                69.16m,
                215.05m,
                1425.38m,
                [
                    new Role(
                        "Part-Time Office",
                        "SEDENTARY_LIGHT",
                        15m,
                        0.35m)
                ])
        };

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 — BUCKET #2 WITH LIFESTYLE PREMIUM");

        _output.WriteLine(
            "40-hour work/home window | Lifestyle premium 0 / 4 / 8 / 16 hours");

        _output.WriteLine("");

        _output.WriteLine(
            new string('-', 166));

        _output.WriteLine(
            $"{"#",2} " +
            $"{"Person",-7} " +
            $"{"Scenario",-20} " +
            $"{"Lifestyle",-12} " +
            $"{"PremH",6} " +
            $"{"Base",9} " +
            $"{"Work/Day",10} " +
            $"{"Home/Day",10} " +
            $"{"Life/Day",10} " +
            $"{"Habit/Day",11} " +
            $"{"Base+Habit",12}");

        _output.WriteLine(
            new string('-', 166));

        foreach (var test in cases)
        {
            var dateOfBirth =
                new DateOnly(
                    actionDate.Year - test.Age,
                    1,
                    1);

            var baseResult =
                _baseLiving.Calculate(
                    new Engine30BaseLivingInput(
                        ActionDate:
                            actionDate,

                        DateOfBirth:
                            dateOfBirth,

                        SexCode:
                            test.Sex,

                        HeightIn:
                            test.HeightIn,

                        WeightLb:
                            test.WeightLb));

            var roles =
                test.Roles
                    .Select(
                        role =>
                            new Engine30OccupationalRoleInput(
                                OccupationName:
                                    role.Name,

                                OccupationActivityCode:
                                    role.Code,

                                TypicalHoursPerWeek:
                                    role.Hours,

                                EffectiveOccupationalActivityFactor:
                                    role.Factor))
                    .ToArray();

            var result =
                _activity.Calculate(
                    baseResult.BaseLivingEnergyKcal,
                    roles,
                    test.LifestyleCode);

            var sex =
                test.Sex == "MALE"
                    ? "M"
                    : "F";

            _output.WriteLine(
                $"{test.Number,2} " +
                $"{test.Age + sex,-7} " +
                $"{test.Description,-20} " +
                $"{result.LifestyleActivityCode,-12} " +
                $"{result.LifestyleEquivalentHoursPerWeek,6:0} " +
                $"{result.BaseLivingEnergyKcal,9:0.00} " +
                $"{result.AverageDailyOccupationalActivityEnergyKcal,10:0.00} " +
                $"{result.AverageDailyHomeActivityEnergyKcal,10:0.00} " +
                $"{result.AverageDailyLifestyleActivityEnergyKcal,10:0.00} " +
                $"{result.AverageDailyHabitualActivityEnergyKcal,11:0.00} " +
                $"{result.BasePlusHabitualEnergyKcal,12:0.00}");

            Assert.Equal(
                test.ExpectedBase,
                result.BaseLivingEnergyKcal);

            Assert.Equal(
                test.ExpectedLifestyleHours,
                result.LifestyleEquivalentHoursPerWeek);

            Assert.Equal(
                test.ExpectedWorkPerDay,
                result.AverageDailyOccupationalActivityEnergyKcal);

            Assert.Equal(
                test.ExpectedHomePerDay,
                result.AverageDailyHomeActivityEnergyKcal);

            Assert.Equal(
                test.ExpectedLifestylePerDay,
                result.AverageDailyLifestyleActivityEnergyKcal);

            Assert.Equal(
                test.ExpectedHabitualPerDay,
                result.AverageDailyHabitualActivityEnergyKcal);

            Assert.Equal(
                test.ExpectedBasePlusHabitual,
                result.BasePlusHabitualEnergyKcal);
        }

        _output.WriteLine(
            new string('-', 166));

        _output.WriteLine("");
        _output.WriteLine(
            "PASS: all 10 lifestyle-adjusted Bucket #2 calculations match.");
    }

    [Theory]
    [InlineData("MINIMAL", 0)]
    [InlineData("LIGHT", 4)]
    [InlineData("MODERATE", 8)]
    [InlineData("VERY_ACTIVE", 16)]
    public void LifestyleCodes_MapToExpectedEquivalentHours(
        string lifestyleCode,
        int expectedHours)
    {
        var result =
            _activity.Calculate(
                1800m,
                Array.Empty<Engine30OccupationalRoleInput>(),
                lifestyleCode);

        Assert.Equal(
            expectedHours,
            result.LifestyleEquivalentHoursPerWeek);
    }

    [Fact]
    public void LifestylePremium_IsMonotonic()
    {
        var roles =
            new[]
            {
                new Engine30OccupationalRoleInput(
                    "Office",
                    "SEDENTARY_LIGHT",
                    40m,
                    0.35m)
            };

        var minimal =
            _activity.Calculate(
                1800m,
                roles,
                "MINIMAL");

        var light =
            _activity.Calculate(
                1800m,
                roles,
                "LIGHT");

        var moderate =
            _activity.Calculate(
                1800m,
                roles,
                "MODERATE");

        var veryActive =
            _activity.Calculate(
                1800m,
                roles,
                "VERY_ACTIVE");

        Assert.True(
            minimal.AverageDailyHabitualActivityEnergyKcal <
            light.AverageDailyHabitualActivityEnergyKcal);

        Assert.True(
            light.AverageDailyHabitualActivityEnergyKcal <
            moderate.AverageDailyHabitualActivityEnergyKcal);

        Assert.True(
            moderate.AverageDailyHabitualActivityEnergyKcal <
            veryActive.AverageDailyHabitualActivityEnergyKcal);
    }

    private sealed record Role(
        string Name,
        string Code,
        decimal Hours,
        decimal Factor);

    private sealed record TestCase(
        int Number,
        int Age,
        string Sex,
        decimal HeightIn,
        decimal WeightLb,
        string Description,
        string LifestyleCode,
        decimal ExpectedLifestyleHours,
        decimal ExpectedBase,
        decimal ExpectedWorkPerDay,
        decimal ExpectedHomePerDay,
        decimal ExpectedLifestylePerDay,
        decimal ExpectedHabitualPerDay,
        decimal ExpectedBasePlusHabitual,
        IReadOnlyCollection<Role> Roles);
}
