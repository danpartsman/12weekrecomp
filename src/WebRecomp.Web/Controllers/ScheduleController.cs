using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services.WorkoutTargeting;
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
    WeeklyPlanningAssessmentService planningAssessment,
    ProgramPlanService planSelection,
    WeeklyPlanService weeklyPlans,
    Engine30WorkoutUiCatalogService engine30WorkoutCatalog,
    Engine30DashboardProjectionService engine30Projection) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        DateOnly? weekStart,
        bool recommendPlanChange = false,
        string? previewGoalCode = null,
        CancellationToken cancellationToken = default)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var today = AppUserContextService.GetClientToday(context.Client);
        var start = weekStart ?? PlanningWeek.Start(today);
        start = PlanningWeek.Start(start);

        var firstProgramWeek = PlanningWeek.Start(context.Program.StartDate);
        var currentWeek = PlanningWeek.Start(today);

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

        var completed = await db.WorkoutSessions.AsNoTracking()
            .Where(x => x.ClientId == context.Client.ClientId && x.ProgramId == context.Program.ProgramId
                && x.CompletedFlag && x.WorkoutDate >= start && x.WorkoutDate <= end)
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        var actualByDate = completed.GroupBy(x => x.WorkoutDate).ToDictionary(x => x.Key, x => x.ToList());
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

        var currentGoalCode =
            NormalizeGoalCode(
                context.Program.GoalCode,
                context.Program.SelectedPlanCode);

        var canChangeGoal =
            start >= currentWeek;

        var requestedPreviewGoalCode =
            canChangeGoal
                ? NormalizeGoalCode(
                    previewGoalCode,
                    context.Program.SelectedPlanCode)
                : currentGoalCode;

        var isGoalPreview =
            canChangeGoal
            && !string.IsNullOrWhiteSpace(previewGoalCode)
            && !string.Equals(
                requestedPreviewGoalCode,
                currentGoalCode,
                StringComparison.OrdinalIgnoreCase);

        var previewPlanCode =
            GoalToPlan(requestedPreviewGoalCode);

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
            CurrentGoalCode = currentGoalCode,
            CurrentGoalDisplayName = GoalDisplayName(currentGoalCode),
            PreviewGoalCode = requestedPreviewGoalCode,
            PreviewGoalDisplayName = GoalDisplayName(requestedPreviewGoalCode),
            PreviewGoalDescription = GoalDescription(requestedPreviewGoalCode),
            IsGoalPreview = isGoalPreview,
            CanChangeGoal = canChangeGoal,
            GoalOptions = GoalOptions(requestedPreviewGoalCode),
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

            actualByDate.TryGetValue(date, out var actuals);
            var day = new PlannedDayViewModel
            {
                CanEditPlan = WorkoutPlanPolicy.CanEdit(date, today, actuals?.Count > 0),
                CoachAdjusted = planned?.SourceCode == "COACH",
                HasActualWorkout = actuals?.Count > 0,
                ActualWorkoutSummary = actuals is null ? null : string.Join(" + ", actuals.Select(x => x.WorkoutName ?? x.WorkoutTypeCode)),
                ActualWorkoutTypeCode = actuals?.OrderByDescending(x => x.SessionRpe).ThenByDescending(x => x.DurationMin).FirstOrDefault()?.WorkoutTypeCode,
                ActualDurationMin = actuals?.Sum(x => x.DurationMin) ?? 0m,
                ActualWorkoutIds = actuals?.Select(x => x.WorkoutSessionId).ToList() ?? [],
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
                    (planned?.SourceCode == "WORKOUT_TARGET" ? planned.Notes : null)
                    ?? plannedTemplate?.TemplateName
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
                    day.HasActualWorkout ? "Nutrition recalculated from completed workouts" : date < today ? "Planned estimate — actual workout not recorded" : "Nutrition estimate from planned workout";
            }

            vm.Days.Add(day);
        }

        // Always expose the recommended seven-day mix to the view so the
        // at-a-glance cards can compare the program recommendation with what
        // the user has actually planned. When a plan change is pending, the
        // recommendation reflects the newly selected plan.
        var recommendationPlanCode =
            isGoalPreview
                ? previewPlanCode
                : recommendPlanChange
                    ? context.Program.SelectedPlanCode
                    : planCodeForWeek;

        IReadOnlyDictionary<DateOnly, Engine30DashboardDayProjection> goalPreviewTargets =
            engine30Targets;

        if (hasUsableWeight
            && !string.Equals(
                recommendationPlanCode,
                planCodeForWeek,
                StringComparison.OrdinalIgnoreCase))
        {
            goalPreviewTargets =
                await engine30Projection.BuildPlanningWeekAsync(
                    context,
                    start,
                    recommendationPlanCode,
                    cancellationToken);
        }

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

        var recommendedMajorCount =
            recommendedTemplate.Count(
                code => code is
                    "MAJOR_LEGS"
                    or "MAJOR_PUSH"
                    or "MAJOR_PULL"
                    or "FULL_BODY_STRENGTH");

        var recommendedAccessoryCount =
            recommendedTemplate.Count(
                code => code is
                    "ACCESSORY"
                    or "RESISTANCE_CIRCUIT");

        var resistanceExposureCount =
            recommendedMajorCount
            + recommendedAccessoryCount;

        vm.PreviewMuscleStimulus =
            resistanceExposureCount >= 4
                ? "HIGH"
                : resistanceExposureCount >= 2
                    ? "MODERATE"
                    : "LOW";

        vm.PreviewMuscleDirection =
            BuildMuscleDirection(
                recommendationPlanCode,
                resistanceExposureCount);

        if (goalPreviewTargets.Count > 0)
        {
            var weeklyMaintenance =
                goalPreviewTargets.Values.Sum(
                    x => x.MaintenanceEnergyKcal);

            var weeklyPrescribed =
                goalPreviewTargets.Values.Sum(
                    x => x.PrescribedEnergyKcal);

            var weeklyDeficit =
                weeklyMaintenance
                - weeklyPrescribed;

            vm.PreviewWeeklyDeficitKcal =
                Math.Round(
                    weeklyDeficit,
                    0);

            vm.PreviewFatLossEnergyEquivalentLb =
                Math.Round(
                    weeklyDeficit / 3500m,
                    2);

            vm.PreviewFatDirection =
                weeklyDeficit > 350m
                    ? "FAT LOSS FAVORED"
                    : weeklyDeficit < -350m
                        ? "FAT GAIN POSSIBLE"
                        : "NEAR MAINTENANCE";
        }

        if (recommendPlanChange)
        {
            vm.PlanCode = context.Program.SelectedPlanCode;
            vm.PlanDisplayName = WeeklyPlanService.FriendlyPlanLabel(context.Program.SelectedPlanCode);
            vm.PlanGuidance = WeeklyPlanService.FriendlyPlanGuidance(context.Program.SelectedPlanCode);
        }

        var assessment = await planningAssessment.AssessWeekAsync(
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

        var today = AppUserContextService.GetClientToday(context.Client);
        var normalizedStart = PlanningWeek.Start(model.WeekStart);
        if (model.Days.Any(x => x.Date < normalizedStart || x.Date > normalizedStart.AddDays(6))
            || model.Days.Select(x => x.Date).Distinct().Count() != model.Days.Count)
            return BadRequest("Choose each date once within the displayed week.");
        var completedDates = await db.WorkoutSessions.AsNoTracking()
            .Where(x => x.ClientId == context.Client.ClientId && x.ProgramId == context.Program.ProgramId && x.CompletedFlag
                && x.WorkoutDate >= normalizedStart && x.WorkoutDate <= normalizedStart.AddDays(6))
            .Select(x => x.WorkoutDate).Distinct().ToListAsync(cancellationToken);

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
            if (!WorkoutPlanPolicy.CanEdit(day.Date, today, completedDates.Contains(day.Date))) continue;
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

        var sourceStart = PlanningWeek.Start(weekStart);
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
    public async Task<IActionResult> ApplyGoal(
        string goalCode,
        DateOnly weekStart,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var normalizedGoal =
            NormalizeGoalCode(
                goalCode,
                context.Program.SelectedPlanCode);

        var newPlanCode =
            GoalToPlan(normalizedGoal);

        var currentGoal =
            NormalizeGoalCode(
                context.Program.GoalCode,
                context.Program.SelectedPlanCode);

        var today =
            AppUserContextService.GetClientToday(
                context.Client);

        var currentWeek =
            PlanningWeek.Start(today);

        var requestedWeek =
            PlanningWeek.Start(weekStart);

        var returnWeek =
            requestedWeek < currentWeek
                ? currentWeek
                : requestedWeek;

        if (string.Equals(
                normalizedGoal,
                currentGoal,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                newPlanCode,
                context.Program.SelectedPlanCode,
                StringComparison.OrdinalIgnoreCase))
        {
            TempData["ScheduleStatus"] =
                "That is already your current Primary Goal.";

            return RedirectToAction(
                "Index",
                new
                {
                    weekStart =
                        returnWeek.ToString("yyyy-MM-dd")
                });
        }

        var oldPlanCode =
            context.Program.SelectedPlanCode;

        var oldGoalCode =
            context.Program.GoalCode;

        var now =
            DateTime.UtcNow;

        context.Program.SelectedPlanCode =
            newPlanCode;

        context.Program.GoalCode =
            normalizedGoal;

        context.Program.UpdatedAtUtc =
            now;

        db.ProgramPlanChanges.Add(
            new ProgramPlanChange
            {
                ProgramPlanChangeId = Guid.NewGuid(),
                ProgramId = context.Program.ProgramId,
                ChangedByUserId = context.User.UserId,
                OldPlanCode = oldPlanCode,
                NewPlanCode = newPlanCode,
                OldGoalCode = oldGoalCode,
                NewGoalCode = normalizedGoal,
                EffectiveDate = today,
                ChangeSourceCode =
                    "WEEKLY_PLAN_GOAL_SELECTOR",
                ChangedAtUtc = now
            });

        await db.SaveChangesAsync(
            cancellationToken);

        TempData["ScheduleStatus"] =
            $"Primary Goal changed to {GoalDisplayName(normalizedGoal)}. " +
            "Your completed history and existing planned workouts were not overwritten. " +
            "Review the new recommendation below and apply it only if you want to change the remaining schedule.";

        return RedirectToAction(
            "Index",
            new
            {
                weekStart =
                    returnWeek.ToString("yyyy-MM-dd"),
                recommendPlanChange = true
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyRecommendedWeek(DateOnly weekStart, CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var today = AppUserContextService.GetClientToday(context.Client);
        var start = PlanningWeek.Start(weekStart);
        var effectiveDate = start == PlanningWeek.Start(today)
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

    private static string NormalizeGoalCode(
        string? goalCode,
        string? fallbackPlanCode)
    {
        var normalized =
            (goalCode ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

        return normalized switch
        {
            "MUSCLE" or "MUSCLE_BUILD" =>
                "MUSCLE_BUILD",

            "FAT_LOSS" or "FAT_LOSS_RECOMP" =>
                "FAT_LOSS_RECOMP",

            "ATHLETIC" or "ATHLETIC_RECOMP" =>
                "ATHLETIC_RECOMP",

            "HEALTH" or "HEALTH_RESET" =>
                "HEALTH_RESET",

            "PAIN_LIMITED" or "MOBILITY" =>
                "PAIN_LIMITED",

            _ => (fallbackPlanCode ?? string.Empty)
                .Trim()
                .ToUpperInvariant() switch
                {
                    "PLAN_1" => "HEALTH_RESET",
                    "PLAN_2" => "FAT_LOSS_RECOMP",
                    "PLAN_3" => "MUSCLE_BUILD",
                    "PLAN_5" => "PAIN_LIMITED",
                    _ => "ATHLETIC_RECOMP"
                }
        };
    }

    private static string GoalToPlan(
        string goalCode)
        => NormalizeGoalCode(goalCode, null) switch
        {
            "HEALTH_RESET" => "PLAN_1",
            "FAT_LOSS_RECOMP" => "PLAN_2",
            "MUSCLE_BUILD" => "PLAN_3",
            "PAIN_LIMITED" => "PLAN_5",
            _ => "PLAN_4"
        };

    private static string GoalDisplayName(
        string goalCode)
        => NormalizeGoalCode(goalCode, null) switch
        {
            "MUSCLE_BUILD" =>
                "RECOMP — Muscle Focus / Fat Loss Secondary",

            "FAT_LOSS_RECOMP" =>
                "RECOMP — Fat Loss Focus / Muscle Secondary",

            "ATHLETIC_RECOMP" =>
                "RECOMP — Athletic Reconditioning",

            "HEALTH_RESET" =>
                "SPECIALIZED — Health Reset",

            "PAIN_LIMITED" =>
                "SPECIALIZED — Pain-Limited / Mobility",

            _ =>
                "RECOMP — Athletic Reconditioning"
        };

    private static string GoalDescription(
        string goalCode)
        => NormalizeGoalCode(goalCode, null) switch
        {
            "MUSCLE_BUILD" =>
                "Prioritizes three major strength anchors plus accessory volume when recovery and schedule allow. Best for users who want muscle gain or strong lean-mass preservation while still reducing body fat gradually.",

            "FAT_LOSS_RECOMP" =>
                "Keeps resistance training non-negotiable while using a larger controlled energy deficit and more lower-demand conditioning or recovery. Best when fat loss is the primary objective without sacrificing lean mass.",

            "ATHLETIC_RECOMP" =>
                "Combines three major strength anchors with a fourth demanding conditioning exposure when training availability supports it, plus easier aerobic or accessory work and a true recovery day.",

            "HEALTH_RESET" =>
                "A lower-stress reconditioning plan for beginners, deconditioned users, older adults returning to structured exercise, people with higher body weight or joint-load concerns, or anyone prioritizing consistency and health over aggressive training. Age or body weight alone does not require this plan.",

            "PAIN_LIMITED" =>
                "A conservative mobility and reconditioning plan for users whose pain, movement limitations, or current tolerance make normal training volume inappropriate. It emphasizes tolerable resistance work, easy aerobic activity, mobility, and recovery.",

            _ =>
                "A balanced athletic recomp approach that keeps resistance training central while improving conditioning and work capacity."
        };

    private static string BuildMuscleDirection(
        string planCode,
        int resistanceExposureCount)
    {
        if (resistanceExposureCount <= 1)
            return "PRESERVATION IS THE PRIMARY REALISTIC TARGET";

        return planCode.Trim().ToUpperInvariant() switch
        {
            "PLAN_3" =>
                resistanceExposureCount >= 4
                    ? "MUSCLE GAIN / PRESERVATION FAVORED"
                    : "PRESERVATION FAVORED; GAIN POSSIBLE",

            "PLAN_2" =>
                "LEAN-MASS PRESERVATION FAVORED; GAIN POSSIBLE",

            "PLAN_4" =>
                "MAINTAIN / GAIN LEAN MASS WITH PERFORMANCE EMPHASIS",

            "PLAN_1" =>
                "GRADUAL LEAN-MASS GAIN / PRESERVATION FAVORED",

            "PLAN_5" =>
                "LEAN-MASS PRESERVATION / REBUILDING AS TOLERATED",

            _ =>
                "LEAN-MASS PRESERVATION FAVORED"
        };
    }

    private static List<SelectListItem> GoalOptions(
        string selectedGoalCode)
    {
        var selected =
            NormalizeGoalCode(
                selectedGoalCode,
                null);

        return
        [
            new SelectListItem(
                "RECOMP — Muscle Focus / Fat Loss Secondary",
                "MUSCLE_BUILD",
                selected == "MUSCLE_BUILD"),

            new SelectListItem(
                "RECOMP — Fat Loss Focus / Muscle Secondary",
                "FAT_LOSS_RECOMP",
                selected == "FAT_LOSS_RECOMP"),

            new SelectListItem(
                "RECOMP — Athletic Reconditioning",
                "ATHLETIC_RECOMP",
                selected == "ATHLETIC_RECOMP"),

            new SelectListItem(
                "SPECIALIZED — Health Reset",
                "HEALTH_RESET",
                selected == "HEALTH_RESET"),

            new SelectListItem(
                "SPECIALIZED — Pain-Limited / Mobility",
                "PAIN_LIMITED",
                selected == "PAIN_LIMITED")
        ];
    }
}
