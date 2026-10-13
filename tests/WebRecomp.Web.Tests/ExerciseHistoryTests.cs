using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.WorkoutHistory;

namespace WebRecomp.Web.Tests;

public sealed class ExerciseHistoryTests
{
    private static RecompDbContext Database() => new(new DbContextOptionsBuilder<RecompDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static void Add(RecompDbContext db, Guid client, Guid exercise, DateOnly date, bool completed, string note)
    {
        var session = new WorkoutSession {WorkoutSessionId=Guid.NewGuid(),ClientId=client,ProgramId=Guid.NewGuid(),WorkoutDate=date,
            WorkoutTypeCode="MAJOR_PULL",WorkoutName="Pull session",CompletedFlag=completed};
        var row = new DetailedWorkoutExerciseRecord {DetailedWorkoutExerciseId=Guid.NewGuid(),WorkoutSessionId=session.WorkoutSessionId,
            ExerciseCatalogItemId=exercise,ExerciseNameSnapshot="Row",Notes=note,MaxWeight=60,LoadUnitCode="LB"};
        row.Sets.Add(new(){DetailedWorkoutSetId=Guid.NewGuid(),DetailedWorkoutExerciseId=row.DetailedWorkoutExerciseId,SetOrder=2,Reps=8,Load=25,LoadUnitCode="KG",Notes="Controlled tempo"});
        row.Sets.Add(new(){DetailedWorkoutSetId=Guid.NewGuid(),DetailedWorkoutExerciseId=row.DetailedWorkoutExerciseId,SetOrder=1,Reps=12,IsWarmup=true,LoadUnitCode="LB"});
        db.WorkoutSessions.Add(session);db.DetailedWorkoutExercises.Add(row);
    }
    [Fact] public async Task HistoryContainsOnlyOwnedCompletedMatchingExerciseAndPreservesRecordedValues()
    {
        using var db=Database();var client=Guid.NewGuid();var exercise=Guid.NewGuid();var today=new DateOnly(2026,10,12);
        Add(db,client,exercise,today.AddDays(-2),true,"Older note");Add(db,client,exercise,today.AddDays(-1),true,"Latest note");
        Add(db,Guid.NewGuid(),exercise,today,true,"Other client");Add(db,client,Guid.NewGuid(),today,true,"Other exercise");
        Add(db,client,exercise,today,false,"Draft");Add(db,client,exercise,today.AddDays(1),true,"Future");await db.SaveChangesAsync();
        var page=await ExerciseHistoryReader.ReadAsync(db,client,exercise,today,0,default);
        Assert.Equal(2,page.Entries.Count);Assert.Equal("Latest note",page.Entries[0].Notes);Assert.Equal("Older note",page.Entries[1].Notes);
        var sets=page.Entries[0].Sets;Assert.True(sets[0].Warmup);Assert.Null(sets[0].Weight);
        Assert.Equal(25m,sets[1].Weight);Assert.Equal("KG",sets[1].Unit);Assert.Equal("Controlled tempo",sets[1].Notes);
        Assert.Equal(60m,page.Entries[0].RecordedMaxWeight);Assert.Null(page.NextOffset);
        Assert.Equal(6,await db.WorkoutSessions.CountAsync());Assert.Empty(db.ChangeTracker.Entries().Where(x=>x.State!=EntityState.Unchanged));
    }
    [Fact] public async Task EarlierSessionsArePagedWithoutDuplicatesOrTruncatingHistory()
    {
        using var db=Database();var client=Guid.NewGuid();var exercise=Guid.NewGuid();var today=new DateOnly(2026,10,12);
        for(var i=0;i<23;i++) Add(db,client,exercise,today.AddDays(-i),true,"Note "+i);
        await db.SaveChangesAsync();
        var first=await ExerciseHistoryReader.ReadAsync(db,client,exercise,today,0,default);
        var next=await ExerciseHistoryReader.ReadAsync(db,client,exercise,today,first.NextOffset!.Value,default);
        Assert.Equal(20,first.Entries.Count);Assert.Equal(3,next.Entries.Count);Assert.Equal("Note 20",next.Entries[0].Notes);
        Assert.Null(next.NextOffset);Assert.Equal(23,first.Entries.Concat(next.Entries).Select(x=>x.Date).Distinct().Count());
    }
}
