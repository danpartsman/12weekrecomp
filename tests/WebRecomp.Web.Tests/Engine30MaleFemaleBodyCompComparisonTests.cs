using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30MaleFemaleBodyCompComparisonTests
{
    private readonly Engine30BaseLivingCalculator _baseLiving = new();
    private readonly Engine30HabitualActivityCalculator _habitual = new();
    private readonly Engine30BodyCompositionEstimator _bodyComposition = new();
    private readonly Engine30WorkoutEnergyCalculator _workout = new();
    private readonly Engine30GlycogenDemandCalculator _glycogen = new();
    private readonly ITestOutputHelper _output;

    public Engine30MaleFemaleBodyCompComparisonTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void MaleFemale_150_180_240_EngineComparison()
    {
        var actionDate = new DateOnly(2026, 9, 2);

        /*
            Standardized comparison population.

            Age:
              40 for every case.

            Heights:
              Male   68.9 in
              Female 63.5 in

            POP_AVG_BF values are external population benchmark inputs
            selected from the corresponding BMI-category DXA means.
            They are NOT produced by Engine 3.0.

            Every row otherwise receives the same:
              - 40 h/week highly sedentary office occupation
              - MINIMAL lifestyle premium
              - 60-minute MAJOR_LEGS workout
              - RPE 8
              - 90 sec reference / 90 sec actual rest

            This intentionally isolates body size, sex, and the
            body-composition source.
        */

        var people = new[]
        {
            new PersonCase("MALE",   68.9m, 150m, 0.227m),
            new PersonCase("MALE",   68.9m, 180m, 0.282m),
            new PersonCase("MALE",   68.9m, 240m, 0.369m),

            new PersonCase("FEMALE", 63.5m, 150m, 0.408m),
            new PersonCase("FEMALE", 63.5m, 180m, 0.442m),
            new PersonCase("FEMALE", 63.5m, 240m, 0.482m)
        };

        var roles =
            new[]
            {
                new Engine30OccupationalRoleInput(
                    OccupationName:
                        "Office Work",

                    OccupationActivityCode:
                        "HIGHLY_SEDENTARY",

                    TypicalHoursPerWeek:
                        40m,

                    EffectiveOccupationalActivityFactor:
                        0.30m)
            };

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 — MALE/FEMALE BODY-COMPOSITION COMPARISON");

        _output.WriteLine(
            "All calorie, FFM, glycogen and reload outputs below are produced from Engine 3.0 calculators.");

        _output.WriteLine(
            "Workout: MAJOR_LEGS | 60 min | RPE 8 | 90 sec rest | FFM scaling");

        _output.WriteLine(
            "Habitual: 40 h highly sedentary office | MINIMAL lifestyle");

        _output.WriteLine("");

        _output.WriteLine(
            new string('-', 159));

        _output.WriteLine(
            $"{"Sex",-6} " +
            $"{"Wt",5} " +
            $"{"BF source",-13} " +
            $"{"BF%",7} " +
            $"{"FFM lb",8} " +
            $"{"SM kg",7} " +
            $"{"B1 Base",9} " +
            $"{"B2 Habit",9} " +
            $"{"B3 Work",9} " +
            $"{"TOTAL",9} " +
            $"{"Glyc g",8} " +
            $"{"Reload g",9} " +
            $"{"Carb kcal",10}");

        _output.WriteLine(
            new string('-', 159));

        var rows = 0;

        foreach (var person in people)
        {
            foreach (var scenario in new[] { "ESTIMATED", "POP_AVG_BF" })
            {
                var suppliedBodyFat =
                    scenario == "POP_AVG_BF"
                        ? person.PopulationAverageBodyFatFraction
                        : (decimal?)null;

                var bodyComp =
                    _bodyComposition.Calculate(
                        new Engine30BodyCompositionInput(
                            AgeYears:
                                40,

                            SexCode:
                                person.Sex,

                            HeightIn:
                                person.HeightIn,

                            WeightLb:
                                person.WeightLb,

                            BodyFatFraction:
                                suppliedBodyFat));

                var baseResult =
                    _baseLiving.Calculate(
                        new Engine30BaseLivingInput(
                            ActionDate:
                                actionDate,

                            DateOfBirth:
                                new DateOnly(1986, 1, 1),

                            SexCode:
                                person.Sex,

                            HeightIn:
                                person.HeightIn,

                            WeightLb:
                                person.WeightLb));

                var habitualResult =
                    _habitual.Calculate(
                        baseResult.BaseLivingEnergyKcal,
                        roles,
                        "MINIMAL");

                /*
                    DRAFT workout calibration values.

                    These remain calibration/config values and are
                    intentionally visible in this test.

                    FFM scaling is the behavior under test here.
                */
                var workoutResult =
                    _workout.Calculate(
                        new Engine30WorkoutEnergyInput(
                            WorkoutTypeCode:
                                "MAJOR_LEGS",

                            BaseLivingEnergyKcal:
                                baseResult.BaseLivingEnergyKcal,

                            WeightLb:
                                person.WeightLb,

                            DurationMinutes:
                                60m,

                            ReferenceWorkoutCaloriesPerMinute:
                                5.50m,

                            ReferenceWeightLb:
                                180m,

                            SessionRpe:
                                8m,

                            DefaultRestSeconds:
                                90m,

                            ActualRestSeconds:
                                90m,

                            ScalingBasisCode:
                                "FAT_FREE_MASS",

                            FatFreeMassLb:
                                bodyComp.FatFreeMassLb,

                            ReferenceFatFreeMassLb:
                                130m));

                /*
                    DRAFT glycogen profile for a hard major-leg session.

                    Baseline glycogen:
                      23 g/kg skeletal muscle

                    Skeletal muscle / FFM:
                      male   0.528
                      female 0.473

                    LOWER_BODY:
                      50% of whole-body skeletal muscle
                      85% effective recruitment
                      28% depletion of recruited muscle glycogen

                    Reload target:
                      replace 100% of modeled workout depletion

                    These coefficients are inputs to the glycogen engine;
                    they are NOT inferred from workout calories.
                */

                var skeletalMuscleFraction =
                    person.Sex == "MALE"
                        ? 0.528m
                        : 0.473m;

                var glycogenResult =
                    _glycogen.Calculate(
                        new Engine30GlycogenDemandInput(
                            FatFreeMassLb:
                                bodyComp.FatFreeMassLb,

                            SkeletalMuscleFractionOfFfm:
                                skeletalMuscleFraction,

                            BaselineGlycogenGPerKgMuscle:
                                23m,

                            TargetRestorationFraction:
                                1.00m,

                            CarbohydratePerGlycogenG:
                                1.00m,

                            MuscleGroups:
                                new[]
                                {
                                    new Engine30MuscleGlycogenInput(
                                        MuscleGroupCode:
                                         "LOWER_BODY",

                                        WholeBodySkeletalMuscleFraction:
                                            0.50m,

                                        RecruitmentFraction:
                                            0.85m,

                                        DepletionFraction:
                                            0.28m)
                                }));

                /*
                    IMPORTANT:
                    Bucket #2 is activity ONLY, not Base + Activity.

                    Maintenance therefore remains:
                        Bucket 1
                      + Bucket 2
                      + Bucket 3

                    Carb reload kcal is diagnostic macro allocation.
                    It is NOT added to maintenance.
                */
                var bucket1 =
                    baseResult.BaseLivingEnergyKcal;

                var bucket2 =
                    habitualResult.AverageDailyHabitualActivityEnergyKcal;

                var bucket3 =
                    workoutResult.NetWorkoutEnergyKcal;

                var totalMaintenance =
                    Round(
                        bucket1
                        + bucket2
                        + bucket3);

                /*
                    For the ESTIMATED row, the estimator does not claim
                    that body fat was measured. This displayed BF% is
                    simply the complement of the engine-generated FFM
                    fraction.

                    For POP_AVG_BF, BodyFatPercent is the supplied
                    population benchmark carried through the engine.
                */
                var effectiveBodyFatPercent =
                    bodyComp.BodyFatPercent
                    ?? Round(
                        (1m - bodyComp.FatFreeMassFraction)
                        * 100m);

                _output.WriteLine(
                    $"{person.Sex,-6} " +
                    $"{person.WeightLb,5:0} " +
                    $"{scenario,-13} " +
                    $"{effectiveBodyFatPercent,7:0.0} " +
                    $"{bodyComp.FatFreeMassLb,8:0.0} " +
                    $"{glycogenResult.EstimatedWholeBodySkeletalMuscleMassKg,7:0.0} " +
                    $"{bucket1,9:0.0} " +
                    $"{bucket2,9:0.0} " +
                    $"{bucket3,9:0.0} " +
                    $"{totalMaintenance,9:0.0} " +
                    $"{glycogenResult.EstimatedGlycogenDepletedG,8:0.0} " +
                    $"{glycogenResult.CarbohydrateReloadRequirementG,9:0.0} " +
                    $"{glycogenResult.CarbohydrateReloadEnergyKcal,10:0.0}");

                Assert.True(bucket1 > 0m);
                Assert.True(bucket2 >= 0m);
                Assert.True(bucket3 >= 0m);

                Assert.Equal(
                    totalMaintenance,
                    Round(bucket1 + bucket2 + bucket3));

                Assert.True(
                    glycogenResult.EstimatedGlycogenDepletedG >= 0m);

                Assert.True(
                    glycogenResult.CarbohydrateReloadRequirementG >=
                    glycogenResult.EstimatedGlycogenDepletedG);

                var reloadEnergyFromReportedGrams =
                    Round(
                        glycogenResult.CarbohydrateReloadRequirementG
                        * 4m);

                Assert.InRange(
                    Math.Abs(
                        glycogenResult.CarbohydrateReloadEnergyKcal
                        - reloadEnergyFromReportedGrams),
                    0m,
                    0.03m);

                Assert.Equal(
                    "FAT_FREE_MASS",
                    workoutResult.ScalingBasisCode);

                rows++;
            }
        }

        _output.WriteLine(
            new string('-', 159));

        _output.WriteLine("");
        _output.WriteLine(
            "B1 = Base Living kcal/day");

        _output.WriteLine(
            "B2 = Habitual Activity kcal/day ONLY");

        _output.WriteLine(
            "B3 = Net Workout kcal above resting");

        _output.WriteLine(
            "TOTAL = B1 + B2 + B3");

        _output.WriteLine(
            "Reload g/kcal = carbohydrate allocation for modeled glycogen restoration; NOT an added calorie bucket.");

        Assert.Equal(12, rows);
    }

    private static decimal Round(
        decimal value,
        int decimals = 2)
        => Math.Round(
            value,
            decimals,
            MidpointRounding.AwayFromZero);

    private sealed record PersonCase(
        string Sex,
        decimal HeightIn,
        decimal WeightLb,
        decimal PopulationAverageBodyFatFraction);
}
