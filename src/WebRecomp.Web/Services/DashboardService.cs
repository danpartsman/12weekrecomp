using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.ViewModels;
using WebRecomp.Web.Services.Engine;
using WebRecomp.Web.Services.Engine30;

namespace WebRecomp.Web.Services;

public sealed class DashboardService(
    RecompDbContext db,
    ProgramPlanService planSelection,
    Engine30DashboardProjectionService engine30Dashboard,
    Engine30DashboardAnalysisService dashboardAnalysis,
    Engine30WorkoutUiCatalogService engine30WorkoutCatalog,
    IRecompRecommendationEngine recommendationEngine)
{
    public async Task<DashboardViewModel> BuildAsync(
        AppClientContext context,
        DateOnly actionDate,
        CancellationToken cancellationToken = default)
    {
        if (context.Client is null || context.Program is null)
            throw new InvalidOperationException("Active client/program required.");

        var client = context.Client;
        var program = context.Program;

        // v1014A: current Dashboard recommendations are Engine 3.0 only.
        // The application consumes an engine-neutral recommendation contract;
        // no current Dashboard path invokes legacy Engine 2.x recommendation math.
        var workoutTypes =
            await engine30WorkoutCatalog.GetActiveMapAsync(
                cancellationToken);

        var actionPlanCode =
            await planSelection.GetPlanCodeForDateAsync(
                program,
                actionDate,
                cancellationToken);
        var engine30Projection =
            await engine30Dashboard.BuildTodayTomorrowAsync(
                context,
                actionDate,
                cancellationToken);
        var engine30Impact =
            await engine30Dashboard.BuildYesterdayImpactAsync(
                context,
                actionDate,
                engine30Projection,
                cancellationToken);
        var engine30ActualWorkout =
            await engine30Dashboard.BuildActualWorkoutAsync(
                context,
                actionDate,
                cancellationToken);
        var currentMeasurement = await db.Measurements
            .Where(x => x.ClientId == client.ClientId
                        && !x.IsDeleted
                        && x.LocalMeasurementDate <= actionDate
                        && x.WeightLb != null)
            .OrderByDescending(x => x.LocalMeasurementDate)
            .ThenByDescending(x => x.MeasurementAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var todayCheckin = await db.DailyCheckins
            .SingleOrDefaultAsync(x => x.ProgramId == program.ProgramId
                                       && x.CheckinDate == actionDate,
                                  cancellationToken);

        var todayPlan = await db.PlannedWorkouts
            .SingleOrDefaultAsync(x => x.ProgramId == program.ProgramId
                                       && x.PlannedDate == actionDate,
                                  cancellationToken);

        WorkoutTypeDefinition? todayPlannedType =
            ResolveWorkoutType(
                workoutTypes,
                engine30Projection.Today.WorkoutTypeCode);

        var actualSessions = await db.WorkoutSessions
            .Where(x => x.ClientId == client.ClientId
                        && x.ProgramId == program.ProgramId
                        && x.WorkoutDate == actionDate
                        && x.CompletedFlag)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var workoutActual = BuildActualWorkoutSummary(actualSessions, workoutTypes);

        var nutritionActual = await db.NutritionDays
            .SingleOrDefaultAsync(x => x.ClientId == client.ClientId
                                       && x.ProgramId == program.ProgramId
                                       && x.NutritionDate == actionDate,
                                  cancellationToken);

        var tomorrowDate = actionDate.AddDays(1);
        var tomorrowPlan = await db.PlannedWorkouts
            .SingleOrDefaultAsync(x => x.ProgramId == program.ProgramId
                                       && x.PlannedDate == tomorrowDate,
                                  cancellationToken);

        WorkoutTypeDefinition? tomorrowType =
            ResolveWorkoutType(
                workoutTypes,
                engine30Projection.Tomorrow.WorkoutTypeCode);

        // ------------------------------------------------------------
        // v1009 Dashboard analysis evidence.
        //
        // Yesterday's behavior is compared with an EXACT measurement
        // dated today. If today's scale is not entered, the Dashboard
        // asks for it rather than reusing an older weight as if it were
        // this morning's response.
        // ------------------------------------------------------------

        var analysisWindowStart =
            actionDate.AddDays(-4);

        var analysisWindowEnd =
            actionDate.AddDays(-1);

        var recentNutrition =
            await db.NutritionDays
                .AsNoTracking()
                .Where(x =>
                    x.ClientId == client.ClientId
                    && x.ProgramId == program.ProgramId
                    && x.NutritionDate >= analysisWindowStart
                    && x.NutritionDate <= analysisWindowEnd)
                .OrderBy(x => x.NutritionDate)
                .ToListAsync(cancellationToken);

        var recentPlans =
            await db.PlannedWorkouts
                .AsNoTracking()
                .Where(x =>
                    x.ProgramId == program.ProgramId
                    && x.PlannedDate >= analysisWindowStart
                    && x.PlannedDate <= analysisWindowEnd)
                .OrderBy(x => x.PlannedDate)
                .ToListAsync(cancellationToken);

        var recentSessions =
            await db.WorkoutSessions
                .AsNoTracking()
                .Where(x =>
                    x.ClientId == client.ClientId
                    && x.ProgramId == program.ProgramId
                    && x.WorkoutDate >= analysisWindowStart
                    && x.WorkoutDate <= analysisWindowEnd
                    && x.CompletedFlag)
                .OrderBy(x => x.WorkoutDate)
                .ThenBy(x => x.CreatedAtUtc)
                .ToListAsync(cancellationToken);

        var recentMeasurements =
            await db.Measurements
                .AsNoTracking()
                .Where(x =>
                    x.ClientId == client.ClientId
                    && !x.IsDeleted
                    && x.LocalMeasurementDate >= analysisWindowStart
                    && x.LocalMeasurementDate <= actionDate
                    && x.WeightLb != null)
                .OrderBy(x => x.LocalMeasurementDate)
                .ThenBy(x => x.MeasurementAtUtc)
                .ToListAsync(cancellationToken);

        var scaleByDate =
            recentMeasurements
                .GroupBy(x => x.LocalMeasurementDate)
                .Select(x =>
                    x.OrderByDescending(y => y.MeasurementAtUtc)
                        .First())
                .OrderBy(x => x.LocalMeasurementDate)
                .ToList();

        var exactTodayMeasurement =
            scaleByDate.FirstOrDefault(
                x => x.LocalMeasurementDate == actionDate);

        var priorMorningMeasurement =
            scaleByDate
                .Where(x => x.LocalMeasurementDate < actionDate)
                .OrderByDescending(x => x.LocalMeasurementDate)
                .FirstOrDefault();

        var firstTrendMeasurement =
            scaleByDate.FirstOrDefault();

        var lastTrendMeasurement =
            scaleByDate.LastOrDefault();

        var previousDateForAnalysis =
            actionDate.AddDays(-1);

        var previousPlanForAnalysis =
            recentPlans.FirstOrDefault(
                x => x.PlannedDate == previousDateForAnalysis);

        var previousSessionsForAnalysis =
            recentSessions
                .Where(x => x.WorkoutDate == previousDateForAnalysis)
                .ToList();

        var previousActualCodes =
            previousSessionsForAnalysis.Count == 0
                ? null
                : string.Join(
                    "|",
                    previousSessionsForAnalysis
                        .Select(x =>
                            Engine30DashboardProjectionService.NormalizeWorkoutCode(
                                x.WorkoutTypeCode)));

        var completeNutritionDays =
            recentNutrition.Count(x =>
                x.ActualCalories.HasValue
                && x.ActualProteinG.HasValue
                && x.ActualCarbsG.HasValue
                && x.ActualFatG.HasValue);

        var proteinValues =
            recentNutrition
                .Where(x => x.ActualProteinG.HasValue)
                .Select(x => x.ActualProteinG!.Value)
                .ToList();

        decimal? averageProtein =
            proteinValues.Count > 0
                ? proteinValues.Average()
                : null;

        var fatValues =
            recentNutrition
                .Where(x => x.ActualFatG.HasValue)
                .Select(x => x.ActualFatG!.Value)
                .ToList();

        decimal? averageFat =
            fatValues.Count > 0
                ? fatValues.Average()
                : null;

        var plannedTrainingDays =
            recentPlans
                .Where(x =>
                    Engine30DashboardAnalysisService.IsTrainingWorkoutCode(
                        Engine30DashboardProjectionService.NormalizeWorkoutCode(
                            x.WorkoutTypeCode)))
                .Select(x => x.PlannedDate)
                .Distinct()
                .Count();

        var completedTrainingDays =
            recentSessions
                .Where(x =>
                    Engine30DashboardAnalysisService.IsTrainingWorkoutCode(
                        Engine30DashboardProjectionService.NormalizeWorkoutCode(
                            x.WorkoutTypeCode)))
                .Select(x => x.WorkoutDate)
                .Distinct()
                .Count();

        var engine30Analysis =
            dashboardAnalysis.Analyze(
                new Engine30DashboardAnalysisInput(
                    ActionDate:
                        actionDate,

                    PlanCode:
                        actionPlanCode,

                    PlannedWorkoutCode:
                        previousPlanForAnalysis?.WorkoutTypeCode is string previousPlanCode
                            ? Engine30DashboardProjectionService.NormalizeWorkoutCode(
                                previousPlanCode)
                            : null,

                    ActualWorkoutCodes:
                        previousActualCodes,

                    ActualWorkoutSummary:
                        engine30Impact.PreviousWorkoutActual,

                    TargetCalories:
                        engine30Impact.PreviousTargetCalories,

                    ActualCalories:
                        engine30Impact.PreviousActualCalories,

                    TargetProteinG:
                        engine30Impact.PreviousTargetProteinG,

                    ActualProteinG:
                        engine30Impact.PreviousActualProteinG,

                    TargetCarbsG:
                        engine30Impact.PreviousTargetCarbsG,

                    ActualCarbsG:
                        engine30Impact.PreviousActualCarbsG,

                    TargetFatG:
                        engine30Impact.PreviousTargetFatG,

                    ActualFatG:
                        engine30Impact.PreviousActualFatG,

                    GlycogenDemandG:
                        engine30Impact.PreviousGlycogenDemandG,

                    RestorationRatio:
                        engine30Impact.PreviousRestorationRatio,

                    FourDayRestorationRatio:
                        engine30Impact.AbsoluteRestorationRatio,

                    FourDayHardDays:
                        engine30Impact.FourDayHardDays,

                    FourDayModerateHardDays:
                        engine30Impact.FourDayModerateHardDays,

                    FourDayCarbohydrateEvidenceDays:
                        engine30Impact.FourDayCarbohydrateEvidenceDays,

                    FourDayNutritionCompleteDays:
                        completeNutritionDays,

                    FourDayPlannedTrainingDays:
                        plannedTrainingDays,

                    FourDayCompletedTrainingDays:
                        completedTrainingDays,

                    FourDayAverageProteinG:
                        averageProtein,

                    FourDayAverageFatG:
                        averageFat,

                    WeekEnergyOverageKcal:
                        engine30Impact.WeekEnergyOverageKcal,

                    WeekEligibleEnergyCarryoverKcal:
                        engine30Impact.WeekEligibleEnergyCarryoverKcal,

                    CurrentMorningWeightLb:
                        exactTodayMeasurement?.WeightLb,

                    PriorMorningWeightLb:
                        priorMorningMeasurement?.WeightLb,

                    CurrentMorningBodyFatFraction:
                        exactTodayMeasurement?.BodyFatPct,

                    PriorMorningBodyFatFraction:
                        priorMorningMeasurement?.BodyFatPct,

                    CurrentMorningMuscleMassLb:
                        exactTodayMeasurement?.MuscleMassLb,

                    PriorMorningMuscleMassLb:
                        priorMorningMeasurement?.MuscleMassLb,

                    FourDayScaleDays:
                        scaleByDate.Count,

                    TrendStartWeightLb:
                        firstTrendMeasurement?.WeightLb,

                    TrendEndWeightLb:
                        lastTrendMeasurement?.WeightLb,

                    TrendStartBodyFatFraction:
                        firstTrendMeasurement?.BodyFatPct,

                    TrendEndBodyFatFraction:
                        lastTrendMeasurement?.BodyFatPct,

                    TrendStartMuscleMassLb:
                        firstTrendMeasurement?.MuscleMassLb,

                    TrendEndMuscleMassLb:
                        lastTrendMeasurement?.MuscleMassLb));
        // ------------------------------------------------------------
        // v1009 adaptive decision layer.
        //
        // This interprets Engine 3.0 evidence; it does not rewrite the
        // Weekly Plan and it never creates exercise calorie eat-back.
        // ------------------------------------------------------------
        var recommendation =
            recommendationEngine.Evaluate(
                new RecompRecommendationInput(
                    HasPlannedWorkout:
                        engine30Projection.Today.HasPlannedWorkout,
                    PlannedWorkoutTypeCode:
                        engine30Projection.Today.WorkoutTypeCode,
                    PlannedWorkoutDisplayName:
                        engine30Projection.Today.WorkoutDisplayName,
                    PlannedWorkoutCategoryCode:
                        engine30Projection.Today.WorkoutCategoryCode,
                    PlannedGlycogenDemandG:
                        engine30Impact.TodayPlannedGlycogenDemandG,
                    PreWorkoutRescueG:
                        engine30Impact.PreWorkoutRescueG,
                    IncomingReadinessBandCode:
                        engine30Impact.IncomingReadinessBandCode,
                    ProjectedReadinessBandCode:
                        engine30Impact.ProjectedReadinessBandCode,
                    PreviousTargetCalories:
                        engine30Impact.PreviousTargetCalories,
                    PreviousEnergyVarianceKcal:
                        engine30Impact.PreviousEnergyVarianceKcal,
                    PreviousEligibleEnergyCarryoverKcal:
                        engine30Impact.PreviousEligibleEnergyCarryoverKcal,
                    FourDayHardDays:
                        engine30Impact.FourDayHardDays,
                    FourDayModerateHardDays:
                        engine30Impact.FourDayModerateHardDays,
                    SleepHours:
                        todayCheckin?.SleepHours,
                    EnergyScore:
                        todayCheckin?.EnergyScore,
                    SorenessScore:
                        todayCheckin?.SorenessScore,
                    PainFlag:
                        todayCheckin?.PainFlag ?? false,
                    IllnessFlag:
                        todayCheckin?.IllnessFlag ?? false));

        var recommendedType =
            ResolveWorkoutType(
                workoutTypes,
                recommendation.RecommendedWorkoutTypeCode)
            ?? todayPlannedType;

        var coach =
            BuildCoachRecommendation(
                recommendation,
                todayCheckin,
                engine30Impact);

        return new DashboardViewModel
        {
            ActionDate = actionDate,
            ClientName = client.ClientName,
            ProgramWeek = Math.Max(1, (actionDate.DayNumber - program.StartDate.DayNumber) / 7 + 1),
            PlanCode = actionPlanCode,
            Engine30VersionCode = engine30Projection.Today.EngineVersionCode,
            Engine30ConfigVersionCode = engine30Projection.Today.ConfigVersionCode,
            Engine30WorkoutProfileVersionCode = engine30Projection.Today.WorkoutProfileVersionCode,
            Engine30WorkoutProfileSourceCode = engine30Projection.Today.WorkoutProfileSourceCode,
            Engine30WorkoutCategoryCode = engine30Projection.Today.WorkoutCategoryCode,
            Engine30WorkoutTypeCode = engine30Projection.Today.WorkoutTypeCode,
            Engine30CarbGoverningRuleCode = engine30Projection.Today.CarbGoverningRuleCode,
            Engine30BaseLivingKcal = engine30Projection.Today.BaseLivingEnergyKcal,
            Engine30HabitualActivityKcal = engine30Projection.Today.HabitualActivityEnergyKcal,
            Engine30NetWorkoutKcal = engine30Projection.Today.NetWorkoutActivityEnergyKcal,
            Engine30MaintenanceKcal = engine30Projection.Today.MaintenanceEnergyKcal,
            Engine30EffectiveDeficitPct = engine30Projection.Today.EffectiveDeficitPct,

            TodayStatusLabel = BuildStatusLabel(recommendation),
            TodayStatusClass = BuildStatusClass(recommendation),
            PlannedWorkout = engine30Projection.Today.HasPlannedWorkout ? engine30Projection.Today.WorkoutDisplayName : "No workout planned",
            RecommendedWorkout = recommendation.RecommendedWorkoutDisplayName,
            ScheduleChangeRecommended = recommendation.WorkoutChangeRecommended,
            WorkoutRecommendationLabel = !engine30Projection.Today.HasPlannedWorkout
                ? $"Use {recommendation.RecommendedWorkoutDisplayName}"
                : recommendation.WorkoutChangeRecommended
                    ? $"Change to {recommendation.RecommendedWorkoutDisplayName}"
                    : $"Keep {engine30Projection.Today.WorkoutDisplayName}",
            WorkoutFocus = recommendedType is null
                ? HumanizeCode(engine30Projection.Today.WorkoutCategoryCode)
                : BuildWorkoutFocus(recommendedType),
            WorkoutDescription = recommendedType is null
                ? recommendation.Summary
                : BuildWorkoutDescription(recommendedType),
            WorkoutDurationMin = engine30Projection.Today.PlannedDurationMinutes,
            WorkoutEffort = engine30Projection.Today.PlannedRpe.HasValue ? $"RPE {engine30Projection.Today.PlannedRpe.Value:0.#}" : "Recovery / no planned effort",
            WorkoutActual = engine30ActualWorkout.WorkoutSummary ?? workoutActual,
            PlannedWorkoutCaloriesKcal = engine30Projection.Today.HasPlannedWorkout
                ? engine30Projection.Today.GrossWorkoutEnergyKcal
                : null,
            PlannedWorkoutGlycogenG = engine30Projection.Today.HasPlannedWorkout
                ? engine30Projection.Today.GlycogenDemandG
                : null,
            ActualWorkoutCaloriesKcal = engine30ActualWorkout.HasActualWorkout
                ? engine30ActualWorkout.GrossWorkoutEnergyKcal
                : null,
            ActualWorkoutGlycogenG = engine30ActualWorkout.HasActualWorkout
                ? engine30ActualWorkout.GlycogenDemandG
                : null,
            ActualWorkoutDurationMin = engine30ActualWorkout.HasActualWorkout
                ? engine30ActualWorkout.TotalDurationMinutes
                : null,
            ActualWorkoutRpe = engine30ActualWorkout.DurationWeightedRpe,
            WorkoutRecommendationRationale = BuildWorkoutRecommendationRationale(
                recommendation,
                engine30Projection.Today.HasPlannedWorkout),
            Calories = engine30Projection.Today.PrescribedEnergyKcal,
            ProteinG = engine30Projection.Today.ProteinG,
            CarbsG = engine30Projection.Today.RecommendedCarbsG,
            FatG = engine30Projection.Today.FatG,
            NutritionDayLabel = BuildNutritionDayLabel(recommendedType),
            NutritionTip = recommendation.NutritionSummary,
            NutritionActualLogged = nutritionActual is not null,
            ActualCalories = nutritionActual?.ActualCalories,
            ActualProteinG = nutritionActual?.ActualProteinG,
            ActualCarbsG = nutritionActual?.ActualCarbsG,
            ActualFatG = nutritionActual?.ActualFatG,

            CoachTitle = coach.Title,
            CoachRecommendation = coach.Body,
            CoachWhy = BuildCoachWhy(recommendation, engine30Impact, todayCheckin),
            EnergyScore = todayCheckin?.EnergyScore,
            SorenessScore = todayCheckin?.SorenessScore,
            SleepHours = todayCheckin?.SleepHours,
            PainFlag = todayCheckin?.PainFlag == true,
            IllnessFlag = todayCheckin?.IllnessFlag == true,

            BaseCarbsG = engine30Projection.Today.BaseRestCarbsG,
            CarryoverCarbsG = engine30Impact.RecoveryAllocationG,
            PreWorkoutCarbsG = engine30Impact.PreWorkoutRescueG,
            FourDayGlycogenDemandG = engine30Impact.FourDayGlycogenDemandG,
            FourDayRestorationCreditG = engine30Impact.FourDayRestorationCreditG,
            AbsoluteRestorationRatio = engine30Impact.AbsoluteRestorationRatio,
            FunctionalTargetRatio = engine30Impact.FunctionalTargetRatio,
            TargetAchievementRatio = engine30Impact.ProjectedTargetAchievementRatio,
            GlycogenReadiness = Engine30DashboardProjectionService.HumanizeReadiness(engine30Impact.IncomingReadinessBandCode),
            PreviousWorkoutActual = engine30Impact.PreviousWorkoutActual,
            FourDayCarbohydrateEvidenceDays = engine30Impact.FourDayCarbohydrateEvidenceDays,
            FourDayModerateHardDays = engine30Impact.FourDayModerateHardDays,
            FourDayHardDays = engine30Impact.FourDayHardDays,
            TodayPlannedGlycogenDemandG = engine30Impact.TodayPlannedGlycogenDemandG,
            HistoricalFuelShortfallG = engine30Impact.HistoricalShortfallG,
            IncomingTargetAchievementRatio = engine30Impact.IncomingTargetAchievementRatio,
            ProjectedTargetAchievementRatio = engine30Impact.ProjectedTargetAchievementRatio,
            ProjectedGlycogenReadiness = Engine30DashboardProjectionService.HumanizeReadiness(engine30Impact.ProjectedReadinessBandCode),
            FuelActionLabel = engine30Impact.FuelActionLabel,
            FuelReasonCodes = string.Join(", ", engine30Impact.ReasonCodes),
            Engine30Analysis = engine30Analysis,
            RecompRecommendation = recommendation,
            PreviousEnergyVarianceKcal = engine30Impact.PreviousEnergyVarianceKcal,
            PreviousEligibleEnergyCarryoverKcal = engine30Impact.PreviousEligibleEnergyCarryoverKcal,
            WeekEnergyOverageKcal = engine30Impact.WeekEnergyOverageKcal,
            WeekEligibleEnergyCarryoverKcal = engine30Impact.WeekEligibleEnergyCarryoverKcal,
            EnergyCorrectionAppliedBeforeTodayKcal = engine30Impact.EnergyCorrectionAppliedBeforeTodayKcal,
            EnergyCorrectionAppliedTodayKcal = engine30Impact.EnergyCorrectionAppliedTodayKcal,
            EnergyCorrectionAppliedFutureKcal = engine30Impact.EnergyCorrectionAppliedFutureKcal,
            EnergyCorrectionUnallocatedKcal = engine30Impact.EnergyCorrectionUnallocatedKcal,
            SurplusRecoveryFraction = engine30Impact.SurplusRecoveryFraction,
            TodayBaselinePrescribedEnergyKcal = engine30Impact.TodayBaselinePrescribedEnergyKcal,
            TodayEnergyCarryoverCorrectionKcal = engine30Impact.TodayEnergyCarryoverCorrectionKcal,
            EnergyCarryoverSummary = engine30Impact.EnergyCarryoverSummary,

            TomorrowDate = tomorrowDate,
            TomorrowPlanAvailable = engine30Projection.Tomorrow.HasPlannedWorkout,
            TomorrowWorkout = engine30Projection.Tomorrow.HasPlannedWorkout ? engine30Projection.Tomorrow.WorkoutDisplayName : null,
            TomorrowEngine30WorkoutTypeCode = engine30Projection.Tomorrow.WorkoutTypeCode,
            TomorrowFocus = tomorrowType is null ? null : BuildWorkoutFocus(tomorrowType),
            TomorrowDescription = tomorrowType is null ? null : BuildWorkoutDescription(tomorrowType),
            TomorrowDurationMin = engine30Projection.Tomorrow.HasPlannedWorkout ? engine30Projection.Tomorrow.PlannedDurationMinutes : null,
            TomorrowEffort = engine30Projection.Tomorrow.PlannedRpe.HasValue ? $"RPE {engine30Projection.Tomorrow.PlannedRpe.Value:0.#}" : null,
            TomorrowCalories = engine30Projection.Tomorrow.PrescribedEnergyKcal,
            TomorrowProteinG = engine30Projection.Tomorrow.ProteinG,
            TomorrowCarbsG = engine30Projection.Tomorrow.RecommendedCarbsG,
            TomorrowFatG = engine30Projection.Tomorrow.FatG,
            TomorrowTip = BuildTomorrowTip(tomorrowType, tomorrowDate),

            PreviousDate = engine30Impact.PreviousDate,
            PreviousTargetAvailable = engine30Impact.PreviousTargetAvailable,
            PreviousTargetSource = "Engine 3.0 weekly prescription",
            PreviousNutritionActualLogged = engine30Impact.PreviousNutritionActualLogged,
            PreviousTargetCalories = engine30Impact.PreviousTargetCalories,
            PreviousTargetProteinG = engine30Impact.PreviousTargetProteinG,
            PreviousTargetCarbsG = engine30Impact.PreviousTargetCarbsG,
            PreviousTargetFatG = engine30Impact.PreviousTargetFatG,
            PreviousActualCalories = engine30Impact.PreviousActualCalories,
            PreviousActualProteinG = engine30Impact.PreviousActualProteinG,
            PreviousActualCarbsG = engine30Impact.PreviousActualCarbsG,
            PreviousActualFatG = engine30Impact.PreviousActualFatG,
            PreviousCarbDifferenceG = engine30Impact.PreviousCarbDifferenceG,
            PreviousGlycogenDemandG = engine30Impact.PreviousGlycogenDemandG,
            PreviousRestorationCreditG = engine30Impact.PreviousRestorationCreditG,
            PreviousRestorationRatio = engine30Impact.PreviousRestorationRatio,
            PreviousFuelSummary = engine30Impact.PreviousFuelSummary,

            WeeklyStatus = recommendation.WorkoutChangeRecommended
                ? "Adaptive change recommended"
                : "On target",
            WeeklyNote = recommendation.Summary,
            StrengthPriority = null,
            CardioNeed = null
        };
    }

    private static string? BuildActualWorkoutSummary(
        IReadOnlyList<WorkoutSession> sessions,
        IReadOnlyDictionary<string, WorkoutTypeDefinition> workoutTypes)
    {
        if (sessions.Count == 0)
            return null;

        return string.Join(" + ", sessions.Select(x =>
        {
            var displayName = Engine30LegacyWorkoutCompatibility.TryResolve(workoutTypes, x.WorkoutTypeCode, out var type)
                ? type.DisplayName
                : x.WorkoutTypeCode;
            var rpe = x.SessionRpe.HasValue ? $", RPE {x.SessionRpe.Value:0.#}" : "";
            return $"{displayName} — {x.DurationMin:0} min{rpe}";
        }));
    }

    private static string BuildWorkoutRecommendationRationale(
        RecommendationResult result,
        bool hasPlannedWorkout)
    {
        if (!hasPlannedWorkout)
            return "No workout is scheduled in Workout Plan for today, so Recomp is using the available recovery and training context.";

        return result.Recommendation.RecommendationBasisCode switch
        {
            "SAFETY_PAIN" =>
                "Pain or an injury was reported, so Recomp recommends adjusting the planned workout.",
            "SAFETY_ILLNESS" =>
                "Illness or systemic fatigue was reported, so Recomp recommends a recovery-focused adjustment.",
            "REGION_NOT_RECOVERED" =>
                "The primary muscle region has not recovered enough for the planned major session.",
            "SWAP_HEAVY_DAY" =>
                "The planned region is not recovered, so Recomp recommends a recovered heavy-session swap to preserve weekly structure.",
            "VERY_LOW_FUEL_READINESS" =>
                "Fuel readiness is too low for the planned high-output workout, so Recomp recommends a lower-demand alternative.",
            _ =>
                "Recovery, recent training load, and fuel status support the planned workout."
        };
    }

    private static string BuildStatusLabel(RecommendationResult result)
        => result.Recommendation.RecommendationBasisCode switch
        {
            "SAFETY_PAIN" or "SAFETY_ILLNESS" => "Recovery priority",
            _ when result.Recommendation.ScheduleChangeRecommended => "Plan adjusted",
            _ when result.WeeklyStrategy.StatusCode == "GOOD" => "On track",
            _ => "Review plan"
        };

    private static string BuildStatusClass(RecommendationResult result)
        => result.Recommendation.RecommendationBasisCode switch
        {
            "SAFETY_PAIN" or "SAFETY_ILLNESS" => "danger",
            _ when result.Recommendation.ScheduleChangeRecommended => "warning",
            _ when result.WeeklyStrategy.StatusCode == "GOOD" => "good",
            _ => "neutral"
        };

    private static string BuildWorkoutFocus(WorkoutTypeDefinition workout)
    {
        if (!string.IsNullOrWhiteSpace(workout.PrimaryFocusCode))
            return workout.PrimaryFocusCode.Replace("/", " / ");

        if (!string.IsNullOrWhiteSpace(workout.RegionCode))
            return workout.RegionCode.ToUpperInvariant() switch
            {
                "PUSH" => "Chest / shoulders / triceps",
                "PULL" => "Back / rear delts / biceps",
                "LEGS" => "Quads / hamstrings / glutes",
                _ => HumanizeCode(workout.RegionCode)
            };

        return HumanizeCode(workout.WeeklyRoleCode);
    }

    private static string BuildWorkoutDescription(WorkoutTypeDefinition workout)
    {
        if (!string.IsNullOrWhiteSpace(workout.PlanningNote))
            return workout.PlanningNote;

        return workout.WeeklyRoleCode switch
        {
            "HEAVY" or "HEAVY_GENERIC" => "Primary strength session with a high training stimulus.",
            "LIGHT_STRENGTH" => "Moderate strength work that supports the major sessions without matching their fatigue cost.",
            "CONDITIONING" => "Conditioning-focused work with meaningful systemic demand.",
            "RECOVERY_CARDIO" => "Easy aerobic work intended to support recovery and aerobic capacity.",
            "RECOVERY" => "Recovery-focused day with minimal training stress.",
            _ => "Planned activity for the day."
        };
    }

    private static string BuildEffortLabel(WorkoutTypeDefinition workout)
        => workout.WeeklyRoleCode switch
        {
            "HEAVY" or "HEAVY_GENERIC" => "RPE 7–9",
            "LIGHT_STRENGTH" => "RPE 6–8",
            "CONDITIONING" => workout.FuelScore >= 4m ? "RPE 7–9" : "RPE 6–8",
            "RECOVERY_CARDIO" => "RPE 3–5",
            "RECOVERY" => "RPE 1–3",
            "OTHER_LOAD" => "RPE 4–7",
            _ => "Use planned effort"
        };

    private static string BuildNutritionDayLabel(WorkoutTypeDefinition workout)
        => workout.FuelPriorityCode.ToUpperInvariant() switch
        {
            "HIGH" => "Higher-carb training day",
            "MODERATE" => "Moderate-carb training day",
            "LOW" => "Lower-carb / recovery day",
            _ => "Balanced nutrition day"
        };

    private static string BuildNutritionTip(WorkoutTypeDefinition workout, DateOnly date)
    {
        string[] tips = workout.FuelPriorityCode.ToUpperInvariant() switch
        {
            "HIGH" =>
            [
                "Put a meaningful share of today's carbohydrates around training. A planned higher-carb treat fits better today than on a recovery day when it still fits the calorie target.",
                "Don't underfuel the session early and then chase hunger at night. Put carbohydrates around training on purpose and keep protein steady.",
                "Performance and recovery are the priority today. A carbohydrate-rich post-workout meal is a good use of the day's larger carb budget."
            ],
            "MODERATE" =>
            [
                "Keep protein evenly distributed and place a moderate share of your carbohydrates around the session.",
                "Use carbohydrates where they help the workout most, but keep the rest of the day controlled rather than grazing.",
                "This is a middle-ground fuel day: enough carbohydrate to support training without treating it like a major-session carb day."
            ],
            _ =>
            [
                "Keep protein high, make carbohydrates purposeful, and watch unplanned grazing on this lower-demand day.",
                "Favor lean protein, vegetables, and filling foods. You do not need to chase a major-training-day carbohydrate intake today.",
                "Recovery still needs food, but lower activity makes portion control more important. Keep snacks intentional rather than automatic."
            ]
        };

        return tips[Math.Abs(date.DayNumber) % tips.Length];
    }

    private static (string Title, string Body) BuildCoachRecommendation(
        RecommendationResult result,
        DailyCheckin? checkin)
    {
        var recommendation = result.Recommendation;

        if (checkin?.PainFlag == true)
        {
            return (
                "Recovery takes priority",
                $"Pain or an injury is logged today. Recomp has overridden the harder plan with {result.RecommendedWorkout.DisplayName}. Avoid movements that reproduce pain and use the recovery recommendation instead of trying to train through it.");
        }

        if (checkin?.IllnessFlag == true)
        {
            return (
                "Recovery takes priority",
                $"Illness or systemic fatigue is logged today. Recomp recommends {result.RecommendedWorkout.DisplayName} rather than hard training. Focus on recovery and resume harder work when you are feeling recovered.");
        }

        if (recommendation.ScheduleChangeRecommended)
        {
            return (
                "Adjust today's plan",
                $"Recomp recommends {result.RecommendedWorkout.DisplayName} instead of the planned {result.SelectedWorkout.DisplayName}. The change protects recovery while keeping the weekly training structure intact.");
        }

        if (checkin?.EnergyScore is <= 3)
        {
            return (
                "Energy is low",
                $"The engine has not triggered a schedule change, but your energy score is {checkin.EnergyScore}/10. Keep {result.RecommendedWorkout.DisplayName} controlled, skip optional extra volume, and reassess if your energy drops further during the session.");
        }

        if (checkin?.SorenessScore is >= 8)
        {
            return (
                "Soreness is elevated",
                $"Your soreness score is {checkin.SorenessScore}/10. The planned session still fits today's recovery picture, but avoid bonus work and keep technique and range of motion clean.");
        }

        if (checkin?.SleepHours is < 5.5m)
        {
            return (
                "Recovery needs attention",
                $"Sleep was {checkin.SleepHours:0.#} hours. The plan has not been automatically downgraded, but keep today's effort disciplined and prioritize recovery after training.");
        }

        if (recommendation.GlycogenReadinessCode is "VERY_LOW" or "LOW" or "MODERATELY_LOW" or "SLIGHTLY_REDUCED")
        {
            var carbLine = recommendation.PreWorkoutPortionG > 0
                ? $" Put about {recommendation.PreWorkoutPortionG:0} g of the planned carbohydrates before training."
                : " Keep today's carbohydrate intake purposeful around the session.";

            return (
                "Fuel before training",
                $"Glycogen recovery is {HumanizeReadiness(recommendation.GlycogenReadinessCode).ToLowerInvariant()}, but recovery and recent workload still support {result.RecommendedWorkout.DisplayName}.{carbLine}");
        }

        if (checkin?.EnergyScore is >= 7)
        {
            return (
                "You're good to go",
                $"Fuel readiness is adequate and energy is {checkin.EnergyScore}/10. Follow the {result.RecommendedWorkout.DisplayName} plan and keep the session inside the intended effort range.");
        }

        return (
            "Stay the course",
            $"Recovery, recent workload, and fuel status support {result.RecommendedWorkout.DisplayName}. Hit the planned nutrition and keep the session within the intended effort range.");
    }

    private static string BuildCoachWhy(RecommendationResult result, DailyCheckin? checkin)
    {
        var factors = new List<string>
        {
            $"fuel readiness {HumanizeReadiness(result.Recommendation.GlycogenReadinessCode).ToLowerInvariant()}",
            $"4-day carbohydrate recovery {result.Recommendation.AbsoluteRestorationRatio:P0}"
        };

        if (checkin?.EnergyScore is not null)
            factors.Add($"energy {checkin.EnergyScore}/10");
        if (checkin?.SorenessScore is not null)
            factors.Add($"soreness {checkin.SorenessScore}/10");
        if (checkin?.SleepHours is not null)
            factors.Add($"sleep {checkin.SleepHours:0.#} h");
        if (checkin?.PainFlag == true)
            factors.Add("pain/injury flag active");
        if (checkin?.IllnessFlag == true)
            factors.Add("illness/systemic-fatigue flag active");

        return "Based on " + string.Join(", ", factors) + ".";
    }

    private static string BuildTomorrowTip(WorkoutTypeDefinition? workout, DateOnly date)
    {
        if (workout is null)
            return "Tomorrow has not been planned yet. Add it on Workout Plan so Recomp can prepare a preliminary fuel target.";

        string[] tips = workout.WeeklyRoleCode switch
        {
            "RECOVERY" =>
            [
                "Enjoy the recovery day. If you feel good and want movement, 20–30 minutes of easy Zone 2 or mobility is enough.",
                "Let the recovery day do its job. Easy walking, mobility, or a short Zone 2 session is optional—not another hard workout."
            ],
            "RECOVERY_CARDIO" =>
            [
                "Keep the aerobic work conversational and leave the session feeling better than you started.",
                "Use the easy cardio to support recovery. Resist the urge to turn it into a race or HIIT session."
            ],
            "HEAVY" or "HEAVY_GENERIC" =>
            [
                "Set up tomorrow by finishing today's recovery nutrition and hydration. Plan a solid carbohydrate-containing meal before the session.",
                "Tomorrow is a demanding strength day. Protect sleep tonight and arrive fueled rather than trying to make up carbohydrates after the workout."
            ],
            "CONDITIONING" =>
            [
                "Hydrate early and place some carbohydrates before the session. Keep the rest of the day's training load under control.",
                "Tomorrow's conditioning will have meaningful systemic demand. Protect sleep and avoid stacking unnecessary hard work around it."
            ],
            "LIGHT_STRENGTH" =>
            [
                "Keep protein steady and use moderate carbohydrates around the session. The goal is productive work without major-day fatigue.",
                "Tomorrow should support the major sessions, not compete with them. Keep the accessory work controlled and leave room for recovery."
            ],
            _ =>
            [
                "Use today's recovery information to arrive ready for tomorrow, and keep the planned workload in perspective with the rest of the week."
            ]
        };

        return tips[Math.Abs(date.DayNumber) % tips.Length];
    }

    private static string BuildPreviousFuelSummary(
        NutritionDay? nutrition,
        decimal demandG,
        decimal? creditG,
        decimal? restorationRatio)
    {
        if (nutrition?.ActualCarbsG is null)
            return "No carbohydrate actual was logged for yesterday. Recomp does not invent an intake value; the rolling recovery calculation therefore has less evidence for that day.";

        if (demandG <= 0)
            return "No meaningful completed-workout glycogen demand was recorded yesterday, so yesterday adds little demand to the refill ledger. The rolling four-day result is driven by the other recent training days.";

        if (creditG is null || restorationRatio is null)
            return "Yesterday's carbohydrate intake is logged, but a refill percentage could not be calculated from the available workout data.";

        if (restorationRatio >= 0.90m)
            return $"Yesterday's carbohydrate intake received about {creditG:0} g of restoration credit against roughly {demandG:0} g of estimated workout glycogen demand—most of that day's estimated demand was covered.";

        if (restorationRatio >= 0.60m)
            return $"Yesterday's carbohydrate intake received about {creditG:0} g of restoration credit against roughly {demandG:0} g of estimated workout glycogen demand. That was a partial refill, so the remaining recovery burden carries into today's rolling calculation.";

        return $"Yesterday's carbohydrate intake received about {creditG:0} g of restoration credit against roughly {demandG:0} g of estimated workout glycogen demand. Refill was limited, so today's plan gives recovery carbohydrate more attention.";
    }

    private static string HumanizeReadiness(string code)
        => code switch
        {
            "ADEQUATE" => "Adequate",
            "SLIGHTLY_REDUCED" => "Slightly reduced",
            "MODERATELY_LOW" => "Moderately low",
            "LOW" => "Low",
            "VERY_LOW" => "Very low",
            _ => HumanizeCode(code)
        };

    private static string? HumanizeStrengthPriority(string? code)
        => code switch
        {
            "PUSH" => "Push",
            "PULL" => "Pull",
            "LEGS" => "Legs",
            "NONE" or null or "" => "No additional strength priority",
            _ => HumanizeCode(code)
        };

    private static string? HumanizeCardioNeed(string? code)
        => code switch
        {
            "HIGH" => "High",
            "MODERATE" => "Moderate",
            "LOW" => "Low",
            null or "" => "Not enough information",
            _ => HumanizeCode(code)
        };

    private static string HumanizeWeeklyStatus(string code)
        => code switch
        {
            "GOOD" => "On target",
            "REVIEW" => "Needs review",
            "NOT_PLANNED" => "Not planned yet",
            _ => HumanizeCode(code)
        };

    private static string HumanizeWeeklyNote(WeeklyStrategyAssessment weekly)
    {
        if (weekly.StatusCode == "NOT_PLANNED")
            return "Set up the week on the Weekly Plan page so Recomp can evaluate the weekly structure.";

        return string.IsNullOrWhiteSpace(weekly.Note)
            ? "Recomp is evaluating this week's structure against your current Primary Plan."
            : weekly.Note;
    }

    private static string HumanizeCode(string code)
        => string.Join(" ", code
            .Replace("_", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => char.ToUpperInvariant(x[0]) + x[1..].ToLowerInvariant()));
}





























