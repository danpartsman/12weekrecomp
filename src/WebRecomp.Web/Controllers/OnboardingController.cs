using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Learning;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

public sealed class OnboardingController(
    RecompDbContext db,
    AppUserContextService userContext,
    AccessEntitlementService entitlements,
    ConfigurationService configuration,
    WeeklyPlanService weeklyPlans) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);

        if (entitlements.IsEntitlementRequired)
        {
            var hasAccess = await entitlements.HasApplicationAccessAsync(
                context.User.UserId,
                cancellationToken);

            if (!hasAccess)
                return RedirectToAction("Index", "Access");
        }

        var model = await BuildModelAsync(context, cancellationToken);

        if (await IsReadyAsync(context, cancellationToken))
            return RedirectToAction("Index", "Dashboard");

        ViewData["OnboardingPage"] = true;
        return View("Setup", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        OnboardingSetupViewModel model,
        CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);

        if (entitlements.IsEntitlementRequired)
        {
            var hasAccess = await entitlements.HasApplicationAccessAsync(
                context.User.UserId,
                cancellationToken);

            if (!hasAccess)
                return RedirectToAction("Index", "Access");
        }

        Normalize(model);

        if (!CoachCatalog.IsValid(model.PreferredCoachCode))
        {
            ModelState.AddModelError(
                nameof(model.PreferredCoachCode),
                "Choose one of the available coaches.");
        }

        if (!ModelState.IsValid)
        {
            ViewData["OnboardingPage"] = true;
            return View("Setup", model);
        }

        var now = DateTime.UtcNow;
        var entryDate = GetEntryDate(now, model.TimeZone);
        // With only a year, use the month and day of the local setup date.
        // Optional exact month/day are honored when both are supplied.
        var birthYear = model.BirthYear!.Value;
        var birthMonth = model.BirthMonth ?? entryDate.Month;
        var birthDay = model.BirthDay ?? Math.Min(entryDate.Day, DateTime.DaysInMonth(birthYear, birthMonth));
        var birthDate = new DateOnly(birthYear, birthMonth, birthDay);
        if (birthDate > entryDate.AddYears(-18))
        {
            ModelState.AddModelError(nameof(model.BirthYear), "You must be at least 18 to start this program.");
            ViewData["OnboardingPage"] = true;
            return View("Setup", model);
        }

        var configVersion = await configuration.GetActiveVersionAsync(cancellationToken);
        var selectedPlanCode = GoalToPlan(model.GoalCode);
        var startingTemplate = weeklyPlans.BuildRecommendedTemplate(
            selectedPlanCode, model.AvailableTrainingDaysWeek, model.TrainingExperienceCode);
        var workoutDays = startingTemplate.Count(code => code != "REST");
        if (model.PreferredTrainingDays.Length > workoutDays
            || model.PreferredRestDays.Length > 7 - workoutDays)
        {
            ModelState.AddModelError(nameof(model.PreferredTrainingDays),
                $"This goal starts with {workoutDays} training days and {7 - workoutDays} rest days. Adjust the preferences to fit.");
            ViewData["OnboardingPage"] = true;
            return View("Setup", model);
        }

        Client? client = context.Client;
        ProgramRecord? program = context.Program;
        DateOnly planWeekStart = default;

        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            if (client is null)
            {
                client = new Client
                {
                    ClientId = Guid.NewGuid(),
                    ClientName = model.ClientName.Trim(),
                    DateOfBirth = birthDate,
                    SexCode = model.SexCode,
                    HeightIn = model.HeightIn!.Value,
                    TimeZone = model.TimeZone,
                    UnitSystemCode = "US",
                    TrainingExperienceCode = model.TrainingExperienceCode,
                    LifestyleActivityCode = model.LifestyleActivityCode,
                    BaselineStepsDay = model.BaselineStepsDay,
                    BaselineSleepHours = model.BaselineSleepHours,
                    ClientStatusCode = "ACTIVE",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                db.Clients.Add(client);
                await db.SaveChangesAsync(cancellationToken);

                db.UserClientAccess.Add(new UserClientAccess
                {
                    UserClientAccessId = Guid.NewGuid(),
                    UserId = context.User.UserId,
                    ClientId = client.ClientId,
                    AccessRoleCode = "OWNER",
                    GrantedAtUtc = now
                });
            }
            else
            {
                client.ClientName = model.ClientName.Trim();
                client.DateOfBirth = birthDate;
                client.SexCode = model.SexCode;
                client.HeightIn = model.HeightIn!.Value;
                client.TimeZone = model.TimeZone;
                client.TrainingExperienceCode = model.TrainingExperienceCode;
                client.LifestyleActivityCode = model.LifestyleActivityCode;
                client.BaselineStepsDay = model.BaselineStepsDay;
                client.BaselineSleepHours = model.BaselineSleepHours;
                client.ClientStatusCode = "ACTIVE";
                client.UpdatedAtUtc = now;
            }

            context.User.PreferredCoachCode =
                model.PreferredCoachCode;

            context.User.UpdatedAtUtc =
                now;

            await db.SaveChangesAsync(cancellationToken);

            var today = AppUserContextService.GetClientToday(client);

            if (program is null)
            {
                program = new ProgramRecord
                {
                    ProgramId = Guid.NewGuid(),
                    ClientId = client.ClientId,
                    ConfigVersionId = configVersion.ConfigVersionId,
                    SelectedPlanCode = selectedPlanCode,
                    GoalCode = model.GoalCode,
                    StartDate = today,
                    DurationWeeks = 12,
                    AvailableTrainingDaysWeek = model.AvailableTrainingDaysWeek,
                    DetailedWorkoutEntryEnabled = true,
                    NutritionFoodLogEnabled = true,
                    ProgramStatusCode = "ACTIVE",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                db.Programs.Add(program);
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                program.SelectedPlanCode = selectedPlanCode;
                program.GoalCode = model.GoalCode;
                program.AvailableTrainingDaysWeek = model.AvailableTrainingDaysWeek;
                program.ProgramStatusCode = "ACTIVE";
                program.UpdatedAtUtc = now;
            }

            var goal = await db.ProgramGoals.SingleOrDefaultAsync(
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

            // Always capture the weight the person just supplied as today's
            // onboarding measurement. This repairs interrupted/legacy profiles
            // without rewriting historical measurements.
            var measurement = await db.Measurements
                .Where(x =>
                    x.ClientId == client.ClientId
                    && x.ProgramId == program.ProgramId
                    && x.LocalMeasurementDate == today
                    && !x.IsDeleted)
                .OrderByDescending(x => x.MeasurementAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (measurement is null)
            {
                measurement = new Measurement
                {
                    MeasurementId = Guid.NewGuid(),
                    ClientId = client.ClientId,
                    ProgramId = program.ProgramId,
                    MeasurementAtUtc = now,
                    LocalMeasurementDate = today,
                    MeasurementSourceCode = "ONBOARDING",
                    SourceRecordId = $"PROGRAM:{program.ProgramId:D}:ONBOARDING:{today:yyyyMMdd}",
                    CreatedAtUtc = now
                };
                db.Measurements.Add(measurement);
            }
            else
            {
                measurement.MeasurementAtUtc = now;
                measurement.MeasurementSourceCode = "ONBOARDING";
            }

            measurement.WeightLb = model.StartingWeightLb!.Value;
            measurement.BodyFatPct = model.StartingBodyFatPercent.HasValue
                ? model.StartingBodyFatPercent.Value / 100m
                : measurement.BodyFatPct;
            measurement.MuscleMassLb = model.StartingMuscleMassLb ?? measurement.MuscleMassLb;
            measurement.BodyWaterLb = model.StartingBodyWaterLb ?? measurement.BodyWaterLb;
            measurement.WaistIn = model.StartingWaistIn ?? measurement.WaistIn;
            measurement.HipsIn = model.StartingHipsIn ?? measurement.HipsIn;
            measurement.VisceralFatRating = model.StartingVisceralFatRating ?? measurement.VisceralFatRating;

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            planWeekStart = PlanningWeek.Start(today);
        });

        await weeklyPlans.SeedRecommendedWeekIfBlankAsync(
            client!,
            program!,
            planWeekStart,
            cancellationToken,
            model.PreferredTrainingDays,
            model.PreferredRestDays);

        TempData["StatusMessage"] =
            "Setup complete. Recomp now has the minimum real data required to calculate your prescription. Review the starting week and make it fit your life.";

        return RedirectToAction(
            "Index",
            "Schedule",
            new { weekStart = planWeekStart.ToString("yyyy-MM-dd") });
    }

    private async Task<OnboardingSetupViewModel> BuildModelAsync(
        AppClientContext context,
        CancellationToken cancellationToken)
    {
        var fallbackName =
            context.User.PreferredDisplayName
            ?? context.User.DisplayName
            ?? context.User.EmailNormalized?.Split('@')[0]
            ?? string.Empty;

        if (context.Client is null)
        {
            return new OnboardingSetupViewModel
            {
                ClientName = fallbackName,
                TrainingExperienceCode = "INTERMEDIATE",
                AvailableTrainingDaysWeek = 4,
                PreferredCoachCode =
                    CoachCatalog.Resolve(
                        context.User.PreferredCoachCode).Code
            };
        }

        var client = context.Client;
        var today = AppUserContextService.GetClientToday(client);

        var latestMeasurement = await db.Measurements
            .AsNoTracking()
            .Where(x =>
                x.ClientId == client.ClientId
                && !x.IsDeleted
                && x.LocalMeasurementDate <= today)
            .OrderByDescending(x => x.LocalMeasurementDate)
            .ThenByDescending(x => x.MeasurementAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return new OnboardingSetupViewModel
        {
            ClientName = string.IsNullOrWhiteSpace(client.ClientName) ? fallbackName : client.ClientName,
            BirthYear = client.DateOfBirth?.Year,
            BirthMonth = client.DateOfBirth?.Month,
            BirthDay = client.DateOfBirth?.Day,
            SexCode = client.SexCode ?? string.Empty,
            HeightIn = client.HeightIn > 0 ? client.HeightIn : null,
            StartingWeightLb = latestMeasurement?.WeightLb,
            GoalCode = context.Program?.GoalCode == "FAT_LOSS" ? "FAT_LOSS_RECOMP" : context.Program?.GoalCode ?? string.Empty,
            TrainingExperienceCode = client.TrainingExperienceCode ?? "INTERMEDIATE",
            AvailableTrainingDaysWeek = context.Program?.AvailableTrainingDaysWeek is > 0
                ? context.Program.AvailableTrainingDaysWeek
                : (byte)4,
            TimeZone = string.IsNullOrWhiteSpace(client.TimeZone)
                ? "America/Los_Angeles"
                : client.TimeZone,
            StartingBodyFatPercent = latestMeasurement?.BodyFatPct * 100m,
            StartingMuscleMassLb = latestMeasurement?.MuscleMassLb,
            StartingBodyWaterLb = latestMeasurement?.BodyWaterLb,
            StartingWaistIn = latestMeasurement?.WaistIn,
            StartingHipsIn = latestMeasurement?.HipsIn,
            StartingVisceralFatRating = latestMeasurement?.VisceralFatRating,
            LifestyleActivityCode = client.LifestyleActivityCode,
            BaselineStepsDay = client.BaselineStepsDay,
            BaselineSleepHours = client.BaselineSleepHours,
            PreferredCoachCode =
                CoachCatalog.Resolve(
                    context.User.PreferredCoachCode).Code
        };
    }

    private async Task<bool> IsReadyAsync(
        AppClientContext context,
        CancellationToken cancellationToken)
    {
        if (context.Client is null || context.Program is null)
            return false;

        var client = context.Client;
        var program = context.Program;

        if (client.DateOfBirth is null
            || string.IsNullOrWhiteSpace(client.SexCode)
            || client.HeightIn < 48m
            || client.HeightIn > 90m
            || string.IsNullOrWhiteSpace(program.GoalCode)
            || program.AvailableTrainingDaysWeek is < 1 or > 7)
            return false;

        var today = AppUserContextService.GetClientToday(client);

        return await db.Measurements.AsNoTracking().AnyAsync(
            x =>
                x.ClientId == client.ClientId
                && x.ProgramId == program.ProgramId
                && !x.IsDeleted
                && x.WeightLb.HasValue
                && x.WeightLb.Value > 0m
                && x.LocalMeasurementDate <= today,
            cancellationToken);
    }

    private static void Normalize(OnboardingSetupViewModel model)
    {
        model.ClientName = model.ClientName?.Trim() ?? string.Empty;
        model.SexCode = model.SexCode?.Trim().ToUpperInvariant() ?? string.Empty;
        model.GoalCode = model.GoalCode?.Trim().ToUpperInvariant() ?? string.Empty;
        model.TrainingExperienceCode = model.TrainingExperienceCode?.Trim().ToUpperInvariant() ?? "INTERMEDIATE";
        model.TimeZone = string.IsNullOrWhiteSpace(model.TimeZone)
            ? "America/Los_Angeles"
            : model.TimeZone.Trim();
        model.PreferredCoachCode =
            CoachCatalog.Resolve(
                model.PreferredCoachCode).Code;

        model.LifestyleActivityCode = string.IsNullOrWhiteSpace(model.LifestyleActivityCode)
            ? null
            : model.LifestyleActivityCode.Trim().ToUpperInvariant();
    }

    private static DateOnly GetEntryDate(DateTime utcNow, string zoneId)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone));
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(utcNow);
        }
        catch (InvalidTimeZoneException)
        {
            return DateOnly.FromDateTime(utcNow);
        }
    }

    private static string GoalToPlan(string goalCode)
        => goalCode.ToUpperInvariant() switch
        {
            "HEALTH" or "HEALTH_RESET" => "PLAN_1",
            "FAT_LOSS" or "FAT_LOSS_RECOMP" => "PLAN_2",
            "MUSCLE" or "MUSCLE_BUILD" => "PLAN_3",
            "ATHLETIC_RECOMP" => "PLAN_4",
            "PAIN_LIMITED" or "MOBILITY" => "PLAN_5",
            _ => "PLAN_4"
        };
}
