using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

/// <summary>
/// TEST v1012 live-training coordination.
///
/// WorkoutSession.CreatedAtUtc is the authoritative segment start. The browser
/// may display a ticking clock, but it never owns elapsed workout time. This
/// lets a user lock the phone, answer a text, switch applications, refresh the
/// browser, or resume on another device without losing the session clock.
/// </summary>
public sealed class TrainingSessionController(
    RecompDbContext db,
    AppUserContextService userContext) : Controller
{
    private static readonly HashSet<string> CardioWorkoutTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "GENERAL_CARDIO",
            "EASY_CARDIO",
            "ZONE2_CARDIO",
            "WALKING",
            "RUNNING",
            "TREADMILL",
            "CYCLING",
            "ROWING",
            "ELLIPTICAL",
            "STAIR_CLIMBER",
            "SWIMMING"
        };

    [HttpGet]
    public async Task<IActionResult> Status(
        CancellationToken cancellationToken = default)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        var serverNowUtc = DateTime.UtcNow;

        if (context.Client is null || context.Program is null)
        {
            return Json(
                new
                {
                    active = false,
                    serverNowUtc
                });
        }

        var clientToday =
            AppUserContextService.GetClientToday(
                context.Client);

        // Keep a just-before-midnight workout resumable after the local date
        // rolls over, but do not resurrect old abandoned builder rows forever.
        var earliestWorkoutDate =
            clientToday.AddDays(-1);

        var session =
            await db.WorkoutSessions
                .AsNoTracking()
                .Where(
                    x =>
                        x.ClientId == context.Client.ClientId
                        && x.ProgramId == context.Program.ProgramId
                        && !x.CompletedFlag
                        && x.EntrySourceCode
                            == RecompInputSourceCodes.DetailedWorkout
                        && x.WorkoutDate >= earliestWorkoutDate)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(
                    cancellationToken);

        if (session is null
            || session.CreatedAtUtc == default
            || session.CreatedAtUtc < serverNowUtc.AddHours(-18))
        {
            return Json(
                new
                {
                    active = false,
                    serverNowUtc
                });
        }

        var cardio =
            IsCardio(session.WorkoutTypeCode)
            || IsCardio(session.SystemWorkoutCode);

        var completedSegments =
            await db.WorkoutSessions
                .AsNoTracking()
                .Where(
                    x =>
                        x.ClientId == context.Client.ClientId
                        && x.ProgramId == context.Program.ProgramId
                        && x.WorkoutDate == session.WorkoutDate
                        && x.CompletedFlag)
                .OrderBy(x => x.CreatedAtUtc)
                .Select(
                    x =>
                        new
                        {
                            x.WorkoutSessionId,
                            name = x.WorkoutName,
                            workoutTypeCode = x.WorkoutTypeCode,
                            durationMin = x.DurationMin
                        })
                .ToListAsync(
                    cancellationToken);

        var workoutName =
            string.IsNullOrWhiteSpace(session.WorkoutName)
                ? (cardio ? "Cardio" : "Workout")
                : session.WorkoutName;

        return Json(
            new
            {
                active = true,
                serverNowUtc,
                workoutSessionId = session.WorkoutSessionId,
                workoutDate = session.WorkoutDate.ToString("yyyy-MM-dd"),
                workoutName,
                workoutTypeCode = session.WorkoutTypeCode,
                isCardio = cardio,
                startedAtUtc = DateTime.SpecifyKind(
                        session.CreatedAtUtc,
                        DateTimeKind.Utc)
                    .ToString("O"),
                continueUrl = Url.Action(
                    "Index",
                    "DetailedWorkout",
                    new
                    {
                        mode = "live",
                        workoutSessionId = session.WorkoutSessionId,
                        date = session.WorkoutDate.ToString("yyyy-MM-dd")
                    }),
                endUrl = Url.Action(
                    "Index",
                    "DetailedWorkout",
                    new
                    {
                        mode = "live",
                        workoutSessionId = session.WorkoutSessionId,
                        date = session.WorkoutDate.ToString("yyyy-MM-dd"),
                        endPrompt = 1
                    }),
                completedSegments
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndCardio(
        Guid workoutSessionId,
        decimal? sessionRpe,
        CancellationToken cancellationToken = default)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!context.Program.DetailedWorkoutEntryEnabled)
            return StatusCode(403);

        if (sessionRpe.HasValue
            && sessionRpe.Value is < 1m or > 10m)
        {
            return BadRequest(
                "Cardio RPE must be between 1 and 10.");
        }

        var session =
            await db.WorkoutSessions
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutSessionId == workoutSessionId
                        && x.ClientId == context.Client.ClientId
                        && x.ProgramId == context.Program.ProgramId,
                    cancellationToken);

        if (session is null)
            return NotFound();

        if (session.CompletedFlag)
        {
            return RedirectToAction(
                "Complete",
                "DetailedWorkout",
                new
                {
                    workoutSessionId
                });
        }

        if (!IsCardio(session.WorkoutTypeCode)
            && !IsCardio(session.SystemWorkoutCode))
        {
            return BadRequest(
                "Only cardio segments can use the cardio completion endpoint.");
        }

        var stoppedAtUtc = DateTime.UtcNow;

        var elapsedMinutes =
            session.CreatedAtUtc == default
                ? session.DurationMin
                : (decimal)(stoppedAtUtc - session.CreatedAtUtc)
                    .TotalMinutes;

        session.DurationMin =
            Math.Clamp(
                Math.Max(
                    1m,
                    Math.Round(
                        elapsedMinutes,
                        1,
                        MidpointRounding.AwayFromZero)),
                1m,
                360m);

        if (sessionRpe.HasValue)
            session.SessionRpe = sessionRpe.Value;

        session.TypicalRestSec = null;
        session.CompletedFlag = true;
        session.EntrySourceCode =
            RecompInputSourceCodes.DetailedWorkout;
        session.CalculationEngineVersion = "3.0";
        session.UpdatedAtUtc = stoppedAtUtc;

        await db.SaveChangesAsync(
            cancellationToken);

        return RedirectToAction(
            "Complete",
            "DetailedWorkout",
            new
            {
                workoutSessionId
            });
    }

    private static bool IsCardio(string? workoutTypeCode)
        => !string.IsNullOrWhiteSpace(workoutTypeCode)
            && CardioWorkoutTypes.Contains(
                workoutTypeCode.Trim());
}
