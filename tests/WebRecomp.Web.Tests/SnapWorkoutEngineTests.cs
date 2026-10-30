using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.Engine30;
using Xunit;
namespace WebRecomp.Web.Tests;
public sealed class SnapWorkoutEngineTests
{
    private static Engine30WorkoutAdminProfile Profile()=>new Engine30WorkoutProfileService(null!).BuildBaselineProfile(new WorkoutTypeDefinition{WorkoutTypeCode=SnapWorkoutEngine.WorkoutCode,DisplayName="Full body",WorkoutCategoryCode="STRENGTH",WeeklyRoleCode="STRENGTH",FuelPriorityCode="HIGH",SchedulePrincipleCode="STRENGTH"});
    [Fact]
    public void ActualInputUsesOnlyPerformedRepsAndReportedRpeAndMuscles()
    {
        var workout=new AccountabilityWorkout{TimeLimitMinutes=120,Exercises=[new(){Id="one",Sets=3,Reps=10,Rpe=7,MuscleFocus="quads, glutes"}]};
        var result=new AccountabilityResult{Finalized=true,Sets=new(){["one"]=[new(){Reps=15,Rpe=9},new(){Reps=0},new(){Reps=0}]}};
        var input=SnapWorkoutEngine.Input(workout,result,Profile());var details=Assert.Single(input.Segments!).Resistance!;
        Assert.Equal(1,details.TotalHardSets);Assert.Equal(15,details.AverageRepsPerSet);Assert.Equal(9,input.ActualRpe);
        Assert.Equal(1,details.MuscleGroups!.Single(x=>x.MuscleGroupCode=="LOWER_BODY").HardSets);Assert.Equal(0,details.MuscleGroups!.Single(x=>x.MuscleGroupCode=="UPPER_BODY").HardSets);
        Assert.Equal(1,SnapWorkoutEngine.Duration(workout,result));result.DurationMinutes=17;Assert.Equal(17,SnapWorkoutEngine.Duration(workout,result));
        var planned=SnapWorkoutEngine.Input(workout,null,Profile());Assert.Equal(3,Assert.Single(planned.Segments!).Resistance!.TotalHardSets);Assert.Equal(7,planned.PlannedRpeOverride);
        Assert.Throws<ArgumentException>(()=>SnapWorkoutEngine.ValidateFocus("quads, mystery"));
    }
    [Fact]
    public async Task CanonicalSessionIsCreatedOnlyOnFinishAndCorrectionsUpdateSameRowOnCompletionDay()
    {
        using var db=new RecompDbContext(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var client=new Client{ClientId=Guid.NewGuid(),ClientName="Test",TimeZone="America/Los_Angeles"};db.Clients.Add(client);db.Programs.Add(new(){ProgramId=Guid.NewGuid(),ClientId=client.ClientId,SelectedPlanCode="TEST",GoalCode="TEST"});await db.SaveChangesAsync();
        var w=new AccountabilityWorkout{Id="snap-one",Name="Evening",Date="2026-10-29",Exercises=[new(){Id="one",Sets=1,Reps=10,Rpe=6,MuscleFocus="chest"}]};
        var r=new AccountabilityResult{Sets=new(){["one"]=[new(){Reps=10,Rpe=8}]}};await SnapWorkoutEngine.SyncAsync(db,client,w,r,default);Assert.Empty(db.WorkoutSessions);
        r.Finalized=true;r.CompletedAtUtc=DateTimeOffset.Parse("2026-10-31T06:00:00Z");await SnapWorkoutEngine.SyncAsync(db,client,w,r,default);await db.SaveChangesAsync();var session=Assert.Single(db.WorkoutSessions);var id=session.WorkoutSessionId;
        Assert.Equal(new DateOnly(2026,10,30),session.WorkoutDate);Assert.Equal(8,session.SessionRpe);Assert.Equal("3000.0",session.CalculationEngineVersion);
        r.Sets["one"][0].Reps=20;r.DurationMinutes=15;await SnapWorkoutEngine.SyncAsync(db,client,w,r,default);await db.SaveChangesAsync();Assert.Equal(id,Assert.Single(db.WorkoutSessions).WorkoutSessionId);Assert.Equal(15,session.DurationMin);
    }
}
