using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;

namespace WebRecomp.Web.Services;

/// <summary>
/// v1014B canonical completed-workout boundary.
///
/// WorkoutSession is the single stored actual. Daily Tracking, Dashboard, and
/// Engine 3.0 all read the same rows. Editing updates the row in place; deleting
/// removes it. No legacy Engine 2.x calculated fields are written for current
/// sessions because Engine 3.0 recomputes physiology from the canonical inputs.
/// </summary>
public sealed class WorkoutActualService(
    RecompDbContext db)
{
    public const string ActiveEngineVersionCode = "3000.0";

    public async Task<List<WorkoutSession>> GetCompletedSessionsAsync(
        Guid clientId,
        Guid programId,
        DateOnly date,
        CancellationToken cancellationToken = default)
        => await db.WorkoutSessions
            .Where(x =>
                x.ClientId == clientId
                && x.ProgramId == programId
                && x.WorkoutDate == date
                && x.CompletedFlag)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.WorkoutSessionId)
            .ToListAsync(cancellationToken);

    public async Task<WorkoutSession?> FindOwnedSessionAsync(
        Guid workoutSessionId,
        Guid clientId,
        Guid programId,
        CancellationToken cancellationToken = default)
        => await db.WorkoutSessions
            .SingleOrDefaultAsync(
                x =>
                    x.WorkoutSessionId == workoutSessionId
                    && x.ClientId == clientId
                    && x.ProgramId == programId,
                cancellationToken);

    public void CorrectCompletedSummary(WorkoutSession session, decimal durationMin, decimal? sessionRpe)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.CompletedFlag)
            throw new ArgumentException("Only completed workouts can be corrected here.");

        var isRest = Engine30WorkoutUiCatalogService.NormalizeCode(session.WorkoutTypeCode) == "REST";
        if (isRest)
        {
            if (durationMin != 0m || sessionRpe.HasValue)
                throw new ArgumentException("Rest days have zero workout minutes and no RPE.");
        }
        else if (durationMin is < 1m or > 360m || sessionRpe is < 1m or > 10m)
            throw new ArgumentException("Enter duration between 1 and 360 minutes and RPE between 1 and 10, or leave RPE blank.");

        session.DurationMin = durationMin;
        session.SessionRpe = sessionRpe;
        ApplyCurrentEngineState(session, completed: true);
    }

    public void ApplyCurrentEngineState(
        WorkoutSession session,
        bool completed)
    {
        ArgumentNullException.ThrowIfNull(session);

        session.WorkoutTypeCode =
            Engine30WorkoutUiCatalogService.NormalizeCode(
                session.WorkoutTypeCode);

        session.CompletedFlag = completed;
        session.CalculationEngineVersion =
            ActiveEngineVersionCode;

        // These columns belong to the superseded Engine 2.x runtime. Leaving a
        // value here after an edit would create stale shadow physiology that no
        // longer matches the canonical actual inputs.
        session.CalculatedWorkoutCalories = null;
        session.CalculatedScaledGlycogenBurnG = null;
        session.CalculatedPushLoad = null;
        session.CalculatedPullLoad = null;
        session.CalculatedLegLoad = null;
        session.CalculatedCardioLoad = null;

        session.UpdatedAtUtc = DateTime.UtcNow;
    }
}

