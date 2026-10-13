using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.Services.WorkoutHistory;

public sealed record ExerciseHistorySet(short Number, short? Reps, decimal? Weight, string Unit, int? DurationSeconds, bool Warmup, string? Notes);
public sealed record ExerciseHistoryEntry(DateOnly Date, string Workout, string? Notes, decimal? RecordedMaxWeight, string Unit, IReadOnlyList<ExerciseHistorySet> Sets);
public sealed record ExerciseHistoryPage(IReadOnlyList<ExerciseHistoryEntry> Entries, int? NextOffset);

public static class ExerciseHistoryReader
{
    public static async Task<ExerciseHistoryPage> ReadAsync(RecompDbContext db, Guid clientId, Guid exerciseId, DateOnly today, int offset, CancellationToken ct)
    {
        offset = Math.Max(0, offset);
        const int pageSize = 20;
        var page = await (from exercise in db.DetailedWorkoutExercises.AsNoTracking()
            join session in db.WorkoutSessions.AsNoTracking() on exercise.WorkoutSessionId equals session.WorkoutSessionId
            where session.ClientId == clientId && session.CompletedFlag && session.WorkoutDate <= today && exercise.ExerciseCatalogItemId == exerciseId
            orderby session.WorkoutDate descending, session.CreatedAtUtc descending, session.WorkoutSessionId, exercise.ExerciseOrder, exercise.DetailedWorkoutExerciseId
            select new { exercise.DetailedWorkoutExerciseId, session.WorkoutDate, Workout = session.WorkoutName ?? session.WorkoutTypeCode })
            .Skip(offset).Take(pageSize + 1).ToListAsync(ct);
        var ids = page.Take(pageSize).Select(x => x.DetailedWorkoutExerciseId).ToArray();
        var rows = await db.DetailedWorkoutExercises.AsNoTracking().Where(x => ids.Contains(x.DetailedWorkoutExerciseId))
            .Include(x => x.Sets).AsSplitQuery().ToDictionaryAsync(x => x.DetailedWorkoutExerciseId, ct);
        var entries = page.Take(pageSize).Select(x => {
            var row = rows[x.DetailedWorkoutExerciseId];
            return new ExerciseHistoryEntry(x.WorkoutDate, x.Workout, row.Notes, row.MaxWeight, row.LoadUnitCode,
                row.Sets.OrderBy(s => s.SetOrder).ThenBy(s => s.DetailedWorkoutSetId)
                    .Select(s => new ExerciseHistorySet(s.SetOrder, s.Reps, s.Load, s.LoadUnitCode, s.DurationSeconds, s.IsWarmup, s.Notes)).ToList());
        }).ToList();
        return new(entries, page.Count > pageSize ? offset + pageSize : null);
    }
}
