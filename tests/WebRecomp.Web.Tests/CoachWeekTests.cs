using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WebRecomp.Web.Controllers;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Tests;
public sealed class CoachWeekTests
{
    private static Engine30DashboardDayProjection Day(DateOnly date) => new(date,true,"REST","Rest",Engine30PrescriptionCategoryCodes.RestRecoveryOther,
        0,null,1800,0,0,0,2000,1820,0,1820,180,140,60,100,100,0,"ENGINE",0,0,"3000.0","CFG",null,null);
    [Fact]
    public void ExactGoalsPreserveWeeklyTotalsAndTrainingCarbPattern()
    {
        var source=Enumerable.Range(0,7).Select(i=>Day(new DateOnly(2026,10,19).AddDays(i)) with {RecommendedCarbsG=100+i*20}).ToArray();
        var result=CoachPrescriptionAllocator.Evaluate(source,new() {DailyProteinTarget=190,WeeklyCarbsTarget=1400,WeeklyFatTarget=385,Reason="Test"});
        Assert.Empty(result.Errors);Assert.Equal(1330,result.Days.Sum(x=>x.ProteinG));Assert.InRange(Math.Abs(result.Days.Sum(x=>x.RecommendedCarbsG)-1400),0,0.000001m);
        Assert.Equal(385,result.Days.Sum(x=>x.FatG));Assert.True(result.Days.Last().RecommendedCarbsG>result.Days.First().RecommendedCarbsG);
        Assert.All(result.Days,d=>{Assert.Equal(190,d.ProteinG);Assert.Equal(55,d.FatG);Assert.Equal(4*d.ProteinG+4*d.RecommendedCarbsG+9*d.FatG,d.PrescribedEnergyKcal);});
    }
    [Fact]
    public void ExactGoalsCannotBeOverriddenByCalorieTopUpsOrInfeasibleBounds()
    {
        var days=Enumerable.Range(0,7).Select(i=>Day(new DateOnly(2026,10,19).AddDays(i))).ToArray();
        Assert.NotEmpty(CoachPrescriptionAllocator.Evaluate(days,new() {DailyProteinTarget=180,WeeklyCarbsTarget=980,WeeklyFatTarget=420,WeeklyCaloriesMin=20000}).Errors);
        Assert.NotEmpty(CoachPrescriptionAllocator.Evaluate(days,new() {WeeklyCarbsTarget=600,CarbsMin=100}).Errors);
        Assert.NotEmpty(CoachPrescriptionAllocator.Evaluate(days,new() {DailyProteinTarget=170,ProteinMin=180}).Errors);
        Assert.NotEmpty(CoachPrescriptionAllocator.Validate(new() {WeeklyFatTarget=420,AdjustCarbsThenFat=true,CarbsMin=0,CarbsMax=500,FatMin=0,FatMax=100}));
        Assert.Throws<ArgumentException>(()=>CoachPrescriptionAllocator.AllocateWeekly(1401,Enumerable.Repeat(1m,7).ToArray(),0,200));
        var bounded=CoachPrescriptionAllocator.AllocateWeekly(1000,new decimal[]{500,100,100,100,100,100,100},100,200);
        Assert.Equal(1000,bounded.Sum());Assert.All(bounded,x=>Assert.InRange(x,100,200));
        var zero=CoachPrescriptionAllocator.AllocateWeekly(0,new decimal[7],null,null);Assert.All(zero,x=>Assert.Equal(0,x));
    }
    [Fact]
    public void MaintenanceReferenceIsSeparateAndReconcilesToEnergy()
    {
        var day=Day(new(2026,10,19));var carbs=CoachWeekService.MaintenanceCarbs(day);
        Assert.Equal(day.MaintenanceEnergyKcal,4*day.ProteinG+9*day.FatG+4*carbs!.Value);
        Assert.Equal(140,day.RecommendedCarbsG);Assert.Null(CoachWeekService.MaintenanceCarbs(day with {MaintenanceEnergyKcal=100}));
    }
    [Theory]
    [InlineData("MAJOR_LEGS","RESISTANCE","Hard strength")]
    [InlineData("ACCESSORY","RESISTANCE","Accessory")]
    [InlineData("TRUE_HIIT","CONDITIONING_CARDIO","Conditioning")]
    [InlineData("ZONE2_CARDIO","CONDITIONING_CARDIO","Cardio")]
    [InlineData("MOBILITY_RECOVERY","REST_RECOVERY_OTHER","Recovery")]
    [InlineData("REST","REST_RECOVERY_OTHER","Rest")]
    public void WeeklyCountsUseUserFacingTrainingGroups(string code,string category,string expected) => Assert.Equal(expected,CoachWeekService.Group(code,category));
    [Fact]
    public void WeeklyEditsPreservePastDaysAndProgramBoundaries()
    {
        var today=new DateOnly(2026,10,15);Assert.False(CoachWeekService.ValidWeek(new(2026,10,12),today,today.AddDays(-30)));
        Assert.False(CoachWeekService.ValidWeek(new(2026,10,20),today,today));Assert.False(CoachWeekService.ValidWeek(new(2026,10,19),today,new(2026,10,20)));
        Assert.False(CoachWeekService.ValidWeek(new(2027,2,1),today,today));Assert.True(CoachWeekService.ValidWeek(new(2026,10,19),today,today));
    }
    [Fact]
    public async Task UnconnectedClientsCannotBeReadOrUpdated()
    {
        using var db=new RecompDbContext(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Add(new UserAccount {UserId=Guid.NewGuid(),ExternalSubject="coach"});await db.SaveChangesAsync();
        var http=new DefaultHttpContext {User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim("oid","coach")},"test"))};
        var controller=new CoachWeekController(db,new AppUserContextService(db,new ConfigurationBuilder().Build()),new CoachCenterService(db),null!) {ControllerContext=new ControllerContext {HttpContext=http}};
        Assert.IsType<NotFoundResult>(await controller.Index(Guid.NewGuid(),null,default));Assert.IsType<NotFoundResult>(await controller.Save(Guid.NewGuid(),new(),default));
    }
    [Theory]
    [InlineData("save")][InlineData("revoked")][InlineData("started")][InlineData("stale-plan")][InlineData("stale-rules")]
    public async Task WeeklySaveIsScopedAuditedAndRejectsConflicts(string scenario)
    {
        using var db=new RecompDbContext(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var coach=new UserAccount {UserId=Guid.NewGuid(),ExternalSubject="coach"};var client=new Client {ClientId=Guid.NewGuid(),ClientName="Client"};
        var today=AppUserContextService.GetClientToday(client);var week=Engine30DashboardProjectionService.WeekStart(today).AddDays(7);
        var program=new ProgramRecord {ProgramId=Guid.NewGuid(),ClientId=client.ClientId,SelectedPlanCode="PLAN_4",GoalCode="RECOMP",StartDate=today.AddDays(-30)};
        var link=new CoachClientRelationship {CoachClientRelationshipId=Guid.NewGuid(),CoachUserId=coach.UserId,ClientId=client.ClientId,StatusCode="ACTIVE"};db.AddRange(coach,client,program,link);await db.SaveChangesAsync();
        var rules=new CoachPrescriptionRules {DailyProteinTarget=180,WeeklyCarbsTarget=980,WeeklyFatTarget=420,EffectiveWeek=week,Reason="Reviewed week"};
        var days=Enumerable.Range(0,7).Select(i=>Day(week.AddDays(i))).ToArray();
        var page=new CoachWeekPage {ClientId=client.ClientId,Input=new() {Rules=rules,PreviewToken="reviewed",Workouts=Enumerable.Repeat("REST",7).ToList()},PreviewToken="reviewed",Preview=days,Assigned=days,Fingerprint="[]",
            Options=[new("REST","Rest","Rest")],Proposed=days.Select(d=>new PlannedWorkout {PlannedWorkoutId=Guid.NewGuid(),ClientId=client.ClientId,ProgramId=program.ProgramId,PlannedDate=d.Date,WorkoutTypeCode="REST",PlannedDurationMin=0,SourceCode="COACH"}).ToArray()};
        if(scenario=="revoked") {link.StatusCode="REVOKED";link.RevokedAtUtc=DateTime.UtcNow;}
        if(scenario=="started") db.WorkoutSessions.Add(new() {WorkoutSessionId=Guid.NewGuid(),ClientId=client.ClientId,ProgramId=program.ProgramId,WorkoutDate=week,WorkoutTypeCode="REST",CompletedFlag=false});
        if(scenario=="stale-plan") db.Add(new PlannedWorkout {PlannedWorkoutId=Guid.NewGuid(),ClientId=client.ClientId,ProgramId=program.ProgramId,PlannedDate=week,WorkoutTypeCode="REST"});
        if(scenario=="stale-rules") db.CoachPrescriptionRuleVersions.Add(new() {CoachPrescriptionRuleVersionId=Guid.NewGuid(),ClientId=client.ClientId,ProgramId=program.ProgramId,CoachUserId=coach.UserId,EffectiveWeek=week,IsEnabled=true,RulesJson="{}",PreviewJson="{}",Reason="Newer",CreatedAtUtc=DateTime.UtcNow});
        await db.SaveChangesAsync();
        var workoutService=new CoachWorkoutPlanService(db,null!,null!,null!,null!);
        var service=new CoachWeekService(db,null!,null!,null!,workoutService);
        var success=await service.SaveAsync(new(coach,client,program),page,default);Assert.Equal(scenario=="save",success);
        if(success) {Assert.Equal(7,await db.PlannedWorkouts.CountAsync());Assert.Equal(7,await db.CoachWorkoutPlanRevisions.CountAsync());var version=await db.CoachPrescriptionRuleVersions.SingleAsync();Assert.Equal(coach.UserId,version.CoachUserId);Assert.Contains("DailyProteinTarget",version.RulesJson);}
        else Assert.Empty(await db.CoachWorkoutPlanRevisions.ToListAsync());
    }
}
