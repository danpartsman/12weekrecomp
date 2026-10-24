using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Controllers;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.WorkoutTargeting;
namespace WebRecomp.Web.Tests;

public sealed class CoachWeeklySummaryTests
{
    private static RecompDbContext Database() => new(new DbContextOptionsBuilder<RecompDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task UnassignedAndRevokedCoachesCannotReadAClientWeek()
    {
        using var db = Database();
        var coach = Guid.NewGuid(); var client = Guid.NewGuid();
        db.Clients.Add(new() { ClientId = client, ClientName = "Private client" });
        var relation = new CoachClientRelationship { CoachClientRelationshipId = Guid.NewGuid(),
            CoachUserId = coach, ClientId = client, StatusCode = "ACTIVE", RevokedAtUtc = DateTime.UtcNow };
        db.CoachClientRelationships.Add(relation);
        await db.SaveChangesAsync();
        var service = new CoachWeeklySummaryService(db, new CoachCenterService(db));
        Assert.Null(await service.BuildAsync(Guid.NewGuid(), client, null));
        Assert.Null(await service.BuildAsync(coach, client, null));
        relation.RevokedAtUtc = null; await db.SaveChangesAsync();
        Assert.NotNull(await service.BuildAsync(coach, client, null));
    }

    [Fact]
    public async Task WeekIncludesLatestScaleWithoutProgramAllActualWorkoutsAndUnknownNutrition()
    {
        using var db = Database(); var client = Guid.NewGuid(); var coach = Guid.NewGuid();
        var monday = new DateOnly(2026, 1, 5); var program = Guid.NewGuid();
        db.Clients.Add(new() { ClientId = client, ClientName = "Connected client" });
        db.CoachClientRelationships.Add(new() { CoachClientRelationshipId = Guid.NewGuid(), CoachUserId = coach,
            ClientId = client, StatusCode = "ACTIVE" });
        db.Measurements.AddRange(
            new() { MeasurementId = Guid.NewGuid(), ClientId = client, LocalMeasurementDate = monday, WeightLb = 210, MeasurementAtUtc = new(2026, 1, 5, 7, 0, 0) },
            new() { MeasurementId = Guid.NewGuid(), ClientId = client, LocalMeasurementDate = monday, WeightLb = 211, MeasurementAtUtc = new(2026, 1, 5, 8, 0, 0) },
            new() { MeasurementId = Guid.NewGuid(), ClientId = client, LocalMeasurementDate = monday, WeightLb = 999, IsDeleted = true, MeasurementAtUtc = new(2026, 1, 5, 9, 0, 0) },
            new() { MeasurementId = Guid.NewGuid(), ClientId = Guid.NewGuid(), LocalMeasurementDate = monday, WeightLb = 888 });
        db.WorkoutSessions.AddRange(
            new() { WorkoutSessionId = Guid.NewGuid(), ClientId = client, ProgramId = program, WorkoutDate = monday, WorkoutTypeCode = "LEGS", DurationMin = 30, CompletedFlag = true },
            new() { WorkoutSessionId = Guid.NewGuid(), ClientId = client, ProgramId = Guid.NewGuid(), WorkoutDate = monday, WorkoutTypeCode = "CARDIO", DurationMin = 20, CompletedFlag = true },
            new() { WorkoutSessionId = Guid.NewGuid(), ClientId = client, ProgramId = program, WorkoutDate = monday, WorkoutTypeCode = "PUSH", DurationMin = 60, CompletedFlag = false });
        db.NutritionDays.Add(new() { NutritionDayId = Guid.NewGuid(), ClientId = client, ProgramId = program, NutritionDate = monday, ActualProteinG = 0, ActualFatG = 60, ActualCalories = 1900 });
        db.DailyCheckins.Add(new() { CheckinId = Guid.NewGuid(), ClientId = client, ProgramId = program,
            CheckinDate = monday, Notes = BodyCheckinCodec.Write("Felt better", true, []) });
        await db.SaveChangesAsync();
        var service = new CoachWeeklySummaryService(db, new CoachCenterService(db));
        var page = await service.BuildAsync(coach, client, monday.AddDays(6));
        Assert.NotNull(page); Assert.Equal(monday, page.WeekStart); Assert.Equal(7, page.Days.Count);
        Assert.Equal(DayOfWeek.Monday, page.Days[0].Date.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, page.Days[6].Date.DayOfWeek);
        Assert.Equal(211m, page.Days[0].Scale!.WeightLb); Assert.Equal(2, page.Days[0].ScaleCount);
        Assert.Equal(2, page.CompletedWorkouts); Assert.Equal(50m, page.TrainingMinutes);
        Assert.Equal(1, page.NutritionDays); Assert.Equal(0m, page.Days[0].Nutrition!.ActualProteinG);
        Assert.Null(page.Days[1].Nutrition); Assert.Null(page.Days[1].Scale);
        Assert.Equal("Felt better", BodyCheckinCodec.Read(page.Days[0].Checkin!.Notes).Note);
        Assert.All(db.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    [Fact]
    public async Task CoachRoleUsesExistingPortalAuthorizationAndOrdinaryClientsAreNotElevated()
    {
        var coach = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "COACH")], "test"));
        var client = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "CLIENT")], "test"));
        var transform = new CanonicalRoleClaimsTransformation();
        Assert.True((await transform.TransformAsync(coach)).IsInRole("Coach"));
        Assert.False((await transform.TransformAsync(client)).IsInRole("Coach"));
        var authorization = Assert.Single(typeof(CoachSummaryController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Coach,Administrator", authorization.Roles);
    }
}
