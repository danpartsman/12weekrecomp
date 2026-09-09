using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record RecompFuelState(
    decimal DemandG,
    decimal RestorationCreditG,
    decimal AbsoluteRestorationRatio,
    decimal FunctionalTargetRatio,
    decimal PreRescueAchievementRatio,
    string PreRescueReadinessCode,
    short ModerateHardDays,
    short HardDays);

public sealed record RecompRescuePlan(
    decimal ShortfallG,
    decimal AllocationG,
    decimal PreWorkoutG,
    decimal ProjectedAchievementRatio,
    string ProjectedReadinessCode,
    bool RescueIsMeaningful);

public sealed record RecompEngineAlert(
    string Code,
    string Severity,
    string Message);

public sealed record RecompWorkoutDecision(
    WorkoutTypeDefinition PlannedWorkout,
    WorkoutTypeDefinition RecommendedWorkout,
    bool ScheduleChangeRecommended,
    string BasisCode,
    IReadOnlyList<RecompEngineAlert> Alerts);

public sealed record RecompNutritionTargets(
    decimal BaseCalories,
    decimal RecommendedCalories,
    decimal ProteinG,
    decimal BaseCarbsG,
    decimal RecoveryAllocationG,
    decimal PreWorkoutG,
    decimal RecommendedCarbsG,
    decimal FatG,
    string TimingNote);

public sealed record RecompGuidanceCodes(
    string TaxonomyVersion,
    string WorkoutScenarioCode,
    string NutritionScenarioCode,
    string RecoveryScenarioCode,
    string DailyGuidanceKey,
    IReadOnlyList<string> Tags);

public sealed record RecompEngineResult(
    DateOnly ActionDate,
    WeeklyStrategyAssessment WeeklyStrategy,
    RegionRecovery RegionRecovery,
    RecompFuelState Fuel,
    RecompRescuePlan Rescue,
    RecompWorkoutDecision Decision,
    RecompTrainingPrescription TrainingPrescription,
    RecompFuelState FinalFuel,
    RecompRescuePlan FinalRescue,
    RecompNutritionTargets Nutrition,
    RecompGuidanceCodes Guidance,
    string Explanation);
