using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Services.Coaching;

/// <summary>Read-only saved entries. Does not calculate or persist new prescriptions.</summary>
public sealed class CoachWeeklySummaryService(RecompDbContext db, CoachCenterService coaches)
{
    public async Task<CoachWeeklySummaryViewModel?> BuildAsync(Guid coachUserId, Guid clientId,
        DateOnly? requestedWeek, CancellationToken ct = default)
    {
        // Check the relationship before reading any client health records.
        if (!await coaches.CanAccessAsync(coachUserId, clientId, ct)) return null;
        var client = await db.Clients.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ClientId == clientId && x.ClientStatusCode == "ACTIVE", ct);
        if (client is null) return null;
        var today = AppUserContextService.GetClientToday(client);
        var date = requestedWeek ?? today;
        if (date < new DateOnly(1900, 1, 1) || date > today.AddDays(84))
            throw new ArgumentOutOfRangeException(nameof(requestedWeek), "Choose a week from 1900 through the next 12 weeks.");
        var monday = Engine30DashboardProjectionService.WeekStart(date);
        var sunday = monday.AddDays(6);
        // Client-scoped dates include scale readings with no ProgramId and program transitions.
        var scales = await db.Measurements.AsNoTracking().Where(x => x.ClientId == clientId && !x.IsDeleted
            && x.LocalMeasurementDate >= monday && x.LocalMeasurementDate <= sunday).ToListAsync(ct);
        var checkins = await db.DailyCheckins.AsNoTracking().Where(x => x.ClientId == clientId
            && x.CheckinDate >= monday && x.CheckinDate <= sunday).ToListAsync(ct);
        var nutrition = await db.NutritionDays.AsNoTracking().Where(x => x.ClientId == clientId
            && x.NutritionDate >= monday && x.NutritionDate <= sunday).ToListAsync(ct);
        var sessions = await db.WorkoutSessions.AsNoTracking().Where(x => x.ClientId == clientId && x.CompletedFlag
            && x.WorkoutDate >= monday && x.WorkoutDate <= sunday).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
        var ids = sessions.Select(x => x.WorkoutSessionId).ToArray();
        var exercises = await db.DetailedWorkoutExercises.AsNoTracking().Include(x => x.Sets)
            .Where(x => ids.Contains(x.WorkoutSessionId)).ToListAsync(ct);
        var plans = await db.PlannedWorkouts.AsNoTracking().Where(x => x.ClientId == clientId
            && x.PlannedDate >= monday && x.PlannedDate <= sunday).ToListAsync(ct);
        var templateIds = plans.Where(x => x.WorkoutTemplateId.HasValue).Select(x => x.WorkoutTemplateId!.Value).ToArray();
        var templates = await db.WorkoutTemplates.AsNoTracking().Where(x => x.ClientId == clientId
            && templateIds.Contains(x.WorkoutTemplateId)).ToDictionaryAsync(x => x.WorkoutTemplateId, x => x.TemplateName, ct);
        var days = Enumerable.Range(0, 7).Select(offset =>
        {
            var d = monday.AddDays(offset);
            var readings = scales.Where(x => x.LocalMeasurementDate == d)
                .OrderByDescending(x => x.MeasurementAtUtc).ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.MeasurementId).ToArray();
            var plan = plans.Where(x => x.PlannedDate == d).OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.PlannedWorkoutId).FirstOrDefault();
            var planName = plan is null ? null : plan.WorkoutTemplateId is Guid id && templates.TryGetValue(id, out var name)
                ? name : plan.SystemWorkoutCode ?? plan.WorkoutTypeCode;
            return new CoachSummaryDay(d, readings.FirstOrDefault(), readings.Length,
                checkins.Where(x => x.CheckinDate == d).OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.CheckinId).FirstOrDefault(),
                nutrition.Where(x => x.NutritionDate == d).OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.NutritionDayId).FirstOrDefault(),
                planName, sessions.Where(x => x.WorkoutDate == d).Select(x => new CoachSummaryWorkout(x,
                    exercises.Where(e => e.WorkoutSessionId == x.WorkoutSessionId).OrderBy(e => e.ExerciseOrder).ToArray())).ToArray());
        }).ToArray();
        return new() { ClientId = clientId, ClientName = client.ClientName, Today = today, WeekStart = monday, Days = days };
    }
}
