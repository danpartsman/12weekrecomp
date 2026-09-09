using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

public sealed class WorkoutsController(
    RecompDbContext db,
    AppUserContextService userContext,
    ConfigurationService configuration,
    WorkoutMathService math,
    IConfiguration appConfiguration) : Controller
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
                WorkoutTypeCode = existing.WorkoutTypeCode,
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

            vm = new WorkoutEntryViewModel
            {
                Date = targetDate,
                WorkoutTypeCode = planned?.WorkoutTypeCode ?? "",
                DurationMin = plannedDefinition?.DefaultMinutes ?? 60,
                TypicalRestSec = plannedDefinition is { DefaultRestSec: > 0 }
                    ? plannedDefinition.DefaultRestSec
                    : null,
                CompletedFlag = true
            };
        }

        await PopulateTypes(
            vm,
            context.Program.ConfigVersionId,
            vm.WorkoutTypeCode,
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

        var workout = await db.WorkoutTypeDefinitions
            .SingleOrDefaultAsync(
                x => x.ConfigVersionId == context.Program.ConfigVersionId
                     && x.WorkoutTypeCode == model.WorkoutTypeCode
                     && x.IsActive,
                cancellationToken);

        if (workout is null)
            ModelState.AddModelError(nameof(model.WorkoutTypeCode), "Select a valid workout type.");

        var weight = await db.Measurements
            .Where(x => x.ClientId == context.Client.ClientId
                        && !x.IsDeleted
                        && x.LocalMeasurementDate <= model.Date
                        && x.WeightLb != null)
            .OrderByDescending(x => x.LocalMeasurementDate)
            .ThenByDescending(x => x.MeasurementAtUtc)
            .Select(x => x.WeightLb)
            .FirstOrDefaultAsync(cancellationToken);

        if (!weight.HasValue)
            ModelState.AddModelError("", "A weight measurement is required on or before the workout date.");

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
                cancellationToken);

            await PopulateExistingWorkouts(
                context.Program.ProgramId,
                context.Program.ConfigVersionId,
                model.Date,
                cancellationToken);

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
            session.WorkoutTypeCode = model.WorkoutTypeCode;
            session.DurationMin = model.DurationMin;
            session.TypicalRestSec = model.TypicalRestSec;
            session.SessionRpe = model.SessionRpe;
            session.DeviceCalories = model.DeviceCalories;
            session.CompletedFlag = model.CompletedFlag;
            session.Notes = model.Notes;
            session.CalculationEngineVersion =
                appConfiguration["WebRecomp:EngineVersion"] ?? "1001.0";
            session.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            session = new WorkoutSession
            {
                WorkoutSessionId = Guid.NewGuid(),
                ClientId = context.Client.ClientId,
                ProgramId = context.Program.ProgramId,
                WorkoutDate = model.Date,
                WorkoutTypeCode = model.WorkoutTypeCode,
                DurationMin = model.DurationMin,
                TypicalRestSec = model.TypicalRestSec,
                SessionRpe = model.SessionRpe,
                DeviceCalories = model.DeviceCalories,
                CompletedFlag = model.CompletedFlag,
                Notes = model.Notes,
                EntrySourceCode = "MANUAL",
                CalculationEngineVersion =
                    appConfiguration["WebRecomp:EngineVersion"] ?? "1001.0",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            db.WorkoutSessions.Add(session);
        }

        var derived = math.CalculateActualWorkout(weight!.Value, session, workout!, await configuration.GetSettingsAsync(
            context.Program.ConfigVersionId,
            cancellationToken));

        session.CalculatedWorkoutCalories = derived.WorkoutCalories;
        session.CalculatedScaledGlycogenBurnG = derived.ScaledGlycogenBurnG;
        session.CalculatedPushLoad = derived.PushLoad;
        session.CalculatedPullLoad = derived.PullLoad;
        session.CalculatedLegLoad = derived.LegLoad;
        session.CalculatedCardioLoad = derived.CardioLoad;

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
                var displayName = definitions.TryGetValue(x.WorkoutTypeCode, out var definition)
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
        CancellationToken cancellationToken)
    {
        var types = await configuration.GetWorkoutTypeListAsync(
            configVersionId,
            cancellationToken);

        var strengthMajor = new SelectListGroup
        {
            Name = WorkoutCategoryCatalog.DisplayName(
                WorkoutCategoryCatalog.MajorStrength)
        };

        var strengthAccessory = new SelectListGroup
        {
            Name = WorkoutCategoryCatalog.DisplayName(
                WorkoutCategoryCatalog.AccessoryStrength)
        };

        var conditioning = new SelectListGroup
        {
            Name = WorkoutCategoryCatalog.DisplayName(
                WorkoutCategoryCatalog.ConditioningCardio)
        };

        var restRecoveryOther = new SelectListGroup
        {
            Name = WorkoutCategoryCatalog.DisplayName(
                WorkoutCategoryCatalog.RestRecoveryOther)
        };

        vm.WorkoutTypes =
        [
            new SelectListItem("(Select workout)", "")
        ];

        foreach (var type in types
                     .Where(x =>
                         x.IsSelectable
                         || string.Equals(
                             x.WorkoutTypeCode,
                             currentCode,
                             StringComparison.OrdinalIgnoreCase))
                     .Where(x =>
                         !HiddenLegacyDuplicateCodes.Contains(x.WorkoutTypeCode)
                         || string.Equals(
                             x.WorkoutTypeCode,
                             currentCode,
                             StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x =>
                         WorkoutCategoryCatalog.SortOrder(
                             x.WorkoutCategoryCode))
                     .ThenBy(x => x.DisplayOrder)
                     .ThenBy(x => FriendlyDisplayName(x)))
        {
            vm.WorkoutTypes.Add(
                new SelectListItem(
                    FriendlyDisplayName(type),
                    type.WorkoutTypeCode)
                {
                    Group = WorkoutGroup(
                        type,
                        strengthMajor,
                        strengthAccessory,
                        conditioning,
                        restRecoveryOther)
                });
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

