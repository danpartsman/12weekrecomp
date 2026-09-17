using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.WorkoutIntelligence;

namespace WebRecomp.Web.Services.WorkoutIntelligence;

/// <summary>
/// Bridges detailed Workout Builder data into the existing Engine 3.0
/// workout-session contract without changing Engine 3.0 formulas.
///
/// Source precedence is date-scoped:
/// 1. Completed Workout Builder sessions with detailed exercise data.
/// 2. Otherwise completed Daily Entry/manual sessions.
/// The two sources are never added together for the same date.
/// </summary>
public static class DetailedWorkoutEngine30Adapter
{
    public static async Task<IReadOnlyList<WorkoutSession>> PreferBuilderSessionsAsync(
        RecompDbContext db,
        IReadOnlyList<WorkoutSession> sessions,
        CancellationToken cancellationToken = default)
    {
        if (sessions.Count == 0)
            return sessions;

        var ids = sessions
            .Select(x => x.WorkoutSessionId)
            .ToArray();

        var detailedIds = await db.DetailedWorkoutExercises
            .AsNoTracking()
            .Where(x => ids.Contains(x.WorkoutSessionId))
            .Select(x => x.WorkoutSessionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var detailedSet = detailedIds.ToHashSet();

        return sessions
            .GroupBy(x => x.WorkoutDate)
            .OrderBy(x => x.Key)
            .SelectMany(group =>
            {
                var builder = group
                    .Where(x =>
                        detailedSet.Contains(x.WorkoutSessionId)
                        && string.Equals(
                            x.EntrySourceCode,
                            RecompInputSourceCodes.DetailedWorkout,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.CreatedAtUtc)
                    .ToArray();

                return builder.Length > 0
                    ? builder
                    : group.OrderBy(x => x.CreatedAtUtc).ToArray();
            })
            .ToArray();
    }

    public static async Task<Engine30WorkoutSessionInput> BuildActualInputAsync(
        RecompDbContext db,
        IWorkoutIntelligenceGateway intelligence,
        WorkoutSession stored,
        Engine30WorkoutAdminProfile profile,
        string normalizedWorkoutCode,
        CancellationToken cancellationToken = default)
    {
        var monitor =
            stored.DeviceCalories.HasValue
                ? new Engine30WorkoutMonitorData(
                    ReportedCalories: stored.DeviceCalories,
                    ReportedCalorieTypeCode:
                        Engine30WorkoutSessionCodes.MonitorCaloriesUnknown)
                : null;

        var detailed = await db.DetailedWorkoutExercises
            .AsNoTracking()
            .Include(x => x.Sets)
            .Where(x => x.WorkoutSessionId == stored.WorkoutSessionId)
            .OrderBy(x => x.ExerciseOrder)
            .ToListAsync(cancellationToken);

        if (detailed.Count == 0
            || !string.Equals(
                stored.EntrySourceCode,
                RecompInputSourceCodes.DetailedWorkout,
                StringComparison.OrdinalIgnoreCase))
        {
            return BuildLegacyActualInput(
                stored,
                profile,
                normalizedWorkoutCode,
                monitor);
        }

        var exerciseIds = detailed
            .Select(x => x.ExerciseCatalogItemId)
            .Distinct()
            .ToArray();

        var targets = await db.ExerciseMuscleTargets
            .AsNoTracking()
            .Where(x => exerciseIds.Contains(x.ExerciseCatalogItemId))
            .ToListAsync(cancellationToken);

        var targetsByExercise = targets
            .GroupBy(x => x.ExerciseCatalogItemId)
            .ToDictionary(
                x => x.Key,
                x => x
                    .OrderBy(t => t.DisplayOrder)
                    .Select(t => new MuscleTargetInput(
                        t.MuscleSlug,
                        t.RoleCode))
                    .ToArray());

        var request = new WorkoutIntelligenceRequest(
            SessionKey: stored.WorkoutSessionId.ToString("D"),
            DurationMinutes:
                Math.Max(
                    1,
                    (int)Math.Round(
                        stored.DurationMin,
                        0,
                        MidpointRounding.AwayFromZero)),
            Exercises:
                detailed.Select(exercise =>
                    new WorkoutExerciseInput(
                        ExerciseId: exercise.ExerciseCatalogItemId,
                        ExerciseName: exercise.ExerciseNameSnapshot,
                        Mechanics: exercise.MechanicsSnapshot,
                        MuscleTargets:
                            targetsByExercise.TryGetValue(
                                exercise.ExerciseCatalogItemId,
                                out var muscleTargets)
                                ? muscleTargets
                                : Array.Empty<MuscleTargetInput>(),
                        Sets:
                            exercise.Sets
                                .OrderBy(s => s.SetOrder)
                                .Select(s =>
                                    new WorkoutSetInput(
                                        Reps: s.Reps,
                                        Load: s.Load,
                                        Rpe: s.Rpe,
                                        Rir: s.Rir,
                                        RestSeconds: s.RestSeconds,
                                        DurationSeconds: s.DurationSeconds,
                                        IsWarmup: s.IsWarmup))
                                .ToArray()))
                .ToArray());

        var handoff = intelligence.Analyze(request);

        IReadOnlyCollection<Engine30WorkoutSegmentInput>? segments = null;

        if (profile.WorkoutFamilyCode.Equals(
                Engine30WorkoutFamilyCodes.Resistance,
                StringComparison.OrdinalIgnoreCase))
        {
            var workingSets = detailed
                .SelectMany(x => x.Sets)
                .Where(x => !x.IsWarmup && (x.Reps ?? 0) > 0)
                .ToArray();

            decimal? averageReps =
                workingSets.Length == 0
                    ? null
                    : Math.Round(
                        workingSets.Average(x => (decimal)(x.Reps ?? 0)),
                        2,
                        MidpointRounding.AwayFromZero);

            var recordedRest = workingSets
                .Where(x => x.RestSeconds.HasValue && x.RestSeconds.Value > 0)
                .Select(x => (decimal)x.RestSeconds!.Value)
                .ToArray();

            decimal? averageRest =
                recordedRest.Length > 0
                    ? Math.Round(recordedRest.Average(), 0)
                    : stored.TypicalRestSec;

            var lower = handoff.MuscleDemand
                .Where(x => IsLowerBodyMuscle(x.MuscleSlug))
                .Sum(x => x.WeightedSetEquivalents);

            var upper = handoff.MuscleDemand
                .Where(x => !IsLowerBodyMuscle(x.MuscleSlug))
                .Sum(x => x.WeightedSetEquivalents);

            var bodyRegions = profile.MuscleRegions
                .Where(x => !string.IsNullOrWhiteSpace(x.MuscleRegionCode))
                .Select(region =>
                {
                    var code = region.MuscleRegionCode.Trim().ToUpperInvariant();

                    decimal? hardSets =
                        code == "LOWER_BODY"
                            ? lower
                            : code == "UPPER_BODY"
                                ? upper
                                : null;

                    return new Engine30WorkoutMuscleWork(
                        MuscleGroupCode: code,
                        HardSets: hardSets,
                        RecruitmentFraction: null);
                })
                .ToArray();

            segments =
            [
                new Engine30WorkoutSegmentInput(
                    SegmentCode: "DETAILED_WORKOUT",
                    ModalityCode: profile.DefaultModalityCode,
                    PlannedDurationMinutes:
                        profile.DefaultDurationMinutes > 0m
                            ? profile.DefaultDurationMinutes
                            : Math.Max(1m, stored.DurationMin),
                    DefaultRpe: profile.DefaultRpe,
                    ActualDurationMinutes: stored.DurationMin,
                    ActualRpe: stored.SessionRpe,
                    Resistance:
                        new Engine30ResistanceSegmentDetails(
                            TotalHardSets: handoff.HardSets,
                            AverageRestSeconds: averageRest,
                            AverageRepsPerSet: averageReps,
                            MuscleGroups: bodyRegions))
            ];
        }

        return new Engine30WorkoutSessionInput(
            WorkoutTypeCode: normalizedWorkoutCode,
            PlannedDurationMinutes: 0m,
            ActualDurationMinutes: stored.DurationMin,
            ActualRpe: stored.SessionRpe,
            MonitorData: monitor,
            Segments: segments);
    }

    private static Engine30WorkoutSessionInput BuildLegacyActualInput(
        WorkoutSession stored,
        Engine30WorkoutAdminProfile profile,
        string normalizedWorkoutCode,
        Engine30WorkoutMonitorData? monitor)
    {
        IReadOnlyCollection<Engine30WorkoutSegmentInput>? segments = null;

        if (stored.TypicalRestSec.HasValue
            && profile.WorkoutFamilyCode.Equals(
                Engine30WorkoutFamilyCodes.Resistance,
                StringComparison.OrdinalIgnoreCase))
        {
            segments =
            [
                new Engine30WorkoutSegmentInput(
                    SegmentCode: "SESSION",
                    ModalityCode: profile.DefaultModalityCode,
                    PlannedDurationMinutes:
                        profile.DefaultDurationMinutes > 0m
                            ? profile.DefaultDurationMinutes
                            : Math.Max(1m, stored.DurationMin),
                    DefaultRpe: profile.DefaultRpe,
                    ActualDurationMinutes: stored.DurationMin,
                    ActualRpe: stored.SessionRpe,
                    Resistance:
                        new Engine30ResistanceSegmentDetails(
                            AverageRestSeconds:
                                stored.TypicalRestSec.Value))
            ];
        }

        return new Engine30WorkoutSessionInput(
            WorkoutTypeCode: normalizedWorkoutCode,
            PlannedDurationMinutes: 0m,
            ActualDurationMinutes: stored.DurationMin,
            ActualRpe: stored.SessionRpe,
            MonitorData: monitor,
            Segments: segments);
    }

    private static bool IsLowerBodyMuscle(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return false;

        var value = slug.Trim().ToLowerInvariant();

        return value.Contains("quad")
            || value.Contains("glute")
            || value.Contains("hamstring")
            || value.Contains("calf")
            || value.Contains("calves")
            || value.Contains("adductor")
            || value.Contains("abductor")
            || value.Contains("hip")
            || value.Contains("gastro")
            || value.Contains("soleus")
            || value.Contains("tibialis");
    }
}
