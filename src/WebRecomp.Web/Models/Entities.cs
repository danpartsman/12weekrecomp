namespace WebRecomp.Web.Models;

public sealed class UserAccount
{
    public Guid UserId { get; set; }
    public required string ExternalSubject { get; set; }
    public string? EmailNormalized { get; set; }
    public string? DisplayName { get; set; }

    // v1004 application-facing profile/settings. These are deliberately
    // separate from the identity-provider values above so a user can choose
    // a preferred name/contact address without changing Entra credentials.
    public string? PreferredDisplayName { get; set; }
    public string? ContactEmail { get; set; }
    public string ThemeCode { get; set; } = "SYSTEM";

    public string AccountStatusCode { get; set; } = "ACTIVE";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class LearningCoachPreference
{
    public Guid UserId { get; set; }
    public string PreferredCoachCode { get; set; } = "MARISOL";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class LearningSeriesProgress
{
    public Guid LearningSeriesProgressId { get; set; }
    public Guid UserId { get; set; }
    public required string SeriesCode { get; set; }
    public int CurrentModuleNumber { get; set; } = 1;
    public int HighestCompletedModuleNumber { get; set; }
    public string StatusCode { get; set; } = "ACTIVE";
    public DateTime? LastShownAtUtc { get; set; }
    public DateTime? LastCompletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class LearningInteraction
{
    public Guid LearningInteractionId { get; set; }
    public Guid UserId { get; set; }
    public required string ContentCode { get; set; }
    public required string SeriesCode { get; set; }
    public required string SurfaceCode { get; set; }
    public required string InteractionCode { get; set; }
    public int ModuleNumber { get; set; }
    public string? TargetUrl { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class Client
{
    public Guid ClientId { get; set; }
    public required string ClientName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? SexCode { get; set; }
    public decimal HeightIn { get; set; }
    public string TimeZone { get; set; } = "America/Los_Angeles";
    public string UnitSystemCode { get; set; } = "US";
    public string? TrainingExperienceCode { get; set; }

    /// <summary>
    /// Engine 3.0 recurring lifestyle activity outside normal work
    /// and outside workouts/exercise reported separately.
    ///
    /// Expected values:
    /// MINIMAL
    /// LIGHT
    /// MODERATE
    /// VERY_ACTIVE
    ///
    /// The client stores the category only. Equivalent activity
    /// hours are Engine/configuration owned.
    /// </summary>
    public string? LifestyleActivityCode { get; set; }
    public int? BaselineStepsDay { get; set; }
    public decimal? BaselineSleepHours { get; set; }
    public string ClientStatusCode { get; set; } = "ACTIVE";
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class UserClientAccess
{
    public Guid UserClientAccessId { get; set; }
    public Guid UserId { get; set; }
    public Guid ClientId { get; set; }
    public string AccessRoleCode { get; set; } = "OWNER";
    public DateTime GrantedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

public sealed class ConfigVersion
{
    public Guid ConfigVersionId { get; set; }
    public required string VersionCode { get; set; }
    public required string StatusCode { get; set; }
    public string? Description { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? EffectiveFromUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class PlanDefinition
{
    public Guid PlanDefinitionId { get; set; }
    public Guid ConfigVersionId { get; set; }
    public required string PlanCode { get; set; }
    public required string DisplayName { get; set; }
    public decimal BaseRestCarbsG { get; set; }
    public required string PlanCategoryCode { get; set; }
    public short SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class WorkoutTypeDefinition
{
    public Guid WorkoutTypeDefinitionId { get; set; }
    public Guid ConfigVersionId { get; set; }
    public required string WorkoutTypeCode { get; set; }
    public required string DisplayName { get; set; }

    public required string WorkoutCategoryCode { get; set; }
    public bool IsSelectable { get; set; }
    public short DisplayOrder { get; set; }

    public string? Description { get; set; }
    public string? Instructions { get; set; }
    public string? OverviewVideoUrl { get; set; }

    public required string WeeklyRoleCode { get; set; }
    public string? RegionCode { get; set; }
    public bool CountsDedicatedHeavy { get; set; }
    public bool CountsLightStrength { get; set; }
    public bool CountsPrimaryConditioning { get; set; }
    public bool CountsRecovery { get; set; }
    public required string FuelPriorityCode { get; set; }
    public required string SchedulePrincipleCode { get; set; }
    public string? StrategyNote { get; set; }

    public string? MacroClassCode { get; set; }
    public string? PrimaryFocusCode { get; set; }
    public decimal FuelScore { get; set; }
    public decimal DefaultMinutes { get; set; }
    public decimal RawGlycogenGMin { get; set; }
    public decimal DefaultIntensityFactor { get; set; }
    public short DefaultRestSec { get; set; }
    public decimal ReferenceWorkoutCalPerMin { get; set; }
    public decimal GlycogenCarbShare { get; set; }

    public decimal PushLoad { get; set; }
    public decimal PullLoad { get; set; }
    public decimal LegLoad { get; set; }
    public decimal CardioLoad { get; set; }
    public decimal StrengthStress { get; set; }
    public decimal PlannedRestorationPct { get; set; }
    public decimal ProteinAdjustmentG { get; set; }
    public decimal TargetFatG { get; set; }
    public decimal TypicalRecoveryHours { get; set; }
    public string? PlanningNote { get; set; }
    public bool IsActive { get; set; }
}


public sealed class Engine30WorkoutProfileVersion
{
    public Guid Engine30WorkoutProfileVersionId { get; set; }
    public Guid WorkoutTypeDefinitionId { get; set; }

    public int VersionNumber { get; set; }
    public required string VersionCode { get; set; }

    public bool IsBaseline { get; set; }
    public bool IsActive { get; set; }

    public required string ProfileJson { get; set; }

    public Guid? RestoredFromProfileVersionId { get; set; }
    public string? ChangeNotes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class GlobalSetting
{
    public Guid GlobalSettingId { get; set; }
    public Guid ConfigVersionId { get; set; }
    public required string SettingCode { get; set; }
    public decimal? DecimalValue { get; set; }
    public string? TextValue { get; set; }
    public string? UnitCode { get; set; }
    public required string Description { get; set; }
}

public sealed class ProgramRecord
{
    public Guid ProgramId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ConfigVersionId { get; set; }
    public required string SelectedPlanCode { get; set; }
    public required string GoalCode { get; set; }
    public DateOnly StartDate { get; set; }
    public short DurationWeeks { get; set; }
    public byte AvailableTrainingDaysWeek { get; set; }

    // v1010 optional-input preferences are program-scoped so a future
    // coach account can manage multiple clients without one account-level
    // preference leaking across client programs. Defaults remain OFF/OFF.
    public bool DetailedWorkoutEntryEnabled { get; set; }
    public bool NutritionFoodLogEnabled { get; set; }

    public string ProgramStatusCode { get; set; } = "ACTIVE";
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ProgramPlanChange
{
    public Guid ProgramPlanChangeId { get; set; }
    public Guid ProgramId { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public required string OldPlanCode { get; set; }
    public required string NewPlanCode { get; set; }
    public required string OldGoalCode { get; set; }
    public required string NewGoalCode { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string ChangeSourceCode { get; set; } = "USER_SETUP_REVIEW";
    public DateTime ChangedAtUtc { get; set; }
}

public sealed class ProgramGoal
{
    public Guid ProgramGoalId { get; set; }
    public Guid ProgramId { get; set; }
    public decimal? TargetWeightLowLb { get; set; }
    public decimal? TargetWeightHighLb { get; set; }
    public decimal? TargetWaistIn { get; set; }
    public string? GoalNotes { get; set; }
}

public sealed class Measurement
{
    public Guid MeasurementId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? ProgramId { get; set; }
    public DateTime MeasurementAtUtc { get; set; }
    public DateOnly LocalMeasurementDate { get; set; }
    public decimal? WeightLb { get; set; }
    public decimal? BodyFatPct { get; set; }
    public decimal? MuscleMassLb { get; set; }
    public decimal? SkeletalMuscleMassLb { get; set; }
    public decimal? BodyWaterLb { get; set; }
    public decimal? VisceralFatRating { get; set; }
    public decimal? SubcutaneousFatPct { get; set; }
    public decimal? WaistIn { get; set; }
    public decimal? HipsIn { get; set; }
    public decimal? ScaleWhr { get; set; }
    public string MeasurementSourceCode { get; set; } = "MANUAL";
    public string? SourceRecordId { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class DailyCheckin
{
    public Guid CheckinId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ProgramId { get; set; }
    public DateOnly CheckinDate { get; set; }
    public decimal? SleepHours { get; set; }
    public short? RestingHr { get; set; }
    public int? Steps { get; set; }
    public decimal? DailyActivityHours { get; set; }
    public string? DailyActivityEffortCode { get; set; }
    public byte? EnergyScore { get; set; }
    public byte? SorenessScore { get; set; }
    public bool PainFlag { get; set; }
    public bool IllnessFlag { get; set; }
    public string? IntendedWorkoutTypeCode { get; set; }
    public string? Notes { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class PlannedWorkout
{
    public Guid PlannedWorkoutId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ProgramId { get; set; }
    public DateOnly PlannedDate { get; set; }

    // Generic Engine/planning category used for target calculations.
    public required string WorkoutTypeCode { get; set; }

    // Optional client-owned reusable workout selected from My Workouts.
    // When present, WorkoutTypeCode remains the template's underlying
    // Engine-compatible planning category.
    public Guid? WorkoutTemplateId { get; set; }

    // Optional built-in 12WR System Workout recipe selected for this day.
    // WorkoutTypeCode remains the canonical Engine 3.0 physiology type.
    public string? SystemWorkoutCode { get; set; }

    // Optional presentation-level plan defaults from a selected System Workout.
    // Null preserves the underlying Engine 3.0 profile defaults. These values
    // are canonical planned-input overrides, not new physiology coefficients.
    public decimal? PlannedDurationMin { get; set; }
    public decimal? PlannedRpe { get; set; }

    public string SourceCode { get; set; } = "CLIENT";
    public string? Notes { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class WorkoutSession
{
    public Guid WorkoutSessionId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ProgramId { get; set; }
    public DateOnly WorkoutDate { get; set; }
    public string? WorkoutName { get; set; }
    public Guid? WorkoutTemplateId { get; set; }
    public string? SystemWorkoutCode { get; set; }
    public required string WorkoutTypeCode { get; set; }
    public decimal DurationMin { get; set; }
    public short? TypicalRestSec { get; set; }
    public decimal? SessionRpe { get; set; }
    public decimal? DeviceCalories { get; set; }
    public bool CompletedFlag { get; set; }

    public decimal? CalculatedWorkoutCalories { get; set; }
    public decimal? CalculatedScaledGlycogenBurnG { get; set; }
    public decimal? CalculatedPushLoad { get; set; }
    public decimal? CalculatedPullLoad { get; set; }
    public decimal? CalculatedLegLoad { get; set; }
    public decimal? CalculatedCardioLoad { get; set; }
    public string? CalculationEngineVersion { get; set; }

    public string EntrySourceCode { get; set; } = "MANUAL";
    public string? SourceRecordId { get; set; }
    public string? Notes { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class NutritionDay
{
    public Guid NutritionDayId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ProgramId { get; set; }
    public DateOnly NutritionDate { get; set; }
    public decimal? ActualCalories { get; set; }
    public decimal? ActualProteinG { get; set; }
    public decimal? ActualCarbsG { get; set; }
    public decimal? ActualFatG { get; set; }
    public decimal? FiberG { get; set; }
    public decimal? WaterOz { get; set; }
    public string EntrySourceCode { get; set; } = "MANUAL";
    public string? SourceRecordId { get; set; }
    public string? Notes { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class AnalysisRun
{
    public Guid AnalysisRunId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ProgramId { get; set; }
    public DateOnly ActionDate { get; set; }
    public DateTime InputCutoffAtUtc { get; set; }
    public required string EngineVersion { get; set; }
    public Guid ConfigVersionId { get; set; }
    public required string RunStatusCode { get; set; }
    public string? DataConfidenceCode { get; set; }
    public string? DiagnosticJson { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public sealed class WeeklyStrategySnapshot
{
    public Guid WeeklyStrategySnapshotId { get; set; }
    public Guid AnalysisRunId { get; set; }
    public DateOnly WeekStartDate { get; set; }
    public short MajorPushCount { get; set; }
    public short MajorPullCount { get; set; }
    public short MajorLegsCount { get; set; }
    public short LightStrengthCount { get; set; }
    public short PrimaryConditioningCount { get; set; }
    public short RecoveryCount { get; set; }
    public short HeavyGenericCount { get; set; }
    public required string StatusCode { get; set; }
    public required string StrategyNote { get; set; }
}

public sealed class Recommendation
{
    public Guid RecommendationId { get; set; }
    public Guid AnalysisRunId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ProgramId { get; set; }
    public DateOnly ActionDate { get; set; }

    public string? PlannedWorkoutTypeCode { get; set; }
    public string? UserSelectedWorkoutTypeCode { get; set; }
    public required string RecommendedWorkoutTypeCode { get; set; }
    public bool ScheduleChangeRecommended { get; set; }
    public required string RecommendationBasisCode { get; set; }

    public decimal BaseCalories { get; set; }
    public decimal RecommendedCalories { get; set; }
    public decimal RecommendedProteinG { get; set; }
    public decimal BaseCarbsG { get; set; }
    public decimal RecoveryAllocationG { get; set; }
    public decimal PreWorkoutPortionG { get; set; }
    public decimal RecommendedCarbsG { get; set; }
    public decimal RecommendedFatG { get; set; }

    public decimal FourDayGlycogenDemandG { get; set; }
    public decimal FourDayRestorationCreditG { get; set; }
    public decimal AbsoluteRestorationRatio { get; set; }
    public decimal FunctionalTargetRatio { get; set; }
    public decimal TargetAchievementRatio { get; set; }
    public required string GlycogenReadinessCode { get; set; }

    public short HardSessionCount4d { get; set; }
    public string? RegionPriorityCode { get; set; }
    public string? CardioNeedCode { get; set; }
    public required string WeeklyStrategyStatusCode { get; set; }
    public required string WeeklyStrategyNote { get; set; }
    public required string TimingNote { get; set; }
    public required string ShortExplanation { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class RecommendationAlert
{
    public Guid RecommendationAlertId { get; set; }
    public Guid RecommendationId { get; set; }
    public required string AlertCode { get; set; }
    public required string SeverityCode { get; set; }
    public required string Message { get; set; }
    public short SortOrder { get; set; }
}

public sealed class GiveawayCodeBatch
{
    public Guid GiveawayCodeBatchId { get; set; }
    public string? BatchName { get; set; }
    public int Quantity { get; set; }
    public int AccessDurationDays { get; set; }
    public DateTime? RedeemByUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class GiveawayCode
{
    public Guid GiveawayCodeId { get; set; }
    public Guid GiveawayCodeBatchId { get; set; }

    public required string Code { get; set; }
    public string StatusCode { get; set; } = "AVAILABLE";

    public int AccessDurationDays { get; set; }
    public DateTime? RedeemByUtc { get; set; }

    public Guid? RedeemedByUserId { get; set; }
    public DateTime? RedeemedAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? AccessExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
    public Guid? RevokedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class UserEntitlement
{
    public Guid UserEntitlementId { get; set; }
    public Guid UserId { get; set; }

    public string EntitlementTypeCode { get; set; } = "FULL_ACCESS";
    public required string SourceTypeCode { get; set; }
    public Guid? SourceId { get; set; }

    public DateTime StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class PromoCode
{
    public Guid PromoCodeId { get; set; }

    public required string PromotionName { get; set; }
    public required string Code { get; set; }

    public string StatusCode { get; set; } = "ACTIVE";

    public int AccessDurationDays { get; set; }
    public int MaxRedemptions { get; set; }
    public int RedemptionCount { get; set; }

    public DateTime? RedeemByUtc { get; set; }

    public Guid CreatedByUserId { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

public sealed class PromoCodeRedemption
{
    public Guid PromoCodeRedemptionId { get; set; }
    public Guid PromoCodeId { get; set; }
    public Guid UserId { get; set; }
    public Guid UserEntitlementId { get; set; }
    public DateTime RedeemedAtUtc { get; set; }
}

public sealed class ContactSetting
{
    public Guid ContactSettingsId { get; set; }

    public string SettingsKey { get; set; } = "DEFAULT";
    public string? DestinationEmail { get; set; }
    public bool ContactEnabled { get; set; } = true;

    public Guid? UpdatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

public sealed class ContactMessage
{
    public Guid ContactMessageId { get; set; }

    public required string SenderName { get; set; }
    public required string SenderEmail { get; set; }
    public required string Subject { get; set; }
    public required string Message { get; set; }

    public string StatusCode { get; set; } = "NEW";

    public DateTime SubmittedAtUtc { get; set; }

    public DateTime? ReadAtUtc { get; set; }
    public Guid? ReadByUserId { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }
    public Guid? ArchivedByUserId { get; set; }

    public string EmailNotificationStatus { get; set; } = "NOT_CONFIGURED";
    public DateTime? EmailSentAtUtc { get; set; }
    public string? EmailError { get; set; }

    public byte[] RowVersion { get; set; } = [];
}


/// <summary>
/// Engine 3.0 habitual/occupational activity input.
///
/// A client may have more than one active occupational role.
/// Each role contributes evidence to ONE Habitual Activity bucket.
///
/// The user enters the occupation and typical weekly hours.
/// OccupationActivityCode maps to an Engine 3.0 configured activity
/// factor; the calorie factor itself is deliberately not stored here.
/// </summary>
public sealed class ClientOccupationalActivity
{
    public Guid ClientOccupationalActivityId { get; set; }

    public Guid ClientId { get; set; }

    /// <summary>
    /// Human-readable occupation or normal activity role.
    /// Example: School Bus Driver, Software Developer.
    /// </summary>
    public required string OccupationName { get; set; }

    /// <summary>
    /// Engine-owned activity classification.
    /// Example: HIGHLY_SEDENTARY, SEDENTARY_LIGHT,
    /// TYPICAL_MIXED, ACTIVE, VERY_ACTIVE, HEAVY_PHYSICAL.
    /// </summary>
    public required string OccupationActivityCode { get; set; }

    /// <summary>
    /// Typical actual hours per week spent in this occupational role.
    /// This is weekly exposure, NOT hours per day multiplied by seven.
    /// </summary>
    public decimal TypicalHoursPerWeek { get; set; }

    /// <summary>
    /// Identifies the user's primary occupation for UI/display purposes.
    /// It does not give that occupation additional calorie weight.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// First local calendar date for which this activity pattern applies.
    /// </summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>
    /// Last local calendar date for which this activity pattern applies.
    /// Null means the role remains current.
    /// </summary>
    public DateOnly? EffectiveTo { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ExerciseCatalogImportRecord
{
    public Guid ExerciseCatalogImportId { get; set; }
    public required string SourceCode { get; set; }
    public required string LicenseCode { get; set; }
    public string? SourceVersion { get; set; }
    public string? ManifestSha256 { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public int ExerciseCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ExerciseCatalogItemRecord
{
    public Guid ExerciseCatalogItemId { get; set; }
    public required string SourceCode { get; set; }
    public required string SourceExternalId { get; set; }
    public required string Name { get; set; }
    public required string MeasurementType { get; set; }
    public string? DifficultyLevel { get; set; }
    public string? ForceType { get; set; }
    public string? Mechanics { get; set; }
    public string? Category { get; set; }
    public string? InstructionsJson { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? LastImportId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public List<ExerciseMuscleTargetRecord> MuscleTargets { get; set; } = [];
    public List<ExerciseEquipmentRequirementRecord> Equipment { get; set; } = [];
}

public sealed class ExerciseMuscleTargetRecord
{
    public Guid ExerciseMuscleTargetId { get; set; }
    public Guid ExerciseCatalogItemId { get; set; }
    public required string ExternalId { get; set; }
    public required string MuscleSlug { get; set; }
    public required string MuscleName { get; set; }
    public required string RoleCode { get; set; }
    public short DisplayOrder { get; set; }
    public ExerciseCatalogItemRecord? Exercise { get; set; }
}

public sealed class ExerciseEquipmentRequirementRecord
{
    public Guid ExerciseEquipmentRequirementId { get; set; }
    public Guid ExerciseCatalogItemId { get; set; }
    public required string ExternalId { get; set; }
    public required string EquipmentName { get; set; }
    public required string EquipmentType { get; set; }
    public string? UsageType { get; set; }
    public int? NumItems { get; set; }
    public short DisplayOrder { get; set; }
    public ExerciseCatalogItemRecord? Exercise { get; set; }
}

public sealed class DetailedWorkoutExerciseRecord
{
    public Guid DetailedWorkoutExerciseId { get; set; }
    public Guid WorkoutSessionId { get; set; }
    public Guid ExerciseCatalogItemId { get; set; }
    public short ExerciseOrder { get; set; }
    public required string ExerciseNameSnapshot { get; set; }
    public string? MechanicsSnapshot { get; set; }
    public string? Notes { get; set; }
    public decimal? MaxWeight { get; set; }
    public string LoadUnitCode { get; set; } = "LB";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<DetailedWorkoutSetRecord> Sets { get; set; } = [];
}

public sealed class DetailedWorkoutSetRecord
{
    public Guid DetailedWorkoutSetId { get; set; }
    public Guid DetailedWorkoutExerciseId { get; set; }
    public short SetOrder { get; set; }
    public short? Reps { get; set; }
    public decimal? Load { get; set; }
    public string LoadUnitCode { get; set; } = "LB";
    public decimal? Rpe { get; set; }
    public decimal? Rir { get; set; }
    public int? RestSeconds { get; set; }
    public int? DurationSeconds { get; set; }
    public bool IsWarmup { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}



public sealed class WorkoutTemplateRecord
{
    public Guid WorkoutTemplateId { get; set; }
    public Guid ClientId { get; set; }
    public required string TemplateName { get; set; }
    public required string WorkoutTypeCode { get; set; }
    public decimal DefaultDurationMin { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<WorkoutTemplateExerciseRecord> Exercises { get; set; } = [];
}

public sealed class WorkoutTemplateExerciseRecord
{
    public Guid WorkoutTemplateExerciseId { get; set; }
    public Guid WorkoutTemplateId { get; set; }
    public Guid ExerciseCatalogItemId { get; set; }
    public short ExerciseOrder { get; set; }
    public required string ExerciseNameSnapshot { get; set; }
    public string? MechanicsSnapshot { get; set; }
    public short Sets { get; set; } = 3;
    public short Reps { get; set; } = 10;

    // Optional JSON array containing one rep value per working set.
    // Legacy rows with null continue to use Reps for every set.
    public string? SetRepsJson { get; set; }

    // B2.35.16 TEMPLATE PRESCRIPTION TEXT
    // These fields preserve the user's intended prescription exactly.
    // Numeric Sets/Reps/MaxWeight remain deterministic launch defaults.
    public string? SetsPrescription { get; set; }
    public string? RepsPrescription { get; set; }
    public string? WeightPrescription { get; set; }

    public decimal? MaxWeight { get; set; }
    public string LoadUnitCode { get; set; } = "LB";
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ClientExercisePreferenceRecord
{
    public Guid ClientExercisePreferenceId { get; set; }
    public Guid ClientId { get; set; }
    public Guid ExerciseCatalogItemId { get; set; }
    public bool IsFavorite { get; set; }
    public short DefaultSets { get; set; } = 3;
    public short DefaultReps { get; set; } = 10;
    public decimal? DefaultMaxWeight { get; set; }
    public string LoadUnitCode { get; set; } = "LB";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ExerciseCatalogSyncSettingRecord
{
    public Guid ExerciseCatalogSyncSettingId { get; set; }
    public required string SettingsKey { get; set; }
    public required string ActiveProviderCode { get; set; }
    public required string MediaProviderCode { get; set; }
    public bool AutoSyncEnabled { get; set; }
    public int RefreshIntervalDays { get; set; }
    public DateTime? LastCheckedAtUtc { get; set; }
    public DateTime? LastSuccessfulSyncAtUtc { get; set; }
    public string? LastDatasetSha256 { get; set; }
    public string? LastProviderVersion { get; set; }
    public string? LastProviderEtag { get; set; }
    public string? LastError { get; set; }
    public Guid? SyncLeaseId { get; set; }
    public DateTime? SyncLeaseUntilUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ExerciseCatalogSyncRunRecord
{
    public Guid ExerciseCatalogSyncRunId { get; set; }
    public required string ProviderCode { get; set; }
    public required string TriggerCode { get; set; }
    public required string StatusCode { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? DatasetSha256 { get; set; }
    public string? SourceVersion { get; set; }
    public int? AddedCount { get; set; }
    public int? UpdatedCount { get; set; }
    public int? DeactivatedCount { get; set; }
    public int? ActiveCount { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class ExerciseMediaAssetRecord
{
    public Guid ExerciseMediaAssetId { get; set; }
    public Guid ExerciseCatalogItemId { get; set; }
    public required string ProviderCode { get; set; }
    public required string SourceExternalId { get; set; }
    public required string MediaTypeCode { get; set; }
    public string? MediaUrl { get; set; }
    public string? LocalPath { get; set; }
    public string? LicenseCode { get; set; }
    public string? Attribution { get; set; }
    public short DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? LastSeenSyncRunId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ExerciseCatalogItemRecord? Exercise { get; set; }
}

