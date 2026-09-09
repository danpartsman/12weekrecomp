using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30HabitualActivityCalculatorTests
{
    private readonly Engine30BaseLivingCalculator _baseLiving = new();
    private readonly Engine30HabitualActivityCalculator _activity = new();
    private readonly ITestOutputHelper _output;

    public Engine30HabitualActivityCalculatorTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void SyntheticPopulation_FortyHourHabitualModel_PrintsPlausibilityTable()
    {
        var actionDate =
            new DateOnly(2026, 9, 2);

        var cases = new[]
        {
            new TestCase(
                Number: 1,
                Age: 25,
                Sex: "MALE",
                HeightIn: 68m,
                WeightLb: 155m,
                Description: "Software Developer",
                ExpectedBase: 1662.57m,
                ExpectedWorkHours: 40m,
                ExpectedHomeHours: 0m,
                ExpectedWorkPerDay: 118.76m,
                ExpectedHomePerDay: 0m,
                ExpectedHabitualPerDay: 118.76m,
                ExpectedBasePlusHabitual: 1781.33m,
                Roles:
                [
                    new Role(
                        "Software Developer",
                        "HIGHLY_SEDENTARY",
                        40m,
                        0.30m)
                ]),

            new TestCase(
                Number: 2,
                Age: 35,
                Sex: "FEMALE",
                HeightIn: 64m,
                WeightLb: 135m,
                Description: "Accountant",
                ExpectedBase: 1292.35m,
                ExpectedWorkHours: 40m,
                ExpectedHomeHours: 0m,
                ExpectedWorkPerDay: 123.08m,
                ExpectedHomePerDay: 0m,
                ExpectedHabitualPerDay: 123.08m,
                ExpectedBasePlusHabitual: 1415.43m,
                Roles:
                [
                    new Role(
                        "Accountant",
                        "SEDENTARY_LIGHT",
                        40m,
                        0.40m)
                ]),

            new TestCase(
                Number: 3,
                Age: 45,
                Sex: "MALE",
                HeightIn: 70m,
                WeightLb: 190m,
                Description: "Teacher",
                ExpectedBase: 1753.08m,
                ExpectedWorkHours: 40m,
                ExpectedHomeHours: 0m,
                ExpectedWorkPerDay: 250.44m,
                ExpectedHomePerDay: 0m,
                ExpectedHabitualPerDay: 250.44m,
                ExpectedBasePlusHabitual: 2003.52m,
                Roles:
                [
                    new Role(
                        "Teacher",
                        "TYPICAL_MIXED",
                        40m,
                        0.60m)
                ]),

            new TestCase(
                Number: 4,
                Age: 55,
                Sex: "FEMALE",
                HeightIn: 66m,
                WeightLb: 175m,
                Description: "Retail 30h + Home 10h",
                ExpectedBase: 1405.54m,
                ExpectedWorkHours: 30m,
                ExpectedHomeHours: 10m,
                ExpectedWorkPerDay: 188.24m,
                ExpectedHomePerDay: 50.20m,
                ExpectedHabitualPerDay: 238.44m,
                ExpectedBasePlusHabitual: 1643.98m,
                Roles:
                [
                    new Role(
                        "Retail Sales",
                        "ACTIVE_LIGHT",
                        30m,
                        0.75m)
                ]),

            new TestCase(
                Number: 5,
                Age: 65,
                Sex: "MALE",
                HeightIn: 73m,
                WeightLb: 210m,
                Description: "Bus 20h + IT 10h",
                ExpectedBase: 1791.42m,
                ExpectedWorkHours: 30m,
                ExpectedHomeHours: 10m,
                ExpectedWorkPerDay: 138.62m,
                ExpectedHomePerDay: 63.98m,
                ExpectedHabitualPerDay: 202.60m,
                ExpectedBasePlusHabitual: 1994.02m,
                Roles:
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
                Number: 6,
                Age: 30,
                Sex: "FEMALE",
                HeightIn: 69m,
                WeightLb: 155m,
                Description: "Hospital RN 36h",
                ExpectedBase: 1487.44m,
                ExpectedWorkHours: 36m,
                ExpectedHomeHours: 4m,
                ExpectedWorkPerDay: 302.80m,
                ExpectedHomePerDay: 21.25m,
                ExpectedHabitualPerDay: 324.05m,
                ExpectedBasePlusHabitual: 1811.49m,
                Roles:
                [
                    new Role(
                        "Hospital Floor RN",
                        "VERY_ACTIVE",
                        36m,
                        0.95m)
                ]),

            new TestCase(
                Number: 7,
                Age: 40,
                Sex: "MALE",
                HeightIn: 76m,
                WeightLb: 250m,
                Description: "Construction 40h",
                ExpectedBase: 2145.48m,
                ExpectedWorkHours: 40m,
                ExpectedHomeHours: 0m,
                ExpectedWorkPerDay: 510.83m,
                ExpectedHomePerDay: 0m,
                ExpectedHabitualPerDay: 510.83m,
                ExpectedBasePlusHabitual: 2656.31m,
                Roles:
                [
                    new Role(
                        "Construction",
                        "HEAVY_PHYSICAL",
                        40m,
                        1.00m)
                ]),

            new TestCase(
                Number: 8,
                Age: 60,
                Sex: "FEMALE",
                HeightIn: 62m,
                WeightLb: 190m,
                Description: "No Current Work",
                ExpectedBase: 1385.08m,
                ExpectedWorkHours: 0m,
                ExpectedHomeHours: 40m,
                ExpectedWorkPerDay: 0m,
                ExpectedHomePerDay: 197.87m,
                ExpectedHabitualPerDay: 197.87m,
                ExpectedBasePlusHabitual: 1582.95m,
                Roles: []),

            new TestCase(
                Number: 9,
                Age: 50,
                Sex: "MALE",
                HeightIn: 66m,
                WeightLb: 165m,
                Description: "Delivery Driver 40h",
                ExpectedBase: 1551.18m,
                ExpectedWorkHours: 40m,
                ExpectedHomeHours: 0m,
                ExpectedWorkPerDay: 295.46m,
                ExpectedHomePerDay: 0m,
                ExpectedHabitualPerDay: 295.46m,
                ExpectedBasePlusHabitual: 1846.64m,
                Roles:
                [
                    new Role(
                        "Delivery Driver",
                        "ACTIVE",
                        40m,
                        0.80m)
                ]),

            new TestCase(
                Number: 10,
                Age: 70,
                Sex: "FEMALE",
                HeightIn: 67m,
                WeightLb: 145m,
                Description: "Office 15h + Home 25h",
                ExpectedBase: 1210.33m,
                ExpectedWorkHours: 15m,
                ExpectedHomeHours: 25m,
                ExpectedWorkPerDay: 37.82m,
                ExpectedHomePerDay: 108.07m,
                ExpectedHabitualPerDay: 145.89m,
                ExpectedBasePlusHabitual: 1356.22m,
                Roles:
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
            "ENGINE 3.0 — FINAL CANDIDATE BUCKET #2 PLAUSIBILITY TABLE");

        _output.WriteLine(
            "40-hour habitual window | Home EOAF 0.60 | Construction EOAF 1.00");

        _output.WriteLine("");

        _output.WriteLine(
            new string('-', 151));

        _output.WriteLine(
            $"{"#",2} " +
            $"{"Person",-8} " +
            $"{"Scenario",-25} " +
            $"{"Base",9} " +
            $"{"WorkH",7} " +
            $"{"HomeH",7} " +
            $"{"Work/Day",10} " +
            $"{"Home/Day",10} " +
            $"{"Habit/Day",11} " +
            $"{"Base+Habit",12}");

        _output.WriteLine(
            new string('-', 151));

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
                        x =>
                            new Engine30OccupationalRoleInput(
                                OccupationName:
                                    x.Name,

                                OccupationActivityCode:
                                    x.Code,

                                TypicalHoursPerWeek:
                                    x.Hours,

                                EffectiveOccupationalActivityFactor:
                                    x.Factor))
                    .ToArray();

            var result =
                _activity.Calculate(
                    baseResult.BaseLivingEnergyKcal,
                    roles);

            var sex =
                test.Sex == "MALE"
                    ? "M"
                    : "F";

            var person =
                $"{test.Age}{sex}";

            _output.WriteLine(
                $"{test.Number,2} " +
                $"{person,-8} " +
                $"{test.Description,-25} " +
                $"{baseResult.BaseLivingEnergyKcal,9:0.00} " +
                $"{result.TotalOccupationalHoursPerWeek,7:0.0} " +
                $"{result.HomeEquivalentHoursPerWeek,7:0.0} " +
                $"{result.AverageDailyOccupationalActivityEnergyKcal,10:0.00} " +
                $"{result.AverageDailyHomeActivityEnergyKcal,10:0.00} " +
                $"{result.AverageDailyHabitualActivityEnergyKcal,11:0.00} " +
                $"{result.BasePlusHabitualEnergyKcal,12:0.00}");

            Assert.Equal(
                test.ExpectedBase,
                baseResult.BaseLivingEnergyKcal);

            Assert.Equal(
                test.ExpectedWorkHours,
                result.TotalOccupationalHoursPerWeek);

            Assert.Equal(
                test.ExpectedHomeHours,
                result.HomeEquivalentHoursPerWeek);

            Assert.Equal(
                test.ExpectedWorkPerDay,
                result.AverageDailyOccupationalActivityEnergyKcal);

            Assert.Equal(
                test.ExpectedHomePerDay,
                result.AverageDailyHomeActivityEnergyKcal);

            Assert.Equal(
                test.ExpectedHabitualPerDay,
                result.AverageDailyHabitualActivityEnergyKcal);

            Assert.Equal(
                test.ExpectedBasePlusHabitual,
                result.BasePlusHabitualEnergyKcal);
        }

        _output.WriteLine(
            new string('-', 151));

        _output.WriteLine("");
        _output.WriteLine(
            "PASS: all 10 Engine 3.0 Bucket #2 calculations match the accepted candidate model.");
    }

    [Fact]
    public void WorkHoursOverForty_DoNotCreateNegativeHomeHours()
    {
        var result =
            _activity.Calculate(
                1800m,
                [
                    new Engine30OccupationalRoleInput(
                        "Construction Overtime",
                        "HEAVY_PHYSICAL",
                        50m,
                        1.00m)
                ]);

        Assert.Equal(
            50m,
            result.TotalOccupationalHoursPerWeek);

        Assert.Equal(
            0m,
            result.HomeEquivalentHoursPerWeek);

        Assert.True(
            result.WeeklyOccupationalActivityEnergyKcal > 0m);

        Assert.Equal(
            0m,
            result.WeeklyHomeActivityEnergyKcal);
    }

    [Fact]
    public void MultipleJobs_ConsumeTheSameFortyHourWindow()
    {
        var result =
            _activity.Calculate(
                1791.42m,
                [
                    new Engine30OccupationalRoleInput(
                        "School Bus Driver",
                        "SEDENTARY_LIGHT",
                        20m,
                        0.50m),

                    new Engine30OccupationalRoleInput(
                        "IT Consultant",
                        "HIGHLY_SEDENTARY",
                        10m,
                        0.30m)
                ]);

        Assert.Equal(
            30m,
            result.TotalOccupationalHoursPerWeek);

        Assert.Equal(
            10m,
            result.HomeEquivalentHoursPerWeek);

        Assert.Equal(
            202.60m,
            result.AverageDailyHabitualActivityEnergyKcal);
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
        decimal ExpectedBase,
        decimal ExpectedWorkHours,
        decimal ExpectedHomeHours,
        decimal ExpectedWorkPerDay,
        decimal ExpectedHomePerDay,
        decimal ExpectedHabitualPerDay,
        decimal ExpectedBasePlusHabitual,
        IReadOnlyCollection<Role> Roles);
}
