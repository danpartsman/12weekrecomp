using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

/// <summary>
/// 12WR v1009 adaptive recommendation layer scenario matrix.
/// </summary>
public sealed class Engine30AdaptiveRecommendationServiceTests
{
    private readonly Engine30AdaptiveRecommendationService _service =
        new();

    [Fact]
    public void RescueableMajorLegs_UsesCarbsThenKeepsPlan()
    {
        var result =
            _service.Evaluate(
                Base(
                    plannedCode: "MAJOR_LEGS",
                    plannedName: "Major Legs",
                    category: "MAJOR_STRENGTH",
                    glycogenG: 94m,
                    rescueG: 35m,
                    incoming: "VERY_LOW",
                    projected: "ADEQUATE"));

        Assert.Equal(
            Engine30AdaptiveDecisionCodes.FuelRescueKeepPlan,
            result.DecisionCode);

        Assert.False(
            result.WorkoutChangeRecommended);

        Assert.Equal(
            "MAJOR_LEGS",
            result.RecommendedWorkoutTypeCode);

        Assert.Equal(
            Engine30AdaptiveNutritionActionCodes.UsePreWorkoutCarbsWithinTarget,
            result.NutritionActionCode);

        Assert.Contains(
            "FUEL_RESCUE_SUFFICIENT",
            result.ReasonCodes);
    }

    [Fact]
    public void SevereUnderfuelMajorLegs_AfterCappedRescue_DowngradesToZone2()
    {
        var result =
            _service.Evaluate(
                Base(
                    plannedCode: "MAJOR_LEGS",
                    plannedName: "Major Legs",
                    category: "MAJOR_STRENGTH",
                    glycogenG: 125m,
                    rescueG: 35m,
                    incoming: "VERY_LOW",
                    projected: "LOW"));

        Assert.Equal(
            Engine30AdaptiveDecisionCodes.LowFuelDowngrade,
            result.DecisionCode);

        Assert.True(
            result.WorkoutChangeRecommended);

        Assert.Equal(
            "ZONE2_CARDIO",
            result.RecommendedWorkoutTypeCode);

        Assert.Equal(
            Engine30AdaptiveNutritionActionCodes.RecoveryCarbsKeepCurrentTarget,
            result.NutritionActionCode);

        Assert.Contains(
            "LOW_FUEL_AFTER_RESCUE",
            result.ReasonCodes);

        Assert.Contains(
            "RECOVERY_CARBOHYDRATE_PRESERVED",
            result.ReasonCodes);
    }

    [Fact]
    public void SevereUnderfuelHiit_AfterRescue_DowngradesToZone2()
    {
        var result =
            _service.Evaluate(
                Base(
                    plannedCode: "TRUE_HIIT",
                    plannedName: "HIIT",
                    category: "CONDITIONING_CARDIO",
                    glycogenG: 155m,
                    rescueG: 55m,
                    incoming: "VERY_LOW",
                    projected: "LOW"));

        Assert.True(
            result.WorkoutChangeRecommended);

        Assert.Equal(
            "ZONE2_CARDIO",
            result.RecommendedWorkoutTypeCode);

        Assert.Equal(
            40m,
            result.RecommendedDurationMinutes);
    }

    [Fact]
    public void LowFuelZone2_DoesNotDowngradeAnAlreadyLowerDemandSession()
    {
        var result =
            _service.Evaluate(
                Base(
                    plannedCode: "ZONE2_CARDIO",
                    plannedName: "Zone 2 Cardio",
                    category: "CONDITIONING_CARDIO",
                    glycogenG: 39m,
                    rescueG: 25m,
                    incoming: "VERY_LOW",
                    projected: "LOW"));

        Assert.Equal(
            Engine30AdaptiveDecisionCodes.KeepPlan,
            result.DecisionCode);

        Assert.False(
            result.WorkoutChangeRecommended);
    }

    [Fact]
    public void MaterialOverageIntoRest_WithGoodRecovery_AddsOnlyLightActivity()
    {
        var input =
            Base(
                plannedCode: "REST",
                plannedName: "Rest",
                category: "REST_RECOVERY_OTHER",
                glycogenG: 0m,
                rescueG: 0m,
                incoming: "ADEQUATE",
                projected: "ADEQUATE")
            with
            {
                PreviousTargetCalories = 1600m,
                PreviousEnergyVarianceKcal = 400m,
                PreviousEligibleEnergyCarryoverKcal = 300m,
                FourDayHardDays = 1,
                FourDayModerateHardDays = 2,
                SleepHours = 7.0m,
                EnergyScore = 7,
                SorenessScore = 4
            };

        var result =
            _service.Evaluate(
                input);

        Assert.Equal(
            Engine30AdaptiveDecisionCodes.AddLightActivityAfterOverage,
            result.DecisionCode);

        Assert.True(
            result.WorkoutChangeRecommended);

        Assert.Equal(
            "WALKING",
            result.RecommendedWorkoutTypeCode);

        Assert.Equal(
            35m,
            result.RecommendedDurationMinutes);

        Assert.Equal(
            Engine30AdaptiveNutritionActionCodes.FollowCurrentTargetNoEatBack,
            result.NutritionActionCode);

        Assert.Contains(
            "NO_EXERCISE_CALORIE_EATBACK",
            result.ReasonCodes);
    }

    [Fact]
    public void OverageIntoRest_WithHighSoreness_PreservesRest()
    {
        var input =
            RestWithOverage()
            with
            {
                SorenessScore = 8
            };

        var result =
            _service.Evaluate(
                input);

        Assert.Equal(
            Engine30AdaptiveDecisionCodes.PreserveRestForRecovery,
            result.DecisionCode);

        Assert.False(
            result.WorkoutChangeRecommended);

        Assert.Equal(
            "REST",
            result.RecommendedWorkoutTypeCode);

        Assert.Contains(
            "HIGH_SORENESS_PRESERVES_REST",
            result.ReasonCodes);
    }

    [Fact]
    public void OverageIntoRest_WithThreeRecentHardDays_PreservesRest()
    {
        var input =
            RestWithOverage()
            with
            {
                FourDayHardDays = 3
            };

        var result =
            _service.Evaluate(
                input);

        Assert.Equal(
            Engine30AdaptiveDecisionCodes.PreserveRestForRecovery,
            result.DecisionCode);

        Assert.Contains(
            "RECENT_HARD_LOAD_PRESERVES_REST",
            result.ReasonCodes);
    }

    [Fact]
    public void SmallOverageIntoRest_DoesNotCreateExerciseRecommendation()
    {
        var input =
            RestWithOverage()
            with
            {
                PreviousTargetCalories = 1600m,
                PreviousEnergyVarianceKcal = 150m,
                PreviousEligibleEnergyCarryoverKcal = 110m
            };

        var result =
            _service.Evaluate(
                input);

        Assert.Equal(
            Engine30AdaptiveDecisionCodes.KeepPlan,
            result.DecisionCode);

        Assert.False(
            result.ShowOnDashboard);

        Assert.False(
            result.WorkoutChangeRecommended);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void PainOrIllness_AlwaysBeatsFuelOrEnergyBalancing(
        bool pain,
        bool illness)
    {
        var input =
            Base(
                plannedCode: "TRUE_HIIT",
                plannedName: "HIIT",
                category: "CONDITIONING_CARDIO",
                glycogenG: 155m,
                rescueG: 55m,
                incoming: "VERY_LOW",
                projected: "LOW")
            with
            {
                PainFlag = pain,
                IllnessFlag = illness,
                PreviousTargetCalories = 2000m,
                PreviousEnergyVarianceKcal = 800m,
                PreviousEligibleEnergyCarryoverKcal = 600m
            };

        var result =
            _service.Evaluate(
                input);

        Assert.Equal(
            Engine30AdaptiveDecisionCodes.SafetyRecovery,
            result.DecisionCode);

        Assert.Equal(
            "REST",
            result.RecommendedWorkoutTypeCode);
    }

    private static Engine30AdaptiveRecommendationInput RestWithOverage()
        => Base(
            plannedCode: "REST",
            plannedName: "Rest",
            category: "REST_RECOVERY_OTHER",
            glycogenG: 0m,
            rescueG: 0m,
            incoming: "ADEQUATE",
            projected: "ADEQUATE")
        with
        {
            PreviousTargetCalories = 1600m,
            PreviousEnergyVarianceKcal = 400m,
            PreviousEligibleEnergyCarryoverKcal = 300m,
            FourDayHardDays = 1,
            FourDayModerateHardDays = 2,
            SleepHours = 7m,
            EnergyScore = 7,
            SorenessScore = 4
        };

    private static Engine30AdaptiveRecommendationInput Base(
        string plannedCode,
        string plannedName,
        string category,
        decimal glycogenG,
        decimal rescueG,
        string incoming,
        string projected)
        => new(
            HasPlannedWorkout:
                true,
            PlannedWorkoutTypeCode:
                plannedCode,
            PlannedWorkoutDisplayName:
                plannedName,
            PlannedWorkoutCategoryCode:
                category,
            PlannedGlycogenDemandG:
                glycogenG,

            PreWorkoutRescueG:
                rescueG,
            IncomingReadinessBandCode:
                incoming,
            ProjectedReadinessBandCode:
                projected,

            PreviousTargetCalories:
                null,
            PreviousEnergyVarianceKcal:
                null,
            PreviousEligibleEnergyCarryoverKcal:
                null,

            FourDayHardDays:
                0,
            FourDayModerateHardDays:
                0,

            SleepHours:
                null,
            EnergyScore:
                null,
            SorenessScore:
                null,
            PainFlag:
                false,
            IllnessFlag:
                false);
}