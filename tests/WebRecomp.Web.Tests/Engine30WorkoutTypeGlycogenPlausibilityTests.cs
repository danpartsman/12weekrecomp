using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30WorkoutTypeGlycogenPlausibilityTests
{
    private readonly Engine30BaseLivingCalculator _baseLiving = new();
    private readonly Engine30HabitualActivityCalculator _habitual = new();
    private readonly Engine30BodyCompositionEstimator _bodyComposition = new();
    private readonly Engine30WorkoutEnergyCalculator _workout = new();
    private readonly Engine30GlycogenDemandCalculator _glycogen = new();
    private readonly ITestOutputHelper _output;

    public Engine30WorkoutTypeGlycogenPlausibilityTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void StandardizedIndividual_WorkoutTypes_PrintEnergyAndGlycogenPlausibilityTable()
    {
        /*
            PURPOSE

            This is a plausibility/diagnostic test, not a population-average
            prescription.

            The same known individual is held constant across every row:

                Male
                Age 40
                Height 69 in
                Weight 180 lb
                Body fat 20% SUPPLIED

            Therefore:
                Bucket #1 remains constant.
                Bucket #2 remains constant.
                Body composition remains constant.

            Only the workout changes.

            This allows us to inspect whether Bucket #3 energy and glycogen
            demand move for the intended physiological reasons.

            IMPORTANT

            Workout reference kcal/min values and glycogen-profile
            coefficients below are DRAFT CFG-3000 calibration inputs.

            The test deliberately does NOT hard-code expected calorie or
            glycogen outputs. Engine 3.0 calculates all output values.

            The assertions test invariants and directional behavior only.
        */

        var actionDate =
            new DateOnly(2026, 9, 2);

        var baseResult =
            _baseLiving.Calculate(
                new Engine30BaseLivingInput(
                    ActionDate:
                        actionDate,

                    DateOfBirth:
                        new DateOnly(1986, 1, 1),

                    SexCode:
                        "MALE",

                    HeightIn:
                        69m,

                    WeightLb:
                        180m));

        var bodyComp =
            _bodyComposition.Calculate(
                new Engine30BodyCompositionInput(
                    AgeYears:
                        40,

                    SexCode:
                        "MALE",

                    HeightIn:
                        69m,

                    WeightLb:
                        180m,

                    BodyFatFraction:
                        0.20m));

        Assert.Equal(
            "BODY_FAT_DERIVED",
            bodyComp.BodyCompositionSourceCode);

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

        var habitualResult =
            _habitual.Calculate(
                baseResult.BaseLivingEnergyKcal,
                roles,
                "MINIMAL");

        /*
            DRAFT common glycogen inputs.

            SkeletalMuscleFractionOfFfm:
                0.528 for this standardized male test case.

            BaselineGlycogen:
                23 g/kg skeletal muscle.

            TargetRestoration:
                100% of modeled workout depletion.

            CarbohydratePerGlycogen:
                1.00 g carbohydrate allocated per 1.00 g modeled
                glycogen restoration for this first-pass diagnostic.

            These are calibration inputs, not frozen Engine constants.
        */

        const decimal skeletalMuscleFractionOfFfm = 0.528m;
        const decimal baselineGlycogenGPerKgMuscle = 23m;
        const decimal targetRestorationFraction = 1.00m;
        const decimal carbohydratePerGlycogenG = 1.00m;

        /*
            WORKOUT PROFILES

            Energy scaling:
                Resistance -> FAT_FREE_MASS
                Cardio/mixed conditioning -> BODY_WEIGHT

            Muscle-group fractions describe the share of whole-body
            skeletal muscle represented by the modeled region.

            Recruitment and depletion coefficients are intentionally
            different from workout calories. They are the variables
            being examined by this plausibility test.
        */

        var profiles =
            new[]
            {
                new WorkoutProfile(
                    Code:
                        "REST",

                    Minutes:
                        0m,

                    Rpe:
                        0m,

                    ReferenceCaloriesPerMinute:
                        0m,

                    ScalingBasisCode:
                        "NONE",

                    DefaultRestSeconds:
                        null,

                    ActualRestSeconds:
                        null,

                    MuscleGroups:
                        Array.Empty<Engine30MuscleGlycogenInput>()),

                new WorkoutProfile(
                    Code:
                        "ACCESSORY",

                    Minutes:
                        45m,

                    Rpe:
                        7m,

                    ReferenceCaloriesPerMinute:
                        4.50m,

                    ScalingBasisCode:
                        "FAT_FREE_MASS",

                    DefaultRestSeconds:
                        60m,

                    ActualRestSeconds:
                        60m,

                    MuscleGroups:
                        new[]
                        {
                            new Engine30MuscleGlycogenInput(
                                MuscleGroupCode:
                                    "UPPER_SMALL",

                                WholeBodySkeletalMuscleFraction:
                                    0.15m,

                                RecruitmentFraction:
                                    0.70m,

                                DepletionFraction:
                                    0.18m)
                        }),

                new WorkoutProfile(
                    Code:
                        "MAJOR_PUSH",

                    Minutes:
                        60m,

                    Rpe:
                        8m,

                    ReferenceCaloriesPerMinute:
                        5.50m,

                    ScalingBasisCode:
                        "FAT_FREE_MASS",

                    DefaultRestSeconds:
                        90m,

                    ActualRestSeconds:
                        90m,

                    MuscleGroups:
                        new[]
                        {
                            new Engine30MuscleGlycogenInput(
                                MuscleGroupCode:
                                    "UPPER_PUSH",

                                WholeBodySkeletalMuscleFraction:
                                    0.22m,

                                RecruitmentFraction:
                                    0.80m,

                                DepletionFraction:
                                    0.25m)
                        }),

                new WorkoutProfile(
                    Code:
                        "MAJOR_PULL",

                    Minutes:
                        60m,

                    Rpe:
                        8m,

                    ReferenceCaloriesPerMinute:
                        5.50m,

                    ScalingBasisCode:
                        "FAT_FREE_MASS",

                    DefaultRestSeconds:
                        90m,

                    ActualRestSeconds:
                        90m,

                    MuscleGroups:
                        new[]
                        {
                            new Engine30MuscleGlycogenInput(
                                MuscleGroupCode:
                                    "UPPER_PULL",

                                WholeBodySkeletalMuscleFraction:
                                    0.25m,

                                RecruitmentFraction:
                                    0.80m,

                                DepletionFraction:
                                    0.25m)
                        }),

                new WorkoutProfile(
                    Code:
                        "MAJOR_LEGS",

                    Minutes:
                        60m,

                    Rpe:
                        8m,

                    ReferenceCaloriesPerMinute:
                        5.50m,

                    ScalingBasisCode:
                        "FAT_FREE_MASS",

                    DefaultRestSeconds:
                        90m,

                    ActualRestSeconds:
                        90m,

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
                        }),

                new WorkoutProfile(
                    Code:
                        "ZONE2",

                    Minutes:
                        45m,

                    Rpe:
                        5m,

                    ReferenceCaloriesPerMinute:
                        6.00m,

                    ScalingBasisCode:
                        "BODY_WEIGHT",

                    DefaultRestSeconds:
                        null,

                    ActualRestSeconds:
                        null,

                    MuscleGroups:
                        new[]
                        {
                            new Engine30MuscleGlycogenInput(
                                MuscleGroupCode:
                                    "LOWER_BODY",

                                WholeBodySkeletalMuscleFraction:
                                    0.50m,

                                RecruitmentFraction:
                                    0.70m,

                                DepletionFraction:
                                    0.12m)
                        }),

                new WorkoutProfile(
                    Code:
                        "CONDITIONING",

                    Minutes:
                        35m,

                    Rpe:
                        8m,

                    ReferenceCaloriesPerMinute:
                        8.00m,

                    ScalingBasisCode:
                        "BODY_WEIGHT",

                    DefaultRestSeconds:
                        null,

                    ActualRestSeconds:
                        null,

                    MuscleGroups:
                        new[]
                        {
                            new Engine30MuscleGlycogenInput(
                                MuscleGroupCode:
                                    "LOWER_BODY",

                                WholeBodySkeletalMuscleFraction:
                                    0.50m,

                                RecruitmentFraction:
                                    0.80m,

                                DepletionFraction:
                                    0.18m),

                            new Engine30MuscleGlycogenInput(
                                MuscleGroupCode:
                                    "UPPER_BODY",

                                WholeBodySkeletalMuscleFraction:
                                    0.20m,

                                RecruitmentFraction:
                                    0.50m,

                                DepletionFraction:
                                    0.12m)
                        }),

                new WorkoutProfile(
                    Code:
                        "OTF_HIIT",

                    Minutes:
                        60m,

                    Rpe:
                        9m,

                    ReferenceCaloriesPerMinute:
                        9.00m,

                    ScalingBasisCode:
                        "BODY_WEIGHT",

                    DefaultRestSeconds:
                        null,

                    ActualRestSeconds:
                        null,

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
                                    0.22m),

                            new Engine30MuscleGlycogenInput(
                                MuscleGroupCode:
                                    "UPPER_BODY",

                                WholeBodySkeletalMuscleFraction:
                                    0.25m,

                                RecruitmentFraction:
                                    0.65m,

                                DepletionFraction:
                                    0.16m)
                        })
            };

        var rows =
            new List<RowResult>();

        foreach (var profile in profiles)
        {
            decimal bucket3 = 0m;
            decimal recruitedMuscleKg = 0m;
            decimal glycogenDepletedG = 0m;
            decimal reloadCarbG = 0m;
            decimal reloadCarbKcal = 0m;

            if (profile.Code != "REST")
            {
                var workoutResult =
                    _workout.Calculate(
                        new Engine30WorkoutEnergyInput(
                            WorkoutTypeCode:
                                profile.Code,

                            BaseLivingEnergyKcal:
                                baseResult.BaseLivingEnergyKcal,

                            WeightLb:
                                180m,

                            DurationMinutes:
                                profile.Minutes,

                            ReferenceWorkoutCaloriesPerMinute:
                                profile.ReferenceCaloriesPerMinute,

                            ReferenceWeightLb:
                                180m,

                            SessionRpe:
                                profile.Rpe,

                            DefaultRestSeconds:
                                profile.DefaultRestSeconds,

                            ActualRestSeconds:
                                profile.ActualRestSeconds,

                            ScalingBasisCode:
                                profile.ScalingBasisCode,

                            FatFreeMassLb:
                                profile.ScalingBasisCode == "FAT_FREE_MASS"
                                    ? bodyComp.FatFreeMassLb
                                    : null,

                            ReferenceFatFreeMassLb:
                                profile.ScalingBasisCode == "FAT_FREE_MASS"
                                    ? 130m
                                    : null));

                bucket3 =
                    workoutResult.NetWorkoutEnergyKcal;

                Assert.Equal(
                    profile.ScalingBasisCode,
                    workoutResult.ScalingBasisCode);

                var glycogenResult =
                    _glycogen.Calculate(
                        new Engine30GlycogenDemandInput(
                            FatFreeMassLb:
                                bodyComp.FatFreeMassLb,

                            SkeletalMuscleFractionOfFfm:
                                skeletalMuscleFractionOfFfm,

                            BaselineGlycogenGPerKgMuscle:
                                baselineGlycogenGPerKgMuscle,

                            TargetRestorationFraction:
                                targetRestorationFraction,

                            CarbohydratePerGlycogenG:
                                carbohydratePerGlycogenG,

                            MuscleGroups:
                                profile.MuscleGroups));

                recruitedMuscleKg =
                    Round(
                        glycogenResult.MuscleGroups.Sum(
                            x => x.EffectiveRecruitedMuscleMassKg));

                glycogenDepletedG =
                    glycogenResult.EstimatedGlycogenDepletedG;

                reloadCarbG =
                    glycogenResult.CarbohydrateReloadRequirementG;

                reloadCarbKcal =
                    glycogenResult.CarbohydrateReloadEnergyKcal;

                var reloadFromDisplayedGrams =
                    Round(
                        reloadCarbG * 4m);

                Assert.InRange(
                    Math.Abs(
                        reloadCarbKcal
                        - reloadFromDisplayedGrams),
                    0m,
                    0.03m);
            }

            var bucket1 =
                baseResult.BaseLivingEnergyKcal;

            var bucket2 =
                habitualResult.AverageDailyHabitualActivityEnergyKcal;

            var total =
                Round(
                    bucket1
                    + bucket2
                    + bucket3);

            rows.Add(
                new RowResult(
                    profile.Code,
                    profile.ScalingBasisCode,
                    profile.Minutes,
                    profile.Rpe,
                    bucket1,
                    bucket2,
                    bucket3,
                    total,
                    recruitedMuscleKg,
                    glycogenDepletedG,
                    reloadCarbG,
                    reloadCarbKcal));
        }

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 â€” WORKOUT-TYPE ENERGY + GLYCOGEN PLAUSIBILITY");

        _output.WriteLine(
            "Known individual: 40M | 69 in | 180 lb | supplied 20% BF");

        _output.WriteLine(
            $"Engine FFM: {bodyComp.FatFreeMassLb:0.0} lb | Source: {bodyComp.BodyCompositionSourceCode}");

        _output.WriteLine(
            "Habitual: 40 h highly sedentary office | MINIMAL lifestyle");

        _output.WriteLine("");

        _output.WriteLine(
            new string('-', 151));

        _output.WriteLine(
            $"{"Workout",-15} " +
            $"{"Scale",-14} " +
            $"{"Min",5} " +
            $"{"RPE",5} " +
            $"{"B1 Base",9} " +
            $"{"B2 Habit",9} " +
            $"{"B3 Work",9} " +
            $"{"TOTAL",9} " +
            $"{"Recruit kg",10} " +
            $"{"Glyc g",8} " +
            $"{"Reload g",9} " +
            $"{"Carb kcal",10}");

        _output.WriteLine(
            new string('-', 151));

        foreach (var row in rows)
        {
            _output.WriteLine(
                $"{row.WorkoutCode,-15} " +
                $"{row.ScalingBasisCode,-14} " +
                $"{row.Minutes,5:0} " +
                $"{row.Rpe,5:0.0} " +
                $"{row.Bucket1,9:0.0} " +
                $"{row.Bucket2,9:0.0} " +
                $"{row.Bucket3,9:0.0} " +
                $"{row.Total,9:0.0} " +
                $"{row.RecruitedMuscleKg,10:0.0} " +
                $"{row.GlycogenDepletedG,8:0.0} " +
                $"{row.ReloadCarbG,9:0.0} " +
                $"{row.ReloadCarbKcal,10:0.0}");
        }

        _output.WriteLine(
            new string('-', 151));

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
            "Reload carbohydrate is macro allocation for modeled glycogen restoration; NOT another calorie bucket.");

        Assert.Equal(
            8,
            rows.Count);

        var rest =
            Get(rows, "REST");

        var accessory =
            Get(rows, "ACCESSORY");

        var push =
            Get(rows, "MAJOR_PUSH");

        var pull =
            Get(rows, "MAJOR_PULL");

        var legs =
            Get(rows, "MAJOR_LEGS");

        var zone2 =
            Get(rows, "ZONE2");

        var conditioning =
            Get(rows, "CONDITIONING");

        var otf =
            Get(rows, "OTF_HIIT");

        // Rest day must add neither workout energy nor workout glycogen demand.
        Assert.Equal(
            0m,
            rest.Bucket3);

        Assert.Equal(
            0m,
            rest.GlycogenDepletedG);

        Assert.Equal(
            0m,
            rest.ReloadCarbG);

        // Resistance-workout glycogen demand should rise with modeled
        // recruited muscle mass and depletion burden.
        Assert.True(
            accessory.GlycogenDepletedG
            < push.GlycogenDepletedG);

        Assert.True(
            push.GlycogenDepletedG
            < pull.GlycogenDepletedG);

        Assert.True(
            pull.GlycogenDepletedG
            < legs.GlycogenDepletedG);

        // Zone 2 should use meaningful but lower modeled glycogen
        // than a hard major-leg session for this configuration.
        Assert.True(
            zone2.GlycogenDepletedG
            < legs.GlycogenDepletedG);

        // Mixed conditioning should exceed accessory glycogen demand.
        Assert.True(
            conditioning.GlycogenDepletedG
            > accessory.GlycogenDepletedG);

        // A long high-intensity mixed OTF session should be at least
        // comparable to the hard leg session in modeled glycogen demand.
        Assert.True(
            otf.GlycogenDepletedG
            >= legs.GlycogenDepletedG);

        // Every row must retain the same B1 and B2 because only the
        // workout is changing in this experiment.
        foreach (var row in rows)
        {
            Assert.Equal(
                rest.Bucket1,
                row.Bucket1);

            Assert.Equal(
                rest.Bucket2,
                row.Bucket2);

            Assert.Equal(
                Round(
                    row.Bucket1
                    + row.Bucket2
                    + row.Bucket3),
                row.Total);

            Assert.True(
                row.Bucket1 > 0m);

            Assert.True(
                row.Bucket2 >= 0m);

            Assert.True(
                row.Bucket3 >= 0m);

            Assert.True(
                row.GlycogenDepletedG >= 0m);

            Assert.True(
                row.ReloadCarbG >= 0m);
        }
    }

    private static RowResult Get(
        IReadOnlyCollection<RowResult> rows,
        string workoutCode)
        => rows.Single(
            x => x.WorkoutCode == workoutCode);

    private static decimal Round(
        decimal value,
        int decimals = 2)
        => Math.Round(
            value,
            decimals,
            MidpointRounding.AwayFromZero);

    private sealed record WorkoutProfile(
        string Code,
        decimal Minutes,
        decimal Rpe,
        decimal ReferenceCaloriesPerMinute,
        string ScalingBasisCode,
        decimal? DefaultRestSeconds,
        decimal? ActualRestSeconds,
        IReadOnlyCollection<Engine30MuscleGlycogenInput> MuscleGroups);

    private sealed record RowResult(
        string WorkoutCode,
        string ScalingBasisCode,
        decimal Minutes,
        decimal Rpe,
        decimal Bucket1,
        decimal Bucket2,
        decimal Bucket3,
        decimal Total,
        decimal RecruitedMuscleKg,
        decimal GlycogenDepletedG,
        decimal ReloadCarbG,
        decimal ReloadCarbKcal);
}