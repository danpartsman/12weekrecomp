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
    public async Task EngineRuntimeUsesSnapRpeVolumeAndMuscleRegionsIncludingZeroWork()
    {
        var profile=Profile();var engine=new Engine30WorkoutRuntimeService(new Profiles(profile));
        var w=new AccountabilityWorkout{Exercises=[new(){Id="one",Sets=3,Reps=10,Rpe=5,MuscleFocus="chest"}]};
        async Task<Engine30WorkoutRuntimeResult> Calculate(AccountabilityResult? result=null)=>await engine.CalculateAsync(new(SnapWorkoutEngine.Input(w,result,profile),1900,200,160,.528m,23));
        var original=await Calculate();Assert.True(original.Energy.EngineNetWorkoutEnergyKcal>0);Assert.True(original.Glycogen.EstimatedGlycogenDepletedG>0);
        w.Exercises[0].Rpe=9;var harder=await Calculate();Assert.True(harder.Energy.EngineNetWorkoutEnergyKcal>original.Energy.EngineNetWorkoutEnergyKcal);
        w.Exercises[0].MuscleFocus="quads, glutes";var lower=await Calculate();Assert.NotEqual(harder.Glycogen.EstimatedGlycogenDepletedG,lower.Glycogen.EstimatedGlycogenDepletedG);
        w.Exercises[0].Reps=30;Assert.True(SnapWorkoutEngine.Duration(w,null)>4);var moreReps=await Calculate();Assert.NotEqual(lower.Energy.EngineNetWorkoutEnergyKcal,moreReps.Energy.EngineNetWorkoutEnergyKcal);
        var zero=await Calculate(new(){Finalized=true,Sets=new(){["one"]=[new(){Reps=0},new(){Reps=0},new(){Reps=0}]}});Assert.Equal(0,zero.Energy.EngineNetWorkoutEnergyKcal);Assert.Equal(0,zero.Glycogen.EstimatedGlycogenDepletedG);
    }
    private sealed class Profiles(Engine30WorkoutAdminProfile profile):IEngine30WorkoutRuntimeProfileProvider
    {
        public Task<Engine30WorkoutRuntimeProfileResolution> ResolveAsync(string code,CancellationToken ct=default)=>Task.FromResult(new Engine30WorkoutRuntimeProfileResolution(profile,new("CFG-3000.0",Guid.NewGuid(),code,"Full body","BASELINE_FALLBACK",null,null,null,true,null)));
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
        db.CoachAccountabilityAgreements.Add(new(){ClientId=client.ClientId,StateJson=AccountabilityDocument.Serialize(new(){SnapWorkouts=[w],SnapResults=new(){[w.Id]=r}}),TermsJson="{}",TermsHash="test"});await db.SaveChangesAsync();
        var input=await WebRecomp.Web.Services.WorkoutIntelligence.DetailedWorkoutEngine30Adapter.BuildActualInputAsync(db,null!,session,Profile(),SnapWorkoutEngine.WorkoutCode);
        Assert.Equal(15,input.ActualDurationMinutes);Assert.Equal(8,input.ActualRpe);Assert.Equal(20,Assert.Single(input.Segments!).Resistance!.AverageRepsPerSet);
    }
}
