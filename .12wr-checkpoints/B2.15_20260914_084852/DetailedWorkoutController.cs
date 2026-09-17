using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutIntelligence;
using WebRecomp.Web.ViewModels;
using WebRecomp.WorkoutIntelligence;
using WebRecomp.WorkoutIntelligence.Persistence;

namespace WebRecomp.Web.Controllers;

public sealed class DetailedWorkoutController(
    RecompDbContext db,
    AppUserContextService userContext,
    IExerciseCatalogStore catalogStore,
    IDetailedWorkoutStore detailedWorkoutStore,
    Engine30DashboardProjectionService engine30Projection,
    IWorkoutIntelligenceGateway workoutIntelligence) : Controller
{
    private const string SystemWorkoutConfigVersionCode = "CFG-3000.0";

    [HttpGet]
    public async Task<IActionResult> Index(
        Guid? workoutSessionId,
        Guid? addExerciseId,
        DateOnly? date,
        string? mode,
        Guid? templateId,
        string? systemCode,
        bool modifySelection = false,
        bool editCompleted = false,
        string? returnContext = null,
        DateOnly? weekStart = null,
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

        var normalizedMode =
            NormalizeWorkspaceMode(
                mode,
                workoutSessionId.HasValue);

        var normalizedReturnContext =
            string.Equals(
                returnContext,
                "schedule",
                StringComparison.OrdinalIgnoreCase)
                ? "schedule"
                : string.Equals(
                    returnContext,
                    "daily",
                    StringComparison.OrdinalIgnoreCase)
                    ? "daily"
                    : null;

        var model =
            new DetailedWorkoutEditorViewModel
            {
                FeatureEnabled =
                    context.Program.DetailedWorkoutEntryEnabled,
                Date = targetDate,
                PendingExerciseId = addExerciseId,
                WorkspaceMode = normalizedMode,
                SelectedTemplateId = templateId,
                SelectedSystemWorkoutCode = systemCode,
                ModifySelection = modifySelection,
                TemplateOnlyMode =
                    string.Equals(
                        normalizedReturnContext,
                        "schedule",
                        StringComparison.OrdinalIgnoreCase),
                ReturnContext =
                    normalizedReturnContext,
                ReturnWeekStart = weekStart,
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

        await PopulateTemplatesAsync(
            model,
            context.Client.ClientId,
            cancellationToken);

        await PopulateWorkoutTypesAsync(
            model,
            context.Program.ConfigVersionId,
            cancellationToken);

        if (!model.TemplateOnlyMode)
        {
            await PopulateHeaderAnalysisAsync(
                model,
                context,
                targetDate,
                workoutSessionId,
                cancellationToken);
        }
        else
        {
            model.HeaderAnalysis.SourceLabel =
                "Weekly Plan template";
            model.HeaderAnalysis.ScopeLabel =
                "Not an actual workout yet";
        }

        if (string.Equals(
                model.WorkspaceMode,
                "saved",
                StringComparison.OrdinalIgnoreCase)
            && templateId.HasValue)
        {
            model.SelectedTemplate =
                await GetTemplateDetailAsync(
                    context.Client.ClientId,
                    templateId.Value,
                    model.DefaultLoadUnitCode,
                    cancellationToken);

            if (model.SelectedTemplate is null)
                return NotFound();

            if (modifySelection)
            {
                model.Exercises =
                    CloneExercises(
                        model.SelectedTemplate.Exercises);

                model.WorkoutName =
                    model.SelectedTemplate.TemplateName;

                model.TemplateName =
                    model.SelectedTemplate.TemplateName;

                model.DraftWorkoutTypeCode =
                    model.SelectedTemplate.WorkoutTypeCode;
            }
        }

        if (string.Equals(
                model.WorkspaceMode,
                "system",
                StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(systemCode))
        {
            model.SelectedSystemWorkout =
                await BuildSystemWorkoutPreviewAsync(
                    context.Client.ClientId,
                    context.Program.ConfigVersionId,
                    systemCode,
                    model.DefaultLoadUnitCode,
                    cancellationToken);

            if (model.SelectedSystemWorkout is null)
                return NotFound();

            if (modifySelection)
            {
                model.Exercises =
                    CloneExercises(
                        model.SelectedSystemWorkout.Exercises);

                model.WorkoutName =
                    model.SelectedSystemWorkout.DisplayName;

                model.TemplateName =
                    model.SelectedSystemWorkout.DisplayName;

                model.DraftWorkoutTypeCode =
                    model.SelectedSystemWorkout.WorkoutTypeCode;
            }
        }

        if (string.Equals(
                model.WorkspaceMode,
                "new",
                StringComparison.OrdinalIgnoreCase)
            && !workoutSessionId.HasValue)
        {
            model.DraftWorkoutTypeCode =
                ResolveDefaultCustomWorkoutTypeCode(
                    model.WorkoutTypes);
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

        if (selected.CompletedFlag && !editCompleted)
        {
            TempData["StatusMessage"] =
                "Completed workouts stay locked. Use Edit if you need to correct one.";

            return RedirectToAction(
                nameof(Index),
                new
                {
                    mode = "new",
                    date = targetDate.ToString("yyyy-MM-dd")
                });
        }

        model.WorkspaceMode = "new";
        model.WorkoutSessionId =
            selected.WorkoutSessionId;
        model.WorkoutDisplayName =
            selected.WorkoutDisplayName;
        model.WorkoutName = selected.WorkoutName;
        model.SourceTemplateId = selected.WorkoutTemplateId;
        model.EditCompleted = editCompleted;
        model.TemplateName = selected.WorkoutName;
        model.DraftWorkoutTypeCode =
            selected.WorkoutTypeCode;

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

                            var setCount =
                                sourceSets.Length > 0
                                    ? sourceSets.Length
                                    : 3;

                            var reps =
                                sourceSets
                                    .Select(s => s.Reps)
                                    .FirstOrDefault(r => r.HasValue)
                                ?? 10;

                            var setReps =
                                sourceSets
                                    .Select(
                                        s =>
                                            Math.Clamp(
                                                (int)(s.Reps ?? reps),
                                                1,
                                                500))
                                    .ToList();

                            return new DetailedWorkoutExerciseInputModel
                            {
                                ExerciseId = x.ExerciseId,
                                ExerciseName =
                                    x.ExerciseNameSnapshot,
                                Mechanics =
                                    x.MechanicsSnapshot,
                                Notes = x.Notes,
                                Sets = Math.Clamp(setCount, 1, 20),
                                Reps = Math.Clamp(reps, 1, 500),
                                SetReps = setReps,
                                MaxWeight = x.MaxWeight,
                                LoadUnitCode =
                                    NormalizeLoadUnit(
                                        x.LoadUnitCode,
                                        model.DefaultLoadUnitCode)
                            };
                        })
                    .ToList();
        }

        if (addExerciseId.HasValue
            && model.Exercises.All(
                x => x.ExerciseId != addExerciseId.Value))
        {
            var catalogItem =
                await db.ExerciseCatalogItems
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x =>
                            x.ExerciseCatalogItemId == addExerciseId.Value
                            && x.IsActive,
                        cancellationToken);

            if (catalogItem is not null)
            {
                var preference =
                    await db.ClientExercisePreferences
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            x =>
                                x.ClientId == context.Client.ClientId
                                && x.ExerciseCatalogItemId
                                    == catalogItem.ExerciseCatalogItemId,
                            cancellationToken);

                model.Exercises.Add(
                    new DetailedWorkoutExerciseInputModel
                    {
                        ExerciseId =
                            catalogItem.ExerciseCatalogItemId,
                        ExerciseName = catalogItem.Name,
                        Mechanics = catalogItem.Mechanics,
                        Sets = preference?.DefaultSets ?? 3,
                        Reps = preference?.DefaultReps ?? 10,
                        SetReps =
                            Enumerable.Repeat(
                                    (int)(preference?.DefaultReps ?? 10),
                                    Math.Clamp(
                                        (int)(preference?.DefaultSets ?? 3),
                                        1,
                                        20))
                                .ToList(),
                        MaxWeight =
                            preference?.DefaultMaxWeight,
                        LoadUnitCode =
                            NormalizeLoadUnit(
                                preference?.LoadUnitCode,
                                model.DefaultLoadUnitCode)
                    });
            }
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateWorkout(
        DateOnly date,
        string workoutTypeCode,
        string? workoutName,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!context.Program.DetailedWorkoutEntryEnabled)
            return StatusCode(403);

        if (string.IsNullOrWhiteSpace(workoutTypeCode))
        {
            TempData["StatusMessage"] =
                "Choose a workout type to start the workout.";

            return RedirectToAction(
                nameof(Index),
                new
                {
                    date =
                        date.ToString("yyyy-MM-dd")
                });
        }

        if (!SystemWorkoutRecipeCatalog.TryGet(
                workoutTypeCode,
                out var selectedSystemWorkout)
            || selectedSystemWorkout is null)
        {
            return BadRequest(
                "The selected System Workout is not available.");
        }

        var systemConfigVersionId =
            await GetSystemWorkoutConfigVersionIdAsync(
                cancellationToken);

        if (!systemConfigVersionId.HasValue)
            return BadRequest(
                "The Engine 3.0 System Workout configuration is not available.");

        var workoutType =
            await db.WorkoutTypeDefinitions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.ConfigVersionId
                            == systemConfigVersionId.Value
                        && x.WorkoutTypeCode
                            == selectedSystemWorkout.EngineWorkoutTypeCode
                        && x.IsActive
                        && x.IsSelectable,
                    cancellationToken);

        if (workoutType is null)
            return BadRequest(
                "The selected workout type is not available.");

        var engineCatalog =
            new Engine30WorkoutTypeCatalog();

        var baseRpe =
            engineCatalog.TryGet(
                selectedSystemWorkout.EngineWorkoutTypeCode,
                out var engineDefinition)
            && engineDefinition is not null
                ? engineDefinition.DefaultRpe
                : 5m;

        var defaultDuration =
            SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(
                selectedSystemWorkout,
                workoutType.DefaultMinutes);

        var defaultRpe =
            SystemWorkoutRecipeCatalog.ResolveDefaultRpe(
                selectedSystemWorkout,
                baseRpe);

        var name =
            string.IsNullOrWhiteSpace(workoutName)
                ? selectedSystemWorkout.DisplayName
                : workoutName.Trim();

        if (name.Length > 120)
            return BadRequest(
                "Workout name may not exceed 120 characters.");

        var now = DateTime.UtcNow;

        var session =
            new WorkoutSession
            {
                WorkoutSessionId = Guid.NewGuid(),
                ClientId = context.Client.ClientId,
                ProgramId = context.Program.ProgramId,
                WorkoutDate = date,
                WorkoutName = name,
                SystemWorkoutCode =
                    selectedSystemWorkout.SystemCode,
                WorkoutTypeCode =
                    workoutType.WorkoutTypeCode,
                DurationMin =
                    defaultDuration,
                TypicalRestSec =
                    workoutType.DefaultRestSec,
                SessionRpe =
                    defaultRpe,
                CompletedFlag = false,
                EntrySourceCode =
                    RecompInputSourceCodes.DetailedWorkout,
                CalculationEngineVersion = "3.0",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        db.WorkoutSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] =
            "Workout started. Search below and click exercises to add them.";

        return RedirectToAction(
            nameof(Index),
            new
            {
                workoutSessionId =
                    session.WorkoutSessionId,
                date =
                    date.ToString("yyyy-MM-dd")
            });
    }

    [HttpGet]
    public async Task<IActionResult> SearchExercises(
        string? q,
        int take = 40,
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

        var normalized =
            q?.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            return Json(Array.Empty<object>());

        var muscleIntent =
            await db.ExerciseMuscleTargets
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.MuscleName.Contains(
                            normalized)
                        || x.MuscleSlug.Contains(
                            normalized),
                    cancellationToken);

        var rows =
            await catalogStore.SearchAsync(
                normalized,
                1000,
                cancellationToken);

        var exerciseIds =
            rows
                .Select(x => x.ExerciseId)
                .ToArray();

        if (exerciseIds.Length == 0)
            return Json(Array.Empty<object>());

        var matchingMuscles =
            await db.ExerciseMuscleTargets
                .AsNoTracking()
                .Where(
                    x =>
                        exerciseIds.Contains(
                            x.ExerciseCatalogItemId)
                        && (
                            x.MuscleName.Contains(
                                normalized)
                            || x.MuscleSlug.Contains(
                                normalized)))
                .ToListAsync(
                    cancellationToken);

        var bestMuscleMatch =
            matchingMuscles
                .GroupBy(
                    x => x.ExerciseCatalogItemId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                        g.OrderByDescending(
                                x =>
                                    MuscleRolePriority(
                                        x.RoleCode))
                            .ThenByDescending(
                                x =>
                                    TextMatchPriority(
                                        x.MuscleName,
                                        normalized))
                            .ThenBy(x => x.DisplayOrder)
                            .First());

        var details =
            await db.ExerciseCatalogItems
                .AsNoTracking()
                .Where(
                    x =>
                        exerciseIds.Contains(
                            x.ExerciseCatalogItemId))
                .Select(
                    x =>
                        new
                        {
                            x.ExerciseCatalogItemId,
                            x.ForceType,
                            x.InstructionsJson
                        })
                .ToDictionaryAsync(
                    x => x.ExerciseCatalogItemId,
                    cancellationToken);

        var preferences =
            await db.ClientExercisePreferences
                .AsNoTracking()
                .Where(
                    x =>
                        x.ClientId
                            == context.Client.ClientId
                        && exerciseIds.Contains(
                            x.ExerciseCatalogItemId))
                .ToDictionaryAsync(
                    x => x.ExerciseCatalogItemId,
                    cancellationToken);

        var usageCounts =
            await db.DetailedWorkoutExercises
                .AsNoTracking()
                .Join(
                    db.WorkoutSessions
                        .AsNoTracking()
                        .Where(
                            x =>
                                x.ClientId
                                == context.Client.ClientId),
                    exercise =>
                        exercise.WorkoutSessionId,
                    session =>
                        session.WorkoutSessionId,
                    (exercise, session) =>
                        exercise)
                .Where(
                    x =>
                        exerciseIds.Contains(
                            x.ExerciseCatalogItemId))
                .GroupBy(
                    x => x.ExerciseCatalogItemId)
                .Select(
                    g =>
                        new
                        {
                            ExerciseId = g.Key,
                            Count = g.Count()
                        })
                .ToDictionaryAsync(
                    x => x.ExerciseId,
                    x => x.Count,
                    cancellationToken);

        var globalFavoriteCounts =
            await db.ClientExercisePreferences
                .AsNoTracking()
                .Where(
                    x =>
                        x.IsFavorite
                        && exerciseIds.Contains(
                            x.ExerciseCatalogItemId))
                .GroupBy(
                    x => x.ExerciseCatalogItemId)
                .Select(
                    g =>
                        new
                        {
                            ExerciseId = g.Key,
                            Count = g.Count()
                        })
                .ToDictionaryAsync(
                    x => x.ExerciseId,
                    x => x.Count,
                    cancellationToken);

        var defaultUnit =
            string.Equals(
                context.Client.UnitSystemCode,
                "METRIC",
                StringComparison.OrdinalIgnoreCase)
                ? "KG"
                : "LB";

        var ranked =
            rows.Select(
                    x =>
                    {
                        preferences.TryGetValue(
                            x.ExerciseId,
                            out var preference);

                        bestMuscleMatch.TryGetValue(
                            x.ExerciseId,
                            out var muscleMatch);

                        usageCounts.TryGetValue(
                            x.ExerciseId,
                            out var useCount);

                        globalFavoriteCounts.TryGetValue(
                            x.ExerciseId,
                            out var globalFavoriteCount);

                        details.TryGetValue(
                            x.ExerciseId,
                            out var detail);

                        var nameMatch =
                            TextMatchPriority(
                                x.Name,
                                normalized);

                        var rolePriority =
                            muscleMatch is null
                                ? 0
                                : MuscleRolePriority(
                                    muscleMatch.RoleCode);

                        var muscleTextMatch =
                            muscleMatch is null
                                ? 0
                                : Math.Max(
                                    TextMatchPriority(
                                        muscleMatch.MuscleName,
                                        normalized),
                                    TextMatchPriority(
                                        muscleMatch.MuscleSlug,
                                        normalized));

                        int relevanceScore;

                        if (muscleIntent)
                        {
                            if (muscleMatch is null)
                            {
                                relevanceScore =
                                    int.MinValue;
                            }
                            else
                            {
                                relevanceScore =
                                    (rolePriority * 1000)
                                    + muscleTextMatch;
                            }
                        }
                        else
                        {
                            relevanceScore =
                                nameMatch > 0
                                    ? 2000 + nameMatch
                                    : rolePriority > 0
                                        ? 500
                                            + (rolePriority * 100)
                                            + muscleTextMatch
                                        : 0;
                        }

                        if (preference?.IsFavorite == true)
                            relevanceScore += 40;

                        relevanceScore +=
                            Math.Min(
                                useCount,
                                20) * 2;

                        relevanceScore +=
                            GlobalFavoriteBonus(
                                globalFavoriteCount);

                        if (string.Equals(
                                x.Mechanics,
                                "compound",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            relevanceScore += 5;
                        }

                        return new
                        {
                            Exercise = x,
                            Preference = preference,
                            MuscleMatch = muscleMatch,
                            UseCount = useCount,
                            GlobalFavoriteCount =
                                globalFavoriteCount,
                            ForceType = detail?.ForceType,
                            Instructions =
                                ParseInstructions(
                                    detail?.InstructionsJson),
                            RelevanceScore =
                                relevanceScore
                        };
                    })
                .Where(
                    x =>
                        !muscleIntent
                        || x.MuscleMatch is not null)
                .OrderByDescending(
                    x => x.RelevanceScore)
                .ThenBy(x => x.Exercise.Name)
                .Take(
                    Math.Clamp(
                        take,
                        1,
                        100))
                .ToArray();

        return Json(
            ranked.Select(
                x =>
                    new
                    {
                        exerciseId =
                            x.Exercise.ExerciseId,
                        name =
                            x.Exercise.Name,
                        mechanics =
                            x.Exercise.Mechanics,
                        category =
                            x.Exercise.Category,
                        difficultyLevel =
                            x.Exercise.DifficultyLevel,
                        forceType =
                            x.ForceType,
                        primaryMuscles =
                            x.Exercise.PrimaryMuscles,
                        equipment =
                            x.Exercise.Equipment,
                        matchedMuscleName =
                            x.MuscleMatch?.MuscleName,
                        matchedMuscleRole =
                            x.MuscleMatch?.RoleCode,
                        personalUseCount =
                            x.UseCount,
                        globalFavoriteCount =
                            x.GlobalFavoriteCount,
                        isFavorite =
                            x.Preference?.IsFavorite
                            ?? false,
                        defaultSets =
                            x.Preference?.DefaultSets
                            ?? 3,
                        defaultReps =
                            x.Preference?.DefaultReps
                            ?? 10,
                        defaultMaxWeight =
                            x.Preference?.DefaultMaxWeight,
                        defaultLoadUnitCode =
                            NormalizeLoadUnit(
                                x.Preference?.LoadUnitCode,
                                defaultUnit),
                        instructions =
                            x.Instructions
                    }));
    }

    [HttpGet]
    public async Task<IActionResult> Technique(
        Guid exerciseId,
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

        var exercise =
            await db.ExerciseCatalogItems
                .AsNoTracking()
                .Where(
                    x =>
                        x.ExerciseCatalogItemId == exerciseId
                        && x.IsActive)
                .Select(
                    x =>
                        new
                        {
                            x.Name,
                            x.InstructionsJson
                        })
                .SingleOrDefaultAsync(
                    cancellationToken);

        if (exercise is null)
            return NotFound();

        return Json(
            new
            {
                exerciseId,
                name = exercise.Name,
                instructions =
                    ParseInstructions(
                        exercise.InstructionsJson)
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetFavorite(
        Guid exerciseId,
        bool favorite,
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

        var exists =
            await db.ExerciseCatalogItems
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ExerciseCatalogItemId == exerciseId
                        && x.IsActive,
                    cancellationToken);

        if (!exists)
            return NotFound();

        var preference =
            await db.ClientExercisePreferences
                .SingleOrDefaultAsync(
                    x =>
                        x.ClientId == context.Client.ClientId
                        && x.ExerciseCatalogItemId
                            == exerciseId,
                    cancellationToken);

        var now = DateTime.UtcNow;

        if (preference is null)
        {
            preference =
                new ClientExercisePreferenceRecord
                {
                    ClientExercisePreferenceId =
                        Guid.NewGuid(),
                    ClientId =
                        context.Client.ClientId,
                    ExerciseCatalogItemId =
                        exerciseId,
                    IsFavorite = favorite,
                    DefaultSets = 3,
                    DefaultReps = 10,
                    LoadUnitCode =
                        string.Equals(
                            context.Client.UnitSystemCode,
                            "METRIC",
                            StringComparison.OrdinalIgnoreCase)
                            ? "KG"
                            : "LB",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            db.ClientExercisePreferences.Add(
                preference);
        }
        else
        {
            preference.IsFavorite = favorite;
            preference.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        var globalFavoriteCount =
            await db.ClientExercisePreferences
                .AsNoTracking()
                .CountAsync(
                    x =>
                        x.ExerciseCatalogItemId
                            == exerciseId
                        && x.IsFavorite,
                    cancellationToken);

        return Json(
            new
            {
                exerciseId,
                isFavorite = preference.IsFavorite,
                globalFavoriteCount
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        DetailedWorkoutEditorViewModel model,
        bool saveTemplate,
        bool completeWorkout,
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
                new
                {
                    date =
                        model.Date.ToString(
                            "yyyy-MM-dd")
                });
        }

        model.Exercises ??= [];

        if (model.Exercises.Count > 50)
            return BadRequest(
                "A detailed workout may contain at most 50 exercises.");

        var workoutName =
            string.IsNullOrWhiteSpace(model.WorkoutName)
                ? $"Workout - {model.Date:yyyy-MM-dd}"
                : model.WorkoutName.Trim();

        if (workoutName.Length > 120)
            return BadRequest(
                "Workout name may not exceed 120 characters.");

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
            return BadRequest(
                "One or more exercises are no longer active in the exercise library.");

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

            if (input.Sets is < 1 or > 20)
                return BadRequest(
                    "Sets must be between 1 and 20.");

            var setReps =
                ResolveSetReps(input);

            if (setReps.Length != input.Sets)
                return BadRequest(
                    "Enter one rep value for each set.");

            if (setReps.Any(x => x is < 1 or > 500))
                return BadRequest(
                    "Reps must be between 1 and 500.");

            input.Reps =
                setReps[0];

            if (input.MaxWeight is < 0 or > 10000)
                return BadRequest(
                    "Heaviest must be between 0 and 10000.");

            if ((input.Notes?.Length ?? 0) > 1000)
                return BadRequest(
                    "Exercise notes may not exceed 1000 characters.");

            var unit =
                ClientLoadUnit(
                    context.Client.UnitSystemCode);

            input.LoadUnitCode =
                unit;

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
                    string.IsNullOrWhiteSpace(
                        input.Notes)
                        ? null
                        : input.Notes.Trim(),
                    sets,
                    input.MaxWeight,
                    unit));
        }

        if (model.TemplateOnlyMode)
        {
            if (!string.Equals(
                    model.ReturnContext,
                    "schedule",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(
                    "Template-only workout creation is only available from Weekly Plan.");
            }

            if (model.WorkoutSessionId.HasValue)
            {
                return BadRequest(
                    "A Weekly Plan custom workout cannot reuse an actual workout session.");
            }

            if (!saveTemplate)
            {
                return BadRequest(
                    "Weekly Plan custom workouts must be saved to My Workouts.");
            }

            if (completeWorkout)
            {
                return BadRequest(
                    "Planning a custom workout does not complete an actual workout.");
            }

            if (canonicalExercises.Count == 0)
            {
                return BadRequest(
                    "Add at least one exercise before saving a custom workout to Weekly Plan.");
            }

            var requestedType =
                string.IsNullOrWhiteSpace(
                    model.DraftWorkoutTypeCode)
                    ? "MAJOR_MIXED"
                    : model.DraftWorkoutTypeCode.Trim();

            var workoutType =
                await db.WorkoutTypeDefinitions
                    .AsNoTracking()
                    .Where(
                        x =>
                            x.ConfigVersionId
                                == context.Program.ConfigVersionId
                            && x.IsActive
                            && x.IsSelectable)
                    .OrderBy(
                        x =>
                            x.WorkoutTypeCode == requestedType
                                ? 0
                                : x.WorkoutTypeCode == "MAJOR_MIXED"
                                    ? 1
                                    : 2)
                    .ThenBy(x => x.DisplayOrder)
                    .FirstOrDefaultAsync(
                        cancellationToken);

            if (workoutType is null)
            {
                return BadRequest(
                    "No active workout type is available for this custom workout.");
            }

            var templateName =
                string.IsNullOrWhiteSpace(
                    model.TemplateName)
                    ? workoutName
                    : model.TemplateName.Trim();

            if (templateName.Length > 120)
            {
                return BadRequest(
                    "Saved workout name may not exceed 120 characters.");
            }

            var templateSession =
                new WorkoutSession
                {
                    WorkoutTypeCode =
                        workoutType.WorkoutTypeCode,
                    DurationMin =
                        workoutType.DefaultMinutes,
                    TypicalRestSec =
                        workoutType.DefaultRestSec,
                    Notes = null
                };

            var template =
                await SaveTemplateAsync(
                    context.Client.ClientId,
                    templateSession,
                    model.SourceTemplateId,
                    templateName,
                    model.Exercises,
                    cancellationToken);

            var plannedDate =
                model.Date;

            var planned =
                await db.PlannedWorkouts
                    .SingleOrDefaultAsync(
                        x =>
                            x.ProgramId
                                == context.Program.ProgramId
                            && x.PlannedDate
                                == plannedDate,
                        cancellationToken);

            var now =
                DateTime.UtcNow;

            if (planned is null)
            {
                planned =
                    new PlannedWorkout
                    {
                        PlannedWorkoutId =
                            Guid.NewGuid(),
                        ClientId =
                            context.Client.ClientId,
                        ProgramId =
                            context.Program.ProgramId,
                        PlannedDate =
                            plannedDate,
                        WorkoutTypeCode =
                            template.WorkoutTypeCode,
                        WorkoutTemplateId =
                            template.WorkoutTemplateId,
                        SourceCode = "CLIENT",
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };

                db.PlannedWorkouts.Add(
                    planned);
            }
            else
            {
                planned.WorkoutTypeCode =
                    template.WorkoutTypeCode;
                planned.WorkoutTemplateId =
                    template.WorkoutTemplateId;
                planned.SourceCode =
                    "CLIENT";
                planned.UpdatedAtUtc =
                    now;
            }

            await db.SaveChangesAsync(
                cancellationToken);

            TempData["ScheduleStatus"] =
                $"{template.TemplateName} was saved to My Workouts and assigned to {plannedDate:dddd, MMM d}.";

            var returnWeek =
                model.ReturnWeekStart
                ?? WeeklyStrategyService
                    .WeekStart(plannedDate);

            return RedirectToAction(
                "Index",
                "Schedule",
                new
                {
                    weekStart =
                        returnWeek.ToString(
                            "yyyy-MM-dd")
                });
        }

        WorkoutSession? session = null;

        if (model.WorkoutSessionId.HasValue)
        {
            session =
                await db.WorkoutSessions
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

            if (session.CompletedFlag && !model.EditCompleted)
                return BadRequest(
                    "Completed workouts may only be changed through explicit Edit.");

            session.WorkoutName = workoutName;
            session.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            var requestedType =
                string.IsNullOrWhiteSpace(
                    model.DraftWorkoutTypeCode)
                    ? "MAJOR_MIXED"
                    : model.DraftWorkoutTypeCode.Trim();

            var workoutType =
                await db.WorkoutTypeDefinitions
                    .AsNoTracking()
                    .Where(
                        x =>
                            x.ConfigVersionId
                                == context.Program.ConfigVersionId
                            && x.IsActive
                            && x.IsSelectable)
                    .OrderBy(
                        x =>
                            x.WorkoutTypeCode == requestedType
                                ? 0
                                : x.WorkoutTypeCode == "MAJOR_MIXED"
                                    ? 1
                                    : 2)
                    .ThenBy(x => x.DisplayOrder)
                    .FirstOrDefaultAsync(
                        cancellationToken);

            if (workoutType is null)
                return BadRequest(
                    "No active workout type is available for a new workout.");

            var now = DateTime.UtcNow;

            session =
                new WorkoutSession
                {
                    WorkoutSessionId = Guid.NewGuid(),
                    ClientId = context.Client.ClientId,
                    ProgramId = context.Program.ProgramId,
                    WorkoutDate = model.Date,
                    WorkoutName = workoutName,
                    WorkoutTypeCode =
                        workoutType.WorkoutTypeCode,
                    DurationMin =
                        workoutType.DefaultMinutes,
                    TypicalRestSec =
                        workoutType.DefaultRestSec,
                    CompletedFlag = false,
                    EntrySourceCode =
                        RecompInputSourceCodes.DetailedWorkout,
                    CalculationEngineVersion = "3.0",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            db.WorkoutSessions.Add(session);
            await db.SaveChangesAsync(
                cancellationToken);
        }

        await detailedWorkoutStore.ReplaceAsync(
            new DetailedWorkoutSession(
                session.WorkoutSessionId,
                canonicalExercises),
            cancellationToken);

        var preferences =
            await db.ClientExercisePreferences
                .Where(
                    x =>
                        x.ClientId
                            == context.Client.ClientId
                        && exerciseIds.Contains(
                            x.ExerciseCatalogItemId))
                .ToDictionaryAsync(
                    x => x.ExerciseCatalogItemId,
                    cancellationToken);

        var preferenceNow = DateTime.UtcNow;

        foreach (var input in model.Exercises)
        {
            var unit =
                ClientLoadUnit(
                    context.Client.UnitSystemCode);

            var preferenceSetReps =
                ResolveSetReps(input);

            if (!preferences.TryGetValue(
                    input.ExerciseId,
                    out var preference))
            {
                preference =
                    new ClientExercisePreferenceRecord
                    {
                        ClientExercisePreferenceId =
                            Guid.NewGuid(),
                        ClientId =
                            context.Client.ClientId,
                        ExerciseCatalogItemId =
                            input.ExerciseId,
                        IsFavorite = false,
                        CreatedAtUtc = preferenceNow
                    };

                db.ClientExercisePreferences.Add(
                    preference);

                preferences[input.ExerciseId] =
                    preference;
            }

            preference.DefaultSets =
                checked((short)input.Sets);

            preference.DefaultReps =
                checked(
                    (short)(
                        preferenceSetReps.Length > 0
                            ? preferenceSetReps[0]
                            : input.Reps));

            preference.DefaultMaxWeight =
                input.MaxWeight;

            preference.LoadUnitCode = unit;
            preference.UpdatedAtUtc =
                preferenceNow;
        }

        if (completeWorkout && canonicalExercises.Count == 0)
            return BadRequest(
                "Add at least one exercise before completing the workout.");

        if (completeWorkout)
        {
            session.CompletedFlag = true;
            session.EntrySourceCode =
                RecompInputSourceCodes.DetailedWorkout;
            session.CalculationEngineVersion = "3.0";
            session.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (saveTemplate)
        {
            var templateName =
                string.IsNullOrWhiteSpace(model.TemplateName)
                    ? workoutName
                    : model.TemplateName.Trim();

            if (templateName.Length > 120)
                return BadRequest(
                    "Saved workout name may not exceed 120 characters.");

            var template =
                await SaveTemplateAsync(
                    context.Client.ClientId,
                    session,
                    model.SourceTemplateId,
                    templateName,
                    model.Exercises,
                    cancellationToken);

            session.WorkoutTemplateId =
                template.WorkoutTemplateId;
            session.WorkoutName =
                template.TemplateName;
        }

        await db.SaveChangesAsync(
            cancellationToken);

        if (completeWorkout)
        {
            var actual =
                await engine30Projection.BuildActualWorkoutAsync(
                    context,
                    session.WorkoutDate,
                    cancellationToken);

            TempData["StatusMessage"] =
                "Workout completed. Engine 3.0 recalculated the day from Workout Builder data.";

            TempData["WorkoutEngineSummary"] =
                actual.WorkoutSummary
                ?? workoutName;

            TempData["WorkoutEngineDuration"] =
                actual.TotalDurationMinutes.ToString("0");

            TempData["WorkoutEngineNetCalories"] =
                actual.NetWorkoutEnergyKcal.ToString("0");

            TempData["WorkoutEngineGlycogen"] =
                actual.GlycogenDemandG.ToString("0");

            if (string.Equals(
                    model.ReturnContext,
                    "daily",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction(
                    "Index",
                    "DailyEntry",
                    new
                    {
                        date =
                            session.WorkoutDate.ToString(
                                "yyyy-MM-dd")
                    });
            }

            return RedirectToAction(
                nameof(Index),
                new
                {
                    date =
                        session.WorkoutDate.ToString(
                            "yyyy-MM-dd")
                });
        }

        TempData["StatusMessage"] =
            saveTemplate
                ? "Workout saved and added to Saved Workout."
                : "Workout saved.";

        return RedirectToAction(
            nameof(Index),
            new
            {
                mode = "new",
                workoutSessionId =
                    session.WorkoutSessionId,
                date =
                    session.WorkoutDate.ToString(
                        "yyyy-MM-dd"),
                editCompleted =
                    session.CompletedFlag
                    && model.EditCompleted,
                returnContext =
                    model.ReturnContext
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UseTemplate(
        Guid templateId,
        DateOnly date,
        string? returnContext,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!context.Program.DetailedWorkoutEntryEnabled)
            return StatusCode(403);

        var template =
            await db.WorkoutTemplates
                .AsNoTracking()
                .Include(x => x.Exercises)
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutTemplateId == templateId
                        && x.ClientId == context.Client.ClientId,
                    cancellationToken);

        if (template is null)
            return NotFound();

        var workoutTypeValid =
            await db.WorkoutTypeDefinitions
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ConfigVersionId
                            == context.Program.ConfigVersionId
                        && x.WorkoutTypeCode
                            == template.WorkoutTypeCode
                        && x.IsActive,
                    cancellationToken);

        if (!workoutTypeValid)
            return BadRequest(
                "This saved workout uses a workout type that is no longer active.");

        var existingOpenSession =
            await db.WorkoutSessions
                .AsNoTracking()
                .Where(
                    x =>
                        x.ClientId
                            == context.Client.ClientId
                        && x.ProgramId
                            == context.Program.ProgramId
                        && x.WorkoutDate == date
                        && x.WorkoutTemplateId
                            == template.WorkoutTemplateId
                        && !x.CompletedFlag)
                .OrderByDescending(
                    x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync(
                    cancellationToken);

        if (existingOpenSession is not null)
        {
            TempData["StatusMessage"] =
                $"{template.TemplateName} is already open for this date. Reopening it instead of creating a duplicate.";

            return RedirectToAction(
                nameof(Index),
                new
                {
                    mode = "new",
                    workoutSessionId =
                        existingOpenSession.WorkoutSessionId,
                    date =
                        date.ToString("yyyy-MM-dd"),
                    returnContext =
                        string.Equals(
                            returnContext,
                            "daily",
                            StringComparison.OrdinalIgnoreCase)
                            ? "daily"
                            : null
                });
        }

        var now = DateTime.UtcNow;

        var session =
            new WorkoutSession
            {
                WorkoutSessionId = Guid.NewGuid(),
                ClientId = context.Client.ClientId,
                ProgramId = context.Program.ProgramId,
                WorkoutDate = date,
                WorkoutName = template.TemplateName,
                WorkoutTemplateId = template.WorkoutTemplateId,
                WorkoutTypeCode = template.WorkoutTypeCode,
                DurationMin = template.DefaultDurationMin,
                CompletedFlag = false,
                EntrySourceCode =
                    RecompInputSourceCodes.DetailedWorkout,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        db.WorkoutSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        var detailed =
            new DetailedWorkoutSession(
                session.WorkoutSessionId,
                template.Exercises
                    .OrderBy(x => x.ExerciseOrder)
                    .Select(
                        x =>
                            new DetailedWorkoutExerciseEntry(
                                Guid.Empty,
                                x.ExerciseCatalogItemId,
                                x.ExerciseOrder,
                                x.ExerciseNameSnapshot,
                                x.MechanicsSnapshot,
                                x.Notes,
                                ParseTemplateSetReps(
                                        x.SetRepsJson,
                                        x.Sets,
                                        x.Reps)
                                    .Select(
                                        (reps, setIndex) =>
                                            new DetailedWorkoutSetEntry(
                                                Guid.Empty,
                                                setIndex + 1,
                                                reps,
                                                null,
                                                NormalizeLoadUnit(
                                                    x.LoadUnitCode,
                                                    "LB"),
                                                null,
                                                null,
                                                null,
                                                null,
                                                false,
                                                null))
                                    .ToArray(),
                                x.MaxWeight,
                                NormalizeLoadUnit(
                                    x.LoadUnitCode,
                                    "LB")))
                    .ToArray());

        await detailedWorkoutStore.ReplaceAsync(
            detailed,
            cancellationToken);

        TempData["StatusMessage"] =
            $"Started {template.TemplateName}.";

        return RedirectToAction(
            nameof(Index),
            new
            {
                mode = "new",
                workoutSessionId =
                    session.WorkoutSessionId,
                date =
                    date.ToString("yyyy-MM-dd"),
                returnContext =
                    string.Equals(
                        returnContext,
                        "daily",
                        StringComparison.OrdinalIgnoreCase)
                        ? "daily"
                        : null
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTemplate(
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
            return StatusCode(403);

        if (!model.SelectedTemplateId.HasValue)
            return BadRequest(
                "Choose a saved workout to modify.");

        model.Exercises ??= [];

        if (model.Exercises.Count > 50)
            return BadRequest(
                "A saved workout may contain at most 50 exercises.");

        var templateName =
            string.IsNullOrWhiteSpace(model.TemplateName)
                ? model.WorkoutName?.Trim()
                : model.TemplateName.Trim();

        if (string.IsNullOrWhiteSpace(templateName))
            return BadRequest(
                "Saved workout name is required.");

        if (templateName.Length > 120)
            return BadRequest(
                "Saved workout name may not exceed 120 characters.");

        var template =
            await db.WorkoutTemplates
                .Include(x => x.Exercises)
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutTemplateId
                            == model.SelectedTemplateId.Value
                        && x.ClientId
                            == context.Client.ClientId,
                    cancellationToken);

        if (template is null)
            return NotFound();

        var duplicateName =
            await db.WorkoutTemplates
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ClientId
                            == context.Client.ClientId
                        && x.WorkoutTemplateId
                            != template.WorkoutTemplateId
                        && x.TemplateName
                            == templateName,
                    cancellationToken);

        if (duplicateName)
            return BadRequest(
                "Another saved workout already uses that name.");

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
            return BadRequest(
                "One or more exercises are no longer active in the exercise library.");

        foreach (var input in model.Exercises)
        {
            if (!catalog.TryGetValue(
                    input.ExerciseId,
                    out _))
            {
                return BadRequest(
                    "Exercise library validation failed.");
            }

            if (input.Sets is < 1 or > 20)
                return BadRequest(
                    "Sets must be between 1 and 20.");

            var setReps =
                ResolveSetReps(input);

            if (setReps.Length != input.Sets)
                return BadRequest(
                    "Enter one rep value for each set.");

            if (setReps.Any(x => x is < 1 or > 500))
                return BadRequest(
                    "Reps must be between 1 and 500.");

            input.Reps =
                setReps[0];

            if (input.MaxWeight is < 0 or > 10000)
                return BadRequest(
                    "Heaviest must be between 0 and 10000.");
        }

        var now = DateTime.UtcNow;

        template.TemplateName = templateName;
        template.UpdatedAtUtc = now;

        SynchronizeTemplateExercises(
            template,
            model.Exercises,
            model.DefaultLoadUnitCode,
            now);

        await db.SaveChangesAsync(
            cancellationToken);

        TempData["StatusMessage"] =
            "Saved workout updated.";

        return RedirectToAction(
            nameof(Index),
            new
            {
                mode = "saved",
                templateId =
                    template.WorkoutTemplateId,
                date =
                    model.Date.ToString(
                        "yyyy-MM-dd"),
                returnContext =
                    model.ReturnContext
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UseSystemWorkout(
        string systemCode,
        DateOnly date,
        string? returnContext,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!context.Program.DetailedWorkoutEntryEnabled)
            return StatusCode(403);

        var engineWorkoutTypeCode =
            SystemWorkoutRecipeCatalog.ResolveEngineWorkoutTypeCode(
                systemCode);

        var systemConfigVersionId =
            await GetSystemWorkoutConfigVersionIdAsync(
                cancellationToken);

        if (!systemConfigVersionId.HasValue)
            return NotFound();

        var workoutType =
            await db.WorkoutTypeDefinitions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.ConfigVersionId
                            == systemConfigVersionId.Value
                        && x.WorkoutTypeCode
                            == engineWorkoutTypeCode
                        && x.IsActive
                        && x.IsSelectable,
                    cancellationToken);

        if (workoutType is null)
            return NotFound();

        var preview =
            await BuildSystemWorkoutPreviewAsync(
                context.Client.ClientId,
                context.Program.ConfigVersionId,
                systemCode,
                string.Equals(
                    context.Client.UnitSystemCode,
                    "METRIC",
                    StringComparison.OrdinalIgnoreCase)
                    ? "KG"
                    : "LB",
                cancellationToken);

        if (preview is null)
            return NotFound();

        var now = DateTime.UtcNow;

        var session =
            new WorkoutSession
            {
                WorkoutSessionId = Guid.NewGuid(),
                ClientId = context.Client.ClientId,
                ProgramId = context.Program.ProgramId,
                WorkoutDate = date,
                WorkoutName =
                    preview.DisplayName,
                SystemWorkoutCode =
                    preview.SystemCode,
                // System Workouts are canonical Engine 3.0 recipes. Store the
                // canonical Engine workout code even when the client program
                // points at an older configuration version.
                WorkoutTypeCode =
                    engineWorkoutTypeCode,
                DurationMin =
                    preview.DefaultMinutes,
                TypicalRestSec =
                    workoutType.DefaultRestSec,
                SessionRpe =
                    preview.DefaultRpe,
                CompletedFlag = false,
                EntrySourceCode =
                    RecompInputSourceCodes.DetailedWorkout,
                CalculationEngineVersion = "3.0",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        db.WorkoutSessions.Add(session);
        await db.SaveChangesAsync(
            cancellationToken);

        var treatSystemSetsAsRecovery =
            string.Equals(
                preview.WorkoutTypeCode,
                "MOBILITY_RECOVERY",
                StringComparison.OrdinalIgnoreCase);

        var detailedExercises =
            preview.Exercises
                .Select(
                    (input, index) =>
                    {
                        var setReps =
                            ResolveSetReps(
                                input);

                        return new DetailedWorkoutExerciseEntry(
                            Guid.Empty,
                            input.ExerciseId,
                            index + 1,
                            input.ExerciseName,
                            input.Mechanics,
                            input.Notes,
                            Enumerable.Range(
                                    1,
                                    Math.Clamp(
                                        input.Sets,
                                        1,
                                        20))
                                .Select(
                                    setOrder =>
                                        new DetailedWorkoutSetEntry(
                                            Guid.Empty,
                                            setOrder,
                                            setReps[
                                                Math.Min(
                                                    setOrder - 1,
                                                    setReps.Length - 1)],
                                            null,
                                            NormalizeLoadUnit(
                                                input.LoadUnitCode,
                                                preview.Exercises.Count > 0
                                                    ? input.LoadUnitCode
                                                    : "LB"),
                                            null,
                                            null,
                                            null,
                                            null,
                                            treatSystemSetsAsRecovery,
                                            null))
                                .ToArray(),
                            input.MaxWeight,
                            NormalizeLoadUnit(
                                input.LoadUnitCode,
                                "LB"));
                    })
                .ToArray();

        await detailedWorkoutStore.ReplaceAsync(
            new DetailedWorkoutSession(
                session.WorkoutSessionId,
                detailedExercises),
            cancellationToken);

        TempData["StatusMessage"] =
            $"Selected {preview.DisplayName}. Modify it below if you want.";

        return RedirectToAction(
            nameof(Index),
            new
            {
                mode = "new",
                workoutSessionId =
                    session.WorkoutSessionId,
                date =
                    date.ToString("yyyy-MM-dd"),
                returnContext =
                    string.Equals(
                        returnContext,
                        "daily",
                        StringComparison.OrdinalIgnoreCase)
                        ? "daily"
                        : null
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTemplate(
        Guid templateId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!context.Program.DetailedWorkoutEntryEnabled)
            return StatusCode(403);

        var template =
            await db.WorkoutTemplates
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutTemplateId == templateId
                        && x.ClientId == context.Client.ClientId,
                    cancellationToken);

        if (template is null)
            return NotFound();

        db.WorkoutTemplates.Remove(template);
        await db.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] =
            "Saved workout deleted. Workout history was not changed.";

        return RedirectToAction(
            nameof(Index),
            new
            {
                mode = "saved",
                date =
                    date.ToString("yyyy-MM-dd")
            });
    }

    private async Task<WorkoutTemplateRecord> SaveTemplateAsync(
        Guid clientId,
        WorkoutSession session,
        Guid? sourceTemplateId,
        string templateName,
        IReadOnlyList<DetailedWorkoutExerciseInputModel> exercises,
        CancellationToken cancellationToken)
    {
        WorkoutTemplateRecord? template = null;

        if (sourceTemplateId.HasValue)
        {
            template =
                await db.WorkoutTemplates
                    .Include(x => x.Exercises)
                    .SingleOrDefaultAsync(
                        x =>
                            x.WorkoutTemplateId
                                == sourceTemplateId.Value
                            && x.ClientId == clientId,
                        cancellationToken);
        }

        if (template is null)
        {
            template =
                await db.WorkoutTemplates
                    .Include(x => x.Exercises)
                    .SingleOrDefaultAsync(
                        x =>
                            x.ClientId == clientId
                            && x.TemplateName == templateName,
                        cancellationToken);
        }

        var now = DateTime.UtcNow;

        if (template is null)
        {
            template =
                new WorkoutTemplateRecord
                {
                    WorkoutTemplateId = Guid.NewGuid(),
                    ClientId = clientId,
                    TemplateName = templateName,
                    WorkoutTypeCode =
                        session.WorkoutTypeCode,
                    DefaultDurationMin =
                        session.DurationMin,
                    Notes = session.Notes,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            db.WorkoutTemplates.Add(template);
        }
        else
        {
            template.TemplateName = templateName;
            template.WorkoutTypeCode =
                session.WorkoutTypeCode;
            template.DefaultDurationMin =
                session.DurationMin;
            template.Notes = session.Notes;
            template.UpdatedAtUtc = now;

        }

        SynchronizeTemplateExercises(
            template,
            exercises,
            "LB",
            now);

        return template;
    }

    private void SynchronizeTemplateExercises(
        WorkoutTemplateRecord template,
        IReadOnlyList<DetailedWorkoutExerciseInputModel> exercises,
        string defaultLoadUnitCode,
        DateTime now)
    {
        var existing =
            template.Exercises
                .OrderBy(x => x.ExerciseOrder)
                .ThenBy(x => x.CreatedAtUtc)
                .ToList();

        for (var i = 0; i < exercises.Count; i++)
        {
            var input = exercises[i];
            var setReps = ResolveSetReps(input);

            WorkoutTemplateExerciseRecord row;

            if (i < existing.Count)
            {
                row = existing[i];
            }
            else
            {
                row =
                    new WorkoutTemplateExerciseRecord
                    {
                        WorkoutTemplateExerciseId =
                            Guid.NewGuid(),
                        WorkoutTemplateId =
                            template.WorkoutTemplateId,
                        ExerciseNameSnapshot =
                            input.ExerciseName,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };

                template.Exercises.Add(row);
            }

            row.ExerciseCatalogItemId =
                input.ExerciseId;
            row.ExerciseOrder =
                checked((short)(i + 1));
            row.ExerciseNameSnapshot =
                input.ExerciseName;
            row.MechanicsSnapshot =
                input.Mechanics;
            row.Sets =
                checked((short)input.Sets);
            row.Reps =
                checked((short)setReps[0]);
            row.SetRepsJson =
                JsonSerializer.Serialize(setReps);
            row.MaxWeight =
                input.MaxWeight;
            row.LoadUnitCode =
                NormalizeLoadUnit(
                    input.LoadUnitCode,
                    defaultLoadUnitCode);
            row.Notes =
                string.IsNullOrWhiteSpace(input.Notes)
                    ? null
                    : input.Notes.Trim();
            row.UpdatedAtUtc = now;
        }

        if (existing.Count > exercises.Count)
        {
            var obsolete =
                existing
                    .Skip(exercises.Count)
                    .ToArray();

            db.WorkoutTemplateExercises.RemoveRange(
                obsolete);

            foreach (var row in obsolete)
                template.Exercises.Remove(row);
        }
    }


    private async Task PopulateHeaderAnalysisAsync(
        DetailedWorkoutEditorViewModel model,
        AppClientContext context,
        DateOnly targetDate,
        Guid? selectedWorkoutSessionId,
        CancellationToken cancellationToken)
    {
        model.HeaderAnalysis =
            new WorkoutHeaderAnalysisViewModel();

        var sessions =
            await db.WorkoutSessions
                .AsNoTracking()
                .Where(
                    x =>
                        x.ClientId == context.Client.ClientId
                        && x.ProgramId == context.Program.ProgramId
                        && x.WorkoutDate == targetDate)
                .OrderBy(x => x.CreatedAtUtc)
                .ToListAsync(cancellationToken);

        if (sessions.Count == 0)
            return;

        var sessionIds =
            sessions
                .Select(x => x.WorkoutSessionId)
                .ToArray();

        var detailedSessionIds =
            await db.DetailedWorkoutExercises
                .AsNoTracking()
                .Where(
                    x =>
                        sessionIds.Contains(
                            x.WorkoutSessionId))
                .Select(x => x.WorkoutSessionId)
                .Distinct()
                .ToListAsync(cancellationToken);

        var detailedSet =
            detailedSessionIds.ToHashSet();

        var completedDetailed =
            sessions
                .Where(
                    x =>
                        x.CompletedFlag
                        && detailedSet.Contains(
                            x.WorkoutSessionId)
                        && string.Equals(
                            x.EntrySourceCode,
                            RecompInputSourceCodes.DetailedWorkout,
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

        Guid[] analysisSessionIds;
        decimal analysisDuration;
        string analysisScope;

        var selected =
            selectedWorkoutSessionId.HasValue
                ? sessions.SingleOrDefault(
                    x =>
                        x.WorkoutSessionId
                        == selectedWorkoutSessionId.Value)
                : null;

        if (selected is not null
            && detailedSet.Contains(
                selected.WorkoutSessionId))
        {
            analysisSessionIds =
            [
                selected.WorkoutSessionId
            ];

            analysisDuration =
                selected.DurationMin;

            analysisScope =
                selected.CompletedFlag
                    ? "Selected completed workout"
                    : "Current workout";
        }
        else if (completedDetailed.Length > 0)
        {
            analysisSessionIds =
                completedDetailed
                    .Select(x => x.WorkoutSessionId)
                    .ToArray();

            analysisDuration =
                completedDetailed
                    .Sum(x => x.DurationMin);

            analysisScope =
                completedDetailed.Length == 1
                    ? "Completed workout"
                    : $"{completedDetailed.Length} completed workouts";
        }
        else
        {
            analysisSessionIds =
                Array.Empty<Guid>();

            analysisDuration =
                0m;

            analysisScope =
                "";
        }

        if (analysisSessionIds.Length > 0)
        {
            var detailed =
                await db.DetailedWorkoutExercises
                    .AsNoTracking()
                    .Include(x => x.Sets)
                    .Where(
                        x =>
                            analysisSessionIds.Contains(
                                x.WorkoutSessionId))
                    .OrderBy(x => x.WorkoutSessionId)
                    .ThenBy(x => x.ExerciseOrder)
                    .ToListAsync(cancellationToken);

            var exerciseIds =
                detailed
                    .Select(
                        x =>
                            x.ExerciseCatalogItemId)
                    .Distinct()
                    .ToArray();

            var targets =
                await db.ExerciseMuscleTargets
                    .AsNoTracking()
                    .Where(
                        x =>
                            exerciseIds.Contains(
                                x.ExerciseCatalogItemId))
                    .OrderBy(x => x.DisplayOrder)
                    .ToListAsync(cancellationToken);

            var targetsByExercise =
                targets
                    .GroupBy(
                        x =>
                            x.ExerciseCatalogItemId)
                    .ToDictionary(
                        x => x.Key,
                        x =>
                            x.Select(
                                    target =>
                                        new MuscleTargetInput(
                                            target.MuscleSlug,
                                            target.RoleCode))
                                .ToArray());

            var muscleNames =
                targets
                    .Where(
                        x =>
                            !string.IsNullOrWhiteSpace(
                                x.MuscleSlug))
                    .GroupBy(
                        x => x.MuscleSlug,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        x => x.Key,
                        x =>
                            x.Select(
                                    target =>
                                        target.MuscleName)
                                .FirstOrDefault(
                                    name =>
                                        !string.IsNullOrWhiteSpace(
                                            name))
                            ?? x.Key,
                        StringComparer.OrdinalIgnoreCase);

            var request =
                new WorkoutIntelligenceRequest(
                    SessionKey:
                        $"header-{targetDate:yyyy-MM-dd}",
                    DurationMinutes:
                        Math.Max(
                            1,
                            (int)Math.Round(
                                analysisDuration,
                                0,
                                MidpointRounding.AwayFromZero)),
                    Exercises:
                        detailed.Select(
                                exercise =>
                                    new WorkoutExerciseInput(
                                        ExerciseId:
                                            exercise.ExerciseCatalogItemId,
                                        ExerciseName:
                                            exercise.ExerciseNameSnapshot,
                                        Mechanics:
                                            exercise.MechanicsSnapshot,
                                        MuscleTargets:
                                            targetsByExercise.TryGetValue(
                                                exercise.ExerciseCatalogItemId,
                                                out var exerciseTargets)
                                                ? exerciseTargets
                                                : Array.Empty<MuscleTargetInput>(),
                                        Sets:
                                            exercise.Sets
                                                .OrderBy(
                                                    set =>
                                                        set.SetOrder)
                                                .Select(
                                                    set =>
                                                        new WorkoutSetInput(
                                                            Reps:
                                                                set.Reps,
                                                            Load:
                                                                set.Load,
                                                            Rpe:
                                                                set.Rpe,
                                                            Rir:
                                                                set.Rir,
                                                            RestSeconds:
                                                                set.RestSeconds,
                                                            DurationSeconds:
                                                                set.DurationSeconds,
                                                            IsWarmup:
                                                                set.IsWarmup))
                                                .ToArray()))
                            .ToArray());

            var handoff =
                workoutIntelligence.Analyze(
                    request);

            model.HeaderAnalysis.HasDetailedAnalysis =
                handoff.ExerciseCount > 0;

            model.HeaderAnalysis.HardSets =
                handoff.HardSets;

            model.HeaderAnalysis.MuscleDemandSummary =
                string.Join(
                    " · ",
                    handoff.MuscleDemand
                        .Take(3)
                        .Select(
                            muscle =>
                            {
                                var displayName =
                                    muscleNames.TryGetValue(
                                        muscle.MuscleSlug,
                                        out var name)
                                        ? name
                                        : muscle.MuscleSlug
                                            .Replace(
                                                '-',
                                                ' ');

                                return
                                    $"{displayName} {muscle.WeightedSetEquivalents:0.#}";
                            }));

            model.HeaderAnalysis.SourceLabel =
                "Workout Builder";

            model.HeaderAnalysis.ScopeLabel =
                analysisScope;
        }

        var completedExists =
            sessions.Any(
                x => x.CompletedFlag);

        if (!completedExists)
        {
            if (!model.HeaderAnalysis.HasDetailedAnalysis)
            {
                model.HeaderAnalysis.SourceLabel =
                    "Build or open a workout";
            }

            return;
        }

        var engineActual =
            await engine30Projection
                .BuildActualWorkoutAsync(
                    context,
                    targetDate,
                    cancellationToken);

        if (!engineActual.HasActualWorkout)
            return;

        model.HeaderAnalysis.HasEngineResult =
            true;

        model.HeaderAnalysis.EstimatedCalories =
            engineActual.NetWorkoutEnergyKcal;

        model.HeaderAnalysis.GlycogenDemandG =
            engineActual.GlycogenDemandG;

        if (completedDetailed.Length > 0)
        {
            model.HeaderAnalysis.SourceLabel =
                "Workout Builder";
        }
        else if (!model.HeaderAnalysis.HasDetailedAnalysis)
        {
            model.HeaderAnalysis.SourceLabel =
                "Daily Entry";

            model.HeaderAnalysis.ScopeLabel =
                "Completed workout";
        }
    }

    private static string NormalizeWorkspaceMode(
        string? requested,
        bool hasWorkoutSession)
    {
        if (hasWorkoutSession)
            return "new";

        var normalized =
            requested?.Trim().ToLowerInvariant();

        return normalized is "new" or "saved" or "system"
            ? normalized
            : "";
    }

    private static string ResolveDefaultCustomWorkoutTypeCode(
        IReadOnlyList<WorkoutBuilderTypeOptionViewModel> workoutTypes)
    {
        return workoutTypes
                   .FirstOrDefault(
                       x =>
                           string.Equals(
                               x.DisplayName,
                               "Standard Full Body Strength",
                               StringComparison.OrdinalIgnoreCase))
                   ?.EngineWorkoutTypeCode
               ?? workoutTypes
                   .FirstOrDefault(
                       x =>
                           string.Equals(
                               x.DisplayName,
                               "Standard Accessory",
                               StringComparison.OrdinalIgnoreCase))
                   ?.EngineWorkoutTypeCode
               ?? workoutTypes.FirstOrDefault()
                   ?.EngineWorkoutTypeCode
               ?? "FULL_BODY_STRENGTH";
    }

    private static List<DetailedWorkoutExerciseInputModel> CloneExercises(
        IEnumerable<DetailedWorkoutExerciseInputModel> source)
    {
        return source
            .Select(
                x =>
                    new DetailedWorkoutExerciseInputModel
                    {
                        ExerciseId = x.ExerciseId,
                        ExerciseName = x.ExerciseName,
                        Mechanics = x.Mechanics,
                        Notes = x.Notes,
                        Sets = x.Sets,
                        Reps = x.Reps,
                        SetReps =
                            x.SetReps.ToList(),
                        MaxWeight = x.MaxWeight,
                        LoadUnitCode = x.LoadUnitCode
                    })
            .ToList();
    }

    private async Task<WorkoutTemplateDetailViewModel?> GetTemplateDetailAsync(
        Guid clientId,
        Guid templateId,
        string defaultLoadUnitCode,
        CancellationToken cancellationToken)
    {
        var template =
            await db.WorkoutTemplates
                .AsNoTracking()
                .Include(x => x.Exercises)
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutTemplateId
                            == templateId
                        && x.ClientId == clientId,
                    cancellationToken);

        if (template is null)
            return null;

        return new WorkoutTemplateDetailViewModel
        {
            WorkoutTemplateId =
                template.WorkoutTemplateId,
            TemplateName =
                template.TemplateName,
            WorkoutTypeCode =
                template.WorkoutTypeCode,
            DefaultDurationMin =
                template.DefaultDurationMin,
            Exercises =
                template.Exercises
                    .OrderBy(x => x.ExerciseOrder)
                    .Select(
                        x =>
                            new DetailedWorkoutExerciseInputModel
                            {
                                ExerciseId =
                                    x.ExerciseCatalogItemId,
                                ExerciseName =
                                    x.ExerciseNameSnapshot,
                                Mechanics =
                                    x.MechanicsSnapshot,
                                Notes =
                                    x.Notes,
                                Sets =
                                    Math.Clamp(
                                        (int)x.Sets,
                                        1,
                                        20),
                                Reps =
                                    Math.Clamp(
                                        (int)x.Reps,
                                        1,
                                        500),
                                SetReps =
                                    ParseTemplateSetReps(
                                            x.SetRepsJson,
                                            x.Sets,
                                            x.Reps)
                                        .ToList(),
                                MaxWeight =
                                    x.MaxWeight,
                                LoadUnitCode =
                                    NormalizeLoadUnit(
                                        x.LoadUnitCode,
                                        defaultLoadUnitCode)
                            })
                    .ToList()
        };
    }

    private async Task<SystemWorkoutPreviewViewModel?> BuildSystemWorkoutPreviewAsync(
        Guid clientId,
        Guid configVersionId,
        string systemCode,
        string defaultLoadUnitCode,
        CancellationToken cancellationToken)
    {
        SystemWorkoutRecipeCatalog.TryGet(
            systemCode,
            out var recipe);

        var engineWorkoutTypeCode =
            recipe?.EngineWorkoutTypeCode
            ?? systemCode;

        // System Workouts are a platform-level Engine 3.0 library, not a
        // reflection of whichever historical config version a client program
        // happens to reference. Always resolve their prescription metadata
        // from the authoritative CFG-3000.0 System Workout configuration.
        _ = configVersionId;

        var systemConfigVersionId =
            await GetSystemWorkoutConfigVersionIdAsync(
                cancellationToken);

        if (!systemConfigVersionId.HasValue)
            return null;

        var workoutType =
            await db.WorkoutTypeDefinitions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.ConfigVersionId
                            == systemConfigVersionId.Value
                        && x.WorkoutTypeCode
                            == engineWorkoutTypeCode
                        && x.IsActive
                        && x.IsSelectable,
                    cancellationToken);

        if (workoutType is null)
            return null;

        var slots =
            recipe?.Slots
            ?? Array.Empty<SystemWorkoutExerciseSlot>();

        // Recovery, pure cardio, interval-cardio, and class presets can be
        // duration/intensity driven. Do not invent rep-based exercise rows
        // merely to make every System Workout look populated.
        if (slots.Count == 0)
        {
            var emptyRecipeEngineCatalog =
                new Engine30WorkoutTypeCatalog();

            var emptyRecipeBaseRpe =
                emptyRecipeEngineCatalog.TryGet(
                    engineWorkoutTypeCode,
                    out var emptyRecipeEngineDefinition)
                && emptyRecipeEngineDefinition is not null
                    ? emptyRecipeEngineDefinition.DefaultRpe
                    : 5m;

            return new SystemWorkoutPreviewViewModel
            {
                SystemCode = systemCode,
                WorkoutTypeCode =
                    engineWorkoutTypeCode,
                DisplayName =
                    recipe?.DisplayName
                    ?? workoutType.DisplayName,
                Description =
                    recipe?.Description
                    ?? workoutType.Description,
                GroupName =
                    recipe?.GroupName
                    ?? "Standard",
                DefaultMinutes =
                    recipe is null
                        ? workoutType.DefaultMinutes
                        : SystemWorkoutRecipeCatalog
                            .ResolveDefaultMinutes(
                                recipe,
                                workoutType.DefaultMinutes),
                DefaultRpe =
                    recipe is null
                        ? emptyRecipeBaseRpe
                        : SystemWorkoutRecipeCatalog
                            .ResolveDefaultRpe(
                                recipe,
                                emptyRecipeBaseRpe),
                Exercises = []
            };
        }

        var candidates =
            await db.ExerciseCatalogItems
                .AsNoTracking()
                .Include(x => x.MuscleTargets)
                .Where(x => x.IsActive)
                .Take(1000)
                .ToListAsync(
                    cancellationToken);

        var candidateIds =
            candidates
                .Select(
                    x =>
                        x.ExerciseCatalogItemId)
                .ToArray();

        var candidatePreferences =
            await db.ClientExercisePreferences
                .AsNoTracking()
                .Where(
                    x =>
                        x.ClientId == clientId
                        && candidateIds.Contains(
                            x.ExerciseCatalogItemId))
                .ToDictionaryAsync(
                    x => x.ExerciseCatalogItemId,
                    cancellationToken);

        var candidateUseCounts =
            await db.DetailedWorkoutExercises
                .AsNoTracking()
                .Join(
                    db.WorkoutSessions
                        .AsNoTracking()
                        .Where(
                            x =>
                                x.ClientId == clientId),
                    exercise =>
                        exercise.WorkoutSessionId,
                    session =>
                        session.WorkoutSessionId,
                    (exercise, session) =>
                        exercise)
                .Where(
                    x =>
                        candidateIds.Contains(
                            x.ExerciseCatalogItemId))
                .GroupBy(
                    x => x.ExerciseCatalogItemId)
                .Select(
                    g =>
                        new
                        {
                            ExerciseId = g.Key,
                            Count = g.Count()
                        })
                .ToDictionaryAsync(
                    x => x.ExerciseId,
                    x => x.Count,
                    cancellationToken);

        var candidateGlobalFavoriteCounts =
            await db.ClientExercisePreferences
                .AsNoTracking()
                .Where(
                    x =>
                        x.IsFavorite
                        && candidateIds.Contains(
                            x.ExerciseCatalogItemId))
                .GroupBy(
                    x => x.ExerciseCatalogItemId)
                .Select(
                    g =>
                        new
                        {
                            ExerciseId = g.Key,
                            Count = g.Count()
                        })
                .ToDictionaryAsync(
                    x => x.ExerciseId,
                    x => x.Count,
                    cancellationToken);

        var selected =
            new List<(ExerciseCatalogItemRecord Exercise, SystemWorkoutExerciseSlot Slot)>();

        var selectedIds =
            new HashSet<Guid>();

        foreach (var slot in slots)
        {
            var match =
                candidates
                    .Where(
                        candidate =>
                            !selectedIds.Contains(
                                candidate.ExerciseCatalogItemId))
                    .Select(
                        candidate =>
                            new
                            {
                                Exercise = candidate,
                                Score =
                                    SystemRecipeExerciseScore(
                                        candidate,
                                        slot,
                                        candidatePreferences,
                                        candidateUseCounts,
                                        candidateGlobalFavoriteCounts)
                            })
                    .Where(x => x.Score > int.MinValue)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Exercise.Name)
                    .FirstOrDefault();

            if (match is null)
                continue;

            selected.Add(
                (match.Exercise, slot));

            selectedIds.Add(
                match.Exercise.ExerciseCatalogItemId);
        }

        var exercises =
            selected
                .Select(
                    item =>
                    {
                        candidatePreferences.TryGetValue(
                            item.Exercise.ExerciseCatalogItemId,
                            out var preference);

                        var setCount =
                            Math.Clamp(
                                item.Slot.Sets,
                                1,
                                20);

                        var reps =
                            Math.Clamp(
                                item.Slot.Reps,
                                1,
                                500);

                        return new DetailedWorkoutExerciseInputModel
                        {
                            ExerciseId =
                                item.Exercise.ExerciseCatalogItemId,
                            ExerciseName =
                                item.Exercise.Name,
                            Mechanics =
                                item.Exercise.Mechanics,
                            Notes =
                                item.Slot.Notes,
                            Sets =
                                setCount,
                            Reps =
                                reps,
                            SetReps =
                                Enumerable.Repeat(
                                        reps,
                                        setCount)
                                    .ToList(),
                            MaxWeight =
                                preference?.DefaultMaxWeight,
                            LoadUnitCode =
                                NormalizeLoadUnit(
                                    preference?.LoadUnitCode,
                                    defaultLoadUnitCode)
                        };
                    })
                .ToList();

        var engineCatalog =
            new Engine30WorkoutTypeCatalog();

        var baseRpe =
            engineCatalog.TryGet(
                engineWorkoutTypeCode,
                out var engineDefinition)
            && engineDefinition is not null
                ? engineDefinition.DefaultRpe
                : 5m;

        return new SystemWorkoutPreviewViewModel
        {
            SystemCode = systemCode,
            WorkoutTypeCode =
                engineWorkoutTypeCode,
            DisplayName =
                recipe?.DisplayName
                ?? workoutType.DisplayName,
            Description =
                recipe?.Description
                ?? workoutType.Description,
            GroupName =
                recipe?.GroupName
                ?? "Standard",
            DefaultMinutes =
                recipe is null
                    ? workoutType.DefaultMinutes
                    : SystemWorkoutRecipeCatalog
                        .ResolveDefaultMinutes(
                            recipe,
                            workoutType.DefaultMinutes),
            DefaultRpe =
                recipe is null
                    ? baseRpe
                    : SystemWorkoutRecipeCatalog
                        .ResolveDefaultRpe(
                            recipe,
                            baseRpe),
            Exercises =
                exercises
        };
    }

    private async Task PopulateWorkoutTypesAsync(
        DetailedWorkoutEditorViewModel model,
        Guid configVersionId,
        CancellationToken cancellationToken)
    {
        // System Workouts are platform-level Engine 3.0 recipes. The client
        // program can legitimately reference an older config version, so using
        // its ConfigVersionId here can make the canonical System Workout list
        // disappear. Always populate from CFG-3000.0 instead.
        _ = configVersionId;

        var systemConfigVersionId =
            await GetSystemWorkoutConfigVersionIdAsync(
                cancellationToken);

        if (!systemConfigVersionId.HasValue)
        {
            model.WorkoutTypes =
                new List<WorkoutBuilderTypeOptionViewModel>();

            return;
        }

        var configured =
            await db.WorkoutTypeDefinitions
                .AsNoTracking()
                .Where(
                    x =>
                        x.ConfigVersionId
                            == systemConfigVersionId.Value
                        && x.IsActive
                        && x.IsSelectable)
                .ToListAsync(
                    cancellationToken);

        var byCode =
            configured.ToDictionary(
                x => x.WorkoutTypeCode,
                StringComparer.OrdinalIgnoreCase);

        var engineCatalog =
            new Engine30WorkoutTypeCatalog();

        model.WorkoutTypes =
            SystemWorkoutRecipeCatalog
                .PresentationRecipes
                .Where(
                    recipe =>
                        byCode.ContainsKey(
                            recipe.EngineWorkoutTypeCode))
                .Select(
                    recipe =>
                    {
                        var configuredWorkout =
                            byCode[
                                recipe.EngineWorkoutTypeCode];

                        var baseRpe = 5m;

                        if (engineCatalog.TryGet(
                                recipe.EngineWorkoutTypeCode,
                                out var engineDefinition)
                            && engineDefinition is not null)
                        {
                            baseRpe =
                                engineDefinition.DefaultRpe;
                        }

                        return new WorkoutBuilderTypeOptionViewModel
                        {
                            Code = recipe.SystemCode,
                            EngineWorkoutTypeCode =
                                recipe.EngineWorkoutTypeCode,
                            DisplayName = recipe.DisplayName,
                            GroupName = recipe.GroupName,
                            DefaultMinutes =
                                SystemWorkoutRecipeCatalog
                                    .ResolveDefaultMinutes(
                                        recipe,
                                        configuredWorkout.DefaultMinutes),
                            DefaultRpe =
                                SystemWorkoutRecipeCatalog
                                    .ResolveDefaultRpe(
                                        recipe,
                                        baseRpe)
                        };
                    })
                .ToList();
    }

    private async Task<Guid?> GetSystemWorkoutConfigVersionIdAsync(
        CancellationToken cancellationToken)
    {
        return await db.ConfigVersions
            .AsNoTracking()
            .Where(
                x =>
                    x.VersionCode
                        == SystemWorkoutConfigVersionCode)
            .Select(
                x =>
                    (Guid?)x.ConfigVersionId)
            .SingleOrDefaultAsync(
                cancellationToken);
    }

    private async Task PopulateTemplatesAsync(
        DetailedWorkoutEditorViewModel model,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        model.Templates =
            await db.WorkoutTemplates
                .AsNoTracking()
                .Where(x => x.ClientId == clientId)
                .OrderBy(x => x.TemplateName)
                .Select(
                    x =>
                        new WorkoutTemplateSummaryViewModel
                        {
                            WorkoutTemplateId =
                                x.WorkoutTemplateId,
                            TemplateName =
                                x.TemplateName,
                            WorkoutTypeCode =
                                x.WorkoutTypeCode,
                            DefaultDurationMin =
                                x.DefaultDurationMin,
                            ExerciseCount =
                                x.Exercises.Count
                        })
                .ToListAsync(cancellationToken);
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
                                !string.IsNullOrWhiteSpace(x.WorkoutName)
                                    ? x.WorkoutName
                                    : definitions.TryGetValue(
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
                                x.CompletedFlag,
                            WorkoutName =
                                x.WorkoutName,
                            WorkoutTemplateId =
                                x.WorkoutTemplateId,
                            WorkoutTypeCode =
                                x.WorkoutTypeCode
                        })
                .ToList();
    }

    private static IReadOnlyList<string> ParseInstructions(
        string? instructionsJson)
    {
        if (string.IsNullOrWhiteSpace(
                instructionsJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(
                       instructionsJson)
                   ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static int MuscleRolePriority(
        string? roleCode)
    {
        if (string.Equals(
                roleCode,
                "primary",
                StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (string.Equals(
                roleCode,
                "secondary",
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(
                roleCode,
                "stabilizer",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    private static int TextMatchPriority(
        string? value,
        string query)
    {
        if (string.IsNullOrWhiteSpace(value)
            || string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        if (string.Equals(
                value,
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (value.StartsWith(
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 75;
        }

        return value.Contains(
                query,
                StringComparison.OrdinalIgnoreCase)
            ? 50
            : 0;
    }

    private static int SystemRecipeExerciseScore(
        ExerciseCatalogItemRecord exercise,
        SystemWorkoutExerciseSlot slot,
        IReadOnlyDictionary<Guid, ClientExercisePreferenceRecord> preferences,
        IReadOnlyDictionary<Guid, int> useCounts,
        IReadOnlyDictionary<Guid, int> globalFavoriteCounts)
    {
        if (!slot.AllowCardio
            && string.Equals(
                exercise.Category,
                "cardio",
                StringComparison.OrdinalIgnoreCase))
        {
            return int.MinValue;
        }

        if (!slot.AllowStretching
            && string.Equals(
                exercise.Category,
                "stretching",
                StringComparison.OrdinalIgnoreCase))
        {
            return int.MinValue;
        }

        var nameScore = 0;
        int? trainerSeedRank = null;

        for (var preferredIndex = 0;
             preferredIndex < slot.PreferredNameTerms.Count;
             preferredIndex++)
        {
            var match =
                SystemNameMatchScore(
                    exercise.Name,
                    slot.PreferredNameTerms[preferredIndex]);

            nameScore =
                Math.Max(
                    nameScore,
                    match);

            if (match > 0 && !trainerSeedRank.HasValue)
            {
                // PreferredNameTerms are deliberately ordered. Early terms
                // reflect the initial evidence-oriented trainer seed. Hearts
                // can later overtake this bounded starter bonus.
                trainerSeedRank =
                    preferredIndex;
            }
        }

        var muscleScore = 0;

        foreach (var target in exercise.MuscleTargets)
        {
            foreach (var muscleTerm in slot.MuscleTerms)
            {
                var textMatch =
                    Math.Max(
                        TextMatchPriority(
                            target.MuscleName,
                            muscleTerm),
                        TextMatchPriority(
                            target.MuscleSlug,
                            muscleTerm));

                if (textMatch == 0)
                    continue;

                muscleScore =
                    Math.Max(
                        muscleScore,
                        (MuscleRolePriority(
                                target.RoleCode)
                            * 400)
                        + textMatch);
            }
        }

        // Corrective / mobility slots can require an explicit movement-name
        // match so a missing dead-bug or pelvic-tilt row never falls back to an
        // unrelated abdominal exercise simply because it targets the same area.
        if (slot.RequireNameMatch && nameScore == 0)
            return int.MinValue;

        // A System Workout slot must match the intended movement by name,
        // intended muscle by catalog metadata, or both. Popularity can rank
        // appropriate candidates but can never make an unrelated exercise win.
        if (nameScore == 0 && muscleScore == 0)
            return int.MinValue;

        var score =
            (nameScore * 3)
            + muscleScore
            + SystemTrainerSeedBonus(
                trainerSeedRank);

        if (slot.PreferCompound
            && string.Equals(
                exercise.Mechanics,
                "compound",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 250;
        }

        if (slot.PreferIsolation
            && string.Equals(
                exercise.Mechanics,
                "isolation",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 250;
        }

        if (slot.AllowStretching
            && string.Equals(
                exercise.Category,
                "stretching",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }

        if (preferences.TryGetValue(
                exercise.ExerciseCatalogItemId,
                out var preference)
            && preference.IsFavorite)
        {
            score += 150;
        }

        if (useCounts.TryGetValue(
                exercise.ExerciseCatalogItemId,
                out var useCount))
        {
            score +=
                Math.Min(
                    useCount * 5,
                    75);
        }

        if (globalFavoriteCounts.TryGetValue(
                exercise.ExerciseCatalogItemId,
                out var globalFavoriteCount))
        {
            score +=
                SystemWorkoutRatingBonus(
                    globalFavoriteCount);
        }

        return score;
    }

    private static int SystemNameMatchScore(
        string? exerciseName,
        string? preferredTerm)
    {
        if (string.IsNullOrWhiteSpace(exerciseName)
            || string.IsNullOrWhiteSpace(preferredTerm))
        {
            return 0;
        }

        var name = exerciseName.Trim();
        var term = preferredTerm.Trim();

        if (string.Equals(
                name,
                term,
                StringComparison.OrdinalIgnoreCase))
        {
            return 1000;
        }

        if (name.StartsWith(
                term,
                StringComparison.OrdinalIgnoreCase))
        {
            return 850;
        }

        if (name.Contains(
                term,
                StringComparison.OrdinalIgnoreCase))
        {
            return 700;
        }

        var tokens =
            term.Split(
                new[] { ' ', '-', '/', '_' },
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries);

        if (tokens.Length > 1
            && tokens.All(
                token =>
                    name.Contains(
                        token,
                        StringComparison.OrdinalIgnoreCase)))
        {
            return 550;
        }

        return 0;
    }

    private static int SystemTrainerSeedBonus(
        int? preferredTermIndex)
    {
        if (!preferredTermIndex.HasValue)
            return 0;

        // Public trainer/program research seeds the empty-platform starting
        // order. Keep it bounded: this is a bootstrap signal, not permanent
        // authority. Earlier PreferredNameTerms receive the larger bonus.
        return Math.Max(
            25,
            300 - (preferredTermIndex.Value * 35));
    }

    private static int SystemWorkoutRatingBonus(
        int globalFavoriteCount)
    {
        if (globalFavoriteCount <= 0)
            return 0;

        // Hearts are deliberately able to overtake the bounded trainer seed
        // once real 12WR preference data accumulates. Anatomical/movement fit
        // still qualifies candidates first, so popularity cannot make an
        // unrelated exercise eligible.
        return Math.Min(
            900,
            (int)Math.Round(
                Math.Log2(
                    globalFavoriteCount + 1d)
                * 100d,
                MidpointRounding.AwayFromZero));
    }

    private static int SystemExerciseScore(
        ExerciseCatalogItemRecord exercise,
        string muscleTerm,
        IReadOnlyDictionary<Guid, ClientExercisePreferenceRecord> preferences,
        IReadOnlyDictionary<Guid, int> useCounts,
        IReadOnlyDictionary<Guid, int> globalFavoriteCounts)
    {
        var bestTarget =
            exercise.MuscleTargets
                .Where(
                    muscle =>
                        muscle.MuscleName.Contains(
                            muscleTerm,
                            StringComparison.OrdinalIgnoreCase)
                        || muscle.MuscleSlug.Contains(
                            muscleTerm,
                            StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(
                    muscle =>
                        MuscleRolePriority(
                            muscle.RoleCode))
                .ThenBy(
                    muscle =>
                        muscle.DisplayOrder)
                .FirstOrDefault();

        if (bestTarget is null)
            return int.MinValue;

        var score =
            MuscleRolePriority(
                bestTarget.RoleCode) * 100;

        score +=
            TextMatchPriority(
                bestTarget.MuscleName,
                muscleTerm);

        if (string.Equals(
                exercise.Mechanics,
                "compound",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        if (preferences.TryGetValue(
                exercise.ExerciseCatalogItemId,
                out var preference)
            && preference.IsFavorite)
        {
            score += 20;
        }

        if (useCounts.TryGetValue(
                exercise.ExerciseCatalogItemId,
                out var useCount))
        {
            score +=
                Math.Min(
                    useCount,
                    15);
        }

        if (globalFavoriteCounts.TryGetValue(
                exercise.ExerciseCatalogItemId,
                out var globalFavoriteCount))
        {
            score +=
                GlobalFavoriteBonus(
                    globalFavoriteCount);
        }

        score -=
            Math.Min(
                bestTarget.DisplayOrder,
                (short)20);

        return score;
    }

    private static int GenericExerciseScore(
        ExerciseCatalogItemRecord exercise,
        IReadOnlyDictionary<Guid, ClientExercisePreferenceRecord> preferences,
        IReadOnlyDictionary<Guid, int> useCounts,
        IReadOnlyDictionary<Guid, int> globalFavoriteCounts)
    {
        var score =
            string.Equals(
                exercise.Mechanics,
                "compound",
                StringComparison.OrdinalIgnoreCase)
                ? 50
                : 0;

        if (preferences.TryGetValue(
                exercise.ExerciseCatalogItemId,
                out var preference)
            && preference.IsFavorite)
        {
            score += 20;
        }

        if (useCounts.TryGetValue(
                exercise.ExerciseCatalogItemId,
                out var useCount))
        {
            score +=
                Math.Min(
                    useCount,
                    15);
        }

        if (globalFavoriteCounts.TryGetValue(
                exercise.ExerciseCatalogItemId,
                out var globalFavoriteCount))
        {
            score +=
                GlobalFavoriteBonus(
                    globalFavoriteCount);
        }

        return score;
    }

    private static int GlobalFavoriteBonus(
        int globalFavoriteCount)
    {
        if (globalFavoriteCount <= 0)
            return 0;

        // Popularity is deliberately dampened:
        // 1 heart = 4 points, 3 = 8, 15 = 16,
        // and the platform-wide bonus caps at 24.
        // Muscle role and personal preference therefore remain dominant.
        return Math.Min(
            24,
            (int)Math.Round(
                Math.Log2(
                    globalFavoriteCount + 1d)
                * 4d,
                MidpointRounding.AwayFromZero));
    }

    private static int[] ResolveSetReps(
        DetailedWorkoutExerciseInputModel input)
    {
        var setCount =
            Math.Clamp(
                input.Sets,
                1,
                20);

        if (input.SetReps is { Count: > 0 })
            return input.SetReps.ToArray();

        var fallback =
            Math.Clamp(
                input.Reps,
                1,
                500);

        return Enumerable.Repeat(
                fallback,
                setCount)
            .ToArray();
    }

    private static int[] ParseTemplateSetReps(
        string? setRepsJson,
        short sets,
        short fallbackReps)
    {
        var setCount =
            Math.Clamp(
                (int)sets,
                1,
                20);

        if (!string.IsNullOrWhiteSpace(
                setRepsJson))
        {
            try
            {
                var parsed =
                    JsonSerializer.Deserialize<int[]>(
                        setRepsJson);

                if (parsed is not null
                    && parsed.Length == setCount
                    && parsed.All(
                        x => x is >= 1 and <= 500))
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
            }
        }

        return Enumerable.Repeat(
                Math.Clamp(
                    (int)fallbackReps,
                    1,
                    500),
                setCount)
            .ToArray();
    }

    private static string ClientLoadUnit(
        string? unitSystemCode)
        =>
            string.Equals(
                unitSystemCode,
                "METRIC",
                StringComparison.OrdinalIgnoreCase)
                ? "KG"
                : "LB";

    private static string NormalizeLoadUnit(
        string? requested,
        string fallback)
    {
        if (string.Equals(
                requested,
                "KG",
                StringComparison.OrdinalIgnoreCase))
        {
            return "KG";
        }

        if (string.Equals(
                requested,
                "LB",
                StringComparison.OrdinalIgnoreCase))
        {
            return "LB";
        }

        return string.Equals(
            fallback,
            "KG",
            StringComparison.OrdinalIgnoreCase)
            ? "KG"
            : "LB";
    }
}