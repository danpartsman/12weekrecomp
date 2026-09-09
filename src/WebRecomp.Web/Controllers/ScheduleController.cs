using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

public sealed class ScheduleController(
    RecompDbContext db,
    AppUserContextService userContext,
    ConfigurationService configuration,
    WorkoutMathService math,
    WeeklyStrategyService strategy,
    ProgramPlanService planSelection,
    WeeklyPlanService weeklyPlans,
    Engine30WorkoutUiCatalogService engine30WorkoutCatalog) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        DateOnly? weekStart,
        bool recommendPlanChange = false,
        CancellationToken cancellationToken = default)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var today = AppUserContextService.GetClientToday(context.Client);
        var start = weekStart ?? WeeklyStrategyService.WeekStart(today);
        start = WeeklyStrategyService.WeekStart(start);

        var firstProgramWeek = WeeklyStrategyService.WeekStart(context.Program.StartDate);
        var currentWeek = WeeklyStrategyService.WeekStart(today);

        if (start == firstProgramWeek)
        {
            await weeklyPlans.SeedRecommendedWeekIfBlankAsync(
                context.Client, context.Program, start, cancellationToken);
        }
        else if (start == currentWeek)
        {
            await weeklyPlans.EnsureCurrentWeekAsync(
                context.Client, context.Program, today, cancellationToken);
        }

        var workoutTypes =
            await engine30WorkoutCatalog.GetActiveListAsync(cancellationToken);

        var workoutMap =
            workoutTypes.ToDictionary(
                x => x.WorkoutTypeCode,
                StringComparer.OrdinalIgnoreCase);

        // Temporary shadow-phase bridge used only for the legacy target panel.
        // Engine 3.0 runtime calculation uses the active Admin workout profile.
        var legacyWorkoutMap =
            await configuration.GetWorkoutTypesAsync(
                context.Program.ConfigVersionId,
                cancellationToken);

        var settings =
            await configuration.GetSettingsAsync(
                context.Program.ConfigVersionId,
                cancellationToken);
        var end = start.AddDays(6);

        // Use the latest known weight on or before each planned date.
        // A later weigh-in must not rewrite the displayed macro targets for an earlier date.
        var weightHistory = await db.Measurements
            .Where(x => x.ClientId == context.Client.ClientId
                        && !x.IsDeleted
                        && x.WeightLb != null
                        && x.LocalMeasurementDate <= end)
            .OrderBy(x => x.LocalMeasurementDate)
            .ThenBy(x => x.MeasurementAtUtc)
            .ToListAsync(cancellationToken);

        var savedRows = await db.PlannedWorkouts
            .Where(x => x.ProgramId == context.Program.ProgramId
                        && x.PlannedDate >= start
                        && x.PlannedDate <= end)
            .OrderBy(x => x.PlannedDate)
            .ToListAsync(cancellationToken);

        var saved = savedRows.ToDictionary(x => x.PlannedDate);
        var nextStart = start.AddDays(7);
        var nextEnd = nextStart.AddDays(6);
        var nextWeekHasPlan = await db.PlannedWorkouts.AnyAsync(
            x => x.ProgramId == context.Program.ProgramId
                 && x.PlannedDate >= nextStart
                 && x.PlannedDate <= nextEnd,
            cancellationToken);

        var planReferenceDate = start == currentWeek ? today : start;
        var planCodeForWeek = await planSelection.GetPlanCodeForDateAsync(
            context.Program, planReferenceDate, cancellationToken);

        var vm = new WeekPlanViewModel
        {
            WeekStart = start,
            PlanCode = planCodeForWeek,
            PlanDisplayName = WeeklyPlanService.FriendlyPlanLabel(planCodeForWeek),
            PlanGuidance = WeeklyPlanService.FriendlyPlanGuidance(planCodeForWeek),
            SeededFromPlan = savedRows.Count > 0 && savedRows.All(x => x.SourceCode == "PLAN_DEFAULT"),
            AutoRolled = savedRows.Count > 0 && savedRows.All(x => x.SourceCode == "AUTO_ROLLOVER"),
            NextWeekHasPlan = nextWeekHasPlan,
            ShowRecommendedPlanPrompt = recommendPlanChange,
            WorkoutTypes = workoutTypes
                .Where(x => x.IsSelectable)
                .Select(x => new SelectListItem(x.DisplayName, x.WorkoutTypeCode))
                .ToList()
        };

        for (var i = 0; i < 7; i++)
        {
            var date = start.AddDays(i);
            saved.TryGetValue(date, out var planned);

            var normalizedWorkoutTypeCode =
                Engine30WorkoutUiCatalogService.NormalizeCode(
                    planned?.WorkoutTypeCode);

            var day = new PlannedDayViewModel
            {
                Date = date,
                DayName = date.DayOfWeek.ToString(),
                WorkoutTypeCode =
                    string.IsNullOrWhiteSpace(normalizedWorkoutTypeCode)
                        ? null
                        : normalizedWorkoutTypeCode
            };

            if (planned is not null
                && !string.IsNullOrWhiteSpace(normalizedWorkoutTypeCode)
                && workoutMap.TryGetValue(normalizedWorkoutTypeCode, out var type))
            {
                day.WorkoutDisplayName = type.DisplayName;
                day.WeeklyRole = type.WeeklyRoleCode;
                day.Region = type.RegionCode;

                var weightForDate = weightHistory
                    .Where(x => x.LocalMeasurementDate <= date && x.WeightLb.HasValue)
                    .OrderByDescending(x => x.LocalMeasurementDate)
                    .ThenByDescending(x => x.MeasurementAtUtc)
                    .Select(x => x.WeightLb)
                    .FirstOrDefault();

                if (weightForDate.HasValue)
                {
                    var legacyCode =
                        Engine30WorkoutUiCatalogService
                            .ToLegacyCompatibilityCode(
                                normalizedWorkoutTypeCode);

                    if (legacyWorkoutMap.TryGetValue(
                            legacyCode,
                            out var legacyWorkoutType))
                    {
                        var planCodeForDate =
                            await planSelection.GetPlanCodeForDateAsync(
                                context.Program,
                                date,
                                cancellationToken);

                        var planForDate =
                            await configuration.GetPlanAsync(
                                context.Program.ConfigVersionId,
                                planCodeForDate,
                                cancellationToken);

                        var targets =
                            math.CalculatePlannedTargets(
                                weightForDate.Value,
                                planForDate,
                                legacyWorkoutType,
                                settings);

                        day.Calories = targets.Calories;
                        day.ProteinG = targets.ProteinG;
                        day.BaseCarbsG = targets.CarbsG;
                        day.FatG = targets.FatG;
                        day.ScaledGlycogenBurnG =
                            targets.ScaledGlycogenBurnG;
                        day.TimingNote = targets.TimingNote;
                    }
                }
            }

            vm.Days.Add(day);
        }

        if (recommendPlanChange)
        {
            var template = weeklyPlans.BuildRecommendedTemplate(
                context.Program.SelectedPlanCode,
                context.Program.AvailableTrainingDaysWeek,
                context.Client.TrainingExperienceCode);

            for (var i = 0; i < 7; i++)
            {
                var code = template[i];
                vm.RecommendedDays.Add(new RecommendedDayViewModel
                {
                    Date = start.AddDays(i),
                    DayName = start.AddDays(i).DayOfWeek.ToString(),
                    WorkoutTypeCode = code,
                    WorkoutDisplayName = workoutMap.TryGetValue(code, out var definition)
                        ? definition.DisplayName
                        : code.Replace('_', ' ')
                });
            }

            vm.PlanCode = context.Program.SelectedPlanCode;
            vm.PlanDisplayName = WeeklyPlanService.FriendlyPlanLabel(context.Program.SelectedPlanCode);
            vm.PlanGuidance = WeeklyPlanService.FriendlyPlanGuidance(context.Program.SelectedPlanCode);
        }

        var assessment = await strategy.AssessWeekAsync(
            context.Program.ProgramId,
            context.Program.ConfigVersionId,
            start,
            planCodeForWeek,
            context.Program.AvailableTrainingDaysWeek,
            cancellationToken);

        vm.WeeklyStatus = assessment.StatusCode;
        vm.WeeklyNote = assessment.Note;

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(WeekPlanPostModel model, CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var valid =
            await engine30WorkoutCatalog
                .GetActiveCodeSetAsync(cancellationToken);
        var dates = model.Days.Select(x => x.Date).ToList();

        var existing = await db.PlannedWorkouts
            .Where(x => x.ProgramId == context.Program.ProgramId && dates.Contains(x.PlannedDate))
            .ToListAsync(cancellationToken);

        foreach (var day in model.Days)
        {
            var old =
                existing.SingleOrDefault(
                    x => x.PlannedDate == day.Date);

            day.WorkoutTypeCode =
                Engine30WorkoutUiCatalogService.NormalizeCode(
                    day.WorkoutTypeCode);

            if (string.IsNullOrWhiteSpace(day.WorkoutTypeCode))
            {
                if (old is not null)
                    db.PlannedWorkouts.Remove(old);
                continue;
            }

            if (!valid.Contains(day.WorkoutTypeCode))
            {
                ModelState.AddModelError("", $"Unknown workout type for {day.Date:yyyy-MM-dd}.");
                continue;
            }

            if (old is null)
            {
                db.PlannedWorkouts.Add(new PlannedWorkout
                {
                    PlannedWorkoutId = Guid.NewGuid(),
                    ClientId = context.Client.ClientId,
                    ProgramId = context.Program.ProgramId,
                    PlannedDate = day.Date,
                    WorkoutTypeCode = day.WorkoutTypeCode,
                    SourceCode = "CLIENT",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                old.WorkoutTypeCode = day.WorkoutTypeCode;
                old.SourceCode = "CLIENT";
                old.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        if (!ModelState.IsValid)
        {
            if (IsAjaxRequest())
            {
                return BadRequest(new
                {
                    success = false,
                    message = ModelState.Values
                        .SelectMany(x => x.Errors)
                        .Select(x => x.ErrorMessage)
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                        ?? "The weekly plan could not be saved."
                });
            }

            return RedirectToAction("Index", new { weekStart = model.WeekStart.ToString("yyyy-MM-dd") });
        }

        await db.SaveChangesAsync(cancellationToken);

        if (IsAjaxRequest())
        {
            return Json(new
            {
                success = true,
                message = "Weekly plan saved automatically."
            });
        }

        TempData["ScheduleStatus"] = "Weekly plan saved. Recomp recalculated the plan from your updated workouts.";

        return RedirectToAction(
            "Index",
            new { weekStart = model.WeekStart.ToString("yyyy-MM-dd") });
    }

    private bool IsAjaxRequest()
        => string.Equals(
            Request.Headers["X-Requested-With"].ToString(),
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CopyToNextWeek(DateOnly weekStart, CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var sourceStart = WeeklyStrategyService.WeekStart(weekStart);
        var targetStart = sourceStart.AddDays(7);
        var result = await weeklyPlans.CopyWeekIfTargetBlankAsync(
            context.Client,
            context.Program,
            sourceStart,
            targetStart,
            "WEEK_COPY",
            cancellationToken);

        TempData["ScheduleStatus"] = result.Message;

        return RedirectToAction(
            "Index",
            new { weekStart = (result.Success ? targetStart : sourceStart).ToString("yyyy-MM-dd") });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyRecommendedWeek(DateOnly weekStart, CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var today = AppUserContextService.GetClientToday(context.Client);
        var start = WeeklyStrategyService.WeekStart(weekStart);
        var effectiveDate = start == WeeklyStrategyService.WeekStart(today)
            ? today
            : start;

        var changed = await weeklyPlans.ApplyRecommendedWeekFromDateAsync(
            context.Client,
            context.Program,
            start,
            effectiveDate,
            cancellationToken);

        TempData["ScheduleStatus"] = changed > 0
            ? "Recommended schedule applied from the effective date forward. Earlier days in this week were left unchanged."
            : "No future days in this week needed to be changed.";

        return RedirectToAction(
            "Index",
            new { weekStart = start.ToString("yyyy-MM-dd") });
    }
}
