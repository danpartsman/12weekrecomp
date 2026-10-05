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

public sealed class ScheduleController(
    RecompDbContext db,
    AppUserContextService userContext,
    WeeklyStrategyService strategy,
    ProgramPlanService planSelection,
    WeeklyPlanService weeklyPlans,
    Engine30WorkoutUiCatalogService engine30WorkoutCatalog,
    Engine30DashboardProjectionService engine30Projection) : Controller
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

        var engineTypeCatalog =
            new Engine30WorkoutTypeCatalog();

        var systemWorkouts =
            SystemWorkoutRecipeCatalog
                .PlanningRecipes
                .Where(
                    recipe =>
                        workoutMap.ContainsKey(
                            recipe.EngineWorkoutTypeCode))
                .Select(
                    recipe =>
                    {
                        var configured =
                            workoutMap[
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
                            SystemCode =
                                recipe.SystemCode,
                            EngineWorkoutTypeCode =
                                recipe.EngineWorkoutTypeCode,
                            DisplayName =
                                recipe.DisplayName,
                            GroupName =
                                recipe.GroupName,
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

        var systemWorkoutMap =
            systemWorkouts.ToDictionary(
                x => x.SystemCode,
                StringComparer.OrdinalIgnoreCase);

        var myWorkouts =
            await db.WorkoutTemplates
                .AsNoTracking()
                .Where(
                    x =>
                        x.ClientId
                            == context.Client.ClientId)
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

        var myWorkoutMap =
            myWorkouts.ToDictionary(
                x => x.WorkoutTemplateId);

        var end = start.AddDays(6);

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

        var hasUsableWeight =
            await db.Measurements
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ClientId == context.Client.ClientId
                        && !x.IsDeleted
                        && x.WeightLb != null,
                    cancellationToken);

        IReadOnlyDictionary<DateOnly, Engine30DashboardDayProjection> engine30Targets =
            new Dictionary<DateOnly, Engine30DashboardDayProjection>();

        if (hasUsableWeight)
        {
            engine30Targets =
                await engine30Projection.BuildPlanningWeekAsync(
                    context,
                    start,
                    planCodeForWeek,
                    cancellationToken);
        }

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
                .ToList(),
            SystemWorkouts = systemWorkouts,
            MyWorkouts = myWorkouts
        };

        for (var i = 0; i < 7; i++)
        {
            var date = start.AddDays(i);
            saved.TryGetValue(date, out var planned);

            var normalizedWorkoutTypeCode =
                Engine30WorkoutUiCatalogService.NormalizeCode(
                    planned?.WorkoutTypeCode);

            WorkoutTemplateOptionViewModel? plannedTemplate = null;

            if (planned?.WorkoutTemplateId is Guid plannedTemplateId)
            {
                myWorkoutMap.TryGetValue(
                    plannedTemplateId,
                    out plannedTemplate);
            }

            var plannedSystemCode =
                plannedTemplate is null
                    ? planned?.SystemWorkoutCode
                    : null;

            if (plannedTemplate is null
                && string.IsNullOrWhiteSpace(plannedSystemCode)
                && !string.IsNullOrWhiteSpace(normalizedWorkoutTypeCode))
            {
                plannedSystemCode =
                    SystemWorkoutRecipeCatalog
                        .GetDefaultPresentationForEngine(
                            normalizedWorkoutTypeCode)
                        ?.SystemCode;
            }

            plannedSystemCode =
                SystemWorkoutRecipeCatalog
                    .NormalizePlanningSystemCode(
                        plannedSystemCode);

            var plannedSystem =
                !string.IsNullOrWhiteSpace(
                    plannedSystemCode)
                && systemWorkoutMap.TryGetValue(
                    plannedSystemCode,
                    out var systemOption)
                    ? systemOption
                    : null;

            var day = new PlannedDayViewModel
            {
                Date = date,
                DayName = date.DayOfWeek.ToString(),
                WorkoutTypeCode =
                    string.IsNullOrWhiteSpace(normalizedWorkoutTypeCode)
                        ? null
                        : normalizedWorkoutTypeCode,
                WorkoutTemplateId =
                    plannedTemplate?.WorkoutTemplateId,
                SystemWorkoutCode =
                    plannedSystemCode,
                PlannedDurationMin =
                    planned?.PlannedDurationMin
                    ?? plannedSystem?.DefaultMinutes
                    ?? plannedTemplate?.DefaultDurationMin,
                PlannedRpe =
                    planned?.PlannedRpe
                    ?? plannedSystem?.DefaultRpe
            };

            if (planned is not null
                && !string.IsNullOrWhiteSpace(normalizedWorkoutTypeCode)
                && workoutMap.TryGetValue(normalizedWorkoutTypeCode, out var type))
            {

                day.WorkoutDisplayName =
                    plannedTemplate?.TemplateName
                    ?? plannedSystem?.DisplayName
                    ?? type.DisplayName;
                day.WeeklyRole = type.WeeklyRoleCode;
                day.Region = type.RegionCode;

            }

            if (engine30Targets.TryGetValue(
                    date,
                    out var engine30Day))
            {
                day.Calories =
                    engine30Day.PrescribedEnergyKcal;
                day.ProteinG =
                    engine30Day.ProteinG;
                day.BaseCarbsG =
                    engine30Day.RecommendedCarbsG;
                day.FatG =
                    engine30Day.FatG;
                day.ScaledGlycogenBurnG =
                    engine30Day.GlycogenDemandG;
                day.TimingNote =
                    "Engine 3.0 prescription";
            }

            vm.Days.Add(day);
        }

        // Always expose the recommended seven-day mix to the view so the
        // at-a-glance cards can compare the program recommendation with what
        // the user has actually planned. When a plan change is pending, the
        // recommendation reflects the newly selected plan.
        var recommendationPlanCode =
            recommendPlanChange
                ? context.Program.SelectedPlanCode
                : planCodeForWeek;

        var recommendedTemplate =
            weeklyPlans.BuildRecommendedTemplate(
                recommendationPlanCode,
                context.Program.AvailableTrainingDaysWeek,
                context.Client.TrainingExperienceCode);

        for (var i = 0; i < 7; i++)
        {
            var code = recommendedTemplate[i];
            vm.RecommendedDays.Add(new RecommendedDayViewModel
            {
                Date = start.AddDays(i),
                DayName = start.AddDays(i).DayOfWeek.ToString(),
                WorkoutTypeCode = code,
                WorkoutDisplayName =
                    SystemWorkoutRecipeCatalog
                        .GetDefaultPresentationForEngine(code)
                        ?.DisplayName
                    ?? (workoutMap.TryGetValue(code, out var definition)
                        ? definition.DisplayName
                        : code.Replace('_', ' '))
            });
        }

        if (recommendPlanChange)
        {
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

        var activeWorkoutTypes =
            await engine30WorkoutCatalog
                .GetActiveListAsync(cancellationToken);

        var valid =
            activeWorkoutTypes
                .Select(x => x.WorkoutTypeCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var activeWorkoutMap =
            activeWorkoutTypes.ToDictionary(
                x => x.WorkoutTypeCode,
                StringComparer.OrdinalIgnoreCase);

        var engineTypeCatalog =
            new Engine30WorkoutTypeCatalog();

        var templateIds =
            model.Days
                .Where(x => x.WorkoutTemplateId.HasValue)
                .Select(x => x.WorkoutTemplateId!.Value)
                .Distinct()
                .ToArray();

        var templates =
            templateIds.Length == 0
                ? new Dictionary<Guid, WorkoutTemplateRecord>()
                : await db.WorkoutTemplates
                    .AsNoTracking()
                    .Where(
                        x =>
                            x.ClientId
                                == context.Client.ClientId
                            && templateIds.Contains(
                                x.WorkoutTemplateId))
                    .ToDictionaryAsync(
                        x => x.WorkoutTemplateId,
                        cancellationToken);

        var dates = model.Days.Select(x => x.Date).ToList();

        var existing = await db.PlannedWorkouts
            .Where(x => x.ProgramId == context.Program.ProgramId && dates.Contains(x.PlannedDate))
            .ToListAsync(cancellationToken);

        foreach (var day in model.Days)
        {
            var old =
                existing.SingleOrDefault(
                    x => x.PlannedDate == day.Date);

            WorkoutTemplateRecord? selectedTemplate = null;
            SystemWorkoutRecipe? selectedSystemWorkout = null;
            decimal? plannedDurationMin = null;
            decimal? plannedRpe = null;

            if (!string.IsNullOrWhiteSpace(
                    day.SystemWorkoutCode))
            {
                if (!SystemWorkoutRecipeCatalog.TryGet(
                        day.SystemWorkoutCode,
                        out selectedSystemWorkout)
                    || selectedSystemWorkout is null)
                {
                    ModelState.AddModelError(
                        "",
                        $"System workout is not available for {day.Date:yyyy-MM-dd}.");

                    continue;
                }

                day.WorkoutTypeCode =
                    selectedSystemWorkout
                        .EngineWorkoutTypeCode;

                if (!activeWorkoutMap.TryGetValue(
                        day.WorkoutTypeCode,
                        out var configuredSystemType))
                {
                    ModelState.AddModelError(
                        "",
                        $"System workout Engine type is unavailable for {day.Date:yyyy-MM-dd}.");
                    continue;
                }

                var systemBaseRpe = 5m;
                if (engineTypeCatalog.TryGet(
                        day.WorkoutTypeCode,
                        out var engineDefinition)
                    && engineDefinition is not null)
                {
                    systemBaseRpe =
                        engineDefinition.DefaultRpe;
                }

                plannedDurationMin =
                    SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(
                        selectedSystemWorkout,
                        configuredSystemType.DefaultMinutes);

                plannedRpe =
                    SystemWorkoutRecipeCatalog.ResolveDefaultRpe(
                        selectedSystemWorkout,
                        systemBaseRpe);

                day.WorkoutTemplateId = null;
            }
            else if (day.WorkoutTemplateId.HasValue)
            {
                if (!templates.TryGetValue(
                        day.WorkoutTemplateId.Value,
                        out selectedTemplate))
                {
                    ModelState.AddModelError(
                        "",
                        $"Saved workout is not available for {day.Date:yyyy-MM-dd}.");

                    continue;
                }

                day.WorkoutTypeCode =
                    selectedTemplate.WorkoutTypeCode;
                plannedDurationMin =
                    selectedTemplate.DefaultDurationMin;
            }
            else
            {
                day.WorkoutTypeCode =
                    Engine30WorkoutUiCatalogService.NormalizeCode(
                        day.WorkoutTypeCode);
            }

            if (string.IsNullOrWhiteSpace(day.WorkoutTypeCode))
            {
                if (old is not null)
                    db.PlannedWorkouts.Remove(old);
                continue;
            }

            if (!valid.Contains(day.WorkoutTypeCode))
            {
                ModelState.AddModelError(
                    "",
                    $"Unknown workout type for {day.Date:yyyy-MM-dd}.");

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
                    WorkoutTemplateId =
                        selectedTemplate?.WorkoutTemplateId,
                    SystemWorkoutCode =
                        selectedSystemWorkout?.SystemCode,
                    PlannedDurationMin =
                        plannedDurationMin,
                    PlannedRpe =
                        plannedRpe,
                    SourceCode = "CLIENT",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                old.WorkoutTypeCode = day.WorkoutTypeCode;
                old.WorkoutTemplateId =
                    selectedTemplate?.WorkoutTemplateId;
                old.SystemWorkoutCode =
                    selectedSystemWorkout?.SystemCode;
                old.PlannedDurationMin =
                    plannedDurationMin;
                old.PlannedRpe =
                    plannedRpe;
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
