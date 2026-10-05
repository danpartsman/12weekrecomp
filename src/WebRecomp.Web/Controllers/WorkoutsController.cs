using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutIntelligence;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

public sealed class WorkoutsController(
    RecompDbContext db,
    AppUserContextService userContext,
    Engine30WorkoutUiCatalogService engine30WorkoutCatalog,
    WorkoutActualService workoutActuals) : Controller
{
    private static readonly HashSet<string> HiddenLegacyDuplicateCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Z2_40_RPE4",
            "ACCESSORY_45_RPE7",
            "MAJOR_60_RPE8",
            "ACC_PLUS_Z2_75_RPE6"
        };

    private static readonly Dictionary<string, string> FriendlyNameOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["RECOVERY_0_RPE2"] = "Recovery",
            ["HIIT_45_RPE9"] = "HIIT",
            ["LONG_MIXED_75_RPE9"] = "Long Mixed",
            ["PICKLEBALL_120_RPE6"] = "Pickleball"
        };

    [HttpGet]
    public async Task<IActionResult> New(
        DateOnly? date,
        Guid? editId,
        bool addAnother = false,
        CancellationToken cancellationToken = default)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        ViewBag.DetailedWorkoutEntryEnabled =
            context.Program.DetailedWorkoutEntryEnabled;

        WorkoutEntryViewModel vm;

        if (editId.HasValue)
        {
            var existing = await db.WorkoutSessions
                .SingleOrDefaultAsync(
                    x => x.WorkoutSessionId == editId.Value
                         && x.ClientId == context.Client.ClientId
                         && x.ProgramId == context.Program.ProgramId,
                    cancellationToken);

            if (existing is null)
                return NotFound();

            vm = new WorkoutEntryViewModel
            {
                WorkoutSessionId = existing.WorkoutSessionId,
                Date = existing.WorkoutDate,
                WorkoutTypeCode =
                    Engine30WorkoutUiCatalogService.NormalizeCode(
                        existing.WorkoutTypeCode),
                SystemWorkoutCode =
                    existing.SystemWorkoutCode
                    ?? SystemWorkoutRecipeCatalog
                        .GetDefaultPresentationForEngine(
                            existing.WorkoutTypeCode)
                        ?.SystemCode,
                DurationMin = existing.DurationMin,
                TypicalRestSec = existing.TypicalRestSec,
                SessionRpe = existing.SessionRpe,
                DeviceCalories = existing.DeviceCalories,
                CompletedFlag = existing.CompletedFlag,
                Notes = existing.Notes
            };
        }
        else
        {
            var targetDate = date ?? AppUserContextService.GetClientToday(context.Client);

            PlannedWorkout? planned = null;
            WorkoutTypeDefinition? plannedDefinition = null;

            if (!addAnother)
            {
                planned = await db.PlannedWorkouts
                    .SingleOrDefaultAsync(
                        x => x.ProgramId == context.Program.ProgramId
                             && x.PlannedDate == targetDate,
                        cancellationToken);

                if (planned is not null)
                {
                    plannedDefinition = await db.WorkoutTypeDefinitions
                        .SingleOrDefaultAsync(
                            x => x.ConfigVersionId == context.Program.ConfigVersionId
                                 && x.WorkoutTypeCode == planned.WorkoutTypeCode
                                 && x.IsActive,
                            cancellationToken);
                }
            }

            var normalizedPlannedCode =
                string.IsNullOrWhiteSpace(planned?.WorkoutTypeCode)
                    ? ""
                    : Engine30WorkoutUiCatalogService.NormalizeCode(
                        planned!.WorkoutTypeCode);

            var plannedSystemCode =
                planned?.SystemWorkoutCode;

            if (string.IsNullOrWhiteSpace(plannedSystemCode)
                && !string.IsNullOrWhiteSpace(normalizedPlannedCode))
            {
                plannedSystemCode =
                    SystemWorkoutRecipeCatalog
                        .GetDefaultPresentationForEngine(
                            normalizedPlannedCode)
                        ?.SystemCode;
            }

            vm = new WorkoutEntryViewModel
            {
                Date = targetDate,
                WorkoutTypeCode = normalizedPlannedCode,
                SystemWorkoutCode = plannedSystemCode,
                DurationMin =
                    planned?.PlannedDurationMin
                    ?? plannedDefinition?.DefaultMinutes
                    ?? 60,
                TypicalRestSec = plannedDefinition is { DefaultRestSec: > 0 }
                    ? plannedDefinition.DefaultRestSec
                    : null,
                SessionRpe = planned?.PlannedRpe,
                CompletedFlag = true
            };
        }

        await PopulateTypes(
            vm,
            context.Program.ConfigVersionId,
            vm.WorkoutTypeCode,
            vm.SystemWorkoutCode,
            cancellationToken);

        await PopulateExistingWorkouts(
            context.Program.ProgramId,
            context.Program.ConfigVersionId,
            vm.Date,
            cancellationToken);

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(
        WorkoutEntryViewModel model,
        CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        SystemWorkoutRecipe? selectedSystemWorkout = null;

        if (!string.IsNullOrWhiteSpace(model.SystemWorkoutCode))
        {
            if (!SystemWorkoutRecipeCatalog.TryGet(
                    model.SystemWorkoutCode,
                    out selectedSystemWorkout)
                || selectedSystemWorkout is null)
            {
                ModelState.AddModelError(
                    nameof(model.WorkoutTypeCode),
                    "Select a valid System Workout.");
            }
            else
            {
                model.WorkoutTypeCode =
                    selectedSystemWorkout.EngineWorkoutTypeCode;
            }
        }

        model.WorkoutTypeCode =
            Engine30WorkoutUiCatalogService.NormalizeCode(
                model.WorkoutTypeCode);

        // The visible selector binds SystemWorkoutCode. Re-evaluate the
        // canonical hidden Engine code after resolving the System Workout.
        ModelState.Remove(nameof(model.WorkoutTypeCode));

        if (string.IsNullOrWhiteSpace(model.WorkoutTypeCode))
        {
            ModelState.AddModelError(
                nameof(model.WorkoutTypeCode),
                "Select a valid workout type.");
        }

        var activeWorkoutTypes =
            await engine30WorkoutCatalog
                .GetActiveListAsync(cancellationToken);

        var engineWorkout =
            activeWorkoutTypes.SingleOrDefault(
                x => x.WorkoutTypeCode.Equals(
                    model.WorkoutTypeCode,
                    StringComparison.OrdinalIgnoreCase));

        if (engineWorkout is null)
        {
            ModelState.AddModelError(
                nameof(model.WorkoutTypeCode),
                "Select a valid workout type.");
        }

        if (selectedSystemWorkout is not null
            && engineWorkout is not null)
        {
            if (model.DurationMin <= 0m)
            {
                model.DurationMin =
                    SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(
                        selectedSystemWorkout,
                        engineWorkout.DefaultMinutes);
            }

            if (!model.SessionRpe.HasValue)
            {
                var engineTypeCatalog =
                    new Engine30WorkoutTypeCatalog();

                var baseRpe =
                    engineTypeCatalog.TryGet(
                        model.WorkoutTypeCode,
                        out var engineDefinition)
                    && engineDefinition is not null
                        ? engineDefinition.DefaultRpe
                        : 5m;

                model.SessionRpe =
                    SystemWorkoutRecipeCatalog.ResolveDefaultRpe(
                        selectedSystemWorkout,
                        baseRpe);
            }
        }

        if (!ModelState.IsValid)
        {
            if (IsAjaxRequest())
            {
                return BadRequest(new
                {
                    success = false,
                    message = FirstModelError("Complete the required workout fields before Recomp can save this session.")
                });
            }

            await PopulateTypes(
                model,
                context.Program.ConfigVersionId,
                model.WorkoutTypeCode,
                model.SystemWorkoutCode,
                cancellationToken);

            await PopulateExistingWorkouts(
                context.Program.ProgramId,
                context.Program.ConfigVersionId,
                model.Date,
                cancellationToken);

            ViewBag.DetailedWorkoutEntryEnabled =
                context.Program.DetailedWorkoutEntryEnabled;

            return View(model);
        }

        WorkoutSession session;
        var isEdit = model.WorkoutSessionId.HasValue;

        if (isEdit)
        {
            session = await db.WorkoutSessions
                .SingleOrDefaultAsync(
                    x => x.WorkoutSessionId == model.WorkoutSessionId!.Value
                         && x.ClientId == context.Client.ClientId
                         && x.ProgramId == context.Program.ProgramId,
                    cancellationToken)
                ?? throw new InvalidOperationException("The workout session could not be found.");

            // Editing changes the stored actual session; it does not create a duplicate row.
            session.WorkoutDate = model.Date;
            session.WorkoutName =
                selectedSystemWorkout?.DisplayName
                ?? session.WorkoutName;
            session.SystemWorkoutCode =
                selectedSystemWorkout?.SystemCode;
            session.WorkoutTypeCode = model.WorkoutTypeCode;
            session.DurationMin = model.DurationMin;
            session.TypicalRestSec = model.TypicalRestSec;
            session.SessionRpe = model.SessionRpe;
            session.DeviceCalories = model.DeviceCalories;
            session.CompletedFlag = model.CompletedFlag;
            session.Notes = model.Notes;
            workoutActuals.ApplyCurrentEngineState(
                session,
                model.CompletedFlag);
        }
        else
        {
            session = new WorkoutSession
            {
                WorkoutSessionId = Guid.NewGuid(),
                ClientId = context.Client.ClientId,
                ProgramId = context.Program.ProgramId,
                WorkoutDate = model.Date,
                WorkoutName = selectedSystemWorkout?.DisplayName,
                SystemWorkoutCode =
                    selectedSystemWorkout?.SystemCode,
                WorkoutTypeCode = model.WorkoutTypeCode,
                DurationMin = model.DurationMin,
                TypicalRestSec = model.TypicalRestSec,
                SessionRpe = model.SessionRpe,
                DeviceCalories = model.DeviceCalories,
                CompletedFlag = model.CompletedFlag,
                Notes = model.Notes,
                EntrySourceCode = RecompInputSourceCodes.Manual,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            workoutActuals.ApplyCurrentEngineState(
                session,
                model.CompletedFlag);

            db.WorkoutSessions.Add(session);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (IsAjaxRequest())
        {
            return Json(new
            {
                success = true,
                message = "Saved automatically.",
                workoutSessionId = session.WorkoutSessionId
            });
        }

        TempData["StatusMessage"] = isEdit ? "Workout updated." : "Workout saved.";
        return RedirectToAction(nameof(New), new { date = model.Date.ToString("yyyy-MM-dd") });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
        Guid workoutSessionId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var session = await db.WorkoutSessions
            .SingleOrDefaultAsync(
                x => x.WorkoutSessionId == workoutSessionId
                     && x.ClientId == context.Client.ClientId
                     && x.ProgramId == context.Program.ProgramId,
                cancellationToken);

        if (session is null)
        {
            TempData["StatusMessage"] = "Workout was not found.";
            return RedirectToAction(nameof(New), new { date = date.ToString("yyyy-MM-dd") });
        }

        var redirectDate = session.WorkoutDate;

        db.WorkoutSessions.Remove(session);
        await db.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Workout deleted.";
        return RedirectToAction(nameof(New), new { date = redirectDate.ToString("yyyy-MM-dd") });
    }

    private bool IsAjaxRequest()
        => string.Equals(
            Request.Headers["X-Requested-With"].ToString(),
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);

    private string FirstModelError(string fallback)
        => ModelState.Values
               .SelectMany(x => x.Errors)
               .Select(x => x.ErrorMessage)
               .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
           ?? fallback;

    private async Task PopulateExistingWorkouts(
        Guid programId,
        Guid configVersionId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var rows = await db.WorkoutSessions
            .Where(x => x.ProgramId == programId && x.WorkoutDate == date)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var codes = rows.Select(x => x.WorkoutTypeCode).Distinct().ToList();

        var definitions = await db.WorkoutTypeDefinitions
            .Where(x => x.ConfigVersionId == configVersionId
                        && codes.Contains(x.WorkoutTypeCode))
            .ToDictionaryAsync(x => x.WorkoutTypeCode, StringComparer.OrdinalIgnoreCase, cancellationToken);

        ViewBag.ExistingWorkouts = rows
            .Select(x =>
            {
                var displayName =
                    !string.IsNullOrWhiteSpace(x.WorkoutName)
                        ? x.WorkoutName
                        : !string.IsNullOrWhiteSpace(x.SystemWorkoutCode)
                          && SystemWorkoutRecipeCatalog.TryGet(
                              x.SystemWorkoutCode,
                              out var systemRecipe)
                          && systemRecipe is not null
                            ? systemRecipe.DisplayName
                            : definitions.TryGetValue(
                                x.WorkoutTypeCode,
                                out var definition)
                                ? FriendlyDisplayName(definition)
                                : x.WorkoutTypeCode.Replace('_', ' ');

                return new WorkoutSessionSummaryViewModel
                {
                    WorkoutSessionId = x.WorkoutSessionId,
                    WorkoutDisplayName = displayName,
                    DurationMin = x.DurationMin,
                    SessionRpe = x.SessionRpe,
                    CompletedFlag = x.CompletedFlag
                };
            })
            .ToList();
    }

    private async Task PopulateTypes(
        WorkoutEntryViewModel vm,
        Guid configVersionId,
        string? currentCode,
        string? currentSystemCode,
        CancellationToken cancellationToken)
    {
        _ = configVersionId;

        var active =
            await engine30WorkoutCatalog
                .GetActiveListAsync(cancellationToken);

        var activeMap =
            active.ToDictionary(
                x => x.WorkoutTypeCode,
                StringComparer.OrdinalIgnoreCase);

        var engineTypeCatalog =
            new Engine30WorkoutTypeCatalog();

        var groupNames = new[]
        {
            "Set A",
            "Set B",
            "Easy",
            "Standard",
            "Hard",
            "Recovery"
        };

        var groups =
            groupNames.ToDictionary(
                x => x,
                x => new SelectListGroup { Name = x },
                StringComparer.OrdinalIgnoreCase);

        vm.SystemWorkouts =
            SystemWorkoutRecipeCatalog
                .PresentationRecipes
                .Where(x => activeMap.ContainsKey(
                    x.EngineWorkoutTypeCode))
                .Select(
                    recipe =>
                    {
                        var configured =
                            activeMap[recipe.EngineWorkoutTypeCode];

                        var baseRpe =
                            engineTypeCatalog.TryGet(
                                recipe.EngineWorkoutTypeCode,
                                out var engineDefinition)
                            && engineDefinition is not null
                                ? engineDefinition.DefaultRpe
                                : 5m;

                        return new SystemWorkoutOptionViewModel
                        {
                            SystemCode = recipe.SystemCode,
                            EngineWorkoutTypeCode =
                                recipe.EngineWorkoutTypeCode,
                            DisplayName = recipe.DisplayName,
                            GroupName = recipe.GroupName,
                            DefaultMinutes =
                                SystemWorkoutRecipeCatalog
                                    .ResolveDefaultMinutes(
                                        recipe,
                                        configured.DefaultMinutes),
                            DefaultRpe =
                                SystemWorkoutRecipeCatalog
                                    .ResolveDefaultRpe(
                                        recipe,
                                        baseRpe)
                        };
                    })
                .ToList();

        vm.WorkoutTypes =
        [
            new SelectListItem("(Select workout)", "")
        ];

        foreach (var recipe in SystemWorkoutRecipeCatalog
                     .PresentationRecipes
                     .Where(x => activeMap.ContainsKey(
                         x.EngineWorkoutTypeCode)))
        {
            vm.WorkoutTypes.Add(
                new SelectListItem(
                    recipe.DisplayName,
                    recipe.SystemCode,
                    string.Equals(
                        recipe.SystemCode,
                        currentSystemCode,
                        StringComparison.OrdinalIgnoreCase))
                {
                    Group = groups[recipe.GroupName]
                });
        }

        if (string.IsNullOrWhiteSpace(currentSystemCode)
            && !string.IsNullOrWhiteSpace(currentCode))
        {
            var fallback =
                SystemWorkoutRecipeCatalog
                    .GetDefaultPresentationForEngine(
                        Engine30WorkoutUiCatalogService.NormalizeCode(
                            currentCode));

            if (fallback is not null)
            {
                var item =
                    vm.WorkoutTypes.FirstOrDefault(
                        x => string.Equals(
                            x.Value,
                            fallback.SystemCode,
                            StringComparison.OrdinalIgnoreCase));

                if (item is not null)
                    item.Selected = true;
            }
        }
    }

    private static string FriendlyDisplayName(WorkoutTypeDefinition type)
        => FriendlyNameOverrides.TryGetValue(type.WorkoutTypeCode, out var name)
            ? name
            : type.DisplayName;

    private static SelectListGroup WorkoutGroup(
        WorkoutTypeDefinition type,
        SelectListGroup strengthMajor,
        SelectListGroup strengthAccessory,
        SelectListGroup conditioning,
        SelectListGroup restRecoveryOther)
        => type.WorkoutCategoryCode.ToUpperInvariant() switch
        {
            WorkoutCategoryCatalog.MajorStrength =>
                strengthMajor,

            WorkoutCategoryCatalog.AccessoryStrength =>
                strengthAccessory,

            WorkoutCategoryCatalog.ConditioningCardio =>
                conditioning,

            WorkoutCategoryCatalog.RestRecoveryOther =>
                restRecoveryOther,

            _ => throw new InvalidOperationException(
                $"Workout type '{type.WorkoutTypeCode}' has invalid workout category '{type.WorkoutCategoryCode}'.")
        };
}
