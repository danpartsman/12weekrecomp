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
}

public sealed class DetailedWorkoutEditorViewModel
{
    public bool FeatureEnabled { get; set; }
    public DateOnly Date { get; set; }
    public Guid? WorkoutSessionId { get; set; }
    public string WorkoutDisplayName { get; set; } = "";
    public string DefaultLoadUnitCode { get; set; } = "LB";
    public List<DetailedWorkoutSessionChoiceViewModel> Sessions { get; set; } = [];
    public List<DetailedWorkoutExerciseInputModel> Exercises { get; set; } = [];
}

public sealed class DetailedWorkoutExerciseInputModel
{
    public Guid ExerciseId { get; set; }
    public string ExerciseName { get; set; } = "";
    public string? Mechanics { get; set; }
    public string? Notes { get; set; }
    public List<DetailedWorkoutSetInputModel> Sets { get; set; } = [];
}

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
