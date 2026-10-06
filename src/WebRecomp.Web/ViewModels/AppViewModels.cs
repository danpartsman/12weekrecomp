using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace WebRecomp.Web.ViewModels;

public sealed class IntakeViewModel
{
    public bool ReviewMode { get; set; }
    public string? OriginalGoalCode { get; set; }
    public bool ConfirmPlanChange { get; set; }

    [Required, StringLength(100)]
    public string ClientName { get; set; } = "";

    [DataType(DataType.Date)]
    public DateOnly? DateOfBirth { get; set; }

    public string? SexCode { get; set; }

    [Range(48, 90)]
    public decimal HeightIn { get; set; } = 73;

    [Required]
    public string TimeZone { get; set; } = "America/Los_Angeles";

    [Required]
    public string TrainingExperienceCode { get; set; } = "INTERMEDIATE";

    [Range(75, 700)]
    public decimal StartingWeightLb { get; set; }

    [Range(2, 70)]
    public decimal? StartingBodyFatPercent { get; set; }

    public decimal? StartingMuscleMassLb { get; set; }
    public decimal? StartingBodyWaterLb { get; set; }
    public decimal? StartingWaistIn { get; set; }
    public decimal? StartingHipsIn { get; set; }
    public decimal? StartingVisceralFatRating { get; set; }

    [Required]
    public string GoalCode { get; set; } = "";

    [Range(1, 7)]
    public byte AvailableTrainingDaysWeek { get; set; } = 6;

    public decimal? TargetWeightLowLb { get; set; }
    public decimal? TargetWeightHighLb { get; set; }
    public decimal? TargetWaistIn { get; set; }
    public string? GoalNotes { get; set; }

    [DataType(DataType.Date)]
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}

public sealed class DailyViewModel
{
    [DataType(DataType.Date)]
    public DateOnly Date { get; set; }

    public decimal? WeightLb { get; set; }
    public decimal? BodyFatPercent { get; set; }
    public decimal? MuscleMassLb { get; set; }
    public decimal? BodyWaterLb { get; set; }
    public decimal? VisceralFatRating { get; set; }
    public decimal? WaistIn { get; set; }
    public decimal? HipsIn { get; set; }

    [Range(0, 16)]
    public decimal? SleepHours { get; set; }

    [Range(25, 220)]
    public short? RestingHr { get; set; }

    [Range(0, 100000)]
    public int? Steps { get; set; }

    [Range(1, 10)]
    public byte? EnergyScore { get; set; }

    [Range(1, 10)]
    public byte? SorenessScore { get; set; }

    public bool PainFlag { get; set; }
    public bool IllnessFlag { get; set; }

    public string? Notes { get; set; }
}

public sealed class WorkoutEntryViewModel
{
    public Guid? WorkoutSessionId { get; set; }

    [DataType(DataType.Date)]
    public DateOnly Date { get; set; }

    [Required]
    public string WorkoutTypeCode { get; set; } = "";

    public string? SystemWorkoutCode { get; set; }

    [Range(0, 360)]
    public decimal DurationMin { get; set; }

    [Range(0, 600)]
    public short? TypicalRestSec { get; set; }

    [Range(1, 10)]
    public decimal? SessionRpe { get; set; }

    [Range(0, 5000)]
    public decimal? DeviceCalories { get; set; }

    public bool CompletedFlag { get; set; } = true;
    public string? Notes { get; set; }

    public List<SelectListItem> WorkoutTypes { get; set; } = [];
    public List<SystemWorkoutOptionViewModel> SystemWorkouts { get; set; } = [];
}

public sealed class WorkoutSessionSummaryViewModel
{
    public Guid WorkoutSessionId { get; set; }
    public string WorkoutDisplayName { get; set; } = "";
    public decimal DurationMin { get; set; }
    public decimal? SessionRpe { get; set; }
    public bool CompletedFlag { get; set; }
}

public sealed class NutritionViewModel
{
    [DataType(DataType.Date)]
    public DateOnly Date { get; set; }

    public decimal? ActualCalories { get; set; }
    public decimal? ActualProteinG { get; set; }
    public decimal? ActualCarbsG { get; set; }
    public decimal? ActualFatG { get; set; }
    public decimal? FiberG { get; set; }
    public decimal? WaterOz { get; set; }
    public string? Notes { get; set; }
}

public sealed class PlannedDayViewModel
{
    [DataType(DataType.Date)]
    public DateOnly Date { get; set; }
    public string DayName { get; set; } = "";
    public string? WorkoutTypeCode { get; set; }
    public Guid? WorkoutTemplateId { get; set; }
    public string? SystemWorkoutCode { get; set; }
    public string? WorkoutDisplayName { get; set; }
    public decimal? PlannedDurationMin { get; set; }
    public decimal? PlannedRpe { get; set; }
    public string? WeeklyRole { get; set; }
    public string? Region { get; set; }

    public decimal? Calories { get; set; }
    public decimal? ProteinG { get; set; }
    public decimal? BaseCarbsG { get; set; }
    public decimal? FatG { get; set; }
    public decimal? ScaledGlycogenBurnG { get; set; }
    public string? TimingNote { get; set; }
}

public sealed class RecommendedDayViewModel
{
    [DataType(DataType.Date)]
    public DateOnly Date { get; set; }
    public string DayName { get; set; } = "";
    public string WorkoutTypeCode { get; set; } = "";
    public string WorkoutDisplayName { get; set; } = "";
}

public sealed class WeekPlanViewModel
{
    [DataType(DataType.Date)]
    public DateOnly WeekStart { get; set; }

    public List<PlannedDayViewModel> Days { get; set; } = [];
    public List<SelectListItem> WorkoutTypes { get; set; } = [];
    public List<SystemWorkoutOptionViewModel> SystemWorkouts { get; set; } = [];
    public List<WorkoutTemplateOptionViewModel> MyWorkouts { get; set; } = [];
    public List<RecommendedDayViewModel> RecommendedDays { get; set; } = [];

    public string PlanCode { get; set; } = "";
    public string PlanDisplayName { get; set; } = "";
    public string PlanGuidance { get; set; } = "";

    // Weekly Plan goal preview. Previewing never mutates the saved program;
    // the user must explicitly choose "Use this goal" before it becomes active.
    public string CurrentGoalCode { get; set; } = "";
    public string CurrentGoalDisplayName { get; set; } = "";
    public string PreviewGoalCode { get; set; } = "";
    public string PreviewGoalDisplayName { get; set; } = "";
    public string PreviewGoalDescription { get; set; } = "";
    public bool IsGoalPreview { get; set; }
    public bool CanChangeGoal { get; set; }
    public List<SelectListItem> GoalOptions { get; set; } = [];

    public decimal? PreviewWeeklyDeficitKcal { get; set; }
    public decimal? PreviewFatLossEnergyEquivalentLb { get; set; }
    public string PreviewFatDirection { get; set; } = "";
    public string PreviewMuscleDirection { get; set; } = "";
    public string PreviewMuscleStimulus { get; set; } = "";

    public bool SeededFromPlan { get; set; }
    public bool AutoRolled { get; set; }
    public bool NextWeekHasPlan { get; set; }
    public bool ShowRecommendedPlanPrompt { get; set; }

    public string WeeklyStatus { get; set; } = "NOT_PLANNED";
    public string WeeklyNote { get; set; } = "";
}

public sealed class WeekPlanPostModel
{
    [DataType(DataType.Date)]
    public DateOnly WeekStart { get; set; }
    public List<WeekPlanDayPostModel> Days { get; set; } = [];
}

public sealed class WeekPlanDayPostModel
{
    [DataType(DataType.Date)]
    public DateOnly Date { get; set; }
    public string? WorkoutTypeCode { get; set; }
    public Guid? WorkoutTemplateId { get; set; }
    public string? SystemWorkoutCode { get; set; }
}

public sealed class SystemWorkoutOptionViewModel
{
    public string SystemCode { get; set; } = "";
    public string EngineWorkoutTypeCode { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string GroupName { get; set; } = "Standard";
    public decimal DefaultMinutes { get; set; }
    public decimal DefaultRpe { get; set; }
}

public sealed class WorkoutTemplateOptionViewModel
{
    public Guid WorkoutTemplateId { get; set; }
    public string TemplateName { get; set; } = "";
    public string WorkoutTypeCode { get; set; } = "";
    public decimal DefaultDurationMin { get; set; }
    public int ExerciseCount { get; set; }
}

public sealed class DashboardViewModel
{
    public DateOnly ActionDate { get; set; }
    public string ClientName { get; set; } = "";
    public int ProgramWeek { get; set; }
    public string PlanCode { get; set; } = "";
    public bool NeedsCurrentWeight { get; set; }
    // Engine 3.0 authoritative Dashboard projection
    public string Engine30VersionCode { get; set; } = "";
    public string Engine30ConfigVersionCode { get; set; } = "";
    public string? Engine30WorkoutProfileVersionCode { get; set; }
    public string? Engine30WorkoutProfileSourceCode { get; set; }
    public string Engine30WorkoutCategoryCode { get; set; } = "";
    // Coaching content resolver.
    // MARISOL is the current default. A later client preference can override it.
    public string PreferredCoachCode { get; set; } = "MARISOL";
    public string? Engine30WorkoutTypeCode { get; set; }
    public string? TomorrowEngine30WorkoutTypeCode { get; set; }
    public string Engine30CarbGoverningRuleCode { get; set; } = "";
    public decimal Engine30BaseLivingKcal { get; set; }
    public decimal Engine30HabitualActivityKcal { get; set; }
    public decimal Engine30NetWorkoutKcal { get; set; }
    public decimal Engine30MaintenanceKcal { get; set; }
    public decimal Engine30EffectiveDeficitPct { get; set; }

    public string TodayStatusLabel { get; set; } = "";
    public string TodayStatusClass { get; set; } = "neutral";

    // Today - workout
    public string PlannedWorkout { get; set; } = "";
    public string RecommendedWorkout { get; set; } = "";
    public bool ScheduleChangeRecommended { get; set; }
    public string WorkoutRecommendationLabel { get; set; } = "";
    public string WorkoutFocus { get; set; } = "";
    public string WorkoutDescription { get; set; } = "";
    public decimal WorkoutDurationMin { get; set; }
    public string WorkoutEffort { get; set; } = "";
    // Engine 3.0 planned vs completed workout physiology.
    public decimal? PlannedWorkoutCaloriesKcal { get; set; }
    public decimal? PlannedWorkoutGlycogenG { get; set; }
    public decimal? ActualWorkoutCaloriesKcal { get; set; }
    public decimal? ActualWorkoutGlycogenG { get; set; }
    public decimal? ActualWorkoutDurationMin { get; set; }
    public decimal? ActualWorkoutRpe { get; set; }
    public string? WorkoutActual { get; set; }
    public string WorkoutRecommendationRationale { get; set; } = "";

    // Today - nutrition
    public decimal Calories { get; set; }
    public decimal ProteinG { get; set; }
    public decimal CarbsG { get; set; }
    public decimal FatG { get; set; }
    public string NutritionDayLabel { get; set; } = "";
    public string NutritionTip { get; set; } = "";
    public bool NutritionActualLogged { get; set; }

    // v1009: switch the lower analysis card to current-day progress after
    // either nutrition or a completed workout has been logged today.
    public bool ShowTodayAnalysis =>
        NutritionActualLogged || ActualWorkoutDurationMin.HasValue;
    public decimal? ActualCalories { get; set; }
    public decimal? ActualProteinG { get; set; }
    public decimal? ActualCarbsG { get; set; }
    public decimal? ActualFatG { get; set; }

    // Today - coach
    public string CoachTitle { get; set; } = "";
    public string CoachRecommendation { get; set; } = "";
    public string CoachWhy { get; set; } = "";
    public byte? EnergyScore { get; set; }
    public byte? SorenessScore { get; set; }
    public decimal? SleepHours { get; set; }
    public bool PainFlag { get; set; }
    public bool IllnessFlag { get; set; }

    // Current fuel/recovery calculation
    public decimal BaseCarbsG { get; set; }
    public decimal CarryoverCarbsG { get; set; }
    public decimal PreWorkoutCarbsG { get; set; }
    public decimal FourDayGlycogenDemandG { get; set; }
    public decimal FourDayRestorationCreditG { get; set; }
    public decimal AbsoluteRestorationRatio { get; set; }
    public decimal FunctionalTargetRatio { get; set; }
    public decimal TargetAchievementRatio { get; set; }
    public string GlycogenReadiness { get; set; } = "";
    // Engine 3.0 Yesterday -> Today explanatory layer
    public string? PreviousWorkoutActual { get; set; }
    public short FourDayCarbohydrateEvidenceDays { get; set; }
    public short FourDayModerateHardDays { get; set; }
    public short FourDayHardDays { get; set; }
    public decimal TodayPlannedGlycogenDemandG { get; set; }
    public decimal HistoricalFuelShortfallG { get; set; }
    public decimal IncomingTargetAchievementRatio { get; set; }
    public decimal ProjectedTargetAchievementRatio { get; set; }
    public string ProjectedGlycogenReadiness { get; set; } = "";
    public string FuelActionLabel { get; set; } = "";
    public string FuelReasonCodes { get; set; } = "";
    // v1009 context-aware Yesterday + Trend interpretation.
    public WebRecomp.Web.Services.Engine30.Engine30DashboardAnalysisResult? Engine30Analysis { get; set; }
    // v1014A application-facing adaptive workout + nutrition recommendation.
    // The Dashboard no longer depends on a concrete engine implementation.
    public WebRecomp.Web.Services.Engine.RecompRecommendationResult? RecompRecommendation { get; set; }
    // Engine 3.0 actual-intake ENERGY carryover.
    public decimal? PreviousEnergyVarianceKcal { get; set; }
    public decimal? PreviousEligibleEnergyCarryoverKcal { get; set; }
    public decimal WeekEnergyOverageKcal { get; set; }
    public decimal WeekEligibleEnergyCarryoverKcal { get; set; }
    public decimal EnergyCorrectionAppliedBeforeTodayKcal { get; set; }
    public decimal EnergyCorrectionAppliedTodayKcal { get; set; }
    public decimal EnergyCorrectionAppliedFutureKcal { get; set; }
    public decimal EnergyCorrectionUnallocatedKcal { get; set; }
    public decimal SurplusRecoveryFraction { get; set; }
    public decimal TodayBaselinePrescribedEnergyKcal { get; set; }
    public decimal TodayEnergyCarryoverCorrectionKcal { get; set; }
    public string EnergyCarryoverSummary { get; set; } = "";

    // Tomorrow - preliminary plan only
    public DateOnly TomorrowDate { get; set; }
    public bool TomorrowPlanAvailable { get; set; }
    public string? TomorrowWorkout { get; set; }
    public string? TomorrowFocus { get; set; }
    public string? TomorrowDescription { get; set; }
    public decimal? TomorrowDurationMin { get; set; }
    public string? TomorrowEffort { get; set; }
    public decimal? TomorrowCalories { get; set; }
    public decimal? TomorrowProteinG { get; set; }
    public decimal? TomorrowCarbsG { get; set; }
    public decimal? TomorrowFatG { get; set; }
    public string TomorrowTip { get; set; } = "";

    // Yesterday - target, actual, and direct contribution to refill
    public DateOnly PreviousDate { get; set; }
    public bool PreviousTargetAvailable { get; set; }
    public string PreviousTargetSource { get; set; } = "";
    public bool PreviousNutritionActualLogged { get; set; }
    public decimal? PreviousTargetCalories { get; set; }
    public decimal? PreviousTargetProteinG { get; set; }
    public decimal? PreviousTargetCarbsG { get; set; }
    public decimal? PreviousTargetFatG { get; set; }
    public decimal? PreviousActualCalories { get; set; }
    public decimal? PreviousActualProteinG { get; set; }
    public decimal? PreviousActualCarbsG { get; set; }
    public decimal? PreviousActualFatG { get; set; }
    public decimal? PreviousCarbDifferenceG { get; set; }
    public decimal PreviousGlycogenDemandG { get; set; }
    public decimal? PreviousRestorationCreditG { get; set; }
    public decimal? PreviousRestorationRatio { get; set; }
    public string PreviousFuelSummary { get; set; } = "";

    // Weekly context retained for the coaching engine and future detail views.
    public string WeeklyStatus { get; set; } = "";
    public string WeeklyNote { get; set; } = "";
    public string? StrengthPriority { get; set; }
    public string? CardioNeed { get; set; }
}

// ===== v1004 profile / settings / progress =====
public sealed class ProfileViewModel
{
    public string ClientName { get; set; } = "";
    public string? PreferredDisplayName { get; set; }
    public string? ContactEmail { get; set; }
    public string? SignInEmail { get; set; }

    public DateOnly? DateOfBirth { get; set; }
    public string? SexCode { get; set; }
    public decimal HeightIn { get; set; }
    public string TimeZone { get; set; } = "America/Los_Angeles";
    public string UnitSystemCode { get; set; } = "US";
    public string? TrainingExperienceCode { get; set; }

    public string GoalCode { get; set; } = "";
    public string SelectedPlanCode { get; set; } = "";
    public string SelectedPlanDisplayName { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public short DurationWeeks { get; set; }
    public int ProgramWeek { get; set; }
    public byte AvailableTrainingDaysWeek { get; set; }

    public decimal? TargetWeightLowLb { get; set; }
    public decimal? TargetWeightHighLb { get; set; }
    public decimal? TargetWaistIn { get; set; }
    public string? GoalNotes { get; set; }

    public DateOnly? LatestMeasurementDate { get; set; }
    public decimal? LatestWeightLb { get; set; }
    public decimal? LatestBodyFatPercent { get; set; }
    public decimal? LatestMuscleMassLb { get; set; }
    public decimal? LatestWaistIn { get; set; }
    public List<PlanChangeRowViewModel> PlanChanges { get; set; } = [];
}

public sealed class PlanChangeRowViewModel
{
    public DateOnly EffectiveDate { get; set; }
    public string OldPlanDisplayName { get; set; } = "";
    public string NewPlanDisplayName { get; set; } = "";
    public DateTime ChangedAtUtc { get; set; }
}

public sealed class ProfileEditViewModel
{
    [Required, StringLength(100)]
    public string ClientName { get; set; } = "";

    [DataType(DataType.Date)]
    public DateOnly? DateOfBirth { get; set; }

    public string? SexCode { get; set; }

    [Range(48, 90)]
    public decimal HeightIn { get; set; }

    [Required, StringLength(100)]
    public string TimeZone { get; set; } = "America/Los_Angeles";

    [Required, RegularExpression("^(US|METRIC)$")]
    public string UnitSystemCode { get; set; } = "US";

    [StringLength(30)]
    public string? TrainingExperienceCode { get; set; }
}

public sealed class SettingsViewModel
{
    public string? SignInEmail { get; set; }

    [StringLength(100)]
    public string? PreferredDisplayName { get; set; }

    [EmailAddress, StringLength(320)]
    public string? ContactEmail { get; set; }

    [Required, RegularExpression("^(SYSTEM|LIGHT|DARK)$")]
    public string ThemeCode { get; set; } = "SYSTEM";

    [Required]
    [RegularExpression("^(MARISOL|CASSIE|MARCUS|NINA)$")]
    public string PreferredCoachCode { get; set; } = "MARISOL";

    // v1010 optional detailed-input preferences for the active program.
    public bool HasActiveProgram { get; set; }
    public bool DetailedWorkoutEntryEnabled { get; set; }
    public bool NutritionFoodLogEnabled { get; set; }
}

public sealed class ProgressViewModel
{
    public string ClientName { get; set; } = "";
    public DateOnly Today { get; set; }
    public int MeasurementDays { get; set; }
    public DateOnly? FirstMeasurementDate { get; set; }
    public DateOnly? LatestMeasurementDate { get; set; }
    public decimal? WeightChangeLb { get; set; }
    public decimal? BodyFatChangePoints { get; set; }
    public decimal? WaistChangeIn { get; set; }
    public List<ProgressMeasurementRowViewModel> Measurements { get; set; } = [];
}

public sealed class ProgressMeasurementRowViewModel
{
    public DateOnly Date { get; set; }
    public decimal? WeightLb { get; set; }
    public decimal? BodyFatPercent { get; set; }
    public decimal? FatMassLb { get; set; }
    public decimal? LeanMassLb { get; set; }
    public decimal? MuscleMassLb { get; set; }
    public decimal? BodyWaterLb { get; set; }
    public decimal? VisceralFatRating { get; set; }
    public decimal? WaistIn { get; set; }
    public decimal? HipsIn { get; set; }
    public string? Notes { get; set; }
}








