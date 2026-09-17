using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;
using WebRecomp.WorkoutIntelligence.Persistence;

namespace WebRecomp.Web.Controllers;

public sealed class DetailedWorkoutController(
    RecompDbContext db,
    AppUserContextService userContext,
    IExerciseCatalogStore catalogStore,
    IDetailedWorkoutStore detailedWorkoutStore) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        Guid? workoutSessionId,
        DateOnly? date,
        CancellationToken cancellationToken = default)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var targetDate =
            date
            ?? AppUserContextService.GetClientToday(
                context.Client);

        var model =
            new DetailedWorkoutEditorViewModel
            {
                FeatureEnabled =
                    context.Program.DetailedWorkoutEntryEnabled,
                Date = targetDate,
                DefaultLoadUnitCode =
                    string.Equals(
                        context.Client.UnitSystemCode,
                        "METRIC",
                        StringComparison.OrdinalIgnoreCase)
                        ? "KG"
                        : "LB"
            };

        if (!model.FeatureEnabled)
            return View(model);

        await PopulateSessionsAsync(
            model,
            context.Program.ProgramId,
            context.Program.ConfigVersionId,
            targetDate,
            cancellationToken);

        if (!workoutSessionId.HasValue
            && model.Sessions.Count == 1)
        {
            workoutSessionId =
                model.Sessions[0].WorkoutSessionId;
        }

        if (!workoutSessionId.HasValue)
            return View(model);

        var selected =
            model.Sessions.SingleOrDefault(
                x =>
                    x.WorkoutSessionId
                    == workoutSessionId.Value);

        if (selected is null)
            return NotFound();

        model.WorkoutSessionId =
            selected.WorkoutSessionId;

        model.WorkoutDisplayName =
            selected.WorkoutDisplayName;

        var detailed =
            await detailedWorkoutStore.GetAsync(
                selected.WorkoutSessionId,
                cancellationToken);

        if (detailed is not null)
        {
            model.Exercises =
                detailed.Exercises
                    .OrderBy(x => x.ExerciseOrder)
                    .Select(
                        x =>
                            new DetailedWorkoutExerciseInputModel
                            {
                                ExerciseId = x.ExerciseId,
                                ExerciseName =
                                    x.ExerciseNameSnapshot,
                                Mechanics =
                                    x.MechanicsSnapshot,
                                Notes = x.Notes,
                                Sets =
                                    x.Sets
                                        .OrderBy(s => s.SetOrder)
                                        .Select(
                                            s =>
                                                new DetailedWorkoutSetInputModel
                                                {
                                                    Reps = s.Reps,
                                                    Load = s.Load,
                                                    LoadUnitCode =
                                                        string.IsNullOrWhiteSpace(
                                                            s.LoadUnitCode)
                                                            ? model.DefaultLoadUnitCode
                                                            : s.LoadUnitCode,
                                                    Rpe = s.Rpe,
                                                    Rir = s.Rir,
                                                    RestSeconds =
                                                        s.RestSeconds,
                                                    DurationSeconds =
                                                        s.DurationSeconds,
                                                    IsWarmup =
                                                        s.IsWarmup,
                                                    Notes = s.Notes
                                                })
                                        .ToList()
                            })
                    .ToList();
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> SearchExercises(
        string? q,
        int take = 30,
        CancellationToken cancellationToken = default)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null
            || context.Program is null
            || !context.Program.DetailedWorkoutEntryEnabled)
        {
            return StatusCode(403);
        }

        var rows =
            await catalogStore.SearchAsync(
                q,
                Math.Clamp(take, 1, 50),
                cancellationToken);

        return Json(
            rows.Select(
                x =>
                    new
                    {
                        exerciseId = x.ExerciseId,
                        name = x.Name,
                        mechanics = x.Mechanics,
                        category = x.Category,
                        difficultyLevel =
                            x.DifficultyLevel,
                        primaryMuscles =
                            x.PrimaryMuscles,
                        equipment = x.Equipment
                    }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        DetailedWorkoutEditorViewModel model,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!context.Program.DetailedWorkoutEntryEnabled)
        {
            TempData["StatusMessage"] =
                "Detailed workout entry is disabled in Settings.";

            return RedirectToAction(
                nameof(Index),
                new { date = model.Date.ToString("yyyy-MM-dd") });
        }

        if (!model.WorkoutSessionId.HasValue)
        {
            TempData["StatusMessage"] =
                "Choose a saved workout session first.";

            return RedirectToAction(
                nameof(Index),
                new { date = model.Date.ToString("yyyy-MM-dd") });
        }

        model.Exercises ??= [];

        var session =
            await db.WorkoutSessions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutSessionId
                            == model.WorkoutSessionId.Value
                        && x.ClientId
                            == context.Client.ClientId
                        && x.ProgramId
                            == context.Program.ProgramId,
                    cancellationToken);

        if (session is null)
            return NotFound();

        if (model.Exercises.Count > 50)
            return BadRequest("A detailed workout may contain at most 50 exercises.");

        foreach (var exercise in model.Exercises)
            exercise.Sets ??= [];

        var totalSets =
            model.Exercises.Sum(
                x => x.Sets.Count);

        if (totalSets > 300)
            return BadRequest("A detailed workout may contain at most 300 sets.");

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
            return BadRequest("One or more exercises are no longer active in the exercise library.");

        var canonicalExercises =
            new List<DetailedWorkoutExerciseEntry>();

        for (var exerciseIndex = 0;
             exerciseIndex < model.Exercises.Count;
             exerciseIndex++)
        {
            var input =
                model.Exercises[exerciseIndex];

            if (!catalog.TryGetValue(
                    input.ExerciseId,
                    out var catalogItem))
            {
                return BadRequest(
                    "Exercise library validation failed.");
            }

            if ((input.Notes?.Length ?? 0) > 1000)
                return BadRequest("Exercise notes may not exceed 1000 characters.");

            var sets =
                new List<DetailedWorkoutSetEntry>();

            for (var setIndex = 0;
                 setIndex < input.Sets.Count;
                 setIndex++)
            {
                var set =
                    input.Sets[setIndex];

                if (set.Reps is < 0 or > 2000)
                    return BadRequest("Reps must be between 0 and 2000.");

                if (set.Load is < 0 or > 10000)
                    return BadRequest("Load must be between 0 and 10000.");

                if (set.Rpe is < 0 or > 10)
                    return BadRequest("RPE must be between 0 and 10.");

                if (set.Rir is < 0 or > 10)
                    return BadRequest("RIR must be between 0 and 10.");

                if (set.RestSeconds is < 0 or > 7200)
                    return BadRequest("Rest seconds must be between 0 and 7200.");

                if (set.DurationSeconds is < 0 or > 21600)
                    return BadRequest("Set duration must be between 0 and 21600 seconds.");

                if ((set.Notes?.Length ?? 0) > 500)
                    return BadRequest("Set notes may not exceed 500 characters.");

                var unit =
                    string.Equals(
                        set.LoadUnitCode,
                        "KG",
                        StringComparison.OrdinalIgnoreCase)
                        ? "KG"
                        : "LB";

                sets.Add(
                    new DetailedWorkoutSetEntry(
                        Guid.Empty,
                        setIndex + 1,
                        set.Reps,
                        set.Load,
                        unit,
                        set.Rpe,
                        set.Rir,
                        set.RestSeconds,
                        set.DurationSeconds,
                        set.IsWarmup,
                        string.IsNullOrWhiteSpace(set.Notes)
                            ? null
                            : set.Notes.Trim()));
            }

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
                    sets));
        }

        await detailedWorkoutStore.ReplaceAsync(
            new DetailedWorkoutSession(
                session.WorkoutSessionId,
                canonicalExercises),
            cancellationToken);

        TempData["StatusMessage"] =
            canonicalExercises.Count == 0
                ? "Detailed workout cleared."
                : "Detailed workout saved.";

        return RedirectToAction(
            nameof(Index),
            new
            {
                workoutSessionId =
                    session.WorkoutSessionId,
                date =
                    session.WorkoutDate.ToString(
                        "yyyy-MM-dd")
            });
    }

    private async Task PopulateSessionsAsync(
        DetailedWorkoutEditorViewModel model,
        Guid programId,
        Guid configVersionId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var sessions =
            await db.WorkoutSessions
                .AsNoTracking()
                .Where(
                    x =>
                        x.ProgramId == programId
                        && x.WorkoutDate == date)
                .OrderBy(x => x.CreatedAtUtc)
                .ToListAsync(cancellationToken);

        var codes =
            sessions
                .Select(x => x.WorkoutTypeCode)
                .Distinct()
                .ToArray();

        var definitions =
            await db.WorkoutTypeDefinitions
                .AsNoTracking()
                .Where(
                    x =>
                        x.ConfigVersionId
                            == configVersionId
                        && codes.Contains(
                            x.WorkoutTypeCode))
                .ToDictionaryAsync(
                    x => x.WorkoutTypeCode,
                    x => x.DisplayName,
                    StringComparer.OrdinalIgnoreCase,
                    cancellationToken);

        model.Sessions =
            sessions.Select(
                    x =>
                        new DetailedWorkoutSessionChoiceViewModel
                        {
                            WorkoutSessionId =
                                x.WorkoutSessionId,
                            WorkoutDisplayName =
                                definitions.TryGetValue(
                                    x.WorkoutTypeCode,
                                    out var name)
                                    ? name
                                    : x.WorkoutTypeCode.Replace(
                                        '_',
                                        ' '),
                            DurationMin =
                                x.DurationMin,
                            SessionRpe =
                                x.SessionRpe,
                            CompletedFlag =
                                x.CompletedFlag
                        })
                .ToList();
    }
}
