namespace WebRecomp.WorkoutIntelligence;

/// <summary>
/// Stable internal handoff contract between detailed workout capture/intelligence
/// and downstream consumers such as Engine 3.0.
///
/// This version intentionally does not couple to EF entities, HTTP models,
/// Kinetic DTOs, or Engine 3.0 implementation classes.
/// </summary>
public static class WorkoutIntelligenceContract
{
    public const string Schema = "workout-intelligence/v1";
}

public interface IWorkoutIntelligenceGateway
{
    WorkoutIntelligenceHandoff Analyze(WorkoutIntelligenceRequest request);
}

public sealed record WorkoutIntelligenceRequest(
    string SessionKey,
    int DurationMinutes,
    IReadOnlyList<WorkoutExerciseInput> Exercises);

public sealed record WorkoutExerciseInput(
    Guid ExerciseId,
    string ExerciseName,
    string? Mechanics,
    IReadOnlyList<MuscleTargetInput> MuscleTargets,
    IReadOnlyList<WorkoutSetInput> Sets);

public sealed record WorkoutSetInput(
    int? Reps,
    decimal? Load,
    decimal? Rpe,
    decimal? Rir,
    int? RestSeconds,
    int? DurationSeconds,
    bool IsWarmup = false);

public sealed record MuscleTargetInput(
    string MuscleSlug,
    string Role);

public sealed record MuscleDemandHandoff(
    string MuscleSlug,
    decimal WeightedSetEquivalents);

public sealed record WorkoutIntelligenceHandoff(
    string Schema,
    string SessionKey,
    int DurationMinutes,
    int ExerciseCount,
    int TotalSets,
    int HardSets,
    decimal VolumeLoad,
    decimal? AverageRpe,
    decimal? AverageRir,
    decimal HardSetsPerMinute,
    IReadOnlyList<MuscleDemandHandoff> MuscleDemand,
    string SourceMode);
