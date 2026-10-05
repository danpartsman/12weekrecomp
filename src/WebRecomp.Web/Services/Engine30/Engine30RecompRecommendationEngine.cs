using WebRecomp.Web.Services.Engine;

namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Engine 3.0 implementation of the application-facing recommendation contract.
/// The Dashboard no longer calls Engine 2.x RecommendationService.
/// </summary>
public sealed class Engine30RecompRecommendationEngine(
    Engine30AdaptiveRecommendationService adaptive)
    : IRecompRecommendationEngine
{
    public RecompRecommendationResult Evaluate(
        RecompRecommendationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var result =
            adaptive.Evaluate(
                new Engine30AdaptiveRecommendationInput(
                    HasPlannedWorkout:
                        input.HasPlannedWorkout,
                    PlannedWorkoutTypeCode:
                        input.PlannedWorkoutTypeCode,
                    PlannedWorkoutDisplayName:
                        input.PlannedWorkoutDisplayName,
                    PlannedWorkoutCategoryCode:
                        input.PlannedWorkoutCategoryCode,
                    PlannedGlycogenDemandG:
                        input.PlannedGlycogenDemandG,
                    PreWorkoutRescueG:
                        input.PreWorkoutRescueG,
                    IncomingReadinessBandCode:
                        input.IncomingReadinessBandCode,
                    ProjectedReadinessBandCode:
                        input.ProjectedReadinessBandCode,
                    PreviousTargetCalories:
                        input.PreviousTargetCalories,
                    PreviousEnergyVarianceKcal:
                        input.PreviousEnergyVarianceKcal,
                    PreviousEligibleEnergyCarryoverKcal:
                        input.PreviousEligibleEnergyCarryoverKcal,
                    FourDayHardDays:
                        input.FourDayHardDays,
                    FourDayModerateHardDays:
                        input.FourDayModerateHardDays,
                    SleepHours:
                        input.SleepHours,
                    EnergyScore:
                        input.EnergyScore,
                    SorenessScore:
                        input.SorenessScore,
                    PainFlag:
                        input.PainFlag,
                    IllnessFlag:
                        input.IllnessFlag));

        return new RecompRecommendationResult(
            EngineVersionCode:
                Engine30DashboardProjectionService.EngineVersionCode,
            ShowOnDashboard:
                result.ShowOnDashboard,
            DecisionCode:
                result.DecisionCode,
            WorkoutChangeRecommended:
                result.WorkoutChangeRecommended,
            PlannedWorkoutTypeCode:
                result.PlannedWorkoutTypeCode,
            PlannedWorkoutDisplayName:
                result.PlannedWorkoutDisplayName,
            RecommendedWorkoutTypeCode:
                result.RecommendedWorkoutTypeCode,
            RecommendedWorkoutDisplayName:
                result.RecommendedWorkoutDisplayName,
            RecommendedDurationMinutes:
                result.RecommendedDurationMinutes,
            NutritionActionCode:
                result.NutritionActionCode,
            Title:
                result.Title,
            Summary:
                result.Summary,
            NutritionSummary:
                result.NutritionSummary,
            MaterialEnergyOverageKcal:
                result.MaterialEnergyOverageKcal,
            MaterialEnergyOverageThresholdKcal:
                result.MaterialEnergyOverageThresholdKcal,
            ReasonCodes:
                result.ReasonCodes);
    }
}
