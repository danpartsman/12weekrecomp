using WebRecomp.Web.Services.Engine30;

namespace WebRecomp.Web.Tests;

public sealed class Engine30DashboardAnalysisServiceTests
{
    private readonly Engine30DashboardAnalysisService _service = new();

    [Theory]
    [InlineData(0.80, "green")]
    [InlineData(0.95, "green")]
    [InlineData(0.79, "yellow")]
    [InlineData(0.60, "yellow")]
    [InlineData(0.59, "orange")]
    [InlineData(0.50, "orange")]
    [InlineData(0.49, "red")]
    [InlineData(0.20, "red")]
    public void HardTraining_GlycogenBands_AreContextAware(
        double ratio,
        string expected)
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    PlannedWorkoutCode = "MAJOR_LEGS",
                    ActualWorkoutCodes = "MAJOR_LEGS",
                    GlycogenDemandG = 100m,
                    RestorationRatio = (decimal)ratio
                });

        Assert.Equal(expected, result.Glycogen.CssClass);
        Assert.Equal(expected, result.Carbohydrate.CssClass);
    }

    [Fact]
    public void HardTraining_GreenRefillAndModestCalorieDeficit_EarnsKudos()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    PlannedWorkoutCode = "MAJOR_LEGS",
                    ActualWorkoutCodes = "MAJOR_LEGS",
                    TargetCalories = 2290m,
                    ActualCalories = 2075m,
                    TargetProteinG = 185m,
                    ActualProteinG = 206m,
                    TargetCarbsG = 275m,
                    ActualCarbsG = 213m,
                    TargetFatG = 50m,
                    ActualFatG = 100m,
                    GlycogenDemandG = 101m,
                    RestorationRatio = 0.83m
                });

        Assert.Equal("green", result.Glycogen.CssClass);
        Assert.Equal("green", result.Carbohydrate.CssClass);
        Assert.NotNull(result.Kudos);
        Assert.Contains("Strong execution", result.Kudos);
    }

    [Theory]
    [InlineData("ZONE2_CARDIO")]
    [InlineData("EASY_CARDIO")]
    [InlineData("WALKING")]
    [InlineData("REST")]
    [InlineData("MOBILITY_RECOVERY")]
    public void EasyOrRecovery_LowCarbohydrate_IsNotAutomaticallyPenalized(
        string workout)
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    PlannedWorkoutCode = workout,
                    ActualWorkoutCodes = workout,
                    TargetCarbsG = 180m,
                    ActualCarbsG = 80m,
                    TargetCalories = 2000m,
                    ActualCalories = 1850m,
                    GlycogenDemandG = workout == "REST" ? 0m : 20m,
                    RestorationRatio = 0.35m
                });

        Assert.Equal("green", result.Carbohydrate.CssClass);
    }

    [Fact]
    public void Zone2_EnergyControlGetsGreen_WhenCaloriesAreReasonablyBelowPlan()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    PlannedWorkoutCode = "ZONE2_CARDIO",
                    ActualWorkoutCodes = "ZONE2_CARDIO",
                    TargetCalories = 2100m,
                    ActualCalories = 1850m
                });

        Assert.Equal("green", result.Calories.CssClass);
    }

    [Fact]
    public void Recovery_ExtremeUnderEating_IsNotCalledGreen()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    PlannedWorkoutCode = "REST",
                    ActualWorkoutCodes = null,
                    TargetCalories = 2000m,
                    ActualCalories = 1100m
                });

        Assert.Equal("red", result.Calories.CssClass);
    }

    [Theory]
    [InlineData(1.00, "green")]
    [InlineData(0.92, "green")]
    [InlineData(0.85, "yellow")]
    [InlineData(0.75, "orange")]
    [InlineData(0.60, "red")]
    public void ProteinBands_AreStableAcrossDayTypes(
        double actualRatio,
        string expected)
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    TargetProteinG = 180m,
                    ActualProteinG = 180m * (decimal)actualRatio
                });

        Assert.Equal(expected, result.Protein.CssClass);
    }

    [Theory]
    [InlineData(1.00, "green")]
    [InlineData(1.30, "green")]
    [InlineData(1.50, "yellow")]
    [InlineData(1.80, "orange")]
    [InlineData(2.00, "red")]
    [InlineData(0.50, "orange")]
    [InlineData(0.30, "red")]
    public void FatBands_FlagMeaningfulDeviation(
        double actualRatio,
        string expected)
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    TargetFatG = 60m,
                    ActualFatG = 60m * (decimal)actualRatio
                });

        Assert.Equal(expected, result.Fat.CssClass);
    }

    [Fact]
    public void MissingMorningScale_PromptsForScaleButDoesNotBlockAnalysis()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    CurrentMorningWeightLb = null,
                    CurrentMorningBodyFatFraction = null,
                    CurrentMorningMuscleMassLb = null
                });

        Assert.False(result.HasCurrentMorningScale);
        Assert.Equal("gray", result.ScaleConsistency.CssClass);
        Assert.Contains(
            "Please enter this morning's scale readings",
            result.ScaleConsistency.Value);
    }

    [Fact]
    public void HardDay_GoodRefillAndTwoPoundGain_IsPlausible_NotTrackingFailure()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    PlannedWorkoutCode = "MAJOR_LEGS",
                    ActualWorkoutCodes = "MAJOR_LEGS",
                    GlycogenDemandG = 100m,
                    RestorationRatio = 0.85m,
                    CurrentMorningWeightLb = 212m,
                    PriorMorningWeightLb = 210m
                });

        Assert.Equal("green", result.ScaleConsistency.CssClass);
        Assert.DoesNotContain(
            "Potential tracking mismatch",
            result.DataQualityNarrative);
    }

    [Fact]
    public void LowLoggedCaloriesAndLowRefill_WithTwoPoundGain_LowersConfidence()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    PlannedWorkoutCode = "MAJOR_LEGS",
                    ActualWorkoutCodes = "MAJOR_LEGS",
                    TargetCalories = 2300m,
                    ActualCalories = 1750m,
                    GlycogenDemandG = 100m,
                    RestorationRatio = 0.45m,
                    CurrentMorningWeightLb = 212m,
                    PriorMorningWeightLb = 210m,
                    FourDayNutritionCompleteDays = 3,
                    FourDayScaleDays = 4
                });

        Assert.Equal("yellow", result.ScaleConsistency.CssClass);
        Assert.Contains(
            "Potential tracking mismatch",
            result.DataQualityNarrative);
        Assert.Contains(
            "review portion sizes",
            result.DataQualityNarrative,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OneScaleMismatch_DoesNotClaimNutritionLogIsWrong()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    PlannedWorkoutCode = "MAJOR_LEGS",
                    ActualWorkoutCodes = "MAJOR_LEGS",
                    TargetCalories = 2300m,
                    ActualCalories = 1750m,
                    GlycogenDemandG = 100m,
                    RestorationRatio = 0.45m,
                    CurrentMorningWeightLb = 212m,
                    PriorMorningWeightLb = 210m
                });

        Assert.Contains("can still explain", result.ScaleConsistency.Detail);
        Assert.DoesNotContain(
            "nutrition log is wrong",
            result.DataQualityNarrative,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingNutrition_IsMissingEvidence_NotZero()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    ActualCalories = null,
                    ActualProteinG = null,
                    ActualCarbsG = null,
                    ActualFatG = null,
                    RestorationRatio = null
                });

        Assert.Equal("gray", result.Calories.CssClass);
        Assert.Equal("gray", result.Protein.CssClass);
        Assert.Contains(
            "missing evidence",
            result.DataQualityNarrative,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlannedHardWorkoutWithoutActual_IsRedWorkoutSignal()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    PlannedWorkoutCode = "MAJOR_PULL",
                    ActualWorkoutCodes = null,
                    ActualWorkoutSummary = null
                });

        Assert.Equal("red", result.Workout.CssClass);
    }

    [Fact]
    public void SameWorkout_IsGreen()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    PlannedWorkoutCode = "MAJOR_PULL",
                    ActualWorkoutCodes = "MAJOR_PULL"
                });

        Assert.Equal("green", result.Workout.CssClass);
    }

    [Fact]
    public void DifferentHardStrengthWorkout_IsYellow()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    PlannedWorkoutCode = "MAJOR_PUSH",
                    ActualWorkoutCodes = "MAJOR_PULL"
                });

        Assert.Equal("yellow", result.Workout.CssClass);
    }

    [Fact]
    public void HardWorkoutOnRestDay_IsOrange()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    PlannedWorkoutCode = "REST",
                    ActualWorkoutCodes = "MAJOR_LEGS"
                });

        Assert.Equal("orange", result.Workout.CssClass);
    }

    [Theory]
    [InlineData("REST", "Recovery / rest")]
    [InlineData("ZONE2_CARDIO", "Zone 2 / easy cardio")]
    [InlineData("GENERAL_CARDIO", "Cardio")]
    [InlineData("ACCESSORY", "Moderate resistance")]
    [InlineData("MAJOR_LEGS", "Hard resistance")]
    [InlineData("TRUE_HIIT", "Hard conditioning")]
    [InlineData("ORANGETHEORY", "Hard conditioning")]
    [InlineData("MOBILITY_RECOVERY", "Recovery / rest")]
    [InlineData("OTF_STANDARD", "Hard conditioning")]
    [InlineData("OTF_STRENGTH_TOTAL", "Moderate resistance")]
    public void WorkoutTaxonomy_MapsToDashboardContexts(
        string code,
        string expected)
    {
        Assert.Equal(
            expected,
            Engine30DashboardAnalysisService.DayContextLabelFromCodes(
                code,
                null));
    }

    [Theory]
    [InlineData(4, 4, "green")]
    [InlineData(4, 3, "yellow")]
    [InlineData(4, 2, "orange")]
    [InlineData(4, 1, "red")]
    public void TrendTraining_GradesMissedDays(
        int planned,
        int completed,
        string expected)
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    FourDayPlannedTrainingDays = planned,
                    FourDayCompletedTrainingDays = completed
                });

        Assert.Equal(expected, result.TrendTraining.CssClass);
    }

    [Fact]
    public void CompleteConsistentFourDayEvidence_GetsHighTrackingConfidence()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    FourDayNutritionCompleteDays = 4,
                    FourDayScaleDays = 4,
                    CurrentMorningWeightLb = 210.2m,
                    PriorMorningWeightLb = 210.0m
                });

        Assert.Equal("green", result.TrackingConfidence.CssClass);
        Assert.Equal("High", result.TrackingConfidence.Value);
    }

    [Fact]
    public void IncompleteEvidence_LowersTrackingConfidence()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    FourDayNutritionCompleteDays = 2,
                    FourDayScaleDays = 1
                });

        Assert.Equal("orange", result.TrackingConfidence.CssClass);
        Assert.Equal("Low", result.TrackingConfidence.Value);
    }

    [Fact]
    public void TrendNarrative_AvoidsOneDayTrackingVerdict()
    {
        var result =
            _service.Analyze(Base());

        Assert.Contains(
            "not from one odd morning",
            result.TrendNarrative,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrackingCaveat_ExplainsCommonLoggingErrorSources()
    {
        var result =
            _service.Analyze(Base());

        Assert.Contains("cooking fats", result.TrackingCaveat);
        Assert.Contains("restaurant meals", result.TrackingCaveat);
        Assert.Contains("repeated intake/scale mismatches", result.TrackingCaveat);
    }

    [Fact]
    public void MissingTodayScale_CannotReceiveHighTrackingConfidence()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    CurrentMorningWeightLb = null,
                    CurrentMorningBodyFatFraction = null,
                    CurrentMorningMuscleMassLb = null,
                    FourDayNutritionCompleteDays = 4,
                    FourDayScaleDays = 4
                });

        Assert.NotEqual(
            "High",
            result.TrackingConfidence.Value);

        Assert.Contains(
            "Today's morning scale is missing",
            result.TrackingConfidence.Detail);
    }

    [Fact]
    public void HardDay_99PercentRefill_QuantifiesGlycogenAndWater()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    PlannedWorkoutCode = "MAJOR_LEGS",
                    ActualWorkoutCodes = "MAJOR_LEGS",
                    GlycogenDemandG = 101m,
                    RestorationRatio = 0.99m,
                    CurrentMorningWeightLb = 212.7m,
                    PriorMorningWeightLb = 210.0m
                });

        Assert.Equal(
            100m,
            result.EstimatedRestoredGlycogenG);

        Assert.Equal(
            300m,
            result.EstimatedAssociatedWaterG);

        Assert.Equal(
            0.9m,
            result.EstimatedGlycogenWaterScaleLb);

        Assert.Contains(
            "did not build 2.7 lb of muscle",
            result.ScaleExplanation,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "about 100 g of glycogen restoration",
            result.ScaleExplanation,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "about 0.9 lb of combined glycogen + water",
            result.ScaleExplanation,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HardDayScaleExplanation_DoesNotPretendToDecomposeWeightExactly()
    {
        var result =
            _service.Analyze(
                Base() with
                {
                    PlannedWorkoutCode = "MAJOR_LEGS",
                    ActualWorkoutCodes = "MAJOR_LEGS",
                    GlycogenDemandG = 101m,
                    RestorationRatio = 0.99m,
                    CurrentMorningWeightLb = 212.7m,
                    PriorMorningWeightLb = 210.0m
                });

        Assert.Contains(
            "not a precise decomposition",
            result.ScaleExplanation,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "hydration",
            result.ScaleExplanation,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "inflammation",
            result.ScaleExplanation,
            StringComparison.OrdinalIgnoreCase);
    }
    private static Engine30DashboardAnalysisInput Base()
        => new(
            ActionDate: new DateOnly(2026, 9, 10),
            PlanCode: "PLAN_4",

            PlannedWorkoutCode: "MAJOR_LEGS",
            ActualWorkoutCodes: "MAJOR_LEGS",
            ActualWorkoutSummary: "Major Legs - 60 min, RPE 9",

            TargetCalories: 2200m,
            ActualCalories: 2100m,
            TargetProteinG: 180m,
            ActualProteinG: 185m,
            TargetCarbsG: 240m,
            ActualCarbsG: 220m,
            TargetFatG: 60m,
            ActualFatG: 60m,

            GlycogenDemandG: 100m,
            RestorationRatio: 0.85m,

            FourDayRestorationRatio: 0.85m,
            FourDayHardDays: 2,
            FourDayModerateHardDays: 3,
            FourDayCarbohydrateEvidenceDays: 4,

            FourDayNutritionCompleteDays: 4,
            FourDayPlannedTrainingDays: 3,
            FourDayCompletedTrainingDays: 3,
            FourDayAverageProteinG: 182m,
            FourDayAverageFatG: 62m,

            WeekEnergyOverageKcal: 0m,
            WeekEligibleEnergyCarryoverKcal: 0m,

            CurrentMorningWeightLb: 210.2m,
            PriorMorningWeightLb: 210.0m,
            CurrentMorningBodyFatFraction: 0.25m,
            PriorMorningBodyFatFraction: 0.251m,
            CurrentMorningMuscleMassLb: 145.0m,
            PriorMorningMuscleMassLb: 144.9m,

            FourDayScaleDays: 4,
            TrendStartWeightLb: 211.0m,
            TrendEndWeightLb: 210.2m,
            TrendStartBodyFatFraction: 0.252m,
            TrendEndBodyFatFraction: 0.25m,
            TrendStartMuscleMassLb: 144.8m,
            TrendEndMuscleMassLb: 145.0m);
}