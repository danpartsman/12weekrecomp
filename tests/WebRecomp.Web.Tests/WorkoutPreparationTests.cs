using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WebRecomp.Web.Controllers;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Tests;
public sealed class WorkoutPreparationTests
{
    [Fact]
    public async Task SavingNamedBuilderWorkoutCreatesTemplateWithoutStartingSession()
    {
        using var db = new RecompDbContext(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new UserAccount { UserId=Guid.NewGuid(), ExternalSubject="builder-owner" };
        var client = new Client { ClientId=Guid.NewGuid(), ClientName="Builder owner" };
        var config = Guid.NewGuid();
        var program = new ProgramRecord {ProgramId=Guid.NewGuid(), ClientId=client.ClientId, ConfigVersionId=config, SelectedPlanCode="RECOMP",GoalCode="RECOMP",DetailedWorkoutEntryEnabled=true};
        var exercise = new ExerciseCatalogItemRecord {ExerciseCatalogItemId=Guid.NewGuid(),Name="Bench press",SourceCode="TEST",SourceExternalId="bench",MeasurementType="REPS"};
        db.AddRange(user,client,program,exercise,new UserClientAccess {UserClientAccessId=Guid.NewGuid(),UserId=user.UserId,ClientId=client.ClientId,AccessRoleCode="OWNER"},new WorkoutTypeDefinition {WorkoutTypeDefinitionId=Guid.NewGuid(),ConfigVersionId=config,WorkoutTypeCode="MAJOR_MIXED",DisplayName="Mixed",WorkoutCategoryCode="STRENGTH",WeeklyRoleCode="HARD",FuelPriorityCode="MIXED",SchedulePrincipleCode="STRENGTH",IsSelectable=true,IsActive=true});
        await db.SaveChangesAsync();
        var http = new DefaultHttpContext {User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim("oid","builder-owner")},"test"))};
        var controller = new DetailedWorkoutController(db,new AppUserContextService(db,new ConfigurationBuilder().Build()),null!,null!,null!,null!,null!,null!,NullLogger<DetailedWorkoutController>.Instance) {ControllerContext=new(){HttpContext=http},TempData=new TempDataDictionary(http,new MemoryTempData())};
        var input = new DetailedWorkoutEditorViewModel {Date=new DateOnly(2026,10,24),TemplateOnlyMode=true,WorkoutName="My new workout",Exercises=[new(){ExerciseId=exercise.ExerciseCatalogItemId,ExerciseName=exercise.Name,Sets=3,Reps=10,SetReps=[10,10,10]}]};
        var result=Assert.IsType<RedirectToActionResult>(await controller.Save(input,true,false,false,0,default));
        Assert.Equal("saved",result.RouteValues!["mode"]);
        Assert.Equal("My new workout",(await db.WorkoutTemplates.SingleAsync()).TemplateName);
        Assert.Empty(await db.WorkoutSessions.ToListAsync());
    }
    private sealed class MemoryTempData : ITempDataProvider {public IDictionary<string,object> LoadTempData(HttpContext context)=>new Dictionary<string,object>();public void SaveTempData(HttpContext context,IDictionary<string,object> values){} }
}
