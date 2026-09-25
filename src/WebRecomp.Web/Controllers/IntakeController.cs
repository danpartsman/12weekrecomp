using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

public sealed class IntakeController(
    RecompDbContext db,
    AppUserContextService userContext) : Controller
{
    // The original full intake page is now a legacy route. New and interrupted
    // accounts must use the compact Onboarding flow so they cannot create a
    // partial profile and then reach calculation surfaces with missing data.
    [HttpGet]
    public IActionResult Index()
        => RedirectToAction("Index", "Onboarding");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Index(
        IntakeViewModel model,
        CancellationToken cancellationToken)
        => RedirectToAction("Index", "Onboarding");

    [HttpGet]
    public async Task<IActionResult> Review(
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(HttpContext, cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Onboarding");

        var model =
            await BuildReviewModelAsync(
                context,
                cancellationToken);

        // Older/interrupted accounts can legitimately have a Client/Program
        // but no usable baseline weight. Do not throw an application error;
        // send them through Quick Start so the missing physiology is repaired.
        if (model is null)
        {
            TempData["StatusMessage"] =
                "Finish the short setup first. Recomp needs a current weight and the other required starting details before setup can be reviewed.";

            return RedirectToAction("Index", "Onboarding");
        }

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
            return RedirectToAction("Index", "Onboarding");

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

    private async Task<IntakeViewModel?> BuildReviewModelAsync(
        AppClientContext context,
        CancellationToken cancellationToken)
    {
        var client = context.Client!;
        var program = context.Program!;

        var baseline = await db.Measurements
            .Where(x =>
                x.ProgramId == program.ProgramId &&
                !x.IsDeleted &&
                x.WeightLb.HasValue &&
                x.WeightLb.Value > 0m)
            .OrderBy(x => x.LocalMeasurementDate)
            .ThenBy(x => x.MeasurementAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (baseline is null)
            return null;

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

            StartingWeightLb = baseline.WeightLb!.Value,

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
