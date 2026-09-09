using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine2ActualWorkoutTests
{
    private readonly WorkoutMathService _math = new();

    private readonly RecompTrainingPrescriptionService _training =
        new();

    private RecompActualWorkoutAnalysisService CreateService() =>
        new(_math, _training);


    [Fact]
    public void Longer_Harder_Workout_Increases_Glycogen_Demand()
    {
        var workout = MajorPush();

        var analysis = CreateService().Calculate(
            210m,
            Plan(),
            PlannedEngineResult(workout),
            CompletedWorkout(
                workout,
                duration: 75m,
                restSeconds: 75,
                sessionRpe: 9m),
            workout,
            nutritionSoFar: null,
            Settings());

        Assert.True(
            analysis.ActualGlycogenDemandG >
            analysis.PlannedGlycogenDemandG);

        Assert.True(
            analysis.GlycogenDemandChangeG > 0m);
    }


    [Fact]
    public void Shorter_Easier_Workout_Reduces_Glycogen_Demand()
    {
        var workout = MajorPush();

        var analysis = CreateService().Calculate(
            210m,
            Plan(),
            PlannedEngineResult(workout),
            CompletedWorkout(
                workout,
                duration: 30m,
                restSeconds: 75,
                sessionRpe: 5m),
            workout,
            nutritionSoFar: null,
            Settings());

        Assert.True(
            analysis.ActualGlycogenDemandG <
            analysis.PlannedGlycogenDemandG);

        Assert.True(
            analysis.GlycogenDemandChangeG < 0m);
    }


    [Fact]
    public void Shorter_Rest_Increases_Actual_Glycogen_Demand()
    {
        var workout = MajorPush();

        var shortRest =
            _math.CalculateActualWorkout(
                210m,
                CompletedWorkout(
                    workout,
                    duration: 60m,
                    restSeconds: 45,
                    sessionRpe: 8m),
                workout,
                Settings());

        var longRest =
            _math.CalculateActualWorkout(
                210m,
                CompletedWorkout(
                    workout,
                    duration: 60m,
                    restSeconds: 120,
                    sessionRpe: 8m),
                workout,
                Settings());

        Assert.True(
            shortRest.ScaledGlycogenBurnG >
            longRest.ScaledGlycogenBurnG);
    }


    [Fact]
    public void Higher_Session_Rpe_Increases_Actual_Glycogen_Demand()
    {
        var workout = MajorPush();

        var moderate =
            _math.CalculateActualWorkout(
                210m,
                CompletedWorkout(
                    workout,
                    duration: 60m,
                    restSeconds: 75,
                    sessionRpe: 5m),
                workout,
                Settings());

        var veryHard =
            _math.CalculateActualWorkout(
                210m,
                CompletedWorkout(
                    workout,
                    duration: 60m,
                    restSeconds: 75,
                    sessionRpe: 9m),
                workout,
                Settings());

        Assert.True(
            veryHard.ScaledGlycogenBurnG >
            moderate.ScaledGlycogenBurnG);
    }


    [Fact]
    public void Estimated_Working_Sets_Use_Actual_Duration_And_Rest()
    {
        var workout = MajorPush();

        var normalSession =
            CompletedWorkout(
                workout,
                duration: 60m,
                restSeconds: 75,
                sessionRpe: 8m);

        var longerSession =
            CompletedWorkout(
                workout,
                duration: 75m,
                restSeconds: 75,
                sessionRpe: 8m);

        var normalSets =
            _training.EstimateActualWorkingSets(
                workout,
                normalSession,
                Settings());

        var longerSets =
            _training.EstimateActualWorkingSets(
                workout,
                longerSession,
                Settings());

        Assert.Equal(23, normalSets);

        Assert.NotNull(longerSets);

        Assert.True(
            longerSets > normalSets);
    }


    [Fact]
    public void Estimated_Sets_Do_Not_Multiply_Expenditure_Again()
    {
        var workout = MajorPush();

        var session =
            CompletedWorkout(
                workout,
                duration: 75m,
                restSeconds: 75,
                sessionRpe: 9m);

        var direct =
            _math.CalculateActualWorkout(
                210m,
                session,
                workout,
                Settings());

        var analysis =
            CreateService().Calculate(
                210m,
                Plan(),
                PlannedEngineResult(workout),
                session,
                workout,
                nutritionSoFar: null,
                Settings());

        Assert.NotNull(
            analysis.EstimatedWorkingSets);

        // The working-set estimate is descriptive only.
        // Actual expenditure must remain exactly the output
        // of WorkoutMathService.CalculateActualWorkout().
        Assert.Equal(
            direct.ScaledGlycogenBurnG,
            analysis.ActualGlycogenDemandG);

        Assert.Equal(
            direct.WorkoutCalories,
            analysis.ActualWorkoutCalories);
    }


    [Fact]
    public void Harder_Actual_Workout_Can_Increase_Daily_Carb_Target()
    {
        var workout = MajorPush();

        var analysis =
            CreateService().Calculate(
                210m,
                Plan(),
                PlannedEngineResult(workout),
                CompletedWorkout(
                    workout,
                    duration: 75m,
                    restSeconds: 75,
                    sessionRpe: 9m),
                workout,
                nutritionSoFar: null,
                Settings());

        Assert.True(
            analysis.Nutrition.RecalculatedCarbsG >
            analysis.Nutrition.OriginalCarbsG);

        Assert.True(
            analysis.Nutrition.CarbsChangeG > 0m);
    }


    [Fact]
    public void Entered_Nutrition_Produces_Remaining_Day_Carb_Balance()
    {
        var workout = MajorPush();

        var nutrition = new NutritionDay
        {
            NutritionDayId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            NutritionDate = new DateOnly(2026, 8, 31),

            ActualCalories = 900m,
            ActualProteinG = 80m,
            ActualCarbsG = 60m,
            ActualFatG = 35m,

            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var analysis =
            CreateService().Calculate(
                210m,
                Plan(),
                PlannedEngineResult(workout),
                CompletedWorkout(
                    workout,
                    duration: 75m,
                    restSeconds: 75,
                    sessionRpe: 9m),
                workout,
                nutrition,
                Settings());

        Assert.NotNull(
            analysis.Nutrition.CarbsBalanceG);

        Assert.Equal(
            analysis.Nutrition.RecalculatedCarbsG - 60m,
            analysis.Nutrition.CarbsBalanceG);
    }


    [Fact]
    public void Original_Nutrition_Target_Is_Preserved_After_Recalculation()
    {
        var workout = MajorPush();

        var planned =
            PlannedEngineResult(workout);

        var analysis =
            CreateService().Calculate(
                210m,
                Plan(),
                planned,
                CompletedWorkout(
                    workout,
                    duration: 75m,
                    restSeconds: 75,
                    sessionRpe: 9m),
                workout,
                nutritionSoFar: null,
                Settings());

        Assert.Equal(
            planned.Nutrition.RecommendedCalories,
            analysis.Nutrition.OriginalCalories);

        Assert.Equal(
            planned.Nutrition.RecommendedCarbsG,
            analysis.Nutrition.OriginalCarbsG);

        Assert.NotEqual(
            analysis.Nutrition.OriginalCarbsG,
            analysis.Nutrition.RecalculatedCarbsG);
    }


    [Fact]
    public void Workout_Calories_Are_Not_Eaten_Back_One_For_One()
    {
        var workout = MajorPush();

        var analysis =
            CreateService().Calculate(
                210m,
                Plan(),
                PlannedEngineResult(workout),
                CompletedWorkout(
                    workout,
                    duration: 75m,
                    restSeconds: 75,
                    sessionRpe: 9m),
                workout,
                nutritionSoFar: null,
                Settings());

        // For the same workout type, protein and fat remain
        // unchanged. Any food-calorie adjustment therefore
        // comes from the carbohydrate model.
        Assert.Equal(
            0m,
            analysis.Nutrition.ProteinChangeG);

        Assert.Equal(
            0m,
            analysis.Nutrition.FatChangeG);

        Assert.Equal(
            analysis.Nutrition.CarbsChangeG * 4m,
            analysis.Nutrition.CaloriesChange);

        // Exercise expenditure is tracked separately.
        Assert.NotEqual(
            analysis.WorkoutCaloriesChange,
            analysis.Nutrition.CaloriesChange);
    }


    // ---------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------

    private static PlanDefinition Plan() =>
        new()
        {
            PlanDefinitionId = Guid.NewGuid(),
            ConfigVersionId = Guid.NewGuid(),
            PlanCode = "TEST",
            DisplayName = "Test Plan",
            BaseRestCarbsG = 120m,
            PlanCategoryCode = "TEST",
            SortOrder = 1,
            IsActive = true
        };


    private static WorkoutSession CompletedWorkout(
        WorkoutTypeDefinition workout,
        decimal duration,
        short restSeconds,
        decimal sessionRpe) =>
        new()
        {
            WorkoutSessionId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),

            WorkoutDate =
                new DateOnly(2026, 8, 31),

            WorkoutTypeCode =
                workout.WorkoutTypeCode,

            DurationMin = duration,

            TypicalRestSec = restSeconds,

            SessionRpe = sessionRpe,

            CompletedFlag = true,

            EntrySourceCode = "MANUAL",

            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };


    private static RecompEngineResult PlannedEngineResult(
        WorkoutTypeDefinition workout)
    {
        var fuel =
            new RecompFuelState(
                DemandG: 100m,
                RestorationCreditG: 100m,
                AbsoluteRestorationRatio: 1m,
                FunctionalTargetRatio: 0.80m,
                PreRescueAchievementRatio: 1.25m,
                PreRescueReadinessCode: "ADEQUATE",
                ModerateHardDays: 1,
                HardDays: 1);

        var rescue =
            new RecompRescuePlan(
                ShortfallG: 0m,
                AllocationG: 0m,
                PreWorkoutG: 0m,
                ProjectedAchievementRatio: 1.25m,
                ProjectedReadinessCode: "ADEQUATE",
                RescueIsMeaningful: false);

        var decision =
            new RecompWorkoutDecision(
                PlannedWorkout: workout,
                RecommendedWorkout: workout,
                ScheduleChangeRecommended: false,
                BasisCode: "KEEP_PLAN",
                Alerts:
                    Array.Empty<RecompEngineAlert>());

        var prescription =
            new RecompTrainingPrescriptionService()
                .BuildPlanned(
                    workout,
                    Settings());

        var nutrition =
            new RecompNutritionTargets(
                BaseCalories: 1900m,
                RecommendedCalories: 1970m,
                ProteinG: 185m,
                BaseCarbsG: 150m,
                RecoveryAllocationG: 0m,
                PreWorkoutG: 0m,
                RecommendedCarbsG: 150m,
                FatG: 70m,
                TimingNote: "Test timing");

        var guidance =
            new RecompGuidanceCodes(
                TaxonomyVersion: "1.0",
                WorkoutScenarioCode: "W01_KEEP_PLAN",
                NutritionScenarioCode: "N01_BASE",
                RecoveryScenarioCode: "R01_READY",
                DailyGuidanceKey:
                    "G1.0-W01_KEEP_PLAN-N01_BASE-R01_READY",
                Tags:
                    Array.Empty<string>());

        return new RecompEngineResult(
            ActionDate:
                new DateOnly(2026, 8, 31),

            WeeklyStrategy:
                Weekly(),

            RegionRecovery:
                FullyRecovered(),

            Fuel:
                fuel,

            Rescue:
                rescue,

            Decision:
                decision,

            TrainingPrescription:
                prescription,

            FinalFuel:
                fuel,

            FinalRescue:
                rescue,

            Nutrition:
                nutrition,

            Guidance:
                guidance,

            Explanation:
                "Test planned Engine 2.0 result.");
    }


    private static IReadOnlyDictionary<string, decimal>
        Settings()
    {
        var result =
            new Dictionary<string, decimal>(
                StringComparer.OrdinalIgnoreCase);

        foreach (
            var parameter
            in RecompModelCalibrationCatalog.All)
        {
            result[parameter.Key] =
                parameter.DefaultValue;
        }

        foreach (
            var parameter
            in RecompTrainingPrescriptionCatalog.All)
        {
            result[parameter.Key] =
                parameter.DefaultValue;
        }

        result["REFERENCE_WEIGHT_LB"] = 210m;
        result["BODY_WEIGHT_GLYCOGEN_EXPONENT"] = 0.50m;
        result["GLYCOGEN_ESTIMATE_MULTIPLIER"] = 0.65m;

        result["CARB_ROUNDING_G"] = 5m;
        result["PROTEIN_ROUNDING_G"] = 5m;

        result["PROTEIN_PER_LB"] = 0.88m;
        result["PROTEIN_MIN_G"] = 175m;
        result["PROTEIN_MAX_G"] = 195m;

        result["MINIMUM_FAT_G"] = 35m;

        return result;
    }


    private static WorkoutTypeDefinition MajorPush() =>
        new()
        {
            WorkoutTypeDefinitionId =
                Guid.NewGuid(),

            ConfigVersionId =
                Guid.NewGuid(),

            WorkoutTypeCode =
                "MAJOR_PUSH",

            DisplayName =
                "Major Push",

            WorkoutCategoryCode =
                "MAJOR_STRENGTH",

            WeeklyRoleCode =
                "HEAVY",

            RegionCode =
                "PUSH",

            CountsDedicatedHeavy =
                true,

            CountsLightStrength =
                false,

            CountsPrimaryConditioning =
                false,

            CountsRecovery =
                false,

            FuelPriorityCode =
                "HIGH",

            SchedulePrincipleCode =
                "TEST",

            MacroClassCode =
                "HEAVY",

            PrimaryFocusCode =
                "Push",

            FuelScore =
                5m,

            DefaultMinutes =
                60m,

            RawGlycogenGMin =
                1m,

            DefaultIntensityFactor =
                1m,

            DefaultRestSec =
                75,

            ReferenceWorkoutCalPerMin =
                8m,

            GlycogenCarbShare =
                1m,

            PushLoad =
                3m,

            PullLoad =
                0.25m,

            LegLoad =
                0m,

            CardioLoad =
                0.25m,

            StrengthStress =
                3m,

            PlannedRestorationPct =
                0.80m,

            ProteinAdjustmentG =
                0m,

            TargetFatG =
                70m,

            TypicalRecoveryHours =
                48m,

            IsActive =
                true
        };


    private static WeeklyStrategyAssessment Weekly() =>
        new(
            WeekStart:
                new DateOnly(2026, 8, 31),

            MajorPushCount: 1,
            MajorPullCount: 1,
            MajorLegsCount: 1,
            LightStrengthCount: 1,
            PrimaryConditioningCount: 1,
            RecoveryCount: 1,
            HeavyGenericCount: 0,

            StatusCode:
                "BALANCED",

            Note:
                "Test week is balanced.");


    private static RegionRecovery FullyRecovered() =>
        new(
            0m, 0m, 0m, 0m,
            true, true, true, true,
            0m, 0m, 0m, 0m,
            0m, 0m, 0m, 0m,
            "BALANCED",
            "ADEQUATE");
}

