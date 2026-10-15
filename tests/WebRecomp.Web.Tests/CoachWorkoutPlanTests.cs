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
public sealed class CoachWorkoutPlanTests
{
    private static RecompDbContext Database() => new(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static AppClientContext Context(RecompDbContext db)
    {
        var user=new UserAccount {UserId=Guid.NewGuid(),ExternalSubject="coach"};
        var client=new Client {ClientId=Guid.NewGuid(),ClientName="Client"};
        var today=AppUserContextService.GetClientToday(client);
        var program=new ProgramRecord {ProgramId=Guid.NewGuid(),ClientId=client.ClientId,SelectedPlanCode="RECOMP",GoalCode="RECOMP",StartDate=today.AddDays(-30)};
        db.AddRange(user,client,program,new CoachClientRelationship {CoachClientRelationshipId=Guid.NewGuid(),CoachUserId=user.UserId,ClientId=client.ClientId,StatusCode="ACTIVE"});db.SaveChanges();
        return new(user,client,program);
    }
    private static CoachWorkoutPlanService Service(RecompDbContext db) => new(db,new Engine30WorkoutUiCatalogService(db),new CoachClientPlanService(db,null!,null!,null!,null!,null!),null!,null!);
    private static PlannedWorkout Plan(AppClientContext c,DateOnly date) => new() {PlannedWorkoutId=Guid.NewGuid(),ClientId=c.Client!.ClientId,ProgramId=c.Program!.ProgramId,PlannedDate=date,WorkoutTypeCode="REST",PlannedDurationMin=0,SourceCode="COACH"};
    [Fact]
    public async Task UnassignedWorkoutEndpointIsHidden()
    {
        using var db=Database();var user=new UserAccount {UserId=Guid.NewGuid(),ExternalSubject="unassigned"};db.Add(user);await db.SaveChangesAsync();
        var http=new DefaultHttpContext {User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim("oid","unassigned")},"test"))};
        var controller=new CoachWorkoutController(db,new AppUserContextService(db,new ConfigurationBuilder().Build()),new CoachCenterService(db),null!) {ControllerContext=new ControllerContext {HttpContext=http}};
        Assert.IsType<NotFoundResult>(await controller.Index(Guid.NewGuid(),DateOnly.FromDateTime(DateTime.UtcNow),null,default));
        Assert.IsType<NotFoundResult>(await controller.Save(Guid.NewGuid(),new(),default));
    }
    [Theory]
    [InlineData("10",3,new[]{10,10,10})]
    [InlineData("10, 8, 6",3,new[]{10,8,6})]
    public void PerSetRepsRemainExplicit(string text,int sets,int[] expected) => Assert.Equal(expected,CoachWorkoutPlanService.ParseReps(text,sets));
    [Theory]
    [InlineData("6-10",3)] [InlineData("10,8",3)] [InlineData("0",3)] [InlineData("10",0)]
    public void InvalidRepsCannotBecomeInventedWork(string text,int sets) => Assert.Throws<ArgumentException>(()=>CoachWorkoutPlanService.ParseReps(text,sets));
    [Fact]
    public async Task RevocationAndStartedSessionsBlockWrites()
    {
        using var db=Database();var c=Context(db);var date=AppUserContextService.GetClientToday(c.Client!).AddDays(1);var candidate=Plan(c,date);
        var input=new CoachWorkoutInput {Date=date,Choice="TYPE:REST",Name="Rest",Reason="Recovery"};
        var link=await db.CoachClientRelationships.SingleAsync();link.StatusCode="REVOKED";link.RevokedAtUtc=DateTime.UtcNow;await db.SaveChangesAsync();
        Assert.False(await Service(db).SaveAsync(c,input,candidate,"[]",new(),default));
        link=await db.CoachClientRelationships.SingleAsync();link.StatusCode="ACTIVE";link.RevokedAtUtc=null;
        db.WorkoutSessions.Add(new() {WorkoutSessionId=Guid.NewGuid(),ClientId=c.Client!.ClientId,ProgramId=c.Program!.ProgramId,WorkoutDate=date,WorkoutTypeCode="REST",CompletedFlag=false});await db.SaveChangesAsync();
        Assert.False(await Service(db).SaveAsync(c,input,candidate,"[]",new(),default));
        Assert.Empty(await db.CoachWorkoutPlanRevisions.ToListAsync());Assert.Empty(await db.PlannedWorkouts.ToListAsync());
    }
    [Fact]
    public async Task StaleWeekCannotOverwriteAnExistingPlan()
    {
        using var db=Database();var c=Context(db);var date=AppUserContextService.GetClientToday(c.Client!).AddDays(1);var existing=Plan(c,date);existing.SourceCode="CLIENT";db.Add(existing);await db.SaveChangesAsync();
        var input=new CoachWorkoutInput {Date=date,Choice="TYPE:REST",Name="Rest",Reason="Recovery"};
        Assert.False(await Service(db).SaveAsync(c,input,Plan(c,date),"[]",new(),default));
        Assert.Equal("CLIENT",(await db.PlannedWorkouts.SingleAsync()).SourceCode);Assert.Empty(await db.CoachWorkoutPlanRevisions.ToListAsync());
    }
    [Fact]
    public async Task ClientScopedTemplateCannotBeSelectedByAnotherClient()
    {
        using var db=Database();var c=Context(db);var config=Guid.NewGuid();db.ConfigVersions.Add(new() {ConfigVersionId=config,VersionCode="CFG-3000.0",StatusCode="PUBLISHED"});
        db.WorkoutTypeDefinitions.Add(new() {WorkoutTypeDefinitionId=Guid.NewGuid(),ConfigVersionId=config,WorkoutTypeCode="REST",DisplayName="Rest",WorkoutCategoryCode="REST",IsActive=true});
        var id=Guid.NewGuid();db.WorkoutTemplates.Add(new() {WorkoutTemplateId=id,ClientId=Guid.NewGuid(),TemplateName="Private routine",WorkoutTypeCode="REST"});await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(()=>Service(db).SourceAsync(c,AppUserContextService.GetClientToday(c.Client!),"TPL:"+id,default));
    }
    [Fact]
    public async Task CustomRoutineIsCopiedIntoClientPlanWithAnImmutableAudit()
    {
        using var db=Database();var c=Context(db);var date=AppUserContextService.GetClientToday(c.Client!).AddDays(1);
        var exerciseId=Guid.NewGuid();var originalId=Guid.NewGuid();
        db.ExerciseCatalogItems.Add(new() {ExerciseCatalogItemId=exerciseId,SourceCode="LIBRARY",SourceExternalId="one",Name="Cable row",MeasurementType="reps",IsActive=true});
        db.WorkoutTemplates.Add(new() {WorkoutTemplateId=originalId,ClientId=c.Client!.ClientId,TemplateName="Original routine",WorkoutTypeCode="FULL_BODY_STRENGTH",DefaultDurationMin=30});await db.SaveChangesAsync();
        var candidate=Plan(c,date);candidate.WorkoutTypeCode="FULL_BODY_STRENGTH";candidate.WorkoutTemplateId=originalId;
        var input=new CoachWorkoutInput {Date=date,Choice="TPL:"+originalId,Name="Adjusted routine",Minutes=40,Rpe=7,Reason="More pulling work",Customize=true,
            Exercises=[new() {ExerciseId=exerciseId,Name="Untrusted submitted name",Sets=2,Reps="10, 8",WeightLb=25}]};
        Assert.True(await Service(db).SaveAsync(c,input,candidate,"[]",new(),default));
        var plan=await db.PlannedWorkouts.SingleAsync();Assert.Equal("COACH",plan.SourceCode);Assert.NotEqual(originalId,plan.WorkoutTemplateId);
        var clone=await db.WorkoutTemplates.Include(x=>x.Exercises).SingleAsync(x=>x.WorkoutTemplateId==plan.WorkoutTemplateId);
        Assert.Equal(c.Client.ClientId,clone.ClientId);Assert.Equal("Adjusted routine",clone.TemplateName);
        Assert.Equal("Cable row",Assert.Single(clone.Exercises).ExerciseNameSnapshot);Assert.Equal("[10,8]",clone.Exercises[0].SetRepsJson);Assert.Equal("LB",clone.Exercises[0].LoadUnitCode);
        Assert.Equal("Original routine",(await db.WorkoutTemplates.SingleAsync(x=>x.WorkoutTemplateId==originalId)).TemplateName);
        var audit=await db.CoachWorkoutPlanRevisions.SingleAsync();Assert.Equal(c.User.UserId,audit.CoachUserId);Assert.Equal("More pulling work",audit.Reason);Assert.Contains("Adjusted routine",audit.AfterJson);
    }
    [Fact]
    public void DatesBeforeTheProgramAndCompletedDatesAreLocked()
    {
        var today=new DateOnly(2026,10,15);
        Assert.False(CoachWorkoutPlanService.Editable(today.AddDays(-1),today,today.AddDays(-30),false));
        Assert.False(CoachWorkoutPlanService.Editable(today,today,today.AddDays(1),false));
        Assert.False(CoachWorkoutPlanService.Editable(today,today,today,true));
        Assert.False(CoachWorkoutPlanService.Editable(today.AddDays(85),today,today,false));
        Assert.True(CoachWorkoutPlanService.Editable(today.AddDays(1),today,today,false));
    }

}
