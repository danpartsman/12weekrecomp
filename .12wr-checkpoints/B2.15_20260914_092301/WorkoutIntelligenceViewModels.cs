namespace WebRecomp.Web.ViewModels;

public sealed class ExerciseCatalogRowViewModel
{
    public Guid ExerciseId { get; set; }
    public string Name { get; set; } = "";
    public string? Mechanics { get; set; }
    public string? Category { get; set; }
    public string? DifficultyLevel { get; set; }
    public IReadOnlyList<string> PrimaryMuscles { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Equipment { get; set; } = Array.Empty<string>();
    public string? MatchedMuscleName { get; set; }
    public string? MatchedMuscleRole { get; set; }
    public int PersonalUseCount { get; set; }
    public bool IsFavorite { get; set; }
    public int DefaultSets { get; set; } = 3;
    public int DefaultReps { get; set; } = 10;
    public decimal? DefaultMaxWeight { get; set; }
    public string DefaultLoadUnitCode { get; set; } = "LB";
}

public sealed class ExerciseLibraryViewModel
{
    public bool FeatureEnabled { get; set; }
    public string? Query { get; set; }
    public IReadOnlyList<ExerciseCatalogRowViewModel> Exercises { get; set; } =
        Array.Empty<ExerciseCatalogRowViewModel>();
}

public sealed class DetailedWorkoutSessionChoiceViewModel
{
    public Guid WorkoutSessionId { get; set; }
    public string WorkoutDisplayName { get; set; } = "";
    public decimal DurationMin { get; set; }
    public decimal? SessionRpe { get; set; }
    public bool CompletedFlag { get; set; }
    public string? WorkoutName { get; set; }
    public Guid? WorkoutTemplateId { get; set; }
    public string WorkoutTypeCode { get; set; } = "";
}

public sealed class DetailedWorkoutEditorViewModel
{
    public bool FeatureEnabled { get; set; }
    public DateOnly Date { get; set; }
    public Guid? WorkoutSessionId { get; set; }
    public Guid? PendingExerciseId { get; set; }
    public string WorkoutDisplayName { get; set; } = "";
    public string? WorkoutName { get; set; }
    public Guid? SourceTemplateId { get; set; }
    public bool EditCompleted { get; set; }
    public string? TemplateName { get; set; }
    public string DefaultLoadUnitCode { get; set; } = "LB";
    public string WorkspaceMode { get; set; } = "";
    public Guid? SelectedTemplateId { get; set; }
    public string? SelectedSystemWorkoutCode { get; set; }
    public bool ModifySelection { get; set; }
    public string? DraftWorkoutTypeCode { get; set; }

    // When opened from Weekly Plan, New Workout can operate as a
    // template-only creator: save to My Workouts + assign to the
    // selected planned date without creating an actual WorkoutSession.
    public bool TemplateOnlyMode { get; set; }
    public string? ReturnContext { get; set; }
    public DateOnly? ReturnWeekStart { get; set; }

    public List<DetailedWorkoutSessionChoiceViewModel> Sessions { get; set; } = [];
    public List<WorkoutTemplateSummaryViewModel> Templates { get; set; } = [];
    public List<WorkoutBuilderTypeOptionViewModel> WorkoutTypes { get; set; } = [];
    public WorkoutTemplateDetailViewModel? SelectedTemplate { get; set; }
    public SystemWorkoutPreviewViewModel? SelectedSystemWorkout { get; set; }
    public List<DetailedWorkoutExerciseInputModel> Exercises { get; set; } = [];
    public WorkoutHeaderAnalysisViewModel HeaderAnalysis { get; set; } = new();
}


public sealed class WorkoutHeaderAnalysisViewModel
{
    public bool HasDetailedAnalysis { get; set; }
    public int HardSets { get; set; }
    public string MuscleDemandSummary { get; set; } = "";
    public bool HasEngineResult { get; set; }
    public decimal EstimatedCalories { get; set; }
    public decimal GlycogenDemandG { get; set; }
    public string SourceLabel { get; set; } = "No workout analyzed";
    public string ScopeLabel { get; set; } = "";
}


public sealed class WorkoutBuilderTypeOptionViewModel
{
    public string Code { get; set; } = "";
    public string EngineWorkoutTypeCode { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string GroupName { get; set; } = "Standard";
    public decimal DefaultMinutes { get; set; }
    public decimal DefaultRpe { get; set; }
}

public sealed class WorkoutTemplateSummaryViewModel
{
    public Guid WorkoutTemplateId { get; set; }
    public string TemplateName { get; set; } = "";
    public string WorkoutTypeCode { get; set; } = "";
    public decimal DefaultDurationMin { get; set; }
    public int ExerciseCount { get; set; }
}

public sealed class WorkoutTemplateDetailViewModel
{
    public Guid WorkoutTemplateId { get; set; }
    public string TemplateName { get; set; } = "";
    public string WorkoutTypeCode { get; set; } = "";
    public decimal DefaultDurationMin { get; set; }
    public List<DetailedWorkoutExerciseInputModel> Exercises { get; set; } = [];
}

public sealed class SystemWorkoutPreviewViewModel
{
    public string SystemCode { get; set; } = "";
    public string WorkoutTypeCode { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string GroupName { get; set; } = "Standard";
    public decimal DefaultMinutes { get; set; }
    public decimal DefaultRpe { get; set; }
    public List<DetailedWorkoutExerciseInputModel> Exercises { get; set; } = [];
}

public sealed class DetailedWorkoutExerciseInputModel
{
    public Guid ExerciseId { get; set; }
    public string ExerciseName { get; set; } = "";
    public string? Mechanics { get; set; }
    public string? Notes { get; set; }
    public int Sets { get; set; } = 3;

    // Reps remains the fallback/default value for compatibility.
    // SetReps is authoritative when the UI supplies one value per set.
    public int Reps { get; set; } = 10;
    public List<int> SetReps { get; set; } = [];

    public decimal? MaxWeight { get; set; }
    public string LoadUnitCode { get; set; } = "LB";
}

// Retained for compatibility with the richer persistence model.
// The normal Workout Builder UI intentionally uses the simpler
// Sets / Reps / MaxWeight summary above.
public sealed class DetailedWorkoutSetInputModel
{
    public int? Reps { get; set; }
    public decimal? Load { get; set; }
    public string LoadUnitCode { get; set; } = "LB";
    public decimal? Rpe { get; set; }
    public decimal? Rir { get; set; }
    public int? RestSeconds { get; set; }
    public int? DurationSeconds { get; set; }
    public bool IsWarmup { get; set; }
    public string? Notes { get; set; }
}