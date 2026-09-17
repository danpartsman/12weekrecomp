using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

public sealed class IntakeController(
    RecompDbContext db,
    AppUserContextService userContext,
    ConfigurationService configuration,
    WeeklyPlanService weeklyPlans) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(HttpContext, cancellationToken);

        if (context.Client is not null && context.Program is not null)
            return RedirectToAction("Index", "Dashboard");

        return View(new IntakeViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        IntakeViewModel model,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(HttpContext, cancellationToken);

        if (!ModelState.IsValid)
            return View(model);

        if (context.Client is not null)
        {
            ModelState.AddModelError(
                "",
                "This account already has a client profile.");

            return View(model);
        }

        var configVersion =
            await configuration.GetActiveVersionAsync(cancellationToken);

        var selectedPlanCode = GoalToPlan(model.GoalCode);

        var client = new Client
        {
            ClientId = Guid.NewGuid(),
            ClientName = model.ClientName.Trim(),
            DateOfBirth = model.DateOfBirth,
            SexCode = model.SexCode,
            HeightIn = model.HeightIn,
            TimeZone = model.TimeZone,
            UnitSystemCode = "US",
            TrainingExperienceCode = model.TrainingExperienceCode,
            ClientStatusCode = "ACTIVE",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        // The initial body measurement belongs to the day the
        // customer actually enters it, not necessarily the future
        // program start date.


        var intakeDate = AppUserContextService.GetClientToday(client);

        var program = new ProgramRecord
        {
            ProgramId = Guid.NewGuid(),
            ClientId = client.ClientId,
            ConfigVersionId = configVersion.ConfigVersionId,
            SelectedPlanCode = selectedPlanCode,
            GoalCode = model.GoalCode,
            StartDate = model.StartDate,
            DurationWeeks = 12,
            AvailableTrainingDaysWeek =
                model.AvailableTrainingDaysWeek,
            ProgramStatusCode = "ACTIVE",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var measurement = new Measurement
        {
            MeasurementId = Guid.NewGuid(),
            ClientId = client.ClientId,
            ProgramId = program.ProgramId,
            MeasurementAtUtc = DateTime.UtcNow,

            // v1005 fix:
            // baseline measurement is available immediately.
            LocalMeasurementDate = intakeDate,

            WeightLb = model.StartingWeightLb,

            BodyFatPct =
                model.StartingBodyFatPercent.HasValue
                    ? model.StartingBodyFatPercent.Value / 100m
                    : null,

            MuscleMassLb = model.StartingMuscleMassLb,
            BodyWaterLb = model.StartingBodyWaterLb,
            VisceralFatRating = model.StartingVisceralFatRating,
            WaistIn = model.StartingWaistIn,
            HipsIn = model.StartingHipsIn,
            MeasurementSourceCode = "INTAKE",
            SourceRecordId = $"PROGRAM:{program.ProgramId:D}:BASELINE",
            CreatedAtUtc = DateTime.UtcNow
        };

        var goal = new ProgramGoal
        {
            ProgramGoalId = Guid.NewGuid(),
            ProgramId = program.ProgramId,
            TargetWeightLowLb = model.TargetWeightLowLb,
            TargetWeightHighLb = model.TargetWeightHighLb,
            TargetWaistIn = model.TargetWaistIn,
            GoalNotes = model.GoalNotes
        };

        var access = new UserClientAccess
        {
            UserClientAccessId = Guid.NewGuid(),
            UserId = context.User.UserId,
            ClientId = client.ClientId,
            AccessRoleCode = "OWNER",
            GrantedAtUtc = DateTime.UtcNow
        };

        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    cancellationToken);

            // Client must exist before records that reference it.
            db.Clients.Add(client);
            await db.SaveChangesAsync(cancellationToken);

            // These depend on the client.
            db.UserClientAccess.Add(access);
            db.Programs.Add(program);
            await db.SaveChangesAsync(cancellationToken);

            // These depend on the program.
            db.ProgramGoals.Add(goal);
            db.Measurements.Add(measurement);
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        });

        var firstWeekStart =
            WeeklyStrategyService.WeekStart(program.StartDate);

        await weeklyPlans.SeedRecommendedWeekIfBlankAsync(
            client,
            program,
            firstWeekStart,
            cancellationToken);

        return RedirectToAction(
            "Index",
            "Schedule",
            new
            {
                weekStart =
                    firstWeekStart.ToString("yyyy-MM-dd")
            });
    }

    [HttpGet]
    public async Task<IActionResult> Review(
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(HttpContext, cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction(nameof(Index));

        var model =
            await BuildReviewModelAsync(
                context,
                cancellationToken);

        return View("Index", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(
        IntakeViewModel model,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(HttpContext, cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction(nameof(Index));

        model.ReviewMode = true;
        model.OriginalGoalCode = context.Program.GoalCode;

        var selectedPlanCode = GoalToPlan(model.GoalCode);

        var planChanged = !string.Equals(
            selectedPlanCode,
            context.Program.SelectedPlanCode,
            StringComparison.OrdinalIgnoreCase);

        if (planChanged && !model.ConfirmPlanChange)
        {
            ModelState.AddModelError(
                nameof(model.ConfirmPlanChange),
                "Confirm the Primary Plan change before saving. " +
                "It changes Recomp recommendations from today forward.");
        }

        if (!ModelState.IsValid)
            return View("Index", model);

        var client = context.Client;
        var program = context.Program;

        var today =
            AppUserContextService.GetClientToday(client);

        var now = DateTime.UtcNow;

        client.ClientName = model.ClientName.Trim();
        client.DateOfBirth = model.DateOfBirth;

        client.SexCode =
            string.IsNullOrWhiteSpace(model.SexCode)
                ? null
                : model.SexCode.Trim().ToUpperInvariant();

        client.HeightIn = model.HeightIn;

        client.TimeZone =
            string.IsNullOrWhiteSpace(model.TimeZone)
                ? client.TimeZone
                : model.TimeZone.Trim();

        client.TrainingExperienceCode =
            string.IsNullOrWhiteSpace(
                model.TrainingExperienceCode)
                ? null
                : model.TrainingExperienceCode
                    .Trim()
                    .ToUpperInvariant();

        client.UpdatedAtUtc = now;

        var oldPlanCode = program.SelectedPlanCode;
        var oldGoalCode = program.GoalCode;
        var oldAvailableTrainingDays =
            program.AvailableTrainingDaysWeek;

        program.GoalCode =
            model.GoalCode.Trim().ToUpperInvariant();

        program.SelectedPlanCode = selectedPlanCode;

        program.AvailableTrainingDaysWeek =
            model.AvailableTrainingDaysWeek;

        program.UpdatedAtUtc = now;

        var goal = await db.ProgramGoals
            .SingleOrDefaultAsync(
                x => x.ProgramId == program.ProgramId,
                cancellationToken);

        if (goal is null)
        {
            goal = new ProgramGoal
            {
                ProgramGoalId = Guid.NewGuid(),
                ProgramId = program.ProgramId
            };

            db.ProgramGoals.Add(goal);
        }

        goal.TargetWeightLowLb = model.TargetWeightLowLb;
        goal.TargetWeightHighLb = model.TargetWeightHighLb;
        goal.TargetWaistIn = model.TargetWaistIn;
        goal.GoalNotes = model.GoalNotes;

        if (planChanged)
        {
            db.ProgramPlanChanges.Add(
                new ProgramPlanChange
                {
                    ProgramPlanChangeId = Guid.NewGuid(),
                    ProgramId = program.ProgramId,
                    ChangedByUserId = context.User.UserId,
                    OldPlanCode = oldPlanCode,
                    NewPlanCode = selectedPlanCode,
                    OldGoalCode = oldGoalCode,
                    NewGoalCode = program.GoalCode,
                    EffectiveDate = today,
                    ChangeSourceCode =
                        "USER_SETUP_REVIEW",
                    ChangedAtUtc = now
                });
        }

        await db.SaveChangesAsync(cancellationToken);

        var scheduleShapeChanged =
            planChanged ||
            oldAvailableTrainingDays !=
            model.AvailableTrainingDaysWeek;

        TempData["StatusMessage"] =
            planChanged
                ? "Setup updated. Your new Primary Plan takes effect today. " +
                  "Review the recommended weekly schedule before deciding whether to apply it."
                : scheduleShapeChanged
                    ? "Setup updated. Your training availability changed, " +
                      "so Recomp prepared an updated recommended week for you to review."
                    : "Setup updated.";

        if (scheduleShapeChanged)
        {
            return RedirectToAction(
                "Index",
                "Schedule",
                new
                {
                    weekStart =
                        WeeklyStrategyService
                            .WeekStart(today)
                            .ToString("yyyy-MM-dd"),

                    recommendPlanChange = true
                });
        }

        return RedirectToAction("Index", "Profile");
    }

    private async Task<IntakeViewModel> BuildReviewModelAsync(
        AppClientContext context,
        CancellationToken cancellationToken)
    {
        var client = context.Client!;
        var program = context.Program!;

        var baseline = await db.Measurements
            .Where(x =>
                x.ProgramId == program.ProgramId &&
                !x.IsDeleted)
            .OrderBy(x => x.LocalMeasurementDate)
            .ThenBy(x => x.MeasurementAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (baseline?.WeightLb is null)
        {
            throw new InvalidOperationException(
                "A starting-weight measurement is required.");
        }

        var goal = await db.ProgramGoals
            .SingleOrDefaultAsync(
                x => x.ProgramId == program.ProgramId,
                cancellationToken);

        return new IntakeViewModel
        {
            ReviewMode = true,
            OriginalGoalCode = program.GoalCode,
            ClientName = client.ClientName,
            DateOfBirth = client.DateOfBirth,
            SexCode = client.SexCode,
            HeightIn = client.HeightIn,
            TimeZone = client.TimeZone,

            TrainingExperienceCode =
                client.TrainingExperienceCode
                ?? "INTERMEDIATE",

            StartingWeightLb = baseline.WeightLb.Value,

            StartingBodyFatPercent =
                baseline.BodyFatPct * 100m,

            StartingMuscleMassLb =
                baseline.MuscleMassLb,

            StartingBodyWaterLb =
                baseline.BodyWaterLb,

            StartingWaistIn =
                baseline.WaistIn,

            StartingHipsIn =
                baseline.HipsIn,

            StartingVisceralFatRating =
                baseline.VisceralFatRating,

            GoalCode = program.GoalCode,

            AvailableTrainingDaysWeek =
                program.AvailableTrainingDaysWeek,

            TargetWeightLowLb =
                goal?.TargetWeightLowLb,

            TargetWeightHighLb =
                goal?.TargetWeightHighLb,

            TargetWaistIn =
                goal?.TargetWaistIn,

            GoalNotes =
                goal?.GoalNotes,

            StartDate =
                program.StartDate
        };
    }

    private static string GoalToPlan(string goalCode)
        => goalCode.ToUpperInvariant() switch
        {
            "HEALTH" or "HEALTH_RESET" => "PLAN_1",
            "FAT_LOSS" or "FAT_LOSS_RECOMP" => "PLAN_2",
            "MUSCLE" or "MUSCLE_BUILD" => "PLAN_3",
            "PAIN_LIMITED" or "MOBILITY" => "PLAN_5",
            _ => "PLAN_4"
        };
}


