using WebRecomp.WorkoutIntelligence.Catalog;

namespace WebRecomp.WorkoutIntelligence.Persistence;

public interface IExerciseCatalogStore
{
    Task<ExerciseCatalogImportResult> ImportSnapshotAsync(
        ExerciseCatalogSnapshot snapshot,
        ExerciseCatalogProvenance provenance,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExerciseCatalogSearchItem>> SearchAsync(
        string? query,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task<ExerciseCatalogItem?> GetAsync(
        Guid exerciseId,
        CancellationToken cancellationToken = default);
}

public sealed record ExerciseCatalogProvenance(
    string SourceCode,
    string LicenseCode,
    string? SourceVersion,
    string? ManifestSha256,
    DateTime CapturedAtUtc);

public sealed record ExerciseCatalogImportResult(
    Guid ImportId,
    string SourceCode,
    int Added,
    int Updated,
    int Deactivated,
    int ActiveCount);

public sealed record ExerciseCatalogSearchItem(
    Guid ExerciseId,
    string Name,
    string? Mechanics,
    string? Category,
    string? DifficultyLevel,
    IReadOnlyList<string> PrimaryMuscles,
    IReadOnlyList<string> Equipment);

public interface IDetailedWorkoutStore
{
    Task<DetailedWorkoutSession?> GetAsync(
        Guid workoutSessionId,
        CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        DetailedWorkoutSession session,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid workoutSessionId,
        CancellationToken cancellationToken = default);
}

public sealed record DetailedWorkoutSession(
    Guid WorkoutSessionId,
    IReadOnlyList<DetailedWorkoutExerciseEntry> Exercises);

public sealed record DetailedWorkoutExerciseEntry(
    Guid DetailedExerciseId,
    Guid ExerciseId,
    int ExerciseOrder,
    string ExerciseNameSnapshot,
    string? MechanicsSnapshot,
    string? Notes,
    IReadOnlyList<DetailedWorkoutSetEntry> Sets,
    decimal? MaxWeight = null,
    string LoadUnitCode = "LB");

public sealed record DetailedWorkoutSetEntry(
    Guid DetailedSetId,
    int SetOrder,
    int? Reps,
    decimal? Load,
    string LoadUnitCode,
    decimal? Rpe,
    decimal? Rir,
    int? RestSeconds,
    int? DurationSeconds,
    bool IsWarmup,
    string? Notes);
