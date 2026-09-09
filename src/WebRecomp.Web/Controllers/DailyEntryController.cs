using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

public sealed class DailyEntryController(
    RecompDbContext db,
    AppUserContextService userContext,
    ConfigurationService configuration,
    WorkoutMathService workoutMath,
    IConfiguration appConfiguration,
    Engine30WorkoutUiCatalogService engine30WorkoutCatalog) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        DateOnly? date,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(HttpContext, cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var targetDate =
            date ?? AppUserContextService.GetClientToday(context.Client);

        var model =
            await BuildAsync(context, targetDate, cancellationToken);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        DailyEntryViewModel model,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(HttpContext, cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var isAjax = IsAjaxRequest();

        ValidateWorkoutRows(
            model,
            allowIncomplete: isAjax);

        if (!ModelState.IsValid)
        {
            if (isAjax)
            {
                return BadRequest(new
                {
                    success = false,
                    message = FirstModelError(
                        "Check the highlighted Daily Entry values.")
                });
            }

            await PopulatePresentationAsync(
                context,
                model,
                cancellationToken);

            return View(model);
        }

        var client = context.Client;
        var program = context.Program;
        var now = DateTime.UtcNow;

        var checkin = await db.DailyCheckins
            .SingleOrDefaultAsync(
                x =>
                    x.ProgramId == program.ProgramId
                    && x.CheckinDate == model.Date,
                cancellationToken);

        if (checkin is null)
        {
            checkin = new DailyCheckin
            {
                CheckinId = Guid.NewGuid(),
                ClientId = client.ClientId,
                ProgramId = program.ProgramId,
                CheckinDate = model.Date,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            db.DailyCheckins.Add(checkin);
        }

        checkin.SleepHours = model.SleepHours;
        checkin.RestingHr = model.RestingHr;
        checkin.DailyActivityHours = model.DailyActivityHours;
        checkin.DailyActivityEffortCode = Clean(model.DailyActivityEffortCode)?.ToUpperInvariant();
        checkin.EnergyScore = model.EnergyScore;
        checkin.SorenessScore = model.SorenessScore;
        checkin.PainFlag = model.PainFlag;
        checkin.IllnessFlag = model.IllnessFlag;
        checkin.IntendedWorkoutTypeCode = null;
        checkin.Notes = Clean(model.RecoveryNotes);
        checkin.UpdatedAtUtc = now;

        var bodyDataEntered =
            model.WeightLb.HasValue
            || model.BodyFatPercent.HasValue
            || model.SkeletalMuscleMassLb.HasValue
            || model.SubcutaneousFatPercent.HasValue
            || model.BodyWaterLb.HasValue
            || model.VisceralFatRating.HasValue
            || model.WaistIn.HasValue
            || model.HipsIn.HasValue;

        var measurement = await db.Measurements
            .Where(x =>
                x.ProgramId == program.ProgramId
                && x.LocalMeasurementDate == model.Date
                && !x.IsDeleted)
            .OrderByDescending(x => x.MeasurementAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (bodyDataEntered || measurement is not null)
        {
            if (measurement is null)
            {
                measurement = new Measurement
                {
                    MeasurementId = Guid.NewGuid(),
                    ClientId = client.ClientId,
                    ProgramId = program.ProgramId,
                    LocalMeasurementDate = model.Date,
                    MeasurementAtUtc = now,
                    MeasurementSourceCode = "MANUAL",
                    CreatedAtUtc = now
                };

                db.Measurements.Add(measurement);
            }

            measurement.WeightLb = model.WeightLb;

            measurement.BodyFatPct =
                model.BodyFatPercent.HasValue
                    ? model.BodyFatPercent.Value / 100m
                    : null;

            // Skeletal muscle is the preferred customer-facing muscle metric.
            measurement.SkeletalMuscleMassLb =
                model.SkeletalMuscleMassLb;

            measurement.SubcutaneousFatPct =
                model.SubcutaneousFatPercent.HasValue
                    ? model.SubcutaneousFatPercent.Value / 100m
                    : null;

            measurement.BodyWaterLb = model.BodyWaterLb;
            measurement.VisceralFatRating =
                model.VisceralFatRating;
            measurement.WaistIn = model.WaistIn;
            measurement.HipsIn = model.HipsIn;
            measurement.MeasurementAtUtc = now;

            // Deliberately DO NOT overwrite MuscleMassLb. It remains available
            // for legacy/provider data when a device supplies only generic
            // muscle mass. New manual entry uses SkeletalMuscleMassLb instead.

            if (string.IsNullOrWhiteSpace(
                    measurement.MeasurementSourceCode))
            {
                measurement.MeasurementSourceCode = "MANUAL";
            }
        }

        var nutritionEntered =
            model.Calories.HasValue
            || model.ProteinG.HasValue
            || model.CarbsG.HasValue
            || model.FatG.HasValue
            || model.FiberG.HasValue
            || model.WaterOz.HasValue
            || !string.IsNullOrWhiteSpace(model.NutritionNotes);

        var nutrition = await db.NutritionDays
            .SingleOrDefaultAsync(
                x =>
                    x.ClientId == client.ClientId
                    && x.ProgramId == program.ProgramId
                    && x.NutritionDate == model.Date,
                cancellationToken);

        if (nutritionEntered || nutrition is not null)
        {
            if (nutrition is null)
            {
                nutrition = new NutritionDay
                {
                    NutritionDayId = Guid.NewGuid(),
                    ClientId = client.ClientId,
                    ProgramId = program.ProgramId,
                    NutritionDate = model.Date,
                    EntrySourceCode = "MANUAL",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                db.NutritionDays.Add(nutrition);
            }

            nutrition.ActualCalories = model.Calories;
            nutrition.ActualProteinG = model.ProteinG;
            nutrition.ActualCarbsG = model.CarbsG;
            nutrition.ActualFatG = model.FatG;
            nutrition.FiberG = model.FiberG;
            nutrition.WaterOz = model.WaterOz;
            nutrition.Notes = Clean(model.NutritionNotes);
            nutrition.UpdatedAtUtc = now;

            if (string.IsNullOrWhiteSpace(nutrition.EntrySourceCode))
                nutrition.EntrySourceCode = "MANUAL";
        }

        await db.SaveChangesAsync(cancellationToken);

        var workoutTypes =
            await engine30WorkoutCatalog
                .GetActiveMapAsync(cancellationToken);

        // Temporary compatibility map for fields still populated for frozen
        // Engine 2.0 displays. Engine 3.0 runtime uses the saved Engine 3.0 code.
        var legacyWorkoutTypes =
            await configuration.GetWorkoutTypesAsync(
                program.ConfigVersionId,
                cancellationToken);

        var settings =
            await configuration.GetSettingsAsync(
                program.ConfigVersionId,
                cancellationToken);

        var currentWeight =
            model.WeightLb
            ?? await db.Measurements
                .Where(x =>
                    x.ClientId == client.ClientId
                    && !x.IsDeleted
                    && x.LocalMeasurementDate <= model.Date
                    && x.WeightLb != null)
                .OrderByDescending(x => x.LocalMeasurementDate)
                .ThenByDescending(x => x.MeasurementAtUtc)
                .Select(x => x.WeightLb)
                .FirstOrDefaultAsync(cancellationToken);

        foreach (var input in model.Workouts.Where(x => x.HasAnyEntry))
        {
            if (string.IsNullOrWhiteSpace(input.WorkoutTypeCode)
                || !input.DurationMin.HasValue
                || input.DurationMin.Value <= 0)
            {
                continue;
            }

            input.WorkoutTypeCode =
                Engine30WorkoutUiCatalogService.NormalizeCode(
                    input.WorkoutTypeCode);

            if (!workoutTypes.ContainsKey(
                    input.WorkoutTypeCode))
            {
                ModelState.AddModelError(
                    nameof(model.Workouts),
                    $"Unknown Engine 3.0 workout type: {input.WorkoutTypeCode}");

                continue;
            }

            WorkoutSession? session = null;

            if (input.WorkoutSessionId.HasValue)
            {
                session = await db.WorkoutSessions
                    .SingleOrDefaultAsync(
                        x =>
                            x.WorkoutSessionId
                                == input.WorkoutSessionId.Value
                            && x.ClientId == client.ClientId
                            && x.ProgramId == program.ProgramId,
                        cancellationToken);
            }

            if (session is null)
            {
                session = new WorkoutSession
                {
                    WorkoutSessionId = Guid.NewGuid(),
                    ClientId = client.ClientId,
                    ProgramId = program.ProgramId,
                    WorkoutDate = model.Date,
                    WorkoutTypeCode = input.WorkoutTypeCode!,
                    EntrySourceCode = "MANUAL",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                db.WorkoutSessions.Add(session);
            }

            session.WorkoutDate = model.Date;
            session.WorkoutTypeCode = input.WorkoutTypeCode;
            session.DurationMin = input.DurationMin.Value;
            session.TypicalRestSec = input.TypicalRestSec;
            session.SessionRpe = input.SessionRpe;
            session.DeviceCalories = input.DeviceCalories;
            session.CompletedFlag = true;
            session.Notes = Clean(input.Notes);
            session.UpdatedAtUtc = now;

            if (string.IsNullOrWhiteSpace(session.EntrySourceCode))
                session.EntrySourceCode = "MANUAL";

            input.WorkoutSessionId =
                session.WorkoutSessionId;

            if (currentWeight.HasValue)
            {
                var legacyCode =
                    Engine30WorkoutUiCatalogService
                        .ToLegacyCompatibilityCode(
                            input.WorkoutTypeCode);

                if (legacyWorkoutTypes.TryGetValue(
                        legacyCode,
                        out var legacyWorkoutType))
                {
                    var derived =
                        workoutMath.CalculateActualWorkout(
                            currentWeight.Value,
                            session,
                            legacyWorkoutType,
                            settings);

                    session.CalculatedWorkoutCalories =
                        derived.WorkoutCalories;

                    session.CalculatedScaledGlycogenBurnG =
                        derived.ScaledGlycogenBurnG;

                    session.CalculatedPushLoad =
                        derived.PushLoad;
                    session.CalculatedPullLoad =
                        derived.PullLoad;
                    session.CalculatedLegLoad =
                        derived.LegLoad;
                    session.CalculatedCardioLoad =
                        derived.CardioLoad;

                    session.CalculationEngineVersion =
                        appConfiguration[
                            "WebRecomp:EngineVersion"]
                        ?? "1001.0";
                }
            }
        }

        if (!ModelState.IsValid)
        {
            if (isAjax)
            {
                return BadRequest(new
                {
                    success = false,
                    message = FirstModelError(
                        "Check the highlighted Daily Entry values.")
                });
            }

            await PopulatePresentationAsync(
                context,
                model,
                cancellationToken);

            return View(model);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (isAjax)
        {
            return Json(new
            {
                success = true,
                message = "Saved automatically.",
                workoutSessionIds =
                    model.Workouts
                        .Select(x =>
                            x.WorkoutSessionId?.ToString())
                        .ToArray()
            });
        }

        TempData["StatusMessage"] =
            "Daily Entry saved. Your Dashboard will use these inputs the next time it calculates your recommendation.";

        return RedirectToAction(
            nameof(Index),
            new
            {
                date = model.Date.ToString("yyyy-MM-dd")
            });
    }

    private async Task<DailyEntryViewModel> BuildAsync(
        AppClientContext context,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var client = context.Client!;
        var program = context.Program!;

        var measurement = await db.Measurements
            .Where(x =>
                x.ProgramId == program.ProgramId
                && x.LocalMeasurementDate == date
                && !x.IsDeleted)
            .OrderByDescending(x => x.MeasurementAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var checkin = await db.DailyCheckins
            .SingleOrDefaultAsync(
                x =>
                    x.ProgramId == program.ProgramId
                    && x.CheckinDate == date,
                cancellationToken);

        var nutrition = await db.NutritionDays
            .SingleOrDefaultAsync(
                x =>
                    x.ClientId == client.ClientId
                    && x.ProgramId == program.ProgramId
                    && x.NutritionDate == date,
                cancellationToken);

        var sessions = await db.WorkoutSessions
            .Where(x =>
                x.ClientId == client.ClientId
                && x.ProgramId == program.ProgramId
                && x.WorkoutDate == date)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var model = new DailyEntryViewModel
        {
            Date = date,

            WeightLb = measurement?.WeightLb,

            BodyFatPercent =
                measurement?.BodyFatPct.HasValue == true
                    ? measurement.BodyFatPct.Value * 100m
                    : null,

            SkeletalMuscleMassLb =
                measurement?.SkeletalMuscleMassLb,

            SubcutaneousFatPercent =
                measurement?.SubcutaneousFatPct.HasValue == true
                    ? measurement.SubcutaneousFatPct.Value * 100m
                    : null,

            BodyWaterLb = measurement?.BodyWaterLb,

            VisceralFatRating =
                measurement?.VisceralFatRating,

            WaistIn = measurement?.WaistIn,
            HipsIn = measurement?.HipsIn,

            SleepHours = checkin?.SleepHours,
            RestingHr = checkin?.RestingHr,
            DailyActivityHours = checkin?.DailyActivityHours,
            DailyActivityEffortCode = checkin?.DailyActivityEffortCode,
            EnergyScore = checkin?.EnergyScore,
            SorenessScore = checkin?.SorenessScore,
            PainFlag = checkin?.PainFlag == true,
            IllnessFlag = checkin?.IllnessFlag == true,
            RecoveryNotes = checkin?.Notes,

            Calories = nutrition?.ActualCalories,
            ProteinG = nutrition?.ActualProteinG,
            CarbsG = nutrition?.ActualCarbsG,
            FatG = nutrition?.ActualFatG,
            FiberG = nutrition?.FiberG,
            WaterOz = nutrition?.WaterOz,
            NutritionNotes = nutrition?.Notes,

            MeasurementSourceLabel =
                SourceLabel(
                    measurement?.MeasurementSourceCode),

            NutritionSourceLabel =
                SourceLabel(
                    nutrition?.EntrySourceCode),

            BodyRecoveryComplete =
                measurement?.WeightLb.HasValue == true
                || measurement?.BodyFatPct.HasValue == true
                || measurement?.SkeletalMuscleMassLb.HasValue == true
                || measurement?.SubcutaneousFatPct.HasValue == true
                || measurement?.BodyWaterLb.HasValue == true
                || measurement?.VisceralFatRating.HasValue == true
                || measurement?.WaistIn.HasValue == true
                || measurement?.HipsIn.HasValue == true
                || checkin?.SleepHours.HasValue == true
                || checkin?.RestingHr.HasValue == true
                || checkin?.EnergyScore.HasValue == true
                || checkin?.SorenessScore.HasValue == true
                || checkin?.PainFlag == true
                || checkin?.IllnessFlag == true
                || !string.IsNullOrWhiteSpace(checkin?.Notes),

            WorkoutComplete =
                sessions.Any(x => x.CompletedFlag)
                || checkin?.DailyActivityHours.HasValue == true,

            NutritionComplete =
                nutrition is not null
        };

        model.Workouts = sessions
            .Select(x => new DailyEntryWorkoutInput
            {
                WorkoutSessionId = x.WorkoutSessionId,
                WorkoutTypeCode =
                    Engine30WorkoutUiCatalogService.NormalizeCode(
                        x.WorkoutTypeCode),
                DurationMin = x.DurationMin,
                TypicalRestSec = x.TypicalRestSec,
                SessionRpe = x.SessionRpe,
                DeviceCalories = x.DeviceCalories,
                Notes = x.Notes,
                SourceLabel = SourceLabel(x.EntrySourceCode)
            })
            .ToList();

        await PopulatePresentationAsync(
            context,
            model,
            cancellationToken);

        return model;
    }

    private async Task PopulatePresentationAsync(
        AppClientContext context,
        DailyEntryViewModel model,
        CancellationToken cancellationToken)
    {
        var program = context.Program!;

        var workoutTypeList =
            await engine30WorkoutCatalog
                .GetActiveListAsync(cancellationToken);

        var workoutTypes =
            workoutTypeList.ToDictionary(
                x => x.WorkoutTypeCode,
                StringComparer.OrdinalIgnoreCase);

        model.WorkoutTypeOptions = workoutTypeList
            .Where(x => x.IsSelectable)
            .Select(x =>
                new DailyEntryWorkoutTypeOption(
                    x.WorkoutTypeCode,
                    x.DisplayName))
            .ToList();

        var planned = await db.PlannedWorkouts
            .SingleOrDefaultAsync(
                x =>
                    x.ProgramId == program.ProgramId
                    && x.PlannedDate == model.Date,
                cancellationToken);

        model.PlannedWorkoutTypeCode =
            string.IsNullOrWhiteSpace(planned?.WorkoutTypeCode)
                ? null
                : Engine30WorkoutUiCatalogService.NormalizeCode(
                    planned.WorkoutTypeCode);

        if (planned is not null
            && !string.IsNullOrWhiteSpace(
                model.PlannedWorkoutTypeCode)
            && workoutTypes.TryGetValue(
                model.PlannedWorkoutTypeCode,
                out var plannedType))
        {
            model.PlannedWorkoutName =
                plannedType.DisplayName;
        }
        else
        {
            model.PlannedWorkoutName =
                "No workout planned";
        }

        if (model.Workouts.Count == 0)
        {
            model.Workouts.Add(
                new DailyEntryWorkoutInput
                {
                    WorkoutTypeCode =
                        model.PlannedWorkoutTypeCode
                });
        }
    }

    private void ValidateWorkoutRows(
        DailyEntryViewModel model,
        bool allowIncomplete = false)
    {
        for (var i = 0; i < model.Workouts.Count; i++)
        {
            var workout = model.Workouts[i];

            if (!workout.HasAnyEntry)
                continue;

            if (allowIncomplete
                && (string.IsNullOrWhiteSpace(
                        workout.WorkoutTypeCode)
                    || !workout.DurationMin.HasValue
                    || workout.DurationMin.Value <= 0))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(
                    workout.WorkoutTypeCode))
            {
                ModelState.AddModelError(
                    $"Workouts[{i}].WorkoutTypeCode",
                    "Choose the workout type.");
            }

            if (!workout.DurationMin.HasValue
                || workout.DurationMin.Value <= 0)
            {
                ModelState.AddModelError(
                    $"Workouts[{i}].DurationMin",
                    "Enter the workout duration.");
            }
        }
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
               .FirstOrDefault(x =>
                   !string.IsNullOrWhiteSpace(x))
           ?? fallback;
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string? SourceLabel(
        string? sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return null;

        if (string.Equals(
                sourceCode,
                "MANUAL",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Manual entry";
        }

        return sourceCode
            .Replace("_", " ")
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x =>
                char.ToUpperInvariant(x[0])
                + x[1..])
            .Aggregate((a, b) => $"{a} {b}");
    }
}
