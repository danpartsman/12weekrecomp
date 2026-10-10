using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutIntelligence;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

public sealed class DailyEntryController(
    RecompDbContext db,
    AppUserContextService userContext,
    Engine30WorkoutUiCatalogService engine30WorkoutCatalog,
    WorkoutActualService workoutActuals,
    INutritionFoodLogStore nutritionStore) : Controller
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

                // B2.35.30 REV2 DAILY ENTRY FRESH GET
        // This page is a dated editor over authoritative database records.
        // Never allow a previously visited blank form to be reused by browser/proxy cache.
        Response.Headers["Cache-Control"] =
            "no-store, no-cache, must-revalidate";
        Response.Headers["Pragma"] =
            "no-cache";
        Response.Headers["Expires"] =
            "0";

        // Razor input tag helpers prefer ModelState values over Model values.
        // A GET must show the freshly rebuilt DB-backed model.
        ModelState.Clear();

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        DailyEntryViewModel model,
        bool nutritionManualOverride,
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

        var unchangedDeviceMeasurement = measurement?.MeasurementSourceCode == "FITDAYS"
            && WebRecomp.Web.Services.ExternalConnections.FitdaysMeasurementMapper.MatchesEntry(measurement, model);
        if (!unchangedDeviceMeasurement && (bodyDataEntered || measurement is not null))
        {
            if (measurement?.MeasurementSourceCode == "FITDAYS")
            {
                // Preserve the original provider reading and its deduplication key.
                // A separate manual record makes this day ineligible for future imports.
                var original = measurement;
                measurement = new Measurement
                {
                    MeasurementId = Guid.NewGuid(), ClientId = original.ClientId,
                    ProgramId = original.ProgramId, LocalMeasurementDate = model.Date,
                    MeasurementAtUtc = now, MeasurementSourceCode = "MANUAL",
                    MuscleMassLb = original.MuscleMassLb, ScaleWhr = original.ScaleWhr,
                    Notes = original.Notes, CreatedAtUtc = now
                };
                db.Measurements.Add(measurement);
            }
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

        NutritionDailyLogReadModel? detailedNutrition = null;

        if (program.NutritionFoodLogEnabled)
        {
            detailedNutrition =
                await nutritionStore.GetDailyLogAsync(
                    client.ClientId,
                    model.Date,
                    cancellationToken);
        }

        var foodLogAvailable =
            detailedNutrition?.Totals is not null
            && detailedNutrition.Items.Count > 0;

        var useFoodLog =
            program.NutritionFoodLogEnabled
            && foodLogAvailable
            && !nutritionManualOverride;

        if (useFoodLog)
        {
            if (nutrition is null)
            {
                nutrition = new NutritionDay
                {
                    NutritionDayId = Guid.NewGuid(),
                    ClientId = client.ClientId,
                    ProgramId = program.ProgramId,
                    NutritionDate = model.Date,
                    EntrySourceCode = RecompInputSourceCodes.FoodLog,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                db.NutritionDays.Add(nutrition);
            }

            nutrition.ActualCalories =
                detailedNutrition!.Totals!.EnergyKcal;
            nutrition.ActualProteinG =
                detailedNutrition.Totals.ProteinG;
            nutrition.ActualCarbsG =
                detailedNutrition.Totals.CarbohydrateG;
            nutrition.ActualFatG =
                detailedNutrition.Totals.FatG;
            nutrition.FiberG =
                detailedNutrition.Totals.FiberG;
            nutrition.WaterOz = model.WaterOz;
            nutrition.Notes = Clean(model.NutritionNotes);
            nutrition.EntrySourceCode =
                RecompInputSourceCodes.FoodLog;
            nutrition.SourceRecordId =
                detailedNutrition.DailyLogId?.ToString("D");
            nutrition.UpdatedAtUtc = now;
        }
        else if (nutritionEntered || nutrition is not null)
        {
            if (nutrition is null)
            {
                nutrition = new NutritionDay
                {
                    NutritionDayId = Guid.NewGuid(),
                    ClientId = client.ClientId,
                    ProgramId = program.ProgramId,
                    NutritionDate = model.Date,
                    EntrySourceCode =
                        program.NutritionFoodLogEnabled
                        && foodLogAvailable
                            ? "MANUAL_OVERRIDE"
                            : RecompInputSourceCodes.Manual,
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
            nutrition.EntrySourceCode =
                program.NutritionFoodLogEnabled
                && foodLogAvailable
                    ? "MANUAL_OVERRIDE"
                    : RecompInputSourceCodes.Manual;
            nutrition.SourceRecordId = null;
            nutrition.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        var workoutTypes =
            await engine30WorkoutCatalog
                .GetActiveMapAsync(cancellationToken);

        foreach (var input in model.Workouts.Where(x => x.HasAnyEntry))
        {
            SystemWorkoutRecipe? selectedSystemWorkout = null;

            if (!string.IsNullOrWhiteSpace(
                    input.SystemWorkoutCode)
                && SystemWorkoutRecipeCatalog.TryGet(
                    input.SystemWorkoutCode,
                    out selectedSystemWorkout)
                && selectedSystemWorkout is not null)
            {
                input.WorkoutTypeCode =
                    selectedSystemWorkout
                        .EngineWorkoutTypeCode;

                input.WorkoutName =
                    selectedSystemWorkout.DisplayName;

                if (workoutTypes.TryGetValue(
                        selectedSystemWorkout.EngineWorkoutTypeCode,
                        out var systemType))
                {
                    input.DurationMin ??=
                        SystemWorkoutRecipeCatalog
                            .ResolveDefaultMinutes(
                                selectedSystemWorkout,
                                systemType.DefaultMinutes);

                    if (!input.SessionRpe.HasValue)
                    {
                        var engineCatalog =
                            new Engine30WorkoutTypeCatalog();

                        var baseRpe =
                            engineCatalog.TryGet(
                                selectedSystemWorkout.EngineWorkoutTypeCode,
                                out var engineDefinition)
                            && engineDefinition is not null
                                ? engineDefinition.DefaultRpe
                                : 5m;

                        input.SessionRpe =
                            SystemWorkoutRecipeCatalog
                                .ResolveDefaultRpe(
                                    selectedSystemWorkout,
                                    baseRpe);
                    }
                }

                if (selectedSystemWorkout.SystemCode.Equals(
                        CardioActivityCatalog.PresentationSystemCode,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!CardioActivityCatalog.TryGet(
                            input.CardioActivityCode,
                            out var cardioActivity)
                        || cardioActivity is null)
                    {
                        ModelState.AddModelError(
                            nameof(model.Workouts),
                            "Choose the cardio activity you actually performed.");

                        continue;
                    }

                    input.WorkoutTypeCode =
                        cardioActivity.EngineWorkoutTypeCode;

                    input.WorkoutName =
                        cardioActivity.DisplayName;

                    if (workoutTypes.TryGetValue(
                            cardioActivity.EngineWorkoutTypeCode,
                            out var cardioType))
                    {
                        input.DurationMin ??=
                            cardioType.DefaultMinutes;

                        if (!input.SessionRpe.HasValue)
                        {
                            var cardioCatalog =
                                new Engine30WorkoutTypeCatalog();

                            if (cardioCatalog.TryGet(
                                    cardioActivity.EngineWorkoutTypeCode,
                                    out var cardioDefinition)
                                && cardioDefinition is not null)
                            {
                                input.SessionRpe =
                                    cardioDefinition.DefaultRpe;
                            }
                        }
                    }
                }
            }

            var isRest =
                string.Equals(
                    input.WorkoutTypeCode,
                    "REST",
                    StringComparison.OrdinalIgnoreCase);

            if (isRest)
            {
                input.DurationMin = 0m;
                input.SessionRpe = null;
                input.TypicalRestSec = null;
                input.DeviceCalories = null;
            }

            if (string.IsNullOrWhiteSpace(input.WorkoutTypeCode)
                || (!isRest
                    && (!input.DurationMin.HasValue
                        || input.DurationMin.Value <= 0)))
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

            // Autosave idempotency: a manual row carries a stable source key
            // before its first POST. If the browser has not yet received the
            // generated WorkoutSessionId, a repeated POST must update the same
            // row rather than inserting another physical workout.
            if (session is null
                && !string.IsNullOrWhiteSpace(input.SourceRecordId))
            {
                var sourceRecordId =
                    input.SourceRecordId.Trim();

                session = await db.WorkoutSessions
                    .SingleOrDefaultAsync(
                        x =>
                            x.ClientId == client.ClientId
                            && x.ProgramId == program.ProgramId
                            && x.WorkoutDate == model.Date
                            && x.EntrySourceCode == RecompInputSourceCodes.Manual
                            && x.SourceRecordId == sourceRecordId,
                        cancellationToken);
            }

            if (session is null)
            {
                if (string.IsNullOrWhiteSpace(input.SourceRecordId))
                {
                    input.SourceRecordId =
                        NewManualWorkoutSourceRecordId();
                }

                session = new WorkoutSession
                {
                    WorkoutSessionId = Guid.NewGuid(),
                    ClientId = client.ClientId,
                    ProgramId = program.ProgramId,
                    WorkoutDate = model.Date,
                    WorkoutName = input.WorkoutName,
                    SystemWorkoutCode =
                        input.SystemWorkoutCode,
                    WorkoutTypeCode = input.WorkoutTypeCode!,
                    EntrySourceCode = RecompInputSourceCodes.Manual,
                    SourceRecordId = input.SourceRecordId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                db.WorkoutSessions.Add(session);
            }

            session.WorkoutDate = model.Date;
            session.WorkoutName =
                string.IsNullOrWhiteSpace(input.WorkoutName)
                    ? session.WorkoutName
                    : input.WorkoutName.Trim();
            session.SystemWorkoutCode =
                string.IsNullOrWhiteSpace(
                    input.SystemWorkoutCode)
                    ? null
                    : input.SystemWorkoutCode.Trim();
            session.WorkoutTypeCode = input.WorkoutTypeCode;
            session.DurationMin =
                isRest
                    ? 0m
                    : input.DurationMin!.Value;
            session.TypicalRestSec = input.TypicalRestSec;
            session.SessionRpe = input.SessionRpe;
            session.DeviceCalories = input.DeviceCalories;
            session.CompletedFlag = true;
            session.Notes = Clean(input.Notes);
            session.UpdatedAtUtc = now;

            if (string.IsNullOrWhiteSpace(session.EntrySourceCode))
                session.EntrySourceCode = RecompInputSourceCodes.Manual;

            if (string.Equals(
                    session.EntrySourceCode,
                    RecompInputSourceCodes.Manual,
                    StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(session.SourceRecordId))
            {
                if (string.IsNullOrWhiteSpace(input.SourceRecordId))
                {
                    input.SourceRecordId =
                        NewManualWorkoutSourceRecordId();
                }

                session.SourceRecordId =
                    input.SourceRecordId;
            }

            input.WorkoutSessionId =
                session.WorkoutSessionId;

            input.SourceRecordId =
                session.SourceRecordId;

            workoutActuals.ApplyCurrentEngineState(
                session,
                completed: true);
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
                        .ToArray(),
                workoutSourceRecordIds =
                    model.Workouts
                        .Select(x =>
                            x.SourceRecordId)
                        .ToArray(),
                nutrition = new
                {
                    calories = nutrition?.ActualCalories,
                    proteinG = nutrition?.ActualProteinG,
                    carbsG = nutrition?.ActualCarbsG,
                    fatG = nutrition?.ActualFatG,
                    fiberG = nutrition?.FiberG,
                    source =
                        SourceLabel(
                            nutrition?.EntrySourceCode)
                }
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

    // B2.35.26 DAILY ENTRY ENGINE ACTUALS LEDGER
    // Daily Entry is the human-readable ledger of completed actual WorkoutSessions
    // used by Engine 3.0. Deleting here deletes only the selected ACTUAL session.
    //
    // Existing EF relationships cascade WorkoutSession -> DetailedWorkoutExercises
    // -> DetailedWorkoutSets. Client WorkoutTemplates and System Workout definitions
    // are separate records and are intentionally left untouched.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteWorkoutActual(
        Guid workoutSessionId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
        {
            return Unauthorized(new
            {
                success = false,
                message = "Active client/program required."
            });
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
        {
            return NotFound(new
            {
                success = false,
                message = "Workout was not found."
            });
        }

        if (session.WorkoutDate != date)
        {
            return BadRequest(new
            {
                success = false,
                message = "Workout date does not match the Daily Entry date."
            });
        }

        if (!session.CompletedFlag)
        {
            return BadRequest(new
            {
                success = false,
                message = "Only completed actual workouts can be deleted from Daily Entry."
            });
        }

        // The WorkoutSession is the authoritative completed actual. Removing it
        // immediately removes this workout from subsequent Engine 3.0 input reads.
        // Do NOT remove WorkoutTemplate or System Workout definitions.
        db.WorkoutSessions.Remove(session);

        await db.SaveChangesAsync(cancellationToken);

                TempData["StatusMessage"] =
            "Workout deleted. Daily Entry now reflects the remaining Engine 3.0 actual workouts.";

        return RedirectToAction(
            nameof(Index),
            new
            {
                date = date.ToString("yyyy-MM-dd")
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

        // v1014B: Daily Tracking reads the same canonical completed
        // WorkoutSession rows used by Dashboard and Engine 3.0. Multiple real
        // sessions on the same day are all preserved.
        var sessions =
            await workoutActuals.GetCompletedSessionsAsync(
                client.ClientId,
                program.ProgramId,
                date,
                cancellationToken);

        var model = new DailyEntryViewModel
        {
            Date = date,
            DetailedWorkoutEntryEnabled =
                program.DetailedWorkoutEntryEnabled,
            NutritionFoodLogEnabled =
                program.NutritionFoodLogEnabled,

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
                SourceRecordId = x.SourceRecordId,
                WorkoutTemplateId = x.WorkoutTemplateId,
                SystemWorkoutCode =
                    CardioActivityCatalog.IsCollapsedCardioEngineCode(
                        x.WorkoutTypeCode)
                        ? CardioActivityCatalog.PresentationSystemCode
                        : x.SystemWorkoutCode,
                WorkoutName = x.WorkoutName,
                WorkoutTypeCode =
                    Engine30WorkoutUiCatalogService.NormalizeCode(
                        x.WorkoutTypeCode),
                CardioActivityCode =
                    string.Equals(
                        x.SystemWorkoutCode,
                        CardioActivityCatalog.PresentationSystemCode,
                        StringComparison.OrdinalIgnoreCase)
                    || CardioActivityCatalog.IsCollapsedCardioEngineCode(
                        x.WorkoutTypeCode)
                        ? CardioActivityCatalog.InferActivityCode(
                            x.WorkoutTypeCode,
                            x.WorkoutName)
                        : null,
                DurationMin =
                    string.Equals(
                        x.WorkoutTypeCode,
                        "REST",
                        StringComparison.OrdinalIgnoreCase)
                        ? 0m
                        : EffectiveWorkoutDurationMinutes(x),
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

        var engineTypeCatalog =
            new Engine30WorkoutTypeCatalog();

        model.SystemWorkouts =
            SystemWorkoutRecipeCatalog
                .PresentationRecipes
                .Where(
                    recipe =>
                        workoutTypes.ContainsKey(
                            recipe.EngineWorkoutTypeCode))
                .Select(
                    recipe =>
                    {
                        var configured =
                            workoutTypes[
                                recipe.EngineWorkoutTypeCode];

                        var baseRpe = 5m;

                        if (engineTypeCatalog.TryGet(
                                recipe.EngineWorkoutTypeCode,
                                out var engineDefinition)
                            && engineDefinition is not null)
                        {
                            baseRpe =
                                engineDefinition.DefaultRpe;
                        }

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

        model.WorkoutTypeOptions = workoutTypeList
            .Where(x => x.IsSelectable)
            .Select(x =>
                new DailyEntryWorkoutTypeOption(
                    x.WorkoutTypeCode,
                    x.DisplayName))
            .ToList();

        model.CardioActivities =
            CardioActivityCatalog.All
                .Where(
                    x =>
                        workoutTypes.ContainsKey(
                            x.EngineWorkoutTypeCode))
                .Select(
                    x => new CardioActivityOptionViewModel
                    {
                        ActivityCode = x.ActivityCode,
                        DisplayName = x.DisplayName,
                        EngineWorkoutTypeCode =
                            x.EngineWorkoutTypeCode,
                        SearchTerms = x.SearchTerms
                    })
                .ToList();

        model.MyWorkouts =
            await db.WorkoutTemplates
                .AsNoTracking()
                .Where(
                    x =>
                        x.ClientId
                            == context.Client!.ClientId)
                .OrderBy(x => x.TemplateName)
                .Select(
                    x =>
                        new WorkoutTemplateOptionViewModel
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

        model.PlannedWorkoutTemplateId =
            planned?.WorkoutTemplateId;

        model.PlannedSystemWorkoutCode =
            planned?.SystemWorkoutCode;

        if (!model.PlannedWorkoutTemplateId.HasValue
            && string.IsNullOrWhiteSpace(
                model.PlannedSystemWorkoutCode)
            && !string.IsNullOrWhiteSpace(
                model.PlannedWorkoutTypeCode))
        {
            model.PlannedSystemWorkoutCode =
                SystemWorkoutRecipeCatalog
                    .GetDefaultPresentationForEngine(
                        model.PlannedWorkoutTypeCode)
                    ?.SystemCode;
        }

        WorkoutTemplateOptionViewModel? plannedTemplate = null;

        if (model.PlannedWorkoutTemplateId.HasValue)
        {
            plannedTemplate =
                model.MyWorkouts.SingleOrDefault(
                    x =>
                        x.WorkoutTemplateId
                            == model.PlannedWorkoutTemplateId.Value);
        }

        var plannedSystemWorkout =
            !string.IsNullOrWhiteSpace(
                model.PlannedSystemWorkoutCode)
                ? model.SystemWorkouts
                    .SingleOrDefault(
                        x => x.SystemCode.Equals(
                            model.PlannedSystemWorkoutCode,
                            StringComparison.OrdinalIgnoreCase))
                : null;

        if (plannedTemplate is not null)
        {
            model.PlannedWorkoutName =
                plannedTemplate.TemplateName;
        }
        else if (plannedSystemWorkout is not null)
        {
            model.PlannedWorkoutName =
                plannedSystemWorkout.DisplayName;
        }
        else if (planned is not null
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
            // Weekly Plan is presentation/prescription, not an actual workout.
            // Daily Entry is the canonical ACTUAL summary. Leave its manual
            // row blank until the user explicitly records training.
            model.Workouts.Add(
                new DailyEntryWorkoutInput
                {
                    SourceRecordId =
                        NewManualWorkoutSourceRecordId()
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

            var isRest =
                string.Equals(
                    workout.WorkoutTypeCode,
                    "REST",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    workout.SystemWorkoutCode,
                    "REST",
                    StringComparison.OrdinalIgnoreCase);

            var isCardioShell =
                string.Equals(
                    workout.SystemWorkoutCode,
                    CardioActivityCatalog.PresentationSystemCode,
                    StringComparison.OrdinalIgnoreCase);

            if (allowIncomplete
                && (string.IsNullOrWhiteSpace(
                        workout.WorkoutTypeCode)
                    || (!isRest
                        && (!workout.DurationMin.HasValue
                            || workout.DurationMin.Value <= 0))))
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

            if (isCardioShell
                && string.IsNullOrWhiteSpace(
                    workout.CardioActivityCode))
            {
                ModelState.AddModelError(
                    $"Workouts[{i}].CardioActivityCode",
                    "Choose the cardio activity.");
            }

            if (!isRest
                && (!workout.DurationMin.HasValue
                    || workout.DurationMin.Value <= 0))
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
    private static string NewManualWorkoutSourceRecordId()
        => $"daily:{Guid.NewGuid():N}";

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

    // B2.35.5.2 DAILY ENTRY DURATION FALLBACK
    private static decimal EffectiveWorkoutDurationMinutes(
        WorkoutSession session)
    {
        if (session.DurationMin > 0m)
            return session.DurationMin;

        if (session.CompletedFlag
            && string.Equals(
                session.EntrySourceCode,
                RecompInputSourceCodes.DetailedWorkout,
                StringComparison.OrdinalIgnoreCase)
            && session.CreatedAtUtc != default
            && session.UpdatedAtUtc > session.CreatedAtUtc)
        {
            var elapsed =
                (decimal)(
                    session.UpdatedAtUtc
                    - session.CreatedAtUtc)
                    .TotalMinutes;

            if (elapsed > 0m)
            {
                return Math.Max(
                    1m,
                    Math.Round(
                        elapsed,
                        1,
                        MidpointRounding.AwayFromZero));
            }
        }

        return session.DurationMin;
    }
}