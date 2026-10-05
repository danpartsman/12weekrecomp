namespace WebRecomp.Web.Services.Engine;

/// <summary>
/// Application-facing recommendation contract.
///
/// Dashboard, planning UI, and future consumers depend on this contract rather
/// than on a specific physiology engine. Engine 3.0 is the active implementation
/// in v1014A; a future Engine 4 implementation can replace it without changing
/// the Dashboard contract.
/// </summary>
public sealed record RecompRecommendationInput(
    bool HasPlannedWorkout,
    string? PlannedWorkoutTypeCode,
    string PlannedWorkoutDisplayName,
    string PlannedWorkoutCategoryCode,
    decimal PlannedGlycogenDemandG,
    decimal PreWorkoutRescueG,
    string IncomingReadinessBandCode,
    string ProjectedReadinessBandCode,
    decimal? PreviousTargetCalories,
    decimal? PreviousEnergyVarianceKcal,
    decimal? PreviousEligibleEnergyCarryoverKcal,
    short FourDayHardDays,
    short FourDayModerateHardDays,
    decimal? SleepHours,
    byte? EnergyScore,
    byte? SorenessScore,
    bool PainFlag,
    bool IllnessFlag);

public sealed record RecompRecommendationResult(
    string EngineVersionCode,
    bool ShowOnDashboard,
    string DecisionCode,
    bool WorkoutChangeRecommended,
    string? PlannedWorkoutTypeCode,
    string PlannedWorkoutDisplayName,
    string? RecommendedWorkoutTypeCode,
    string RecommendedWorkoutDisplayName,
    decimal? RecommendedDurationMinutes,
    string NutritionActionCode,
    string Title,
    string Summary,
    string NutritionSummary,
    decimal? MaterialEnergyOverageKcal,
    decimal? MaterialEnergyOverageThresholdKcal,
    IReadOnlyList<string> ReasonCodes);

public interface IRecompRecommendationEngine
{
    RecompRecommendationResult Evaluate(
        RecompRecommendationInput input);
}
