using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.ViewModels;
using WebRecomp.WorkoutIntelligence.Persistence;

namespace WebRecomp.Web.Services.WorkoutHistory;

public sealed record CompletedExerciseDefaults(int[] SetReps, decimal? WeightLb, string? Notes)
{
    public void Apply(DetailedWorkoutExerciseInputModel input)
    {
        input.Sets = SetReps.Length;
        input.Reps = SetReps[0];
        input.SetReps = SetReps.ToList();
        input.MaxWeight = WeightLb;
        input.LoadUnitCode = "LB";
        input.Notes = Notes;
    }
}

public static class LastCompletedExerciseDefaults
{
    public static async Task<IReadOnlyDictionary<Guid, CompletedExerciseDefaults>> ReadAsync(
        RecompDbContext db, Guid clientId, IEnumerable<Guid> exerciseIds, DateOnly throughDate, CancellationToken ct)
    {
        var requested = exerciseIds.Distinct().ToArray();
        if (requested.Length == 0) return new Dictionary<Guid, CompletedExerciseDefaults>();
        var history = await (from exercise in db.DetailedWorkoutExercises.AsNoTracking()
            join session in db.WorkoutSessions.AsNoTracking() on exercise.WorkoutSessionId equals session.WorkoutSessionId
            where session.ClientId == clientId && session.CompletedFlag && session.WorkoutDate <= throughDate
                && requested.Contains(exercise.ExerciseCatalogItemId)
            orderby session.WorkoutDate descending, session.CreatedAtUtc descending, session.WorkoutSessionId, exercise.ExerciseOrder
            select new { exercise.ExerciseCatalogItemId, exercise.DetailedWorkoutExerciseId }).ToListAsync(ct);
        var latest = history.GroupBy(x => x.ExerciseCatalogItemId).Select(g => g.First()).ToArray();
        var ids = latest.Select(x => x.DetailedWorkoutExerciseId).ToArray();
        var records = await db.DetailedWorkoutExercises.AsNoTracking().Where(x => ids.Contains(x.DetailedWorkoutExerciseId))
            .Include(x => x.Sets).AsSplitQuery().ToDictionaryAsync(x => x.DetailedWorkoutExerciseId, ct);
        var result = new Dictionary<Guid, CompletedExerciseDefaults>();
        foreach (var item in latest)
        {
            var row = records[item.DetailedWorkoutExerciseId];
            var sets = row.Sets.Where(s => !s.IsWarmup && s.Reps is > 0).OrderBy(s => s.SetOrder).Take(20).ToArray();
            if (sets.Length == 0) continue; // Timed/recovery-only records are not lifting defaults.
            var weights = sets.Where(s => s.Load.HasValue).Select(s => ToLb(s.Load!.Value, s.LoadUnitCode)).ToArray();
            var weight = row.MaxWeight.HasValue ? ToLb(row.MaxWeight.Value, row.LoadUnitCode)
                : weights.Length > 0 ? weights.Max() : (decimal?)null;
            result[item.ExerciseCatalogItemId] = new(sets.Select(s => Math.Clamp((int)s.Reps!.Value, 1, 500)).ToArray(), weight, row.Notes);
        }
        return result;
    }

    public static async Task<DetailedWorkoutSession> SeedAsync(RecompDbContext db, Guid clientId,
        DateOnly throughDate, DetailedWorkoutSession draft, CancellationToken ct)
    {
        var previous = await ReadAsync(db, clientId, draft.Exercises.Select(x => x.ExerciseId), throughDate, ct);
        return draft with { Exercises = draft.Exercises.Select(exercise => {
            if (!previous.TryGetValue(exercise.ExerciseId, out var last)) return exercise;
            return exercise with {
                Notes = last.Notes, MaxWeight = last.WeightLb, LoadUnitCode = "LB",
                Sets = last.SetReps.Select((reps, i) => new DetailedWorkoutSetEntry(Guid.Empty, i + 1, reps,
                    null, "LB", null, null, null, null, false, null)).ToArray()
            };
        }).ToArray() };
    }

    private static decimal ToLb(decimal weight, string unit) => string.Equals(unit, "KG", StringComparison.OrdinalIgnoreCase)
        ? Math.Round(weight * 2.2046226218m, 2) : weight;
}
