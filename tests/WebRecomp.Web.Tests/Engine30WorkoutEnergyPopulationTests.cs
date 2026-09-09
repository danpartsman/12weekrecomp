using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30WorkoutEnergyPopulationTests
{
    private readonly Engine30BaseLivingCalculator _baseLiving = new();
    private readonly Engine30HabitualActivityCalculator _habitual = new();
    private readonly Engine30WorkoutEnergyCalculator _workout = new();
    private readonly ITestOutputHelper _output;

    public Engine30WorkoutEnergyPopulationTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TenPersonPopulation_PrintsCompleteMaintenanceTable()
    {
        var actionDate =
            new DateOnly(2026, 9, 2);

        /*
            WORKOUT REFERENCE VALUES ARE DRAFT TEST VALUES.

            Reference body weight = 180 lb.

            Major strength      5.50 gross kcal/min
            Accessory strength  4.50 gross kcal/min
            Zone 2 cardio       6.00 gross kcal/min
            Conditioning        8.00 gross kcal/min
            OTF / HIIT          9.00 gross kcal/min
            Brisk walking       5.00 gross kcal/min

            They are being tested here before becoming
            CFG-3000.0 configuration.
        */

        var cases = new[]
        {
            new TestCase(
                Number: 1,
                Age: 25,
                Sex: "MALE",
                HeightIn: 68m,
                WeightLb: 155m,
                Scenario: "Developer",
                LifestyleCode: "MODERATE",
                Roles:
                [
                    new Role(
                        "Software Developer",
                        "HIGHLY_SEDENTARY",
                        40m,
                        0.30m)
                ],
                Workout:
                    new Workout(
                        "MAJOR_PUSH",
                        60m,
                        5.50m,
                        180m,
                        8m,
                        90m,
                        90m),
                ExpectedHabitualBase: 1828.83m,
                ExpectedGrossWorkout: 289.85m,
                ExpectedRestingWorkout: 69.27m,
                ExpectedNetWorkout: 220.58m,
                ExpectedMaintenance: 2049.41m),

            new TestCase(
                Number: 2,
                Age: 35,
                Sex: "FEMALE",
                HeightIn: 64m,
                WeightLb: 135m,
                Scenario: "Accountant",
                LifestyleCode: "MINIMAL",
                Roles:
                [
                    new Role(
                        "Accountant",
                        "SEDENTARY_LIGHT",
                        40m,
                        0.40m)
                ],
                Workout: null,
                ExpectedHabitualBase: 1415.43m,
                ExpectedGrossWorkout: 0m,
                ExpectedRestingWorkout: 0m,
                ExpectedNetWorkout: 0m,
                ExpectedMaintenance: 1415.43m),

            new TestCase(
                Number: 3,
                Age: 45,
                Sex: "MALE",
                HeightIn: 70m,
                WeightLb: 190m,
                Scenario: "Teacher",
                LifestyleCode: "LIGHT",
                Roles:
                [
                    new Role(
                        "Teacher",
                        "TYPICAL_MIXED",
                        40m,
                        0.60m)
                ],
                Workout:
                    new Workout(
                        "MAJOR_PULL",
                        60m,
                        5.50m,
                        180m,
                        8m,
                        90m,
                        75m),
                ExpectedHabitualBase: 2028.56m,
                ExpectedGrossWorkout: 426.36m,
                ExpectedRestingWorkout: 73.05m,
                ExpectedNetWorkout: 353.32m,
                ExpectedMaintenance: 2381.88m),

            new TestCase(
                Number: 4,
                Age: 55,
                Sex: "FEMALE",
                HeightIn: 66m,
                WeightLb: 175m,
                Scenario: "Retail",
                LifestyleCode: "MODERATE",
                Roles:
                [
                    new Role(
                        "Retail Sales",
                        "ACTIVE_LIGHT",
                        30m,
                        0.75m)
                ],
                Workout:
                    new Workout(
                        "ZONE2_CARDIO",
                        40m,
                        6.00m,
                        180m,
                        5m,
                        null,
                        null),
                ExpectedHabitualBase: 1684.14m,
                ExpectedGrossWorkout: 210.00m,
                ExpectedRestingWorkout: 39.04m,
                ExpectedNetWorkout: 170.96m,
                ExpectedMaintenance: 1855.10m),

            new TestCase(
                Number: 5,
                Age: 65,
                Sex: "MALE",
                HeightIn: 73m,
                WeightLb: 210m,
                Scenario: "Bus + IT",
                LifestyleCode: "LIGHT",
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
                ],
                Workout:
                    new Workout(
                        "MAJOR_LEGS",
                        60m,
                        5.50m,
                        180m,
                        8m,
                        90m,
                        120m),
                ExpectedHabitualBase: 2019.61m,
                ExpectedGrossWorkout: 294.53m,
                ExpectedRestingWorkout: 74.64m,
                ExpectedNetWorkout: 219.88m,
                ExpectedMaintenance: 2239.49m),

            new TestCase(
                Number: 6,
                Age: 30,
                Sex: "FEMALE",
                HeightIn: 69m,
                WeightLb: 155m,
                Scenario: "Hospital RN",
                LifestyleCode: "MINIMAL",
                Roles:
                [
                    new Role(
                        "Hospital Floor RN",
                        "VERY_ACTIVE",
                        36m,
                        0.95m)
                ],
                Workout:
                    new Workout(
                        "OTF_HIIT",
                        60m,
                        9.00m,
                        180m,
                        9m,
                        null,
                        null),
                ExpectedHabitualBase: 1811.49m,
                ExpectedGrossWorkout: 492.90m,
                ExpectedRestingWorkout: 61.98m,
                ExpectedNetWorkout: 430.92m,
                ExpectedMaintenance: 2242.41m),

            new TestCase(
                Number: 7,
                Age: 40,
                Sex: "MALE",
                HeightIn: 76m,
                WeightLb: 250m,
                Scenario: "Construction",
                LifestyleCode: "MINIMAL",
                Roles:
                [
                    new Role(
                        "Construction",
                        "HEAVY_PHYSICAL",
                        40m,
                        1.00m)
                ],
                Workout: null,
                ExpectedHabitualBase: 2656.31m,
                ExpectedGrossWorkout: 0m,
                ExpectedRestingWorkout: 0m,
                ExpectedNetWorkout: 0m,
                ExpectedMaintenance: 2656.31m),

            new TestCase(
                Number: 8,
                Age: 60,
                Sex: "FEMALE",
                HeightIn: 62m,
                WeightLb: 190m,
                Scenario: "No Work",
                LifestyleCode: "VERY_ACTIVE",
                Roles: [],
                Workout:
                    new Workout(
                        "ACCESSORY_STRENGTH",
                        45m,
                        4.50m,
                        180m,
                        7m,
                        75m,
                        60m),
                ExpectedHabitualBase: 1662.10m,
                ExpectedGrossWorkout: 261.84m,
                ExpectedRestingWorkout: 43.28m,
                ExpectedNetWorkout: 218.56m,
                ExpectedMaintenance: 1880.66m),

            new TestCase(
                Number: 9,
                Age: 50,
                Sex: "MALE",
                HeightIn: 66m,
                WeightLb: 165m,
                Scenario: "Delivery",
                LifestyleCode: "MODERATE",
                Roles:
                [
                    new Role(
                        "Delivery Driver",
                        "ACTIVE",
                        40m,
                        0.80m)
                ],
                Workout:
                    new Workout(
                        "CONDITIONING",
                        30m,
                        8.00m,
                        180m,
                        8m,
                        null,
                        null),
                ExpectedHabitualBase: 1890.96m,
                ExpectedGrossWorkout: 224.40m,
                ExpectedRestingWorkout: 32.32m,
                ExpectedNetWorkout: 192.08m,
                ExpectedMaintenance: 2083.04m),

            new TestCase(
                Number: 10,
                Age: 70,
                Sex: "FEMALE",
                HeightIn: 67m,
                WeightLb: 145m,
                Scenario: "Office 15h",
                LifestyleCode: "VERY_ACTIVE",
                Roles:
                [
                    new Role(
                        "Part-Time Office",
                        "SEDENTARY_LIGHT",
                        15m,
                        0.35m)
                ],
                Workout:
                    new Workout(
                        "BRISK_WALK",
                        45m,
                        5.00m,
                        180m,
                        5m,
                        null,
                        null),
                ExpectedHabitualBase: 1425.38m,
                ExpectedGrossWorkout: 163.13m,
                ExpectedRestingWorkout: 37.82m,
                ExpectedNetWorkout: 125.30m,
                ExpectedMaintenance: 1550.68m)
        };

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 — BUCKETS #1 + #2 + #3 MAINTENANCE PLAUSIBILITY TABLE");

        _output.WriteLine(
            "Workout reference kcal/min values are DRAFT calibration values.");

        _output.WriteLine("");

        _output.WriteLine(
            new string('-', 173));

        _output.WriteLine(
            $"{"#",2} " +
            $"{"Person",-7} " +
            $"{"Scenario",-13} " +
            $"{"Workout",-19} " +
            $"{"Min",5} " +
            $"{"RPE",5} " +
            $"{"Density",8} " +
            $"{"Base+Habit",11} " +
            $"{"Gross",9} " +
            $"{"RestSub",9} " +
            $"{"NetWorkout",11} " +
            $"{"Maintenance",12}");

        _output.WriteLine(
            new string('-', 173));

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
                                role.Name,
                                role.Code,
                                role.Hours,
                                role.Factor))
                    .ToArray();

            var habitualResult =
                _habitual.Calculate(
                    baseResult.BaseLivingEnergyKcal,
                    roles,
                    test.LifestyleCode);

            decimal grossWorkout = 0m;
            decimal restingWorkout = 0m;
            decimal netWorkout = 0m;
            decimal densityFactor = 1m;
            decimal workoutMinutes = 0m;
            decimal workoutRpe = 0m;
            var workoutName = "REST";

            if (test.Workout is not null)
            {
                var workoutResult =
                    _workout.Calculate(
                        new Engine30WorkoutEnergyInput(
                            WorkoutTypeCode:
                                test.Workout.Code,

                            BaseLivingEnergyKcal:
                                baseResult.BaseLivingEnergyKcal,

                            WeightLb:
                                test.WeightLb,

                            DurationMinutes:
                                test.Workout.Minutes,

                            ReferenceWorkoutCaloriesPerMinute:
                                test.Workout.ReferenceCaloriesPerMinute,

                            ReferenceWeightLb:
                                test.Workout.ReferenceWeightLb,

                            SessionRpe:
                                test.Workout.Rpe,

                            DefaultRestSeconds:
                                test.Workout.DefaultRestSeconds,

                            ActualRestSeconds:
                                test.Workout.ActualRestSeconds));

                grossWorkout =
                    workoutResult.GrossWorkoutEnergyKcal;

                restingWorkout =
                    workoutResult.RestingEnergyDuringWorkoutKcal;

                netWorkout =
                    workoutResult.NetWorkoutEnergyKcal;

                densityFactor =
                    workoutResult.DensityFactor;

                workoutMinutes =
                    test.Workout.Minutes;

                workoutRpe =
                    test.Workout.Rpe;

                workoutName =
                    test.Workout.Code;
            }

            var maintenance =
                Math.Round(
                    habitualResult.BasePlusHabitualEnergyKcal
                    + netWorkout,
                    2,
                    MidpointRounding.AwayFromZero);

            var sex =
                test.Sex == "MALE"
                    ? "M"
                    : "F";

            _output.WriteLine(
                $"{test.Number,2} " +
                $"{test.Age + sex,-7} " +
                $"{test.Scenario,-13} " +
                $"{workoutName,-19} " +
                $"{workoutMinutes,5:0} " +
                $"{workoutRpe,5:0.0} " +
                $"{densityFactor,8:0.00} " +
                $"{habitualResult.BasePlusHabitualEnergyKcal,11:0.00} " +
                $"{grossWorkout,9:0.00} " +
                $"{restingWorkout,9:0.00} " +
                $"{netWorkout,11:0.00} " +
                $"{maintenance,12:0.00}");

            Assert.Equal(
                test.ExpectedHabitualBase,
                habitualResult.BasePlusHabitualEnergyKcal);

            Assert.Equal(
                test.ExpectedGrossWorkout,
                grossWorkout);

            Assert.Equal(
                test.ExpectedRestingWorkout,
                restingWorkout);

            Assert.Equal(
                test.ExpectedNetWorkout,
                netWorkout);

            Assert.Equal(
                test.ExpectedMaintenance,
                maintenance);
        }

        _output.WriteLine(
            new string('-', 173));

        _output.WriteLine("");
        _output.WriteLine(
            "PASS: all 10 complete Engine 3.0 maintenance calculations match.");
    }

    [Fact]
    public void LongerWorkout_IncreasesNetWorkoutEnergy()
    {
        var shortSession =
            _workout.Calculate(
                new Engine30WorkoutEnergyInput(
                    "MAJOR_STRENGTH",
                    1800m,
                    180m,
                    30m,
                    5.50m,
                    180m,
                    8m,
                    90m,
                    90m));

        var longSession =
            _workout.Calculate(
                new Engine30WorkoutEnergyInput(
                    "MAJOR_STRENGTH",
                    1800m,
                    180m,
                    60m,
                    5.50m,
                    180m,
                    8m,
                    90m,
                    90m));

        Assert.True(
            longSession.NetWorkoutEnergyKcal >
            shortSession.NetWorkoutEnergyKcal);
    }

    [Fact]
    public void NetWorkoutEnergy_IsGrossMinusRestingEnergy()
    {
        var result =
            _workout.Calculate(
                new Engine30WorkoutEnergyInput(
                    "MAJOR_STRENGTH",
                    1800m,
                    180m,
                    60m,
                    5.50m,
                    180m,
                    8m,
                    90m,
                    90m));

        Assert.Equal(
            result.GrossWorkoutEnergyKcal
            - result.RestingEnergyDuringWorkoutKcal,
            result.NetWorkoutEnergyKcal);
    }

    [Fact]
    public void DensityFactor_IsBounded()
    {
        var veryShortRest =
            _workout.Calculate(
                new Engine30WorkoutEnergyInput(
                    "MAJOR_STRENGTH",
                    1800m,
                    180m,
                    60m,
                    5.50m,
                    180m,
                    8m,
                    120m,
                    20m));

        var veryLongRest =
            _workout.Calculate(
                new Engine30WorkoutEnergyInput(
                    "MAJOR_STRENGTH",
                    1800m,
                    180m,
                    60m,
                    5.50m,
                    180m,
                    8m,
                    60m,
                    300m));

        Assert.Equal(
            1.25m,
            veryShortRest.DensityFactor);

        Assert.Equal(
            0.75m,
            veryLongRest.DensityFactor);
    }

    private sealed record Role(
        string Name,
        string Code,
        decimal Hours,
        decimal Factor);

    private sealed record Workout(
        string Code,
        decimal Minutes,
        decimal ReferenceCaloriesPerMinute,
        decimal ReferenceWeightLb,
        decimal Rpe,
        decimal? DefaultRestSeconds,
        decimal? ActualRestSeconds);

    private sealed record TestCase(
        int Number,
        int Age,
        string Sex,
        decimal HeightIn,
        decimal WeightLb,
        string Scenario,
        string LifestyleCode,
        IReadOnlyCollection<Role> Roles,
        Workout? Workout,
        decimal ExpectedHabitualBase,
        decimal ExpectedGrossWorkout,
        decimal ExpectedRestingWorkout,
        decimal ExpectedNetWorkout,
        decimal ExpectedMaintenance);
}
