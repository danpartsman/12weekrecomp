using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;
using WebRecomp.WorkoutIntelligence;
using WebRecomp.WorkoutIntelligence.Persistence;

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
    AppUserContextService userContext,
    IDetailedWorkoutStore detailedWorkoutStore) : Controller
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
                        showEndMenu = 1
                    }),
                completedSegments
            });
    }

    [HttpGet]
    public async Task<IActionResult> LastWorkout(
        Guid workoutSessionId,
        CancellationToken cancellationToken = default)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return StatusCode(403);

        if (!context.Program.DetailedWorkoutEntryEnabled)
            return StatusCode(403);

        var current =
            await db.WorkoutSessions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutSessionId == workoutSessionId
                        && x.ClientId == context.Client.ClientId
                        && x.ProgramId == context.Program.ProgramId,
                    cancellationToken);

        if (current is null)
            return NotFound();

        var priorQuery =
            db.WorkoutSessions
                .AsNoTracking()
                .Where(
                    x =>
                        x.ClientId == context.Client.ClientId
                        && x.ProgramId == context.Program.ProgramId
                        && x.WorkoutSessionId != current.WorkoutSessionId
                        && x.CompletedFlag
                        && x.CreatedAtUtc < current.CreatedAtUtc);

        if (current.WorkoutTemplateId.HasValue)
        {
            priorQuery =
                priorQuery.Where(
                    x =>
                        x.WorkoutTemplateId
                            == current.WorkoutTemplateId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(current.SystemWorkoutCode))
        {
            var systemWorkoutCode = current.SystemWorkoutCode;

            priorQuery =
                priorQuery.Where(
                    x =>
                        x.SystemWorkoutCode
                            == systemWorkoutCode);
        }
        else
        {
            var workoutName = current.WorkoutName;
            var workoutTypeCode = current.WorkoutTypeCode;

            priorQuery =
                priorQuery.Where(
                    x =>
                        x.WorkoutName == workoutName
                        && x.WorkoutTypeCode == workoutTypeCode);
        }

        var previous =
            await priorQuery
                .OrderByDescending(x => x.WorkoutDate)
                .ThenByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(
                    cancellationToken);

        if (previous is null)
        {
            return Json(
                new
                {
                    available = false
                });
        }

        var currentDetailed =
            await detailedWorkoutStore.GetAsync(
                current.WorkoutSessionId,
                cancellationToken);

        var previousDetailed =
            await detailedWorkoutStore.GetAsync(
                previous.WorkoutSessionId,
                cancellationToken);

        if (currentDetailed is null
            || previousDetailed is null
            || currentDetailed.Exercises.Count == 0
            || previousDetailed.Exercises.Count == 0)
        {
            return Json(
                new
                {
                    available = false
                });
        }

        var currentExerciseIds =
            currentDetailed.Exercises
                .Select(x => x.ExerciseId)
                .ToHashSet();

        var exercises =
            previousDetailed.Exercises
                .Where(
                    x =>
                        currentExerciseIds.Contains(
                            x.ExerciseId))
                .OrderBy(x => x.ExerciseOrder)
                .Select(
                    x =>
                    {
                        var workingSets =
                            x.Sets
                                .Where(s => !s.IsWarmup)
                                .OrderBy(s => s.SetOrder)
                                .ToArray();

                        var sourceSets =
                            workingSets.Length > 0
                                ? workingSets
                                : x.Sets
                                    .OrderBy(s => s.SetOrder)
                                    .ToArray();

                        var reps =
                            sourceSets
                                .Select(s => s.Reps)
                                .Where(r => r.HasValue)
                                .Select(r =>
                                    Math.Clamp(
                                        (int)r!.Value,
                                        1,
                                        500))
                                .ToArray();

                        return new
                        {
                            exerciseId = x.ExerciseId,
                            sets =
                                Math.Clamp(
                                    sourceSets.Length > 0
                                        ? sourceSets.Length
                                        : 1,
                                    1,
                                    20),
                            setReps = reps,
                            maxWeight = x.MaxWeight,
                            loadUnitCode = x.LoadUnitCode
                        };
                    })
                .ToArray();

        if (exercises.Length == 0)
        {
            return Json(
                new
                {
                    available = false
                });
        }

        return Json(
            new
            {
                available = true,
                workoutSessionId = previous.WorkoutSessionId,
                workoutDate = previous.WorkoutDate.ToString("yyyy-MM-dd"),
                workoutName = previous.WorkoutName,
                exercises
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Autosave(
        DetailedWorkoutEditorViewModel model,
        CancellationToken cancellationToken = default)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return StatusCode(403);

        if (!context.Program.DetailedWorkoutEntryEnabled)
            return StatusCode(403);

        if (!model.WorkoutSessionId.HasValue)
            return BadRequest("An active workout session is required.");

        var session =
            await db.WorkoutSessions
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutSessionId == model.WorkoutSessionId.Value
                        && x.ClientId == context.Client.ClientId
                        && x.ProgramId == context.Program.ProgramId,
                    cancellationToken);

        if (session is null)
            return NotFound();

        if (session.CompletedFlag)
            return Conflict("Completed workouts cannot be autosaved.");

        model.Exercises ??= [];

        if (model.Exercises.Count > 50)
            return BadRequest("A workout may contain at most 50 exercises.");

        var exerciseIds =
            model.Exercises
                .Select(x => x.ExerciseId)
                .Distinct()
                .ToArray();

        var catalog =
            await db.ExerciseCatalogItems
                .AsNoTracking()
                .Where(
                    x =>
                        exerciseIds.Contains(
                            x.ExerciseCatalogItemId)
                        && x.IsActive)
                .ToDictionaryAsync(
                    x => x.ExerciseCatalogItemId,
                    cancellationToken);

        if (catalog.Count != exerciseIds.Length)
        {
            return BadRequest(
                "One or more exercises are no longer active in the exercise library.");
        }

        var unit =
            string.Equals(
                context.Client.UnitSystemCode,
                "METRIC",
                StringComparison.OrdinalIgnoreCase)
                ? "KG"
                : "LB";

        var canonicalExercises =
            new List<DetailedWorkoutExerciseEntry>();

        for (var exerciseIndex = 0;
             exerciseIndex < model.Exercises.Count;
             exerciseIndex++)
        {
            var input = model.Exercises[exerciseIndex];

            if (!catalog.TryGetValue(
                    input.ExerciseId,
                    out var catalogItem))
            {
                return BadRequest(
                    "Exercise library validation failed.");
            }

            if (input.Sets is < 1 or > 20)
                return BadRequest("Sets must be between 1 and 20.");

            var setReps =
                ResolveSetReps(input);

            if (setReps.Length != input.Sets
                || setReps.Any(x => x is < 1 or > 500))
            {
                return BadRequest(
                    "Enter one rep value between 1 and 500 for each set.");
            }

            if (input.MaxWeight is < 0 or > 10000)
                return BadRequest("Weight must be between 0 and 10000.");

            if ((input.Notes?.Length ?? 0) > 1000)
                return BadRequest("Exercise notes may not exceed 1000 characters.");

            var sets =
                Enumerable.Range(1, input.Sets)
                    .Select(
                        setOrder =>
                            new DetailedWorkoutSetEntry(
                                Guid.Empty,
                                setOrder,
                                setReps[setOrder - 1],
                                null,
                                unit,
                                null,
                                null,
                                null,
                                null,
                                false,
                                null))
                    .ToArray();

            canonicalExercises.Add(
                new DetailedWorkoutExerciseEntry(
                    Guid.Empty,
                    catalogItem.ExerciseCatalogItemId,
                    exerciseIndex + 1,
                    catalogItem.Name,
                    catalogItem.Mechanics,
                    string.IsNullOrWhiteSpace(input.Notes)
                        ? null
                        : input.Notes.Trim(),
                    sets,
                    input.MaxWeight,
                    unit));
        }

        if (!string.IsNullOrWhiteSpace(model.WorkoutName))
        {
            var workoutName = model.WorkoutName.Trim();

            if (workoutName.Length > 120)
                return BadRequest("Workout name may not exceed 120 characters.");

            session.WorkoutName = workoutName;
        }

        await detailedWorkoutStore.ReplaceAsync(
            new DetailedWorkoutSession(
                session.WorkoutSessionId,
                canonicalExercises),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Json(
            new
            {
                saved = true,
                savedAtUtc = DateTime.UtcNow
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Discard(
        Guid workoutSessionId,
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

        var workoutDate = session.WorkoutDate;

        // Detailed exercises and their sets cascade from WorkoutSession.
        // A discarded session never becomes an Engine 3.0 completed actual.
        db.WorkoutSessions.Remove(session);
        await db.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Workout discarded.";

        return RedirectToAction(
            "Index",
            "DetailedWorkout",
            new
            {
                mode = "start",
                date = workoutDate.ToString("yyyy-MM-dd")
            });
    }

    private static int[] ResolveSetReps(
        DetailedWorkoutExerciseInputModel input)
    {
        var fallback =
            input.Reps is >= 1 and <= 500
                ? input.Reps
                : 10;

        var submitted =
            input.SetReps ?? [];

        return Enumerable.Range(0, input.Sets)
            .Select(
                index =>
                    index < submitted.Count
                    && submitted[index] is >= 1 and <= 500
                        ? submitted[index]
                        : fallback)
            .ToArray();
    }

    private static bool IsCardio(string? workoutTypeCode)
        => !string.IsNullOrWhiteSpace(workoutTypeCode)
            && CardioWorkoutTypes.Contains(
                workoutTypeCode.Trim());
}
