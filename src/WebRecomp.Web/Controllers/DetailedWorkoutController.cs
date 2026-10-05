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
    WorkoutActualService workoutActuals,
    IWorkoutIntelligenceGateway workoutIntelligence,
    Microsoft.Extensions.Logging.ILogger<DetailedWorkoutController> logger) : Controller
{
    // B2.35.1 WORKOUT UX SEPARATION
    // Builder/template management and live workout sessions are distinct modes.
    private const string SystemWorkoutConfigVersionCode = "CFG-3000.0";

    // B2.35.15 CSV UNKNOWN EXERCISE FALLBACK
    // Unknown CSV exercises need a real FK identity because workout/template rows
    // require ExerciseCatalogItemId. These rows are active for referential integrity
    // but hidden from normal Exercise Library search.
    private const string CsvUnmappedExerciseSourceCode =
        "12WR_CSV_UNMAPPED";

    private const string CsvUnmappedExerciseMechanics =
        "CSV_UNMAPPED_ESTIMATED";

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

        var planned =
            await db.PlannedWorkouts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.ProgramId
                            == context.Program.ProgramId
                        && x.PlannedDate
                            == targetDate,
                    cancellationToken);

        model.PlannedWorkoutTemplateId =
            planned?.WorkoutTemplateId;

        model.PlannedSystemWorkoutCode =
            planned?.SystemWorkoutCode;

        if (!model.PlannedWorkoutTemplateId.HasValue
            && string.IsNullOrWhiteSpace(
                model.PlannedSystemWorkoutCode)
            && !string.IsNullOrWhiteSpace(
                planned?.WorkoutTypeCode))
        {
            model.PlannedSystemWorkoutCode =
                SystemWorkoutRecipeCatalog
                    .GetDefaultPresentationForEngine(
                        planned.WorkoutTypeCode)
                    ?.SystemCode;
        }

        await PopulateTemplatesAsync(
            model,
            context.Client.ClientId,
            model.PlannedWorkoutTemplateId,
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

                model.WorkoutName = string.Empty;
                model.TemplateName = string.Empty;

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

        model.WorkspaceMode = "live";
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

        // B2.35.20A: reuse the same unfinished System Workout for this
        // client/program/date. An unfinished Builder row is a draft, not
        // another actual workout. Completed sessions are intentionally not
        // reused so multiple real workouts in one day remain supported.
        var existingOpenSession =
            await db.WorkoutSessions
                .AsNoTracking()
                .Where(
                    x =>
                        x.ClientId == context.Client.ClientId
                        && x.ProgramId == context.Program.ProgramId
                        && x.WorkoutDate == date
                        && x.SystemWorkoutCode
                            == selectedSystemWorkout.SystemCode
                        && !x.CompletedFlag
                        && x.EntrySourceCode
                            == RecompInputSourceCodes.DetailedWorkout)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

        if (existingOpenSession is not null)
        {
            TempData["StatusMessage"] =
                $"{name} is already open for this date. Reopening it instead of creating a duplicate.";

            return RedirectToAction(
                nameof(Index),
                new
                {
                    workoutSessionId =
                        existingOpenSession.WorkoutSessionId,
                    date =
                        date.ToString("yyyy-MM-dd")
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
                CalculationEngineVersion = WorkoutActualService.ActiveEngineVersionCode,
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
    public IActionResult DownloadWorkoutCsvTemplate()
    {
        const string content =
            "WorkoutName,Exercise,Sets,Reps,Weight,Technique,MusclesWorked,ExerciseType,Notes\r\n"
            + "Example Full Body,Barbell Bench Press,3-4,8-10,135-155,Controlled lowering - keep shoulder blades set,Chest; Triceps,Compound,Optional coaching note\r\n"
            + "Example Full Body,Lat Pulldown,3,10-12,120-140,Drive elbows toward ribs,Lats; Biceps,Compound,\r\n"
            + "Example Full Body,Goblet Squat,3,10-12,as tolerated,Brace and sit between hips,Quadriceps; Glutes,Compound,";

        return File(
            System.Text.Encoding.UTF8.GetBytes(content),
            "text/csv",
            "12WR-workout-template.csv");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(524288)]
    public async Task<IActionResult> ImportWorkoutCsv(
        Microsoft.AspNetCore.Http.IFormFile csvFile,
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

        IActionResult ImportError(string message)
        {
            TempData["WorkoutImportError"] =
                message;

            return RedirectToAction(
                nameof(Index),
                new
                {
                    mode = "new",
                    date =
                        date.ToString(
                            "yyyy-MM-dd")
                });
        }

        if (csvFile is null || csvFile.Length <= 0)
            return ImportError(
                "Choose a CSV file to import.");

        if (csvFile.Length > 524288)
            return ImportError(
                "CSV workout files must be 512 KB or smaller.");

        if (!string.Equals(
                Path.GetExtension(csvFile.FileName),
                ".csv",
                StringComparison.OrdinalIgnoreCase))
        {
            return ImportError(
                "Workout import currently accepts .csv files only.");
        }

        var rows =
            new List<IReadOnlyList<string>>();

        try
        {
            using var reader =
                new StreamReader(
                    csvFile.OpenReadStream());

            while (!reader.EndOfStream)
            {
                var line =
                    await reader.ReadLineAsync(
                        cancellationToken);

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                rows.Add(
                    ParseWorkoutCsvLine(line));

                if (rows.Count > 51)
                {
                    return ImportError(
                        "A CSV workout may contain at most 50 exercises.");
                }
            }
        }
        catch (FormatException ex)
        {
            return ImportError(
                "CSV format error: "
                + ex.Message);
        }

        if (rows.Count < 2)
        {
            return ImportError(
                "The CSV must contain a header row and at least one exercise.");
        }

        var headers =
            rows[0]
                .Select(
                    NormalizeWorkoutCsvHeader)
                .ToArray();

        var exerciseColumn =
            FindWorkoutCsvColumn(
                headers,
                "exercise",
                "exercisename");

        var setsColumn =
            FindWorkoutCsvColumn(
                headers,
                "sets");

        var repsColumn =
            FindWorkoutCsvColumn(
                headers,
                "reps",
                "repetitions");

        var nameColumn =
            FindWorkoutCsvColumn(
                headers,
                "workoutname",
                "workout");

        var weightColumn =
            FindWorkoutCsvColumn(
                headers,
                "weight",
                "maxweight",
                "heaviest");

        var notesColumn =
            FindWorkoutCsvColumn(
                headers,
                "notes",
                "note");

        var techniqueColumn =
            FindWorkoutCsvColumn(
                headers,
                "technique",
                "instructions");

        var musclesColumn =
            FindWorkoutCsvColumn(
                headers,
                "musclesworked",
                "muscles",
                "muscletargets");

        var exerciseTypeColumn =
            FindWorkoutCsvColumn(
                headers,
                "exercisetype",
                "compoundorisolated",
                "compoundisolation",
                "mechanics",
                "type");

        if (exerciseColumn < 0
            || setsColumn < 0
            || repsColumn < 0)
        {
            return ImportError(
                "CSV requires Exercise, Sets, and Reps columns.");
        }

        var catalogRows =
            await db.ExerciseCatalogItems
                .AsNoTracking()
                .Where(
                    x =>
                        x.IsActive
                        && x.SourceCode
                            != CsvUnmappedExerciseSourceCode)
                .ToListAsync(
                    cancellationToken);

        var catalogByName =
            catalogRows
                .GroupBy(
                    x => x.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.First(),
                    StringComparer.OrdinalIgnoreCase);

        var importedExercises =
            new List<DetailedWorkoutExerciseInputModel>();

        string? importedWorkoutName = null;

        for (var rowIndex = 1;
             rowIndex < rows.Count;
             rowIndex++)
        {
            var row =
                rows[rowIndex];

            string ValueAt(int index) =>
                index >= 0 && index < row.Count
                    ? row[index].Trim()
                    : string.Empty;

            var exerciseName =
                ValueAt(
                    exerciseColumn);

            if (string.IsNullOrWhiteSpace(
                    exerciseName))
            {
                return ImportError(
                    $"CSV row {rowIndex + 1}: Exercise is required.");
            }

            if (exerciseName.Length > 200)
            {
                return ImportError(
                    $"CSV row {rowIndex + 1}: Exercise may not exceed 200 characters.");
            }

            if (!catalogByName.TryGetValue(
                    exerciseName,
                    out var catalogItem))
            {
                catalogItem =
                    await ResolveCsvUnmappedExerciseAsync(
                        context.Client.ClientId,
                        exerciseName,
                        cancellationToken);
            }

            var setsPrescription =
                ValueAt(setsColumn).Trim();

            if (string.IsNullOrWhiteSpace(setsPrescription))
            {
                return ImportError(
                    $"CSV row {rowIndex + 1}: Sets prescription is required.");
            }

            if (setsPrescription.Length > 100)
            {
                return ImportError(
                    $"CSV row {rowIndex + 1}: Sets prescription may not exceed 100 characters.");
            }

            var sets =
                PrescriptionIntDefault(
                    setsPrescription,
                    fallback: 3,
                    min: 1,
                    max: 20);

            var repsPrescription =
                ValueAt(repsColumn).Trim();

            if (string.IsNullOrWhiteSpace(repsPrescription))
            {
                return ImportError(
                    $"CSV row {rowIndex + 1}: Reps prescription is required.");
            }

            if (repsPrescription.Length > 100)
            {
                return ImportError(
                    $"CSV row {rowIndex + 1}: Reps prescription may not exceed 100 characters.");
            }

            var reps =
                PrescriptionIntDefault(
                    repsPrescription,
                    fallback: 10,
                    min: 1,
                    max: 500);

            var weightPrescription =
                ValueAt(weightColumn).Trim();

            if (weightPrescription.Length > 100)
            {
                return ImportError(
                    $"CSV row {rowIndex + 1}: Weight prescription may not exceed 100 characters.");
            }

            var maxWeight =
                PrescriptionWeightDefault(
                    weightPrescription);

            if (string.IsNullOrWhiteSpace(
                    importedWorkoutName))
            {
                var candidateName =
                    ValueAt(
                        nameColumn);

                if (!string.IsNullOrWhiteSpace(
                        candidateName))
                {
                    importedWorkoutName =
                        candidateName;
                }
            }

            var importedNoteParts =
                new List<string>();

            var techniqueText =
                ValueAt(
                    techniqueColumn);

            if (!string.IsNullOrWhiteSpace(
                    techniqueText))
            {
                importedNoteParts.Add(
                    "Technique: "
                    + techniqueText);
            }

            var musclesText =
                ValueAt(
                    musclesColumn);

            if (!string.IsNullOrWhiteSpace(
                    musclesText))
            {
                importedNoteParts.Add(
                    "Muscles worked: "
                    + musclesText);
            }

            var exerciseTypeText =
                ValueAt(
                    exerciseTypeColumn);

            if (!string.IsNullOrWhiteSpace(
                    exerciseTypeText))
            {
                importedNoteParts.Add(
                    "Exercise type: "
                    + exerciseTypeText);
            }

            var freeNotes =
                ValueAt(
                    notesColumn);

            if (!string.IsNullOrWhiteSpace(
                    freeNotes))
            {
                importedNoteParts.Add(
                    freeNotes);
            }

            var importedNotes =
                importedNoteParts.Count == 0
                    ? null
                    : string.Join(
                        Environment.NewLine,
                        importedNoteParts);

            if ((importedNotes?.Length ?? 0) > 1000)
            {
                return ImportError(
                    $"CSV row {rowIndex + 1}: combined optional metadata and Notes may not exceed 1000 characters.");
            }

            var loadUnit =
                ClientLoadUnit(
                    context.Client.UnitSystemCode);

            importedExercises.Add(
                new DetailedWorkoutExerciseInputModel
                {
                    ExerciseId =
                        catalogItem.ExerciseCatalogItemId,
                    ExerciseName =
                        catalogItem.Name,
                    Mechanics =
                        catalogItem.Mechanics,
                    SetsPrescription =
                        setsPrescription,
                    RepsPrescription =
                        repsPrescription,
                    WeightPrescription =
                        string.IsNullOrWhiteSpace(weightPrescription)
                            ? null
                            : weightPrescription,
                    Sets = sets,
                    Reps = reps,
                    SetReps =
                        Enumerable.Repeat(
                                reps,
                                sets)
                            .ToList(),
                    MaxWeight =
                        maxWeight,
                    LoadUnitCode =
                        loadUnit,
                    Notes =
                        importedNotes
                });
        }

        if (importedExercises.Count == 0)
        {
            return ImportError(
                "The CSV did not contain any exercises to import.");
        }

        var templateName =
            string.IsNullOrWhiteSpace(
                importedWorkoutName)
                ? Path.GetFileNameWithoutExtension(
                    csvFile.FileName)
                : importedWorkoutName.Trim();

        if (string.IsNullOrWhiteSpace(
                templateName))
        {
            templateName =
                "Imported Workout";
        }

        if (templateName.Length > 120)
        {
            return ImportError(
                "Workout name may not exceed 120 characters.");
        }

        var duplicateExists =
            await db.WorkoutTemplates
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ClientId
                            == context.Client.ClientId
                        && x.TemplateName
                            == templateName,
                    cancellationToken);

        if (duplicateExists)
        {
            return ImportError(
                $"My Workouts already contains '{templateName}'. Rename the workout in the CSV and import again.");
        }

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
                        x.WorkoutTypeCode == "MAJOR_MIXED"
                            ? 0
                            : 1)
                .ThenBy(x => x.DisplayOrder)
                .FirstOrDefaultAsync(
                    cancellationToken);

        if (workoutType is null)
        {
            return ImportError(
                "No active workout type is available for the imported workout.");
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
                null,
                templateName,
                importedExercises,
                cancellationToken);

        await db.SaveChangesAsync(
            cancellationToken);

        TempData["StatusMessage"] =
            $"{template.TemplateName} was imported to My Workouts. Select Start Workout from the Workouts page when you are ready to train.";

        return RedirectToAction(
            nameof(Index),
            new
            {
                mode = "saved",
                templateId =
                    template.WorkoutTemplateId,
                date =
                    date.ToString(
                        "yyyy-MM-dd")
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
    public async Task<IActionResult> ExchangeExercises(
        Guid exerciseId,
        string? q,
        int take = 60,
        CancellationToken cancellationToken = default)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null || !context.Program.DetailedWorkoutEntryEnabled)
            return StatusCode(403);

        var sourceMuscles = await db.ExerciseMuscleTargets
            .AsNoTracking()
            .Where(x => x.ExerciseCatalogItemId == exerciseId)
            .OrderByDescending(x => MuscleRolePriority(x.RoleCode))
            .ThenBy(x => x.DisplayOrder)
            .Select(x => new { x.MuscleName, x.MuscleSlug })
            .ToListAsync(cancellationToken);

        var normalized = q?.Trim();
        var searchTerms = string.IsNullOrWhiteSpace(normalized)
            ? sourceMuscles.Select(x => x.MuscleSlug).Concat(sourceMuscles.Select(x => x.MuscleName))
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray()
            : new[] { normalized };

        var combined = new Dictionary<Guid, object>();
        foreach (var term in searchTerms)
        {
            var result = await SearchExercises(term, 100, cancellationToken) as JsonResult;
            if (result?.Value is not System.Collections.IEnumerable values) continue;
            foreach (var value in values)
            {
                var prop = value.GetType().GetProperty("exerciseId");
                if (prop?.GetValue(value) is Guid id && id != exerciseId)
                    combined.TryAdd(id, value);
            }
        }

        return Json(combined.Values.Take(Math.Clamp(take, 1, 100)).ToArray());
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
        var muscleTargets =
            await db.ExerciseMuscleTargets
                .AsNoTracking()
                .Where(
                    x =>
                        x.ExerciseCatalogItemId
                            == exerciseId)
                .OrderBy(x => x.DisplayOrder)
                .Select(
                    x =>
                        new
                        {
                            x.MuscleName,
                            x.RoleCode
                        })
                .ToListAsync(
                    cancellationToken);

        var primaryMuscles =
            muscleTargets
                .Where(
                    x =>
                        string.Equals(
                            x.RoleCode,
                            "primary",
                            StringComparison.OrdinalIgnoreCase))
                .Select(x => x.MuscleName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var secondaryMuscles =
            muscleTargets
                .Where(
                    x =>
                        string.Equals(
                            x.RoleCode,
                            "secondary",
                            StringComparison.OrdinalIgnoreCase))
                .Select(x => x.MuscleName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return Json(
            new
            {
                exerciseId,
                name = exercise.Name,
                primaryMuscles,
                secondaryMuscles,
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
        bool stopWorkout,
        int pausedSeconds,
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
            var returnToSchedule =
                string.Equals(
                    model.ReturnContext,
                    "schedule",
                    StringComparison.OrdinalIgnoreCase);

            if (model.WorkoutSessionId.HasValue)
            {
                return BadRequest(
                    "Workout Builder template creation cannot reuse an actual workout session.");
            }

            if (!saveTemplate)
            {
                return BadRequest(
                    "Workout Builder templates must be saved to My Workouts.");
            }

            if (completeWorkout)
            {
                return BadRequest(
                    "Workout Builder template creation does not complete an actual workout.");
            }

            if (canonicalExercises.Count == 0)
            {
                return BadRequest(
                    "Add at least one exercise before saving the workout.");
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

            if (!returnToSchedule)
            {
                await db.SaveChangesAsync(
                    cancellationToken);

                TempData["StatusMessage"] =
                    $"{template.TemplateName} was saved to My Workouts.";

                return RedirectToAction(
                    nameof(Index),
                    new
                    {
                        mode = "saved",
                        templateId =
                            template.WorkoutTemplateId,
                        date =
                            model.Date.ToString(
                                "yyyy-MM-dd")
                    });
            }

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
                    CalculationEngineVersion = WorkoutActualService.ActiveEngineVersionCode,
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

        if ((completeWorkout || stopWorkout) && canonicalExercises.Count == 0)
            return BadRequest(
                "Add at least one exercise before completing the workout.");

        if (completeWorkout || stopWorkout)
        {
            // B2.35.5.2 REVIEW PENDING
            // Stop freezes the observed elapsed time. Finalize is the point
            // where the session becomes authoritative actual workout data.
            var stoppedAtUtc =
                DateTime.UtcNow;

            var elapsedMinutes =
                session.CreatedAtUtc == default
                    ? session.DurationMin
                    : (decimal)(
                        stoppedAtUtc
                        - session.CreatedAtUtc)
                        .TotalMinutes;
            var safePausedSeconds =
                Math.Clamp(
                    pausedSeconds,
                    0,
                    43200);

            elapsedMinutes =
                Math.Max(
                    0m,
                    elapsedMinutes
                    - safePausedSeconds / 60m);

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

            if (session.TypicalRestSec.HasValue
                && session.TypicalRestSec.Value <= 0)
            {
                session.TypicalRestSec = null;
            }

            if (session.SessionRpe.HasValue
                && (session.SessionRpe.Value < 1m
                    || session.SessionRpe.Value > 10m))
            {
                session.SessionRpe = null;
            }

            session.CompletedFlag = false;
            session.EntrySourceCode =
                RecompInputSourceCodes.DetailedWorkout;
            session.CalculationEngineVersion = WorkoutActualService.ActiveEngineVersionCode;

            // UpdatedAtUtc becomes the persisted Stop timestamp while
            // the workout is waiting for final review.
            session.UpdatedAtUtc =
                stoppedAtUtc;
        }

        if (session.CompletedFlag
            && model.EditCompleted
            && !completeWorkout
            && !stopWorkout)
        {
            // Editing a completed workout changes the same canonical actual
            // row in place. Re-stamp it so no stale Engine 2.x derived values
            // survive the correction.
            workoutActuals.ApplyCurrentEngineState(
                session,
                completed: true);
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

        if (completeWorkout || stopWorkout)
                {
                    return RedirectToAction(
                        nameof(Review),
                        new
                        {
                            workoutSessionId =
                                session.WorkoutSessionId,
                            returnContext =
                                model.ReturnContext
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
                mode = "live",
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

    [HttpGet]
    public async Task<IActionResult> Complete(
        Guid workoutSessionId,
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

        var session =
            await db.WorkoutSessions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutSessionId
                            == workoutSessionId
                        && x.ClientId
                            == context.Client.ClientId
                        && x.ProgramId
                            == context.Program.ProgramId,
                    cancellationToken);

        if (session is null)
            return NotFound();

        if (!session.CompletedFlag)
        {
            return RedirectToAction(
                nameof(Index),
                new
                {
                    mode = "live",
                    workoutSessionId =
                        session.WorkoutSessionId,
                    date =
                        session.WorkoutDate.ToString(
                            "yyyy-MM-dd")
                });
        }

        var detailed =
            await detailedWorkoutStore.GetAsync(
                session.WorkoutSessionId,
                cancellationToken);

        var exerciseCount =
            detailed?.Exercises.Count
            ?? 0;

        var workingSets =
            detailed?.Exercises
                .SelectMany(x => x.Sets)
                .Count(
                    x =>
                        !x.IsWarmup
                        && ((x.Reps ?? 0) > 0
                            || (x.DurationSeconds ?? 0) > 0))
            ?? 0;

        var workoutName =
            !string.IsNullOrWhiteSpace(
                session.WorkoutName)
                ? session.WorkoutName
                : "Completed Workout";

        // B2.35.12 COMPLETE SESSION CONTEXT
        // Duration and title are facts from this one completed session.
        // Do not replace them with the date-level Engine aggregate.
        var durationMinutes =
            session.DurationMin
                .ToString("0.#");

        string? netCalories = null;
        string? glycogenDemand = null;
        var analysisAvailable = false;

        try
        {
            var actual =
                await engine30Projection
                    .BuildActualWorkoutAsync(
                        context,
                        session.WorkoutDate,
                        cancellationToken);

            // The current Engine projection is date-level. It is safe to
            // display its energy/glycogen as this session only when this is
            // the sole completed workout on the date.
            if (actual.HasActualWorkout
                && actual.CompletedSessionCount == 1)
            {
                analysisAvailable = true;

                netCalories =
                    actual.NetWorkoutEnergyKcal
                        .ToString("0");

                glycogenDemand =
                    actual.GlycogenDemandG
                        .ToString("0");
            }
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions
                .LogError(
                    logger,
                    ex,
                    "Workout completion analysis failed for session {WorkoutSessionId}. The workout remains completed.",
                    session.WorkoutSessionId);
        }

        ViewData["WorkoutName"] =
            workoutName;

        ViewData["WorkoutDate"] =
            session.WorkoutDate.ToString(
                "dddd, MMMM d");

        ViewData["WorkoutDateIso"] =
            session.WorkoutDate.ToString(
                "yyyy-MM-dd");

        ViewData["DurationMinutes"] =
            durationMinutes;

        ViewData["SessionRpe"] =
            session.SessionRpe.HasValue
                ? session.SessionRpe.Value.ToString("0.#")
                : "-";

        ViewData["NetCalories"] =
            netCalories;

        ViewData["GlycogenDemand"] =
            glycogenDemand;

        ViewData["ExerciseCount"] =
            exerciseCount;

        ViewData["WorkingSets"] =
            workingSets;

        ViewData["AnalysisAvailable"] =
            analysisAvailable;

        return View("Complete");
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartEmptyWorkout(
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
                        x.WorkoutTypeCode == "MAJOR_MIXED"
                            ? 0
                            : x.WorkoutTypeCode == "FULL_BODY_STRENGTH"
                                ? 1
                                : 2)
                .ThenBy(x => x.DisplayOrder)
                .FirstOrDefaultAsync(
                    cancellationToken);

        if (workoutType is null)
        {
            return BadRequest(
                "No active workout type is available for an empty workout.");
        }

        var now = DateTime.UtcNow;

        var session =
            new WorkoutSession
            {
                WorkoutSessionId = Guid.NewGuid(),
                ClientId = context.Client.ClientId,
                ProgramId = context.Program.ProgramId,
                WorkoutDate = date,
                WorkoutName =
                    $"Workout - {date:yyyy-MM-dd}",
                WorkoutTypeCode =
                    workoutType.WorkoutTypeCode,
                DurationMin =
                    workoutType.DefaultMinutes,
                TypicalRestSec =
                    workoutType.DefaultRestSec,
                CompletedFlag = false,
                EntrySourceCode =
                    RecompInputSourceCodes.DetailedWorkout,
                CalculationEngineVersion = WorkoutActualService.ActiveEngineVersionCode,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        db.WorkoutSessions.Add(session);

        await db.SaveChangesAsync(
            cancellationToken);

        TempData["StatusMessage"] =
            "Started an empty workout. Add exercises as you train.";

        return RedirectToAction(
            nameof(Index),
            new
            {
                mode = "live",
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
    [HttpGet]
    public async Task<IActionResult> Review(
        Guid workoutSessionId,
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
                nameof(Complete),
                new
                {
                    workoutSessionId
                });
        }

        var detailed =
            await detailedWorkoutStore.GetAsync(
                workoutSessionId,
                cancellationToken);

        if (detailed is null
            || detailed.Exercises.Count == 0)
        {
            return BadRequest(
                "Add at least one exercise before reviewing the workout.");
        }

        var stopAtUtc =
            ReviewStopAtUtc(
                session);

        // B2.35.8 REVIEW TIMER FIX
        var reviewRpe =
            ResolveReviewRpe(session);

        if (DateTime.UtcNow >= stopAtUtc.AddMinutes(10))
        {
            await FinalizeWorkoutReviewAsync(
                context,
                session,
                session.DurationMin > 0m
                    ? session.DurationMin
                    : 1m,
                reviewRpe,
                0,
                null,
                cancellationToken);

            return RedirectToAction(
                nameof(Complete),
                new
                {
                    workoutSessionId
                });
        }

        var analysis =
            await BuildReviewAnalysisAsync(
                detailed,
                session.DurationMin,
                cancellationToken);

        ViewData["WorkoutSessionId"] =
            session.WorkoutSessionId;

        ViewData["WorkoutName"] =
            string.IsNullOrWhiteSpace(
                session.WorkoutName)
                ? "Workout"
                : session.WorkoutName;

        ViewData["DurationMin"] =
            session.DurationMin > 0m
                ? session.DurationMin
                : 1m;

        ViewData["SessionRpe"] =
            reviewRpe;

        var recordedWorkingSets =
            detailed.Exercises
                .SelectMany(x => x.Sets)
                .Count(
                    x =>
                        !x.IsWarmup
                        && ((x.Reps ?? 0) > 0
                            || (x.DurationSeconds ?? 0) > 0));

        var totalRecordedReps =
            detailed.Exercises
                .SelectMany(x => x.Sets)
                .Where(x => !x.IsWarmup)
                .Sum(x => x.Reps ?? 0);

        ViewData["RecordedSets"] =
            recordedWorkingSets;

        ViewData["TotalReps"] =
            totalRecordedReps;

        ViewData["ExerciseCount"] =
            analysis.ExerciseCount;

        ViewData["PrimaryMuscles"] =
            string.Join(
                ", ",
                analysis.MuscleDemand
                    .OrderByDescending(
                        x => x.WeightedSetEquivalents)
                    .Take(3)
                    .Select(
                        x =>
                            x.MuscleSlug
                                .Replace("-", " ")));

        ViewData["ReviewDeadlineUtc"] =
            DateTime.SpecifyKind(
                    stopAtUtc,
                    DateTimeKind.Utc)
                .AddMinutes(10)
                .ToString(
                    "yyyy-MM-ddTHH:mm:ss.fff'Z'");

        ViewData["ExerciseOptions"] =
            detailed.Exercises
                .OrderBy(x => x.ExerciseOrder)
                .ToDictionary(
                    x => x.ExerciseId,
                    x => x.ExerciseNameSnapshot);

        return View("Review");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FinalizeWorkout(
        Guid workoutSessionId,
        decimal durationMin,
        decimal? sessionRpe,
        int extraSets,
        Guid? extraExerciseId,
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

        if (durationMin is < 1m or > 360m)
            return BadRequest(
                "Workout duration must be between 1 and 360 minutes.");

        if (sessionRpe.HasValue
            && sessionRpe.Value is < 1m or > 10m)
        {
            return BadRequest(
                "Workout RPE must be between 1 and 10.");
        }

        if (extraSets is < 0 or > 50)
            return BadRequest(
                "Untracked sets must be between 0 and 50.");

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
                nameof(Complete),
                new
                {
                    workoutSessionId
                });
        }

        await FinalizeWorkoutReviewAsync(
            context,
            session,
            durationMin,
            sessionRpe,
            extraSets,
            extraExerciseId,
            cancellationToken);

        return RedirectToAction(
            nameof(Complete),
            new
            {
                workoutSessionId
            });
    }

    private async Task FinalizeWorkoutReviewAsync(
        AppClientContext context,
        WorkoutSession session,
        decimal durationMin,
        decimal? sessionRpe,
        int extraSets,
        Guid? extraExerciseId,
        CancellationToken cancellationToken)
    {
        var detailed =
            await detailedWorkoutStore.GetAsync(
                session.WorkoutSessionId,
                cancellationToken);

        if (detailed is null
            || detailed.Exercises.Count == 0)
        {
            throw new InvalidOperationException(
                "A detailed workout is required before finalization.");
        }

        if (extraSets > 0)
        {
            var targetExercise =
                await ResolveExtraSetExerciseAsync(
                    detailed,
                    durationMin,
                    extraExerciseId,
                    cancellationToken);

            var representativeReps =
                targetExercise.Sets
                    .Where(
                        x =>
                            !x.IsWarmup
                            && (x.Reps ?? 0) > 0)
                    .OrderByDescending(x => x.SetOrder)
                    .Select(x => x.Reps!.Value)
                    .FirstOrDefault();

            if (representativeReps <= 0)
                representativeReps = 1;

            var nextSetOrder =
                targetExercise.Sets.Count == 0
                    ? 1
                    : targetExercise.Sets.Max(
                        x => x.SetOrder) + 1;

            var added =
                Enumerable.Range(
                        0,
                        extraSets)
                    .Select(
                        offset =>
                            new DetailedWorkoutSetEntry(
                                Guid.Empty,
                                nextSetOrder + offset,
                                representativeReps,
                                null,
                                targetExercise.LoadUnitCode,
                                null,
                                null,
                                null,
                                null,
                                false,
                                "Untracked set added during workout review."))
                    .ToArray();

            var revisedExercises =
                detailed.Exercises
                    .Select(
                        exercise =>
                            exercise.ExerciseId
                                == targetExercise.ExerciseId
                                ? exercise with
                                {
                                    Sets =
                                        exercise.Sets
                                            .Concat(added)
                                            .ToArray()
                                }
                                : exercise)
                    .ToArray();

            await detailedWorkoutStore.ReplaceAsync(
                new DetailedWorkoutSession(
                    detailed.WorkoutSessionId,
                    revisedExercises),
                cancellationToken);
        }

        session.DurationMin =
            Math.Clamp(
                Math.Round(
                    durationMin,
                    1,
                    MidpointRounding.AwayFromZero),
                1m,
                360m);

        session.SessionRpe =
            sessionRpe;

        if (session.TypicalRestSec.HasValue
            && session.TypicalRestSec.Value <= 0)
        {
            session.TypicalRestSec = null;
        }

        session.EntrySourceCode =
            RecompInputSourceCodes.DetailedWorkout;

        workoutActuals.ApplyCurrentEngineState(
            session,
            completed: true);

        await db.SaveChangesAsync(
            cancellationToken);
    }

    private async Task<DetailedWorkoutExerciseEntry>
        ResolveExtraSetExerciseAsync(
            DetailedWorkoutSession detailed,
            decimal durationMin,
            Guid? requestedExerciseId,
            CancellationToken cancellationToken)
    {
        if (requestedExerciseId.HasValue)
        {
            var requested =
                detailed.Exercises
                    .SingleOrDefault(
                        x =>
                            x.ExerciseId
                                == requestedExerciseId.Value);

            if (requested is null)
            {
                throw new InvalidOperationException(
                    "The selected exercise is not part of this workout.");
            }

            return requested;
        }

        var exerciseIds =
            detailed.Exercises
                .Select(x => x.ExerciseId)
                .Distinct()
                .ToArray();

        var targets =
            await db.ExerciseMuscleTargets
                .AsNoTracking()
                .Where(
                    x =>
                        exerciseIds.Contains(
                            x.ExerciseCatalogItemId))
                .ToListAsync(
                    cancellationToken);

        var targetsByExercise =
            targets
                .GroupBy(
                    x => x.ExerciseCatalogItemId)
                .ToDictionary(
                    x => x.Key,
                    x =>
                        x.OrderBy(
                                target => target.DisplayOrder)
                            .Select(
                                target =>
                                    new MuscleTargetInput(
                                        target.MuscleSlug,
                                        target.RoleCode))
                            .ToArray());

        var request =
            new WorkoutIntelligenceRequest(
                SessionKey:
                    detailed.WorkoutSessionId
                        .ToString("D"),
                DurationMinutes:
                    Math.Max(
                        1,
                        (int)Math.Round(
                            durationMin,
                            0,
                            MidpointRounding.AwayFromZero)),
                Exercises:
                    detailed.Exercises
                        .Select(
                            exercise =>
                                new WorkoutExerciseInput(
                                    ExerciseId:
                                        exercise.ExerciseId,
                                    ExerciseName:
                                        exercise.ExerciseNameSnapshot,
                                    Mechanics:
                                        exercise.MechanicsSnapshot,
                                    MuscleTargets:
                                        targetsByExercise
                                            .TryGetValue(
                                                exercise.ExerciseId,
                                                out var exerciseTargets)
                                            ? exerciseTargets
                                            : Array.Empty<MuscleTargetInput>(),
                                    Sets:
                                        exercise.Sets
                                            .OrderBy(
                                                set => set.SetOrder)
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

        var analysis =
            workoutIntelligence.Analyze(
                request);

        var primaryMuscle =
            analysis.MuscleDemand
                .OrderByDescending(
                    x => x.WeightedSetEquivalents)
                .Select(x => x.MuscleSlug)
                .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(
                primaryMuscle))
        {
            var primaryMatch =
                detailed.Exercises
                    .FirstOrDefault(
                        exercise =>
                            targets.Any(
                                target =>
                                    target.ExerciseCatalogItemId
                                        == exercise.ExerciseId
                                    && target.MuscleSlug.Equals(
                                        primaryMuscle,
                                        StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(
                                        target.RoleCode,
                                        "PRIMARY",
                                        StringComparison.OrdinalIgnoreCase)));

            if (primaryMatch is not null)
                return primaryMatch;

            var anyMatch =
                detailed.Exercises
                    .FirstOrDefault(
                        exercise =>
                            targets.Any(
                                target =>
                                    target.ExerciseCatalogItemId
                                        == exercise.ExerciseId
                                    && target.MuscleSlug.Equals(
                                        primaryMuscle,
                                        StringComparison.OrdinalIgnoreCase)));

            if (anyMatch is not null)
                return anyMatch;
        }

        return detailed.Exercises
            .OrderByDescending(
                exercise =>
                    exercise.Sets.Count(
                        set =>
                            !set.IsWarmup
                            && ((set.Reps ?? 0) > 0
                                || (set.DurationSeconds ?? 0) > 0)))
            .ThenBy(x => x.ExerciseOrder)
            .First();
    }

    private async Task<WorkoutIntelligenceHandoff>
        BuildReviewAnalysisAsync(
            DetailedWorkoutSession detailed,
            decimal durationMin,
            CancellationToken cancellationToken)
    {
        var exerciseIds =
            detailed.Exercises
                .Select(x => x.ExerciseId)
                .Distinct()
                .ToArray();

        var targets =
            await db.ExerciseMuscleTargets
                .AsNoTracking()
                .Where(
                    x =>
                        exerciseIds.Contains(
                            x.ExerciseCatalogItemId))
                .ToListAsync(
                    cancellationToken);

        var targetsByExercise =
            targets
                .GroupBy(
                    x => x.ExerciseCatalogItemId)
                .ToDictionary(
                    x => x.Key,
                    x =>
                        x.OrderBy(
                                target => target.DisplayOrder)
                            .Select(
                                target =>
                                    new MuscleTargetInput(
                                        target.MuscleSlug,
                                        target.RoleCode))
                            .ToArray());

        return workoutIntelligence.Analyze(
            new WorkoutIntelligenceRequest(
                SessionKey:
                    detailed.WorkoutSessionId
                        .ToString("D"),
                DurationMinutes:
                    Math.Max(
                        1,
                        (int)Math.Round(
                            durationMin,
                            0,
                            MidpointRounding.AwayFromZero)),
                Exercises:
                    detailed.Exercises
                        .Select(
                            exercise =>
                                new WorkoutExerciseInput(
                                    ExerciseId:
                                        exercise.ExerciseId,
                                    ExerciseName:
                                        exercise.ExerciseNameSnapshot,
                                    Mechanics:
                                        exercise.MechanicsSnapshot,
                                    MuscleTargets:
                                        targetsByExercise
                                            .TryGetValue(
                                                exercise.ExerciseId,
                                                out var exerciseTargets)
                                            ? exerciseTargets
                                            : Array.Empty<MuscleTargetInput>(),
                                    Sets:
                                        exercise.Sets
                                            .OrderBy(
                                                set => set.SetOrder)
                                            .Select(
                                                set =>
                                                    new WorkoutSetInput(
                                                        set.Reps,
                                                        set.Load,
                                                        set.Rpe,
                                                        set.Rir,
                                                        set.RestSeconds,
                                                        set.DurationSeconds,
                                                        set.IsWarmup))
                                            .ToArray()))
                        .ToArray()));
    }

    private static decimal ResolveReviewRpe(
        WorkoutSession session)
    {
        if (session.SessionRpe is >= 1m and <= 10m)
            return session.SessionRpe.Value;

        var baseRpe = 5m;
        var engineCatalog =
            new Engine30WorkoutTypeCatalog();

        if (engineCatalog.TryGet(
                session.WorkoutTypeCode,
                out var engineDefinition)
            && engineDefinition is not null)
        {
            baseRpe =
                engineDefinition.DefaultRpe;
        }

        if (!string.IsNullOrWhiteSpace(
                session.SystemWorkoutCode)
            && SystemWorkoutRecipeCatalog.TryGet(
                session.SystemWorkoutCode,
                out var recipe)
            && recipe is not null)
        {
            return Math.Clamp(
                SystemWorkoutRecipeCatalog.ResolveDefaultRpe(
                    recipe,
                    baseRpe),
                1m,
                10m);
        }

        return Math.Clamp(
            baseRpe,
            1m,
            10m);
    }
    private static DateTime ReviewStopAtUtc(
        WorkoutSession session)
    {
        return session.UpdatedAtUtc;
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteRest(
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

        var existing =
            await db.WorkoutSessions
                .Where(
                    x =>
                        x.ClientId == context.Client.ClientId
                        && x.ProgramId == context.Program.ProgramId
                        && x.WorkoutDate == date
                        && x.WorkoutTypeCode == "REST"
                        && x.CompletedFlag)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            var now = DateTime.UtcNow;
            var restSession =
                new WorkoutSession
                {
                    WorkoutSessionId = Guid.NewGuid(),
                    ClientId = context.Client.ClientId,
                    ProgramId = context.Program.ProgramId,
                    WorkoutDate = date,
                    WorkoutName = "Rest / Recovery",
                    SystemWorkoutCode = "REST",
                    WorkoutTypeCode = "REST",
                    DurationMin = 0m,
                    SessionRpe = null,
                    EntrySourceCode =
                        RecompInputSourceCodes.DetailedWorkout,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            workoutActuals.ApplyCurrentEngineState(
                restSession,
                completed: true);

            db.WorkoutSessions.Add(restSession);

            await db.SaveChangesAsync(cancellationToken);
        }

        TempData["StatusMessage"] =
            "Rest day completed. Daily Tracking and the Dashboard now use this completed actual.";

        return string.Equals(
                returnContext,
                "daily",
                StringComparison.OrdinalIgnoreCase)
            ? RedirectToAction(
                "Index",
                "DailyEntry",
                new { date = date.ToString("yyyy-MM-dd") })
            : RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteCardio(
        DateOnly date,
        string cardioActivityCode,
        decimal durationMin,
        decimal sessionRpe,
        bool addAnother,
        string? returnContext,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!CardioActivityCatalog.TryGet(
                cardioActivityCode,
                out var activity)
            || activity is null)
        {
            return BadRequest(
                "Choose the cardio activity you actually performed.");
        }

        if (durationMin is < 1m or > 720m)
            return BadRequest(
                "Cardio duration must be between 1 and 720 minutes.");

        if (sessionRpe is < 1m or > 10m)
            return BadRequest(
                "Cardio RPE must be between 1 and 10.");

        var systemConfigVersionId =
            await GetSystemWorkoutConfigVersionIdAsync(
                cancellationToken);

        if (!systemConfigVersionId.HasValue)
            return BadRequest(
                "The Engine 3.0 workout configuration is unavailable.");

        var active =
            await db.WorkoutTypeDefinitions
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ConfigVersionId == systemConfigVersionId.Value
                        && x.WorkoutTypeCode == activity.EngineWorkoutTypeCode
                        && x.IsActive
                        && x.IsSelectable,
                    cancellationToken);

        if (!active)
            return BadRequest(
                "That cardio activity is not currently available.");

        var now = DateTime.UtcNow;
        var cardioSession =
            new WorkoutSession
            {
                WorkoutSessionId = Guid.NewGuid(),
                ClientId = context.Client.ClientId,
                ProgramId = context.Program.ProgramId,
                WorkoutDate = date,
                WorkoutName = activity.DisplayName,
                SystemWorkoutCode =
                    CardioActivityCatalog.PresentationSystemCode,
                WorkoutTypeCode =
                    activity.EngineWorkoutTypeCode,
                DurationMin =
                    Math.Round(
                        durationMin,
                        1,
                        MidpointRounding.AwayFromZero),
                SessionRpe =
                    Math.Round(
                        sessionRpe,
                        1,
                        MidpointRounding.AwayFromZero),
                EntrySourceCode =
                    RecompInputSourceCodes.DetailedWorkout,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        workoutActuals.ApplyCurrentEngineState(
            cardioSession,
            completed: true);

        db.WorkoutSessions.Add(cardioSession);

        await db.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] =
            $"{activity.DisplayName} completed. Daily Tracking and the Dashboard now use this actual session.";

        if (addAnother)
        {
            return RedirectToAction(
                nameof(Index),
                new
                {
                    mode = "start",
                    date = date.ToString("yyyy-MM-dd"),
                    returnContext
                });
        }

        return string.Equals(
                returnContext,
                "daily",
                StringComparison.OrdinalIgnoreCase)
            ? RedirectToAction(
                "Index",
                "DailyEntry",
                new { date = date.ToString("yyyy-MM-dd") })
            : RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UseTemplate(
        Guid templateId,
        DateOnly date,
        string? returnContext,
        string? entryMode,
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

            if (string.Equals(
                    entryMode,
                    "summary",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction(
                    nameof(Review),
                    new
                    {
                        workoutSessionId =
                            existingOpenSession.WorkoutSessionId,
                        returnContext
                    });
            }

            return RedirectToAction(
                nameof(Index),
                new
                {
                    mode = "live",
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

        if (string.Equals(
                entryMode,
                "summary",
                StringComparison.OrdinalIgnoreCase))
        {
            TempData["StatusMessage"] =
                $"Review {template.TemplateName}, correct what you actually did, then save it.";

            return RedirectToAction(
                nameof(Review),
                new
                {
                    workoutSessionId =
                        session.WorkoutSessionId,
                    returnContext
                });
        }

        TempData["StatusMessage"] =
            $"Started {template.TemplateName}.";

        return RedirectToAction(
            nameof(Index),
            new
            {
                mode = "live",
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
        string? entryMode,
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

        // Rest and generic Cardio are summary-only v1012 activities. Selecting
        // either card opens its purpose-built completion UI instead of creating
        // a fake live workout session.
        if (preview.SystemCode.Equals(
                "REST",
                StringComparison.OrdinalIgnoreCase)
            || preview.SystemCode.Equals(
                CardioActivityCatalog.PresentationSystemCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(
                nameof(Index),
                new
                {
                    mode = "system",
                    systemCode = preview.SystemCode,
                    date = date.ToString("yyyy-MM-dd"),
                    returnContext
                });
        }

        var summaryOnly =
            preview.GroupName.Equals(
                "Orangetheory",
                StringComparison.OrdinalIgnoreCase);

        var useSummary =
            summaryOnly
            || string.Equals(
                entryMode,
                "summary",
                StringComparison.OrdinalIgnoreCase);

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
                        && x.SystemWorkoutCode
                            == preview.SystemCode
                        && !x.CompletedFlag)
                .OrderByDescending(
                    x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync(
                    cancellationToken);

        if (existingOpenSession is not null)
        {
            // An OTF session created before its published movements were
            // resolvable may already exist with zero exercise rows. Repair
            // that draft in place so selecting the card does not reopen
            // another empty session.
            if (preview.Exercises.Count > 0
                && !await db.DetailedWorkoutExercises
                    .AsNoTracking()
                    .AnyAsync(
                        exercise =>
                            exercise.WorkoutSessionId
                                == existingOpenSession.WorkoutSessionId,
                        cancellationToken))
            {
                await detailedWorkoutStore.ReplaceAsync(
                    new DetailedWorkoutSession(
                        existingOpenSession.WorkoutSessionId,
                        detailedExercises),
                    cancellationToken);
            }

            TempData["StatusMessage"] =
                $"{preview.DisplayName} is already open for this date. Reopening it instead of creating a duplicate.";

            if (useSummary)
            {
                return RedirectToAction(
                    nameof(Review),
                    new
                    {
                        workoutSessionId =
                            existingOpenSession.WorkoutSessionId,
                        returnContext
                    });
            }

            return RedirectToAction(
                nameof(Index),
                new
                {
                    mode = "live",
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
                CalculationEngineVersion = WorkoutActualService.ActiveEngineVersionCode,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

        db.WorkoutSessions.Add(session);
        await db.SaveChangesAsync(
            cancellationToken);

        await detailedWorkoutStore.ReplaceAsync(
            new DetailedWorkoutSession(
                session.WorkoutSessionId,
                detailedExercises),
            cancellationToken);

        if (useSummary)
        {
            TempData["StatusMessage"] =
                $"Review {preview.DisplayName}, enter what you actually completed, then save it.";

            return RedirectToAction(
                nameof(Review),
                new
                {
                    workoutSessionId =
                        session.WorkoutSessionId,
                    returnContext
                });
        }

        TempData["StatusMessage"] =
            $"Started {preview.DisplayName}. Modify today's session below if you want.";

        return RedirectToAction(
            nameof(Index),
            new
            {
                mode = "live",
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

            // B2.35.16 TEMPLATE PRESCRIPTION TEXT
            row.SetsPrescription =
                CleanPrescription(
                    input.SetsPrescription);
            row.RepsPrescription =
                CleanPrescription(
                    input.RepsPrescription);
            row.WeightPrescription =
                CleanPrescription(
                    input.WeightPrescription);

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
                string.Join(" - ",
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
                "Workouts";

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
                "Workouts";
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
            return "live";

        var normalized =
            requested?.Trim().ToLowerInvariant();

        return normalized is "start" or "new" or "saved" or "system" or "live"
            ? normalized
            : "";
    }

    // B2.35.16 TEMPLATE PRESCRIPTION TEXT
    private static int PrescriptionIntDefault(
        string prescription,
        int fallback,
        int min,
        int max)
    {
        var match =
            System.Text.RegularExpressions.Regex.Match(
                prescription,
                @"\d+");

        if (!match.Success
            || !int.TryParse(
                match.Value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed))
        {
            return Math.Clamp(
                fallback,
                min,
                max);
        }

        return Math.Clamp(
            parsed,
            min,
            max);
    }

    private static decimal? PrescriptionWeightDefault(
        string? prescription)
    {
        if (string.IsNullOrWhiteSpace(
                prescription))
        {
            return null;
        }

        var match =
            System.Text.RegularExpressions.Regex.Match(
                prescription,
                @"\d+(?:\.\d+)?");

        if (!match.Success)
            return null;

        if (!decimal.TryParse(
                match.Value,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed))
        {
            return null;
        }

        return parsed is >= 0m and <= 10000m
            ? parsed
            : null;
    }

    private static string? CleanPrescription(
        string? prescription)
    {
        if (string.IsNullOrWhiteSpace(
                prescription))
        {
            return null;
        }

        var normalized =
            prescription.Trim();

        return normalized.Length <= 100
            ? normalized
            : normalized[..100];
    }
    // B2.35.15 CSV UNKNOWN EXERCISE FALLBACK
    private async Task<ExerciseCatalogItemRecord> ResolveCsvUnmappedExerciseAsync(
        Guid clientId,
        string exerciseName,
        CancellationToken cancellationToken)
    {
        var normalizedName =
            exerciseName.Trim();

        if (normalizedName.Length == 0
            || normalizedName.Length > 200)
        {
            throw new InvalidOperationException(
                "CSV unmapped exercise name is outside the supported length.");
        }

        // Technical identity is per-client and deterministic. This avoids one
        // client's arbitrary CSV label becoming another client's catalog identity.
        var normalizedKey =
            System.Text.RegularExpressions.Regex.Replace(
                    normalizedName,
                    @"\s+",
                    " ")
                .ToUpperInvariant();

        var nameHash =
            Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(
                        normalizedKey)));

        var sourceExternalId =
            $"{clientId:N}:{nameHash[..32]}";

        var existing =
            await db.ExerciseCatalogItems
                .SingleOrDefaultAsync(
                    x =>
                        x.SourceCode
                            == CsvUnmappedExerciseSourceCode
                        && x.SourceExternalId
                            == sourceExternalId,
                    cancellationToken);

        var now =
            DateTime.UtcNow;

        if (existing is not null)
        {
            existing.Name =
                normalizedName;
            existing.MeasurementType =
                "reps";
            existing.Mechanics =
                CsvUnmappedExerciseMechanics;
            existing.Category =
                "CSV_UNMAPPED";
            existing.InstructionsJson =
                "[]";
            existing.IsActive =
                true;
            existing.UpdatedAtUtc =
                now;

            await db.SaveChangesAsync(
                cancellationToken);

            return existing;
        }

        var entity =
            new ExerciseCatalogItemRecord
            {
                ExerciseCatalogItemId =
                    Guid.NewGuid(),
                SourceCode =
                    CsvUnmappedExerciseSourceCode,
                SourceExternalId =
                    sourceExternalId,
                Name =
                    normalizedName,
                MeasurementType =
                    "reps",
                DifficultyLevel =
                    null,
                ForceType =
                    null,
                Mechanics =
                    CsvUnmappedExerciseMechanics,
                Category =
                    "CSV_UNMAPPED",
                InstructionsJson =
                    "[]",
                IsActive =
                    true,
                LastImportId =
                    null,
                CreatedAtUtc =
                    now,
                UpdatedAtUtc =
                    now
            };

        db.ExerciseCatalogItems.Add(
            entity);

        await db.SaveChangesAsync(
            cancellationToken);

        return entity;
    }
    private static IReadOnlyList<string> ParseWorkoutCsvLine(
        string line)
    {
        var values =
            new List<string>();

        var field =
            new System.Text.StringBuilder();

        var inQuotes =
            false;

        for (var i = 0;
             i < line.Length;
             i++)
        {
            var ch =
                line[i];

            if (ch == '"')
            {
                if (inQuotes
                    && i + 1 < line.Length
                    && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                    continue;
                }

                inQuotes =
                    !inQuotes;

                continue;
            }

            if (ch == ','
                && !inQuotes)
            {
                values.Add(
                    field.ToString());

                field.Clear();
                continue;
            }

            field.Append(ch);
        }

        if (inQuotes)
        {
            throw new FormatException(
                "A quoted CSV field was not closed.");
        }

        values.Add(
            field.ToString());

        return values;
    }

    private static string NormalizeWorkoutCsvHeader(
        string value)
    {
        return new string(
            value
                .Trim()
                .Where(
                    char.IsLetterOrDigit)
                .Select(
                    char.ToLowerInvariant)
                .ToArray());
    }

    private static int FindWorkoutCsvColumn(
        IReadOnlyList<string> headers,
        params string[] names)
    {
        for (var i = 0;
             i < headers.Count;
             i++)
        {
            foreach (var name in names)
            {
                if (string.Equals(
                        headers[i],
                        name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
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
                                SetsPrescription =
                                    x.SetsPrescription,
                                RepsPrescription =
                                    x.RepsPrescription,
                                WeightPrescription =
                                    x.WeightPrescription,
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

        // Dated OTF recipes are a transcription of a specific published class.
        // Keep every movement in order, including repeated finishers. Catalog
        // search cannot be allowed to silently drop unfamiliar exercise names
        // or substitute another movement just because its name is similar.
        if (string.Equals(
                recipe?.GroupName,
                "Orangetheory",
                StringComparison.OrdinalIgnoreCase)
            && slots.Count > 0)
        {
            foreach (var slot in slots)
            {
                var publishedName =
                    slot.PreferredNameTerms.First();

                var exact =
                    candidates.FirstOrDefault(
                        candidate =>
                            string.Equals(
                                candidate.Name,
                                publishedName,
                                StringComparison.OrdinalIgnoreCase)
                            && candidate.SourceCode
                                != CsvUnmappedExerciseSourceCode);

                var exercise =
                    exact
                    ?? await ResolveCsvUnmappedExerciseAsync(
                        clientId,
                        publishedName,
                        cancellationToken);

                selected.Add((exercise, slot));
            }
        }
        else
        {
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
                            BlockName =
                                item.Slot.BlockName,
                            BlockType =
                                item.Slot.BlockType,
                            DurationSeconds =
                                item.Slot.DurationSeconds,
                            DistanceMeters =
                                item.Slot.DistanceMeters,
                            InclinePercent =
                                item.Slot.InclinePercent,
                            EstimatedRounds =
                                item.Slot.EstimatedRounds,
                            IsEstimated =
                                item.Slot.IsEstimated,
                            WorkloadText =
                                item.Slot.WorkloadText,
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

        model.CardioActivities =
            CardioActivityCatalog.All
                .Where(x => byCode.ContainsKey(x.EngineWorkoutTypeCode))
                .Select(
                    x => new CardioActivityOptionViewModel
                    {
                        ActivityCode = x.ActivityCode,
                        DisplayName = x.DisplayName,
                        EngineWorkoutTypeCode = x.EngineWorkoutTypeCode,
                        SearchTerms = x.SearchTerms
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
        Guid? plannedTemplateId,
        CancellationToken cancellationToken)
    {
        var templateRows =
            await db.WorkoutTemplates
                .AsNoTracking()
                .Where(x => x.ClientId == clientId)
                .Select(
                    x =>
                        new
                        {
                            x.WorkoutTemplateId,
                            x.TemplateName,
                            x.WorkoutTypeCode,
                            x.DefaultDurationMin,
                            ExerciseCount =
                                x.Exercises.Count,
                            x.CreatedAtUtc,
                            x.UpdatedAtUtc
                        })
                .ToListAsync(
                    cancellationToken);

        var orderedRows =
            templateRows
                .OrderByDescending(
                    x =>
                        plannedTemplateId.HasValue
                        && x.WorkoutTemplateId
                            == plannedTemplateId.Value)
                .ThenByDescending(
                    x => x.UpdatedAtUtc)
                .Take(10)
                .ToList();

        model.Templates =
            orderedRows
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
                                x.ExerciseCount,
                            UpdatedAtUtc =
                                x.UpdatedAtUtc
                        })
                .ToList();

        var visibleTemplateIds =
            orderedRows
                .Select(x => x.WorkoutTemplateId)
                .ToArray();

        var createdDates =
            orderedRows
                .ToDictionary(
                    x => x.WorkoutTemplateId,
                    x => x.CreatedAtUtc);

        var templateExerciseLinks =
            await db.WorkoutTemplateExercises
                .AsNoTracking()
                .Where(
                    x =>
                        visibleTemplateIds.Contains(
                            x.WorkoutTemplateId))
                .Select(
                    x =>
                        new
                        {
                            x.WorkoutTemplateId,
                            x.ExerciseCatalogItemId,
                            x.ExerciseOrder,
                            x.ExerciseNameSnapshot,
                            x.MechanicsSnapshot,
                            x.Sets,
                            x.Reps,
                            x.SetRepsJson
                        })
                .ToListAsync(
                    cancellationToken);

        var templateExerciseIds =
            templateExerciseLinks
                .Select(x => x.ExerciseCatalogItemId)
                .Distinct()
                .ToArray();

        var catalogExercises =
            await db.ExerciseCatalogItems
                .AsNoTracking()
                .Include(x => x.MuscleTargets)
                .Where(
                    x =>
                        templateExerciseIds.Contains(
                            x.ExerciseCatalogItemId))
                .ToDictionaryAsync(
                    x => x.ExerciseCatalogItemId,
                    cancellationToken);

        var muscleNames =
            catalogExercises
                .Values
                .SelectMany(x => x.MuscleTargets)
                .Where(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x.MuscleSlug))
                .GroupBy(
                    x => x.MuscleSlug,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group =>
                        group
                            .Select(x => x.MuscleName)
                            .FirstOrDefault(
                                name =>
                                    !string.IsNullOrWhiteSpace(
                                        name))
                        ?? group.Key,
                    StringComparer.OrdinalIgnoreCase);

        var muscleSummaries =
            new Dictionary<Guid, string>();

        foreach (var template in orderedRows)
        {
            var exercises =
                templateExerciseLinks
                    .Where(
                        x =>
                            x.WorkoutTemplateId
                                == template.WorkoutTemplateId)
                    .OrderBy(
                        x => x.ExerciseOrder)
                    .ToArray();

            if (exercises.Length == 0)
            {
                continue;
            }

            var request =
                new WorkoutIntelligenceRequest(
                    SessionKey:
                        $"template-list-{template.WorkoutTemplateId:N}",
                    DurationMinutes:
                        Math.Max(
                            1,
                            Convert.ToInt32(
                                template.DefaultDurationMin)),
                    Exercises:
                        exercises
                            .Select(
                                exercise =>
                                    new WorkoutExerciseInput(
                                        ExerciseId:
                                            exercise.ExerciseCatalogItemId,
                                        ExerciseName:
                                            exercise.ExerciseNameSnapshot,
                                        Mechanics:
                                            exercise.MechanicsSnapshot,
                                        MuscleTargets:
                                            catalogExercises.TryGetValue(
                                                exercise.ExerciseCatalogItemId,
                                                out var catalogExercise)
                                                ? catalogExercise
                                                    .MuscleTargets
                                                    .Select(
                                                        target =>
                                                            new MuscleTargetInput(
                                                                target.MuscleSlug,
                                                                target.RoleCode))
                                                    .ToArray()
                                                : Array.Empty<MuscleTargetInput>(),
                                        Sets:
                                            ParseTemplateSetReps(
                                                    exercise.SetRepsJson,
                                                    exercise.Sets,
                                                    exercise.Reps)
                                                .Select(
                                                    reps =>
                                                        new WorkoutSetInput(
                                                            Reps:
                                                                reps,
                                                            Load:
                                                                null,
                                                            Rpe:
                                                                null,
                                                            Rir:
                                                                null,
                                                            RestSeconds:
                                                                null,
                                                            DurationSeconds:
                                                                null,
                                                            IsWarmup:
                                                                false))
                                                .ToArray()))
                            .ToArray());

            var handoff =
                workoutIntelligence.Analyze(
                    request);

            var rankedMuscles =
                handoff.MuscleDemand
                    .OrderByDescending(
                        muscle =>
                            muscle.WeightedSetEquivalents)
                    .ThenBy(
                        muscle =>
                            muscle.MuscleSlug,
                        StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .Select(
                        muscle =>
                            muscleNames.TryGetValue(
                                muscle.MuscleSlug,
                                out var displayName)
                                ? displayName
                                : muscle.MuscleSlug
                                    .Replace(
                                        '-',
                                        ' '))
                    .Where(
                        name =>
                            !string.IsNullOrWhiteSpace(
                                name))
                    .ToArray();

            if (rankedMuscles.Length > 0)
            {
                muscleSummaries[
                    template.WorkoutTemplateId] =
                    string.Join(
                        ", ",
                        rankedMuscles);
            }
        }

        ViewData["WorkoutTemplateCreatedDates"] =
            createdDates;

        ViewData["WorkoutTemplateMuscleSummaries"] =
            muscleSummaries;
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
                                x.WorkoutTypeCode,
                            CreatedAtUtc =
                                x.CreatedAtUtc
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