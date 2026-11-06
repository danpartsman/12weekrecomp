using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.WorkoutHistory;
using WebRecomp.Web.ViewModels;
using WebRecomp.WorkoutIntelligence.Persistence;

namespace WebRecomp.Web.Tests;

public sealed class LastCompletedExerciseDefaultsTests
{
    private static RecompDbContext Database() => new(new DbContextOptionsBuilder<RecompDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static void Add(RecompDbContext db, Guid client, Guid exercise, DateOnly date, bool completed,
        string? notes, decimal? weight = 60, string unit = "LB", int hour = 0)
    {
        var session = new WorkoutSession { WorkoutSessionId = Guid.NewGuid(), ClientId = client,
            ProgramId = Guid.NewGuid(), WorkoutDate = date, WorkoutTypeCode = "MAJOR_PULL", CompletedFlag = completed,
            CreatedAtUtc = date.ToDateTime(new TimeOnly(hour, 0)) };
        var row = new DetailedWorkoutExerciseRecord { DetailedWorkoutExerciseId = Guid.NewGuid(),
            WorkoutSessionId = session.WorkoutSessionId, ExerciseCatalogItemId = exercise, ExerciseNameSnapshot = "Row",
            Notes = notes, MaxWeight = weight, LoadUnitCode = unit };
        row.Sets.Add(new() { DetailedWorkoutSetId = Guid.NewGuid(), SetOrder = 0, Reps = 20, IsWarmup = true, Load = 200 });
        row.Sets.Add(new() { DetailedWorkoutSetId = Guid.NewGuid(), SetOrder = 2, Reps = 8, Load = 20, LoadUnitCode = unit });
        row.Sets.Add(new() { DetailedWorkoutSetId = Guid.NewGuid(), SetOrder = 1, Reps = 11, Load = 25, LoadUnitCode = unit });
        db.WorkoutSessions.Add(session); db.DetailedWorkoutExercises.Add(row);
    }

    [Fact]
    public async Task LatestCompletedExerciseWinsAcrossWorkoutsButNeverUsesDraftsFutureOrOtherClients()
    {
        using var db = Database(); var client = Guid.NewGuid(); var exercise = Guid.NewGuid(); var today = new DateOnly(2026, 11, 6);
        Add(db, client, exercise, today.AddDays(-2), true, "Older");
        Add(db, client, exercise, today.AddDays(-1), true, "Latest", 72.5m);
        Add(db, client, exercise, today, false, "Unfinished", 99);
        Add(db, Guid.NewGuid(), exercise, today, true, "Other client", 999);
        Add(db, client, exercise, today.AddDays(1), true, "Future", 999);
        await db.SaveChangesAsync();
        var result = await LastCompletedExerciseDefaults.ReadAsync(db, client, [exercise], today, default);
        var last = result[exercise];
        Assert.Equal(new[] { 11, 8 }, last.SetReps);
        Assert.Equal(72.5m, last.WeightLb); Assert.Equal("Latest", last.Notes);
        Assert.Empty(db.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged));
    }

    [Fact]
    public async Task SameDayLatestSessionAndItsEmptyNotesAreCopiedWithoutFallingBackToOldNotes()
    {
        using var db = Database(); var client = Guid.NewGuid(); var exercise = Guid.NewGuid(); var today = new DateOnly(2026, 11, 6);
        Add(db, client, exercise, today, true, "Old notes", hour: 8);
        Add(db, client, exercise, today, true, null, 0, hour: 16);
        await db.SaveChangesAsync();
        var result = await LastCompletedExerciseDefaults.ReadAsync(db, client, [exercise], today, default);
        Assert.Null(result[exercise].Notes); Assert.Equal(0m, result[exercise].WeightLb);
    }

    [Fact]
    public async Task SeedPreservesCurrentIdentityAndUnknownExercisesAndDoesNotCompleteOrAlterHistory()
    {
        using var db = Database(); var client = Guid.NewGuid(); var exercise = Guid.NewGuid(); var missing = Guid.NewGuid();
        var today = new DateOnly(2026, 11, 6);
        Add(db, client, exercise, today.AddDays(-1), true, "Slow eccentric", null, "KG");
        await db.SaveChangesAsync();
        var baseline = new DetailedWorkoutExerciseEntry(Guid.Empty, exercise, 1, "Current name", "Compound", "Template notes",
            [new(Guid.Empty, 1, 10, null, "LB", null, null, null, null, false, null)], 10);
        var unchanged = baseline with { ExerciseId = missing, ExerciseOrder = 2 };
        var draft = new DetailedWorkoutSession(Guid.NewGuid(), [baseline, unchanged]);
        var seeded = await LastCompletedExerciseDefaults.SeedAsync(db, client, today, draft, default);
        Assert.Equal(draft.WorkoutSessionId, seeded.WorkoutSessionId);
        Assert.Equal("Current name", seeded.Exercises[0].ExerciseNameSnapshot);
        Assert.Equal("Slow eccentric", seeded.Exercises[0].Notes);
        Assert.Equal(55.12m, seeded.Exercises[0].MaxWeight); // 25 kg working-set max; warm-up excluded.
        Assert.Equal(new int?[] { 11, 8 }, seeded.Exercises[0].Sets.Select(s => s.Reps));
        Assert.All(seeded.Exercises[0].Sets, set => Assert.Equal(Guid.Empty, set.DetailedSetId));
        Assert.Same(unchanged, seeded.Exercises[1]); Assert.Equal(10m, draft.Exercises[0].MaxWeight);
        var input = new DetailedWorkoutExerciseInputModel();
        var last = (await LastCompletedExerciseDefaults.ReadAsync(db, client, [exercise], today, default))[exercise];
        last.Apply(input); Assert.Equal(2, input.Sets); Assert.Equal(new[] { 11, 8 }, input.SetReps);
        Assert.Equal("LB", input.LoadUnitCode); Assert.Equal("Slow eccentric", input.Notes);
        Assert.Single(await db.WorkoutSessions.ToListAsync());
    }
}
