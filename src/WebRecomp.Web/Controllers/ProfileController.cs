using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Learning;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

public sealed class ProfileController(
    RecompDbContext db,
    AppUserContextService userContext,
    LearningOrchestratorService learning) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var client = context.Client;
        var program = context.Program;
        var goal = await db.ProgramGoals
            .FirstOrDefaultAsync(x => x.ProgramId == program.ProgramId, cancellationToken);

        var latest = await db.Measurements
            .Where(x => x.ClientId == client.ClientId && !x.IsDeleted)
            .OrderByDescending(x => x.LocalMeasurementDate)
            .ThenByDescending(x => x.MeasurementAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var planChanges = await db.ProgramPlanChanges
            .AsNoTracking()
            .Where(x => x.ProgramId == program.ProgramId)
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.ChangedAtUtc)
            .Take(8)
            .ToListAsync(cancellationToken);

        var today = AppUserContextService.GetClientToday(client);
        var week = Math.Max(1, ((today.DayNumber - program.StartDate.DayNumber) / 7) + 1);

        return View(new ProfileViewModel
        {
            ClientName = client.ClientName,
            PreferredDisplayName = context.User.PreferredDisplayName ?? context.User.DisplayName,
            ContactEmail = context.User.ContactEmail ?? context.User.EmailNormalized,
            SignInEmail = context.User.EmailNormalized,
            DateOfBirth = client.DateOfBirth,
            SexCode = client.SexCode,
            HeightIn = client.HeightIn,
            TimeZone = client.TimeZone,
            UnitSystemCode = client.UnitSystemCode,
            TrainingExperienceCode = client.TrainingExperienceCode,
            GoalCode = program.GoalCode,
            SelectedPlanCode = program.SelectedPlanCode,
            SelectedPlanDisplayName = WeeklyPlanService.FriendlyPlanLabel(program.SelectedPlanCode),
            StartDate = program.StartDate,
            DurationWeeks = program.DurationWeeks,
            ProgramWeek = week,
            AvailableTrainingDaysWeek = program.AvailableTrainingDaysWeek,
            TargetWeightLowLb = goal?.TargetWeightLowLb,
            TargetWeightHighLb = goal?.TargetWeightHighLb,
            TargetWaistIn = goal?.TargetWaistIn,
            GoalNotes = goal?.GoalNotes,
            LatestMeasurementDate = latest?.LocalMeasurementDate,
            LatestWeightLb = latest?.WeightLb,
            LatestBodyFatPercent = latest?.BodyFatPct * 100m,
            LatestMuscleMassLb = latest?.MuscleMassLb,
            LatestWaistIn = latest?.WaistIn,
            PlanChanges = planChanges.Select(x => new PlanChangeRowViewModel
            {
                EffectiveDate = x.EffectiveDate,
                OldPlanDisplayName = WeeklyPlanService.FriendlyPlanLabel(x.OldPlanCode),
                NewPlanDisplayName = WeeklyPlanService.FriendlyPlanLabel(x.NewPlanCode),
                ChangedAtUtc = x.ChangedAtUtc
            }).ToList()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        return View(new ProfileEditViewModel
        {
            ClientName = context.Client.ClientName,
            DateOfBirth = context.Client.DateOfBirth,
            SexCode = context.Client.SexCode,
            HeightIn = context.Client.HeightIn,
            TimeZone = context.Client.TimeZone,
            UnitSystemCode = context.Client.UnitSystemCode,
            TrainingExperienceCode = context.Client.TrainingExperienceCode
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProfileEditViewModel model, CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!ModelState.IsValid)
            return View(model);

        context.Client.ClientName = model.ClientName.Trim();
        context.Client.DateOfBirth = model.DateOfBirth;
        context.Client.SexCode = string.IsNullOrWhiteSpace(model.SexCode) ? null : model.SexCode.Trim().ToUpperInvariant();
        context.Client.HeightIn = model.HeightIn;
        context.Client.TimeZone = model.TimeZone.Trim();
        context.Client.UnitSystemCode = model.UnitSystemCode.Trim().ToUpperInvariant();
        context.Client.TrainingExperienceCode = string.IsNullOrWhiteSpace(model.TrainingExperienceCode)
            ? null
            : model.TrainingExperienceCode.Trim().ToUpperInvariant();
        context.Client.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Profile updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);

        return View(new SettingsViewModel
        {
            SignInEmail = context.User.EmailNormalized,
            PreferredDisplayName = context.User.PreferredDisplayName ?? context.User.DisplayName,
            ContactEmail = context.User.ContactEmail ?? context.User.EmailNormalized,
            ThemeCode = string.IsNullOrWhiteSpace(context.User.ThemeCode)
                ? "SYSTEM"
                : context.User.ThemeCode.ToUpperInvariant(),
            PreferredCoachCode =
                await learning.GetPreferredCoachCodeAsync(
                    context.User.UserId,
                    cancellationToken),
            HasActiveProgram = context.Program is not null,
            DetailedWorkoutEntryEnabled =
                context.Program?.DetailedWorkoutEntryEnabled ?? false,
            NutritionFoodLogEnabled =
                context.Program?.NutritionFoodLogEnabled ?? false
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(SettingsViewModel model, CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);

        model.ThemeCode = (model.ThemeCode ?? "SYSTEM").Trim().ToUpperInvariant();
        if (!new[] { "SYSTEM", "LIGHT", "DARK" }.Contains(model.ThemeCode))
            ModelState.AddModelError(nameof(model.ThemeCode), "Choose System, Light, or Dark.");

        if (!CoachCatalog.IsValid(model.PreferredCoachCode))
        {
            ModelState.AddModelError(
                nameof(model.PreferredCoachCode),
                "Choose one of the available coaches.");
        }

        model.PreferredCoachCode =
            CoachCatalog.Resolve(
                model.PreferredCoachCode).Code;

        if (!ModelState.IsValid)
        {
            model.SignInEmail = context.User.EmailNormalized;
            model.HasActiveProgram = context.Program is not null;
            return View(model);
        }

        context.User.PreferredDisplayName = string.IsNullOrWhiteSpace(model.PreferredDisplayName)
            ? null
            : model.PreferredDisplayName.Trim();
        context.User.ContactEmail = string.IsNullOrWhiteSpace(model.ContactEmail)
            ? null
            : model.ContactEmail.Trim();
        context.User.ThemeCode = model.ThemeCode;
        context.User.UpdatedAtUtc = DateTime.UtcNow;

        if (context.Program is not null)
        {
            context.Program.DetailedWorkoutEntryEnabled =
                model.DetailedWorkoutEntryEnabled;
            context.Program.NutritionFoodLogEnabled =
                model.NutritionFoodLogEnabled;
            context.Program.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        await learning.SetPreferredCoachAsync(
            context.User.UserId,
            model.PreferredCoachCode,
            cancellationToken);

        TempData["StatusMessage"] = "Settings saved.";
        return RedirectToAction(nameof(Settings));
    }
}
