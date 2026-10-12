using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutTargeting;

namespace WebRecomp.Web.Tests;

public sealed class WorkoutTargetIntegrationTests
{
    private static async Task<(RecompDbContext Db, AppClientContext Context)> SetupAsync(params RegionFeeling[] feelings)
    {
        var db=new RecompDbContext(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var client=new Client{ClientId=Guid.NewGuid(),ClientName="Synthetic test",TimeZone="America/Los_Angeles"};
        var config=new ConfigVersion{ConfigVersionId=Guid.NewGuid(),VersionCode="CFG-3000.0",StatusCode="ACTIVE"};
        var program=new ProgramRecord{ProgramId=Guid.NewGuid(),ClientId=client.ClientId,ConfigVersionId=config.ConfigVersionId,
            SelectedPlanCode="PLAN_3",GoalCode="MUSCLE_BUILD",DetailedWorkoutEntryEnabled=true};
        var user=new UserAccount{UserId=Guid.NewGuid(),ExternalSubject="synthetic"};
        db.ConfigVersions.Add(config);db.Clients.Add(client);db.Programs.Add(program);db.UserAccounts.Add(user);
        db.UserClientAccess.Add(new(){UserClientAccessId=Guid.NewGuid(),UserId=user.UserId,ClientId=client.ClientId});
        foreach(var code in new[]{"ACCESSORY","FULL_BODY_STRENGTH","MAJOR_PUSH","MAJOR_PULL","MAJOR_LEGS","REST","MOBILITY_RECOVERY"})
            db.WorkoutTypeDefinitions.Add(new(){WorkoutTypeDefinitionId=Guid.NewGuid(),ConfigVersionId=config.ConfigVersionId,
                WorkoutTypeCode=code,DisplayName=code,WorkoutCategoryCode="RESISTANCE",WeeklyRoleCode="HEAVY",FuelPriorityCode="NORMAL",
                SchedulePrincipleCode="TEST",IsActive=true,IsSelectable=true,DefaultMinutes=45});
        foreach(var (name,muscle,equipment) in new[]{("Dumbbell Chest Press","Pectorals","Dumbbell"),("Machine Leg Extension","Quadriceps","Machine"),
            ("Dumbbell Row","Latissimus dorsi","Dumbbell"),("Bodyweight Glute Bridge","Glutes","Bodyweight"),("Wall Slide","Deltoids","Bodyweight")})
        {
            var exercise=new ExerciseCatalogItemRecord{ExerciseCatalogItemId=Guid.NewGuid(),SourceCode="TEST",SourceExternalId=name,Name=name,MeasurementType="reps",IsActive=true};
            exercise.MuscleTargets.Add(new(){ExerciseMuscleTargetId=Guid.NewGuid(),ExerciseCatalogItemId=exercise.ExerciseCatalogItemId,ExternalId=muscle,MuscleSlug=muscle,MuscleName=muscle,RoleCode="primary"});
            exercise.Equipment.Add(new(){ExerciseEquipmentRequirementId=Guid.NewGuid(),ExerciseCatalogItemId=exercise.ExerciseCatalogItemId,ExternalId=equipment,EquipmentName=equipment,EquipmentType=equipment});
            db.ExerciseCatalogItems.Add(exercise);
        }
        db.DailyCheckins.Add(new(){CheckinId=Guid.NewGuid(),ClientId=client.ClientId,ProgramId=program.ProgramId,
            CheckinDate=AppUserContextService.GetClientToday(client),Notes=BodyCheckinCodec.Write(null,true,feelings)});
        await db.SaveChangesAsync();
        return (db,new(user,client,program));
    }
    [Fact] public async Task ShoulderPainRemovesPressingButKeepsLegRecommendation()
    {
        var (db,context)=await SetupAsync(new RegionFeeling(){Region="shoulders",Side="right",Pain=3});using(db)
        {
            var service=new WorkoutTargetService(db,new(db),null!);
            var result=await service.BuildAsync(context,new(){Activity="targeted",Targets=["chest","quads"],Effort="hard"},default);
            Assert.NotEmpty(result.Choices);
            Assert.All(result.Choices.SelectMany(x=>x.Exercises),e=>Assert.DoesNotContain("Chest Press",e.Name));
            Assert.Contains(result.Choices.SelectMany(x=>x.Exercises),e=>e.Name.Contains("Leg Extension"));
            Assert.True(result.CanStart);
        }
    }
    [Fact] public async Task EmptyHomeEquipmentCannotSelectMachineOrDumbbellExercises()
    {
        var (db,context)=await SetupAsync();using(db)
        {
            var result=await new WorkoutTargetService(db,new(db),null!).BuildAsync(context,new(){Activity="targeted",Targets=["quads","glutes"],Location="home",Equipment=[]},default);
            Assert.All(result.Choices.SelectMany(x=>x.Exercises),e=>Assert.DoesNotContain("Machine",e.Name));
            Assert.All(result.Choices.SelectMany(x=>x.Exercises),e=>Assert.DoesNotContain("Dumbbell",e.Name));
            Assert.Contains(result.Choices.SelectMany(x=>x.Exercises),e=>e.Name.Contains("Glute Bridge"));
        }
    }
    [Fact] public async Task RecommendationDoesNotCreateOrCompleteWorkoutOrModifyPlan()
    {
        var (db,context)=await SetupAsync();using(db)
        {
            var result=await new WorkoutTargetService(db,new(db),null!).BuildAsync(context,new(){Activity="targeted",Targets=["chest"]},default);
            Assert.NotEmpty(result.Choices);Assert.Empty(await db.WorkoutSessions.ToListAsync());Assert.Empty(await db.PlannedWorkouts.ToListAsync());
            Assert.All(result.Choices,c=>Assert.Equal(64,c.Revision.Length));
        }
    }
    [Fact] public async Task MissingBodyCheckAllowsPreviewButPreventsStart()
    {
        var (db,context)=await SetupAsync();using(db)
        {
            var check=await db.DailyCheckins.SingleAsync();check.Notes=null;await db.SaveChangesAsync();
            var result=await new WorkoutTargetService(db,new(db),null!).BuildAsync(context,new(){Activity="targeted",Targets=["chest"]},default);
            Assert.False(result.CanStart);Assert.NotEmpty(result.Choices);
        }
    }
    [Fact] public async Task RecentHardSetsReduceVolumeAndEffortForThatMuscle()
    {
        var (db,context)=await SetupAsync();using(db)
        {
            var exercise=await db.ExerciseCatalogItems.FirstAsync(x=>x.Name.Contains("Chest Press"));
            var session=new WorkoutSession{WorkoutSessionId=Guid.NewGuid(),ClientId=context.Client!.ClientId,ProgramId=context.Program!.ProgramId,
                WorkoutDate=AppUserContextService.GetClientToday(context.Client).AddDays(-1),WorkoutTypeCode="MAJOR_PUSH",DurationMin=45,SessionRpe=8,CompletedFlag=true};
            db.WorkoutSessions.Add(session);
            var row=new DetailedWorkoutExerciseRecord{DetailedWorkoutExerciseId=Guid.NewGuid(),WorkoutSessionId=session.WorkoutSessionId,
                ExerciseCatalogItemId=exercise.ExerciseCatalogItemId,ExerciseNameSnapshot=exercise.Name};
            for(short i=1;i<=4;i++)row.Sets.Add(new(){DetailedWorkoutSetId=Guid.NewGuid(),DetailedWorkoutExerciseId=row.DetailedWorkoutExerciseId,SetOrder=i,Rpe=8,Reps=10});
            db.DetailedWorkoutExercises.Add(row);await db.SaveChangesAsync();
            var result=await new WorkoutTargetService(db,new(db),null!).BuildAsync(context,new(){Activity="targeted",Targets=["chest"],Effort="hard"},default);
            Assert.All(result.Choices.SelectMany(x=>x.Exercises),e=>{Assert.True(e.Sets<=2);Assert.True(e.Rpe<=6);});
            Assert.Equal("Reduce / reassess",result.Readiness.Single(x=>x.Region=="chest").Status);
        }
    }
    [Fact] public async Task DailyTotalsSaveDoesNotClearScaleOrRegionalCheckin()
    {
        var (db,context)=await SetupAsync(new RegionFeeling(){Region="shoulders",Pain=2});using(db)
        {
            var date=AppUserContextService.GetClientToday(context.Client!);
            db.Measurements.Add(new(){MeasurementId=Guid.NewGuid(),ClientId=context.Client!.ClientId,ProgramId=context.Program!.ProgramId,
                LocalMeasurementDate=date,WeightLb=210,MeasurementSourceCode="MANUAL"});await db.SaveChangesAsync();
            var before=(await db.DailyCheckins.SingleAsync()).Notes;
            var http=new Microsoft.AspNetCore.Http.DefaultHttpContext();
            http.User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new("sub","synthetic")],"test"));
            http.Request.Headers["X-Requested-With"]="XMLHttpRequest";
            var users=new AppUserContextService(db,new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            var controller=new WebRecomp.Web.Controllers.DailyEntryController(db,users,new(db),new(db),null!);
            controller.ControllerContext=new(){HttpContext=http};
            await controller.Index(new(){Date=date,Calories=2100,ProteinG=180},false,default);
            Assert.Equal(before,(await db.DailyCheckins.SingleAsync()).Notes);
            Assert.Equal(210m,(await db.Measurements.SingleAsync()).WeightLb);
            Assert.Equal(2100m,(await db.NutritionDays.SingleAsync()).ActualCalories);
        }
    }
    [Fact] public async Task PastDailyEntryCannotOverwriteCompletedWorkout()
    {
        var (db,context)=await SetupAsync();using(db)
        {
            var date=AppUserContextService.GetClientToday(context.Client!).AddDays(-1);
            var session=new WorkoutSession{WorkoutSessionId=Guid.NewGuid(),ClientId=context.Client!.ClientId,ProgramId=context.Program!.ProgramId,
                WorkoutDate=date,WorkoutTypeCode="MAJOR_PUSH",DurationMin=45,SessionRpe=8,CompletedFlag=true};db.WorkoutSessions.Add(session);await db.SaveChangesAsync();
            var http=new Microsoft.AspNetCore.Http.DefaultHttpContext();http.User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new("sub","synthetic")],"test"));
            http.Request.Headers["X-Requested-With"]="XMLHttpRequest";
            var users=new AppUserContextService(db,new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            var controller=new WebRecomp.Web.Controllers.DailyEntryController(db,users,new(db),new(db),null!);controller.ControllerContext=new(){HttpContext=http};
            await controller.Index(new(){Date=date,Calories=2000,Workouts=[new(){WorkoutSessionId=session.WorkoutSessionId,WorkoutTypeCode="MAJOR_LEGS",DurationMin=90}]},false,default);
            Assert.Equal("MAJOR_PUSH",(await db.WorkoutSessions.SingleAsync()).WorkoutTypeCode);Assert.Equal(45m,session.DurationMin);
            await controller.Index(new(){Date=date.AddDays(1),Workouts=[new(){WorkoutSessionId=session.WorkoutSessionId,WorkoutTypeCode="MAJOR_LEGS",DurationMin=90}]},false,default);
            Assert.Equal(date,session.WorkoutDate);Assert.Equal("MAJOR_PUSH",session.WorkoutTypeCode);
        }
    }
    [Fact] public async Task PlannerSaveCannotRewritePastDayEvenWithForgedPost()
    {
        var (db,context)=await SetupAsync();using(db)
        {
            var date=AppUserContextService.GetClientToday(context.Client!).AddDays(-1);
            var plan=new PlannedWorkout{PlannedWorkoutId=Guid.NewGuid(),ClientId=context.Client!.ClientId,ProgramId=context.Program!.ProgramId,
                PlannedDate=date,WorkoutTypeCode="MAJOR_PUSH"};db.PlannedWorkouts.Add(plan);await db.SaveChangesAsync();
            var http=new Microsoft.AspNetCore.Http.DefaultHttpContext();http.User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new("sub","synthetic")],"test"));
            http.Request.Headers["X-Requested-With"]="XMLHttpRequest";
            var users=new AppUserContextService(db,new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            var controller=new WebRecomp.Web.Controllers.ScheduleController(db,users,null!,null!,null!,new(db),null!);controller.ControllerContext=new(){HttpContext=http};
            await controller.Save(new(){WeekStart=PlanningWeek.Start(date),Days=[new(){Date=date,WorkoutTypeCode="MAJOR_LEGS"}]},default);
            Assert.Equal("MAJOR_PUSH",(await db.PlannedWorkouts.SingleAsync()).WorkoutTypeCode);
        }
    }
}
