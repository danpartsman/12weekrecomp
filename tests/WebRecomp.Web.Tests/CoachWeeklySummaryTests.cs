using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Controllers;
using WebRecomp.Web.ViewModels;
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
    public async Task RollingWindowIncludesLatestScaleAllWorkoutsAndTwoPrescriptionDays()
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
        var page = await service.BuildAsync(coach, client, monday.AddDays(7));
        Assert.NotNull(page); Assert.Equal(monday, page.WeekStart); Assert.Equal(9, page.Days.Count);
        Assert.Equal(DayOfWeek.Monday, page.Days[0].Date.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, page.Days[6].Date.DayOfWeek);
        Assert.Equal(monday.AddDays(7), page.Days[7].Date); Assert.Equal(monday.AddDays(8), page.Days[8].Date);
        Assert.Equal(211m, page.Days[0].Scale!.WeightLb); Assert.Equal(2, page.Days[0].ScaleCount);
        Assert.Equal(2, page.CompletedWorkouts); Assert.Equal(50m, page.TrainingMinutes);
        Assert.Equal(1, page.NutritionDays); Assert.Equal(0m, page.Days[0].Nutrition!.ActualProteinG);
        Assert.Null(page.Days[1].Nutrition); Assert.Null(page.Days[1].Scale);
        Assert.Equal("Felt better", BodyCheckinCodec.Read(page.Days[0].Checkin!.Notes).Note);
        Assert.All(db.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }


    private static CoachWeeklySummaryViewModel Sample()
    {
        var reference = new DateOnly(2026, 10, 24);
        return new() {
            ClientId = Guid.NewGuid(), ClientName = "Boundary sample", Today = reference, ReferenceDate = reference,
            Days = Enumerable.Range(0, 9).Select(i => new WebRecomp.Web.ViewModels.CoachSummaryDay(reference.AddDays(i-7),
                new Measurement { WeightLb = i < 3 ? 200 : i < 7 ? 198 : 999, BodyFatPct = i < 3 ? .30m : i < 7 ? .29m : .90m,
                    SkeletalMuscleMassLb = i < 3 ? 90 : i < 7 ? 90.5m : 999 }, 1, null,
                new NutritionDay { ActualCalories = i < 7 ? 1000 : 99999, ActualProteinG = 80, ActualCarbsG = 100, ActualFatG = 40 },
                null, new[] { new WebRecomp.Web.ViewModels.CoachSummaryWorkout(new WorkoutSession { WorkoutTypeCode="CARDIO", DurationMin=20 }, Array.Empty<DetailedWorkoutExerciseRecord>()) })
                { Prescription = new(reference.AddDays(i-7), 100+i, 120+2*i, 50, 1000+100*i, "Cardio", 20, 4, "Test target") }).ToArray()
        };
    }
    [Fact]
    public void TotalsUseOnlySevenCompletedDaysAndSumTheirIndividualTargets()
    {
        var page = Sample(); var calories = page.Gauges[0]; var protein = page.Gauges[1];
        Assert.Equal(7000m, calories.Actual); Assert.Equal(9100m, calories.Target);
        Assert.Equal(560m, protein.Actual); Assert.Equal(721m, protein.Target);
        Assert.All(page.Gauges, x => { Assert.True(x.Complete); Assert.Equal(7, x.LoggedDays); Assert.Equal(7, x.TargetDays); });
        Assert.Equal(7, page.NutritionDays); Assert.Equal(7, page.CompletedWorkouts); Assert.Equal(140m, page.TrainingMinutes);
        Assert.Equal(page.ReferenceDate.AddDays(-7), page.WeekStart); Assert.Equal(page.ReferenceDate.AddDays(-1), page.WeekEnd);
    }
    [Fact]
    public void MissingIntakeIsUnknownAndMissingTargetsPreventAFalseWeeklyComparison()
    {
        var page = Sample(); page.Days[0].Nutrition!.ActualCalories = null;
        Assert.Equal(6, page.Gauges[0].LoggedDays); Assert.Equal(6000m, page.Gauges[0].Actual); Assert.False(page.Gauges[0].Complete);
        page.Days[0].Nutrition!.ActualCalories = 0;
        Assert.Equal(7, page.Gauges[0].LoggedDays); Assert.True(page.Gauges[0].Complete);
        var days = page.Days.Select((d,i) => i == 0 ? d with { Prescription = null } : d).ToArray();
        var incomplete = new CoachWeeklySummaryViewModel { ClientId=page.ClientId, ClientName=page.ClientName,
            Today=page.Today, ReferenceDate=page.ReferenceDate, Days=days };
        Assert.Null(incomplete.Gauges[0].Target); Assert.Null(incomplete.Gauges[0].Percent); Assert.Equal(6, incomplete.Gauges[0].TargetDays);
    }
    [Fact]
    public void BodyChangesUseFirstAndLastThreeDaysWithoutTodayOrTomorrow()
    {
        var page = Sample(); Assert.Equal(-2.58m, page.FatChange.Change); Assert.Equal(.5m, page.MuscleChange.Change);
        Assert.Equal(3, page.FatChange.EarlierCount); Assert.Equal(3, page.FatChange.RecentCount);
        foreach(var d in page.Days.Take(3)) { d.Scale!.SkeletalMuscleMassLb = null; d.Scale.MuscleMassLb=100; }
        foreach(var d in page.Days.Skip(4).Take(3)) d.Scale!.MuscleMassLb=101;
        Assert.Equal("Muscle mass", page.MuscleChange.Label); Assert.Equal(1m, page.MuscleChange.Change);
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
