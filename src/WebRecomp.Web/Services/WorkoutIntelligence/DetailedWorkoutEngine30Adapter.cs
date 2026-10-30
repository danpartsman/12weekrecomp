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
/// v1014B canonical actual rule:
/// every completed WorkoutSession is a real actual session, regardless of
/// whether it was entered through Workout Builder, Cardio/Recovery quick-log,
/// or Daily Tracking. Multiple legitimate sessions on the same date are
/// preserved and aggregated by Engine 3.0.
/// </summary>
public static class DetailedWorkoutEngine30Adapter
{
    public static Task<IReadOnlyList<WorkoutSession>> CanonicalCompletedSessionsAsync(
        RecompDbContext db,
        IReadOnlyList<WorkoutSession> sessions,
        CancellationToken cancellationToken = default)
    {
        _ = db;
        _ = cancellationToken;

        IReadOnlyList<WorkoutSession> canonical =
            sessions
                .Where(x => x.CompletedFlag)
                .OrderBy(x => x.WorkoutDate)
                .ThenBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.WorkoutSessionId)
                .ToArray();

        return Task.FromResult(canonical);
    }

    [Obsolete("Use CanonicalCompletedSessionsAsync. v1014B no longer suppresses valid same-day sessions by source.")]
    public static Task<IReadOnlyList<WorkoutSession>> PreferBuilderSessionsAsync(
        RecompDbContext db,
        IReadOnlyList<WorkoutSession> sessions,
        CancellationToken cancellationToken = default)
        => CanonicalCompletedSessionsAsync(
            db,
            sessions,
            cancellationToken);

    public static async Task<Engine30WorkoutSessionInput> BuildActualInputAsync(
        RecompDbContext db,
        IWorkoutIntelligenceGateway intelligence,
        WorkoutSession stored,
        Engine30WorkoutAdminProfile profile,
        string normalizedWorkoutCode,
        CancellationToken cancellationToken = default)
    {
        if(stored.EntrySourceCode==WebRecomp.Web.Services.Coaching.SnapWorkoutEngine.Source)
        {
            var json=await db.CoachAccountabilityAgreements.AsNoTracking().Where(x=>x.ClientId==stored.ClientId).Select(x=>x.StateJson).SingleOrDefaultAsync(cancellationToken);
            var state=WebRecomp.Web.Services.Coaching.AccountabilityDocument.Parse(json??"{}");
            var workout=state.SnapWorkouts.SingleOrDefault(x=>x.Id==stored.SourceRecordId);
            if(workout is not null&&state.SnapResults.TryGetValue(workout.Id,out var result))
                return WebRecomp.Web.Services.Coaching.SnapWorkoutEngine.Input(workout,result,profile,stored.DurationMin,stored.SessionRpe);
            throw new InvalidOperationException("Snap workout source details are missing.");
        }

        // B2.35.5.1 POSITIVE ACTUAL DURATION FALLBACK
        // Detailed hard-set analysis requires a positive duration.
        // Preserve the stored value; use the Engine workout-type default only
        // for analysis when an older/generated session has DurationMin <= 0.
        var analysisDurationMinutes =
            stored.DurationMin > 0m
                ? stored.DurationMin
                : profile.DefaultDurationMinutes > 0m
                    ? profile.DefaultDurationMinutes
                    : 1m;
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
                        analysisDurationMinutes,
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

            // B2.35.5.3 POSITIVE REST FALLBACK
            // Zero means "not captured" for historical/session data.
            // Engine 3.0 accepts a positive rest value or null, never zero.
            decimal? averageRest =
                recordedRest.Length > 0
                    ? Math.Round(recordedRest.Average(), 0)
                    : stored.TypicalRestSec is > 0
                        ? stored.TypicalRestSec
                        : null;

            // B2.35.15 CSV UNKNOWN EXERCISE FALLBACK
            //
            // Energy:
            // Workout Intelligence already counts ALL working/hard sets, including
            // unmapped CSV exercises, so TotalHardSets continues to drive the
            // canonical resistance-energy workload without a second calorie path.
            //
            // Glycogen:
            // If this workout has both mapped and unmapped exercises, scale mapped
            // upper/lower muscle demand by total working sets / mapped working sets.
            // This makes each unknown set behave like the average mapped set in THIS
            // workout, exactly matching the requested fallback.
            //
            // If every exercise is unmapped, there is no workout-specific muscle
            // distribution to average. Passing null region HardSets intentionally
            // preserves TotalHardSets while allowing Engine 3.0's canonical workout
            // profile to supply its normal upper/lower region fallback.
            var mappedWorkingSetCount =
                detailed
                    .Where(
                        x =>
                            !string.Equals(
                                x.MechanicsSnapshot,
                                "CSV_UNMAPPED_ESTIMATED",
                                StringComparison.OrdinalIgnoreCase))
                    .SelectMany(x => x.Sets)
                    .Count(
                        x =>
                            !x.IsWarmup
                            && (x.Reps ?? 0) > 0);

            var estimatedWorkingSetCount =
                detailed
                    .Where(
                        x =>
                            string.Equals(
                                x.MechanicsSnapshot,
                                "CSV_UNMAPPED_ESTIMATED",
                                StringComparison.OrdinalIgnoreCase))
                    .SelectMany(x => x.Sets)
                    .Count(
                        x =>
                            !x.IsWarmup
                            && (x.Reps ?? 0) > 0);

            var mappedLower =
                handoff.MuscleDemand
                    .Where(x => IsLowerBodyMuscle(x.MuscleSlug))
                    .Sum(x => x.WeightedSetEquivalents);

            var mappedUpper =
                handoff.MuscleDemand
                    .Where(x => !IsLowerBodyMuscle(x.MuscleSlug))
                    .Sum(x => x.WeightedSetEquivalents);

            decimal? lower;
            decimal? upper;

            if (estimatedWorkingSetCount > 0
                && mappedWorkingSetCount > 0)
            {
                var averageDemandScale =
                    (decimal)(
                        mappedWorkingSetCount
                        + estimatedWorkingSetCount)
                    / mappedWorkingSetCount;

                lower =
                    mappedLower
                    * averageDemandScale;

                upper =
                    mappedUpper
                    * averageDemandScale;
            }
            else if (estimatedWorkingSetCount > 0)
            {
                // All working sets are unmapped. Null is deliberate: the resolved
                // Engine profile owns the fallback region distribution, while
                // handoff.HardSets still carries the actual set count.
                lower =
                    null;

                upper =
                    null;
            }
            else
            {
                lower =
                    mappedLower;

                upper =
                    mappedUpper;
            }

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
                            : analysisDurationMinutes,
                    DefaultRpe: profile.DefaultRpe,
                    ActualDurationMinutes: analysisDurationMinutes,
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
            ActualDurationMinutes: analysisDurationMinutes,
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
        // B2.35.5.1 POSITIVE ACTUAL DURATION FALLBACK
        var analysisDurationMinutes =
            stored.DurationMin > 0m
                ? stored.DurationMin
                : profile.DefaultDurationMinutes > 0m
                    ? profile.DefaultDurationMinutes
                    : 1m;
        IReadOnlyCollection<Engine30WorkoutSegmentInput>? segments = null;

        // B2.35.5.3 POSITIVE REST FALLBACK
        if (stored.TypicalRestSec is > 0
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
                            : analysisDurationMinutes,
                    DefaultRpe: profile.DefaultRpe,
                    ActualDurationMinutes: analysisDurationMinutes,
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
            ActualDurationMinutes: analysisDurationMinutes,
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
