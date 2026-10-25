using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Services.Coaching;

/// <summary>Read-only saved entries and shared engine targets. Never persists prescriptions.</summary>
public sealed class CoachWeeklySummaryService(RecompDbContext db, CoachCenterService coaches, ICoachSummaryPrescriptionSource? prescriptions = null)
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
        var reference = requestedWeek ?? today;
        if (reference < new DateOnly(1900, 1, 8) || reference > today)
            throw new ArgumentOutOfRangeException(nameof(requestedWeek), "Choose a reference date from January 8, 1900 through today.");
        var monday = reference.AddDays(-7);
        var sunday = reference.AddDays(1);
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
        var targets = prescriptions is null ? new CoachSummaryTargets(new Dictionary<DateOnly, CoachSummaryPrescription>())
            : await prescriptions.LoadAsync(coachUserId, client, reference, ct);
        var days = Enumerable.Range(0, 9).Select(offset =>
        {
            var d = monday.AddDays(offset);
            var readings = scales.Where(x => x.LocalMeasurementDate == d)
                .OrderByDescending(x => x.MeasurementAtUtc).ThenByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.MeasurementId).ToArray();
            var plan = plans.Where(x => x.PlannedDate == d).OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.PlannedWorkoutId).FirstOrDefault();
            var planName = plan is null ? null : plan.WorkoutTemplateId is Guid id && templates.TryGetValue(id, out var name)
                ? name : plan.SystemWorkoutCode ?? plan.WorkoutTypeCode;
            return new CoachSummaryDay(d, readings.FirstOrDefault(), readings.Length,
                checkins.Where(x => x.CheckinDate == d).OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.CheckinId).FirstOrDefault(),
                nutrition.Where(x => x.NutritionDate == d).OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.NutritionDayId).FirstOrDefault(),
                planName, sessions.Where(x => x.WorkoutDate == d).Select(x => new CoachSummaryWorkout(x,
                    exercises.Where(e => e.WorkoutSessionId == x.WorkoutSessionId).OrderBy(e => e.ExerciseOrder).ToArray())).ToArray()) { Prescription = targets.Days.GetValueOrDefault(d) };
        }).ToArray();
        return new() { ClientId = clientId, ClientName = client.ClientName, Today = today, ReferenceDate = reference, Days = days, PrescriptionNotice = targets.Notice };
    }
}
