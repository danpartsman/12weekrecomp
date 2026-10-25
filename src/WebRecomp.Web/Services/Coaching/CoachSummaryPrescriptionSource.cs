using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Dashboard3;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Services.Coaching;

public sealed record CoachSummaryTargets(IReadOnlyDictionary<DateOnly, CoachSummaryPrescription> Days, string? Notice = null);
public interface ICoachSummaryPrescriptionSource
{
    Task<CoachSummaryTargets> LoadAsync(Guid coachUserId, Client client, DateOnly referenceDate, CancellationToken ct);
}
public sealed class CoachSummaryPrescriptionSource(RecompDbContext db, Engine30DashboardProjectionService engine,
    ProgramPlanService plans, ILogger<CoachSummaryPrescriptionSource> logger) : ICoachSummaryPrescriptionSource
{
    public async Task<CoachSummaryTargets> LoadAsync(Guid coachUserId, Client client, DateOnly referenceDate, CancellationToken ct)
    {
        var start = referenceDate.AddDays(-7); var end = referenceDate.AddDays(1);
        var user = await db.UserAccounts.AsNoTracking().SingleAsync(x => x.UserId == coachUserId, ct);
        var programs = await db.Programs.AsNoTracking().Where(x => x.ClientId == client.ClientId)
            .OrderByDescending(x => x.StartDate).ToListAsync(ct);
        var intake = await db.NutritionDays.AsNoTracking().Where(x => x.ClientId == client.ClientId && x.NutritionDate >= start && x.NutritionDate <= end).ToListAsync(ct);
        var records = await db.AnalysisRuns.AsNoTracking().Where(x => x.ClientId == client.ClientId && x.ActionDate >= start
            && x.ActionDate <= end && x.EngineVersion.StartsWith("RECOMP_SCORE_V")).OrderBy(x => x.StartedAtUtc).ThenBy(x => x.AnalysisRunId).ToListAsync(ct);
        var saved = new Dictionary<(Guid, DateOnly), RecompDayEvidence>();
        foreach (var record in records)
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<RecompScoreSnapshot>(record.DiagnosticJson ?? "null");
                var captured = snapshot?.TargetCapture is { } target && target.Date == record.ActionDate ? target
                    : snapshot?.Days?.FirstOrDefault(x => x.Date == record.ActionDate);
                if (captured is not null) saved.TryAdd((record.ProgramId, record.ActionDate), captured);
            }
            catch (JsonException) { /* Older invalid snapshots cannot supply targets. */ }
        }
        var result = new Dictionary<DateOnly, CoachSummaryPrescription>();
        var cache = new Dictionary<(Guid, DateOnly, string), IReadOnlyDictionary<DateOnly, Engine30DashboardDayProjection>>();
        var notice = false;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var recordedProgram = intake.Where(x => x.NutritionDate == date).OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefault()?.ProgramId;
            var program = programs.FirstOrDefault(x => x.ProgramId == recordedProgram)
                ?? programs.FirstOrDefault(x => x.StartDate <= date && (date < referenceDate || x.ProgramStatusCode == "ACTIVE"));
            if (program is null) { notice = true; continue; }
            if (date < referenceDate && saved.TryGetValue((program.ProgramId, date), out var captured))
            {
                result[date] = new(date, captured.ProteinTarget, captured.CarbsTarget, captured.FatTarget,
                    captured.EnergyTarget, null, null, null, "Captured same-day prescription");
                continue;
            }
            try
            {
                var planCode = await plans.GetPlanCodeForDateAsync(program, date < program.StartDate ? program.StartDate : date, ct);
                var key = (program.ProgramId, Engine30DashboardProjectionService.WeekStart(date), planCode);
                if (!cache.TryGetValue(key, out var week))
                {
                    week = await engine.BuildPlanningWeekAsync(new(user, client, program), key.Item2, planCode, ct);
                    cache[key] = week;
                }
                if (week.TryGetValue(date, out var target)) result[date] = From(target, date < referenceDate ? "Reconstructed historical prescription" : "Current prescription");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                logger.LogWarning(ex, "Coach summary prescription unavailable for {Date}", date); notice = true;
            }
        }
        // Today's and tomorrow's targets must match the client's current dashboard exactly.
        if (referenceDate == AppUserContextService.GetClientToday(client))
        {
            var active = programs.FirstOrDefault(x => x.ProgramStatusCode == "ACTIVE" && x.StartDate <= referenceDate);
            if (active is not null)
            {
                try
                {
                    var pair = await engine.BuildTodayTomorrowAsync(new(user, client, active), referenceDate, ct);
                    result[referenceDate] = From(pair.Today, "Current prescription");
                    result[end] = From(pair.Tomorrow, "Current prescription");
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                { logger.LogWarning(ex, "Coach summary current prescription unavailable"); notice = true; }
            }
        }
        return new(result, notice ? "Some prescriptions are unavailable. Complete the client's profile and active program to calculate missing targets." : null);
    }
    private static CoachSummaryPrescription From(Engine30DashboardDayProjection x, string source) =>
        new(x.Date, x.ProteinG, x.RecommendedCarbsG, x.FatG, x.PrescribedEnergyKcal, x.WorkoutDisplayName, x.PlannedDurationMinutes, x.PlannedRpe, source);
}
