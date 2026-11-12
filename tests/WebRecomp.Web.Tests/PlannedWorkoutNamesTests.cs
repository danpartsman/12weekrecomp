using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine30;

namespace WebRecomp.Web.Tests;

public sealed class PlannedWorkoutNamesTests
{
    [Fact]
    public void SavedRecoveryDayKeepsItsNameWithoutAnExerciseRuntime()
    {
        var plan = new PlannedWorkout { WorkoutTypeCode = "REST" };
        Assert.Equal("Rest / Recovery", PlannedWorkoutNames.Resolve(plan));
        Assert.NotEqual("No workout planned", PlannedWorkoutNames.Resolve(plan));
    }

    [Fact]
    public void SavedWorkoutIdentityTakesPrecedenceOverGenericEngineLabels()
    {
        var plan = new PlannedWorkout { WorkoutTypeCode = "MAJOR_PUSH" };
        Assert.Equal("My upper chest session", PlannedWorkoutNames.Resolve(plan, "My upper chest session"));
        plan.SourceCode = "WORKOUT_TARGET"; plan.Notes = "Shoulder-friendly push";
        Assert.Equal("Shoulder-friendly push", PlannedWorkoutNames.Resolve(plan, "Old template"));
    }

    [Fact]
    public async Task CurrentWeekRolloverCopiesSavedPlanAndPreservesEdits()
    {
        using var db = new RecompDbContext(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var client = new Client { ClientId=Guid.NewGuid(), ClientName="Client", TimeZone="UTC" };
        var today = AppUserContextService.GetClientToday(client);
        var week = PlanningWeek.Start(today);
        var config = Guid.NewGuid();
        var program = new ProgramRecord { ProgramId=Guid.NewGuid(), ClientId=client.ClientId, ConfigVersionId=config,
            StartDate=week.AddDays(-21), SelectedPlanCode="PLAN_3", GoalCode="RECOMP" };
        db.ConfigVersions.Add(new() { ConfigVersionId=config, VersionCode=Engine30WorkoutUiCatalogService.ConfigVersionCode, StatusCode="ACTIVE" });
        db.WorkoutTypeDefinitions.Add(new() { WorkoutTypeDefinitionId=Guid.NewGuid(), ConfigVersionId=config,
            WorkoutTypeCode="REST", DisplayName="Rest / Recovery", WorkoutCategoryCode="REST", WeeklyRoleCode="RECOVERY",
            FuelPriorityCode="LOW", SchedulePrincipleCode="RECOVERY", IsActive=true });
        db.PlannedWorkouts.Add(new() { PlannedWorkoutId=Guid.NewGuid(), ClientId=client.ClientId, ProgramId=program.ProgramId,
            PlannedDate=today.AddDays(-7), WorkoutTypeCode="REST" });
        await db.SaveChangesAsync();
        var service = new WeeklyPlanService(db, new Engine30WorkoutUiCatalogService(db));
        Assert.True(await service.EnsureCurrentWeekAsync(client,program,today));
        var current = await db.PlannedWorkouts.SingleAsync(p => p.PlannedDate==today);
        current.SourceCode="WORKOUT_TARGET"; current.Notes="My saved recovery day";
        await db.SaveChangesAsync();
        Assert.False(await service.EnsureCurrentWeekAsync(client,program,today));
        var names = await PlannedWorkoutNames.LoadAsync(db,client.ClientId,[current],default);
        Assert.Equal("My saved recovery day",names[today]);
    }
}
