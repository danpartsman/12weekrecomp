using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.WorkoutIntelligence.Persistence;

namespace WebRecomp.Web.Services.WorkoutIntelligence;

public sealed class SqlDetailedWorkoutStore(RecompDbContext db) : IDetailedWorkoutStore
{
    public async Task<DetailedWorkoutSession?> GetAsync(Guid workoutSessionId, CancellationToken cancellationToken = default)
    {
        var exercises = await db.DetailedWorkoutExercises
            .AsNoTracking()
            .Include(x => x.Sets)
            .Where(x => x.WorkoutSessionId == workoutSessionId)
            .OrderBy(x => x.ExerciseOrder)
            .ToListAsync(cancellationToken);

        if (exercises.Count == 0) return null;

        return new DetailedWorkoutSession(
            workoutSessionId,
            exercises.Select(x => new DetailedWorkoutExerciseEntry(
                x.DetailedWorkoutExerciseId,
                x.ExerciseCatalogItemId,
                x.ExerciseOrder,
                x.ExerciseNameSnapshot,
                x.MechanicsSnapshot,
                x.Notes,
                x.Sets.OrderBy(s => s.SetOrder).Select(s => new DetailedWorkoutSetEntry(
                    s.DetailedWorkoutSetId,
                    s.SetOrder,
                    s.Reps,
                    s.Load,
                    s.LoadUnitCode,
                    s.Rpe,
                    s.Rir,
                    s.RestSeconds,
                    s.DurationSeconds,
                    s.IsWarmup,
                    s.Notes)).ToArray(),
                x.MaxWeight,
                string.IsNullOrWhiteSpace(x.LoadUnitCode) ? "LB" : x.LoadUnitCode
            )).ToArray());
    }

    public async Task ReplaceAsync(DetailedWorkoutSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!await db.WorkoutSessions.AsNoTracking().AnyAsync(x => x.WorkoutSessionId == session.WorkoutSessionId, cancellationToken))
            throw new InvalidOperationException("Workout session does not exist.");

        var existing = await db.DetailedWorkoutExercises
            .Include(x => x.Sets)
            .Where(x => x.WorkoutSessionId == session.WorkoutSessionId)
            .ToListAsync(cancellationToken);

        db.DetailedWorkoutExercises.RemoveRange(existing);

        var requestedExerciseIds = session.Exercises.Select(x => x.ExerciseId).Distinct().ToArray();
        var activeExerciseIds = await db.ExerciseCatalogItems
            .AsNoTracking()
            .Where(x => requestedExerciseIds.Contains(x.ExerciseCatalogItemId) && x.IsActive)
            .Select(x => x.ExerciseCatalogItemId)
            .ToListAsync(cancellationToken);

        var activeSet = activeExerciseIds.ToHashSet();
        var now = DateTime.UtcNow;

        foreach (var exercise in session.Exercises.OrderBy(x => x.ExerciseOrder))
        {
            if (!activeSet.Contains(exercise.ExerciseId))
                throw new InvalidOperationException($"Exercise catalog item is not active: {exercise.ExerciseId}");

            var row = new DetailedWorkoutExerciseRecord
            {
                DetailedWorkoutExerciseId = exercise.DetailedExerciseId == Guid.Empty ? Guid.NewGuid() : exercise.DetailedExerciseId,
                WorkoutSessionId = session.WorkoutSessionId,
                ExerciseCatalogItemId = exercise.ExerciseId,
                ExerciseOrder = checked((short)exercise.ExerciseOrder),
                ExerciseNameSnapshot = exercise.ExerciseNameSnapshot,
                MechanicsSnapshot = exercise.MechanicsSnapshot,
                Notes = exercise.Notes,
                MaxWeight = exercise.MaxWeight,
                LoadUnitCode = string.IsNullOrWhiteSpace(exercise.LoadUnitCode)
                    ? "LB"
                    : exercise.LoadUnitCode.Trim().ToUpperInvariant(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            foreach (var set in exercise.Sets.OrderBy(x => x.SetOrder))
            {
                row.Sets.Add(new DetailedWorkoutSetRecord
                {
                    DetailedWorkoutSetId = set.DetailedSetId == Guid.Empty ? Guid.NewGuid() : set.DetailedSetId,
                    DetailedWorkoutExerciseId = row.DetailedWorkoutExerciseId,
                    SetOrder = checked((short)set.SetOrder),
                    Reps = set.Reps.HasValue ? checked((short)set.Reps.Value) : null,
                    Load = set.Load,
                    LoadUnitCode = string.IsNullOrWhiteSpace(set.LoadUnitCode) ? "LB" : set.LoadUnitCode.Trim().ToUpperInvariant(),
                    Rpe = set.Rpe,
                    Rir = set.Rir,
                    RestSeconds = set.RestSeconds,
                    DurationSeconds = set.DurationSeconds,
                    IsWarmup = set.IsWarmup,
                    Notes = set.Notes,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
            db.DetailedWorkoutExercises.Add(row);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid workoutSessionId, CancellationToken cancellationToken = default)
    {
        var rows = await db.DetailedWorkoutExercises.Where(x => x.WorkoutSessionId == workoutSessionId).ToListAsync(cancellationToken);
        if (rows.Count == 0) return;
        db.DetailedWorkoutExercises.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
    }
}
