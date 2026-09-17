using System.ComponentModel.DataAnnotations;

namespace WebRecomp.Web.ViewModels;

public sealed class DailyEntryViewModel
{
    public DateOnly Date { get; set; }

    public string PlannedWorkoutName { get; set; } = "No workout planned";
    public string? PlannedWorkoutTypeCode { get; set; }
    public Guid? PlannedWorkoutTemplateId { get; set; }
    public string? PlannedSystemWorkoutCode { get; set; }

    public bool BodyRecoveryComplete { get; set; }
    public bool WorkoutComplete { get; set; }
    public bool NutritionComplete { get; set; }

    // v1010 program-scoped optional-input switches. These do not change
    // the Engine contract by themselves; they gate richer entry UI.
    public bool DetailedWorkoutEntryEnabled { get; set; }
    public bool NutritionFoodLogEnabled { get; set; }

    public string? MeasurementSourceLabel { get; set; }
    public string? NutritionSourceLabel { get; set; }

    // ------------------------------------------------------------
    // Body + recovery
    // ------------------------------------------------------------

    [Range(50, 1000)]
    [DisplayFormat(DataFormatString = "{0:0.#}", ApplyFormatInEditMode = true)]
    public decimal? WeightLb { get; set; }

    [Range(1, 75)]
    [DisplayFormat(DataFormatString = "{0:0.#}", ApplyFormatInEditMode = true)]
    public decimal? BodyFatPercent { get; set; }

    // Preferred muscle metric for recomposition.
    [Range(1, 500)]
    [DisplayFormat(DataFormatString = "{0:0.#}", ApplyFormatInEditMode = true)]
    public decimal? SkeletalMuscleMassLb { get; set; }

    [Range(0, 100)]
    [DisplayFormat(DataFormatString = "{0:0.#}", ApplyFormatInEditMode = true)]
    public decimal? SubcutaneousFatPercent { get; set; }

    [Range(1, 500)]
    [DisplayFormat(DataFormatString = "{0:0.#}", ApplyFormatInEditMode = true)]
    public decimal? BodyWaterLb { get; set; }

    [Range(1, 100)]
    [DisplayFormat(DataFormatString = "{0:0.#}", ApplyFormatInEditMode = true)]
    public decimal? VisceralFatRating { get; set; }

    [Range(10, 100)]
    [DisplayFormat(DataFormatString = "{0:0.#}", ApplyFormatInEditMode = true)]
    public decimal? WaistIn { get; set; }

    [Range(10, 100)]
    [DisplayFormat(DataFormatString = "{0:0.#}", ApplyFormatInEditMode = true)]
    public decimal? HipsIn { get; set; }

    [Range(0, 24)]
    [DisplayFormat(DataFormatString = "{0:0.#}", ApplyFormatInEditMode = true)]
    public decimal? SleepHours { get; set; }

    [Range(20, 250)]
    public short? RestingHr { get; set; }

    [Range(typeof(decimal), "0", "24")]
    [DisplayFormat(DataFormatString = "{0:0.#}", ApplyFormatInEditMode = true)]
    public decimal? DailyActivityHours { get; set; }

    [StringLength(12)]
    [RegularExpression("^(LIGHT|MODERATE|HARD)$")]
    public string? DailyActivityEffortCode { get; set; }

    [Range(1, 10)]
    public byte? EnergyScore { get; set; }

    [Range(1, 10)]
    public byte? SorenessScore { get; set; }

    public bool PainFlag { get; set; }
    public bool IllnessFlag { get; set; }

    [StringLength(1000)]
    public string? RecoveryNotes { get; set; }

    // ------------------------------------------------------------
    // Actual workout(s)
    // ------------------------------------------------------------

    public List<DailyEntryWorkoutInput> Workouts { get; set; } = [];

    public List<DailyEntryWorkoutTypeOption> WorkoutTypeOptions { get; set; } = [];
    public List<SystemWorkoutOptionViewModel> SystemWorkouts { get; set; } = [];
    public List<WorkoutTemplateOptionViewModel> MyWorkouts { get; set; } = [];

    // ------------------------------------------------------------
    // Nutrition actual
    // ------------------------------------------------------------

    [Range(0, 10000)]
    public decimal? Calories { get; set; }

    [Range(0, 1000)]
    public decimal? ProteinG { get; set; }

    [Range(0, 2000)]
    public decimal? CarbsG { get; set; }

    [Range(0, 1000)]
    public decimal? FatG { get; set; }

    [Range(0, 500)]
    public decimal? FiberG { get; set; }

    [Range(0, 1000)]
    public decimal? WaterOz { get; set; }

    [StringLength(1000)]
    public string? NutritionNotes { get; set; }
}

public sealed class DailyEntryWorkoutInput
{
    public Guid? WorkoutSessionId { get; set; }
    public Guid? WorkoutTemplateId { get; set; }
    public string? SystemWorkoutCode { get; set; }
    public string? WorkoutName { get; set; }

    public string? WorkoutTypeCode { get; set; }

    [Range(0, 1440)]
    [DisplayFormat(DataFormatString = "{0:0.#}", ApplyFormatInEditMode = true)]
    public decimal? DurationMin { get; set; }

    [Range(0, 1800)]
    public short? TypicalRestSec { get; set; }

    [Range(1, 10)]
    [DisplayFormat(DataFormatString = "{0:0.#}", ApplyFormatInEditMode = true)]
    public decimal? SessionRpe { get; set; }

    [Range(0, 10000)]
    [DisplayFormat(DataFormatString = "{0:0.#}", ApplyFormatInEditMode = true)]
    public decimal? DeviceCalories { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public string? SourceLabel { get; set; }

    public bool HasAnyEntry =>
        WorkoutSessionId.HasValue
        || DurationMin.HasValue
        || SessionRpe.HasValue
        || TypicalRestSec.HasValue
        || DeviceCalories.HasValue
        || !string.IsNullOrWhiteSpace(Notes);
}

public sealed record DailyEntryWorkoutTypeOption(
    string Code,
    string DisplayName);
