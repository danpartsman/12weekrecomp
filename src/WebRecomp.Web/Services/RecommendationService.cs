using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record FuelWindow(
    decimal DemandG,
    decimal RestorationCreditG,
    decimal AbsoluteRestorationRatio,
    short ModerateHardDays,
    short HardDays);

public sealed record RecommendationResult(
    Recommendation Recommendation,
    WeeklyStrategyAssessment WeeklyStrategy,
    RegionRecovery RegionRecovery,
    WorkoutTypeDefinition ScheduledWorkout,
    WorkoutTypeDefinition SelectedWorkout,
    WorkoutTypeDefinition RecommendedWorkout,
    MacroTargets BaseTargets);

public sealed class RecommendationService(
    RecompDbContext db,
    IConfiguration configuration,
    ConfigurationService config,
    WorkoutMathService math,
    WeeklyStrategyService strategy,
    ProgramPlanService planSelection,
    WeeklyPlanService weeklyPlans,
    RecompEngineConfigurationService engine2Configuration,
    RecompEngineOrchestrator engine2,
    RecompActualWorkoutAnalysisService actualWorkoutAnalysis)
{
    public async Task<RecommendationResult> CalculateAndStoreAsync(
        AppClientContext context,
        DateOnly actionDate,
        CancellationToken cancellationToken = default)
    {
        if (context.Client is null || context.Program is null)
            throw new InvalidOperationException("An active client and program are required.");

        var client = context.Client;
        var program = context.Program;

        RecompEngineResult? engine2AuthoritativeResult = null;
        Guid? engine2AuthoritativeConfigVersionId = null;
        MacroTargets? engine2AuthoritativeBaseTargets = null;
        var planCode =
            await planSelection.GetPlanCodeForDateAsync(
                program,
                actionDate,
                cancellationToken);

        var clientToday =
            AppUserContextService.GetClientToday(client);

        if (PlanningWeek.Start(actionDate) ==
            PlanningWeek.Start(clientToday))
        {
            await weeklyPlans.EnsureCurrentWeekAsync(
                client,
                program,
                clientToday,
                cancellationToken);
        }

        var latestMeasurement =
            await db.Measurements
                .Where(x =>
                    x.ClientId == client.ClientId &&
                    !x.IsDeleted &&
                    x.LocalMeasurementDate <= actionDate &&
                    x.WeightLb != null)
                .OrderByDescending(x => x.LocalMeasurementDate)
                .ThenByDescending(x => x.MeasurementAtUtc)
                .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "A current body weight is required.");

        var weight =
            latestMeasurement.WeightLb!.Value;

        var planned =
            await db.PlannedWorkouts
                .SingleOrDefaultAsync(
                    x =>
                        x.ProgramId == program.ProgramId &&
                        x.PlannedDate == actionDate,
                    cancellationToken);

        var checkin =
            await db.DailyCheckins
                .SingleOrDefaultAsync(
                    x =>
                        x.ProgramId == program.ProgramId &&
                        x.CheckinDate == actionDate,
                    cancellationToken);

        var chosenCode =
            planned?.WorkoutTypeCode;

        // =====================================================
        // ENGINE 2.0 AUTHORITATIVE
        // =====================================================

        object? engine2Diagnostics = null;
        string? engine2Error = null;

        try
        {
            var shadowConfiguration =
                await engine2Configuration.LoadByVersionCodeAsync(
                    "CFG-1007.0",
                    cancellationToken);

            var shadowConfigId =
                shadowConfiguration.ConfigVersion.ConfigVersionId;

            var shadowSettings =
                shadowConfiguration.Settings;

            var shadowWorkoutTypes =
                await config.GetWorkoutTypesAsync(
                    shadowConfigId,
                    cancellationToken);

            var shadowPlan =
                await config.GetPlanAsync(
                    shadowConfigId,
                    planCode,
                    cancellationToken);

            var authoritativePlannedCode =
                string.IsNullOrWhiteSpace(chosenCode)
                    ? (
                        shadowWorkoutTypes.ContainsKey("RECOVERY_0_RPE2")
                            ? "RECOVERY_0_RPE2"
                            : shadowWorkoutTypes.Values
                                .OrderBy(x => x.WorkoutTypeCode)
                                .First()
                                .WorkoutTypeCode
                      )
                    : Engine30LegacyWorkoutCompatibility.ToEngine2(chosenCode);
            if (!shadowWorkoutTypes.TryGetValue(
                    authoritativePlannedCode,
                    out var shadowPlannedWorkout))
            {
                throw new InvalidOperationException(
                    $"CFG-1007.0 does not contain workout type {authoritativePlannedCode}.");
            }

            var shadowWeekly =
                await weeklyPlanning.AssessWeekAsync(
                    program.ProgramId,
                    shadowConfigId,
                    actionDate,
                    planCode,
                    program.AvailableTrainingDaysWeek,
                    cancellationToken);

            var shadowRegionRecovery =
                await legacyRegionRecovery.CalculateAsync(
                    program.ProgramId,
                    actionDate,
                    shadowSettings,
                    cancellationToken);

            var shadowFuelWindow =
                await CalculateFuelWindowAsync(
                    program,
                    shadowConfigId,
                    actionDate,
                    shadowWorkoutTypes,
                    shadowSettings,
                    cancellationToken);

            var shadowRecoveryWorkout =
                Recovery(shadowWorkoutTypes);

            WorkoutTypeDefinition? shadowHeavySwap = null;

            if (shadowPlannedWorkout.CountsDedicatedHeavy
                && !IsRegionRecovered(
                    shadowPlannedWorkout.RegionCode,
                    shadowRegionRecovery))
            {
                shadowHeavySwap =
                    await FindHeavySwapAsync(
                        program.ProgramId,
                        actionDate,
                        shadowPlannedWorkout,
                        shadowWorkoutTypes,
                        shadowRegionRecovery,
                        cancellationToken);
            }

            var shadowLightAlternative =
                shadowPlannedWorkout.CountsDedicatedHeavy
                    ? FindLightAlternative(
                        shadowPlannedWorkout.RegionCode,
                        shadowWorkoutTypes)
                    : null;

            var shadowRecoverySwap =
                await FindRecoverySwapAsync(
                    program.ProgramId,
                    actionDate,
                    shadowWorkoutTypes,
                    cancellationToken);

            var shadowRecoveryCardio =
                RecoveryCardioOrRecovery(
                    shadowWorkoutTypes);

            var shadowInput =
                new RecompEngineInputSnapshot(
                    ActionDate: actionDate,
                    CurrentWeightLb: weight,
                    Plan: shadowPlan,
                    PlannedWorkout: shadowPlannedWorkout,
                    WeeklyStrategy: shadowWeekly,
                    RegionRecovery: shadowRegionRecovery,

                    FuelDemandG:
                        shadowFuelWindow.DemandG,

                    FuelRestorationCreditG:
                        shadowFuelWindow.RestorationCreditG,

                    FuelAbsoluteRestorationRatio:
                        shadowFuelWindow.AbsoluteRestorationRatio,

                    ModerateHardDays:
                        shadowFuelWindow.ModerateHardDays,

                    HardDays:
                        shadowFuelWindow.HardDays,

                    PainFlag:
                        checkin?.PainFlag == true,

                    IllnessFlag:
                        checkin?.IllnessFlag == true,

                    RecoveryWorkout:
                        shadowRecoveryWorkout,

                    HeavySwapWorkout:
                        shadowHeavySwap,

                    LightAlternativeWorkout:
                        shadowLightAlternative,

                    RecoverySwapWorkout:
                        shadowRecoverySwap,

                    RecoveryCardioWorkout:
                        shadowRecoveryCardio,

                    Settings:
                        shadowSettings);            var shadowResult =
                engine2.Calculate(shadowInput);

            engine2AuthoritativeResult = shadowResult;

            engine2AuthoritativeConfigVersionId =
                shadowConfiguration.ConfigVersion.ConfigVersionId;

            engine2AuthoritativeBaseTargets =
                math.CalculatePlannedTargets(
                    weight,
                    shadowPlan,
                    shadowResult.Decision.RecommendedWorkout,
                    shadowSettings);


            RecompActualWorkoutAnalysis? shadowActualWorkout = null;
            string? shadowActualWorkoutError = null;

            try
            {
                var completedActualSession =
                    await db.WorkoutSessions
                        .Where(x =>
                            x.ClientId == client.ClientId &&
                            x.ProgramId == program.ProgramId &&
                            x.WorkoutDate == actionDate &&
                            x.CompletedFlag)
                        .OrderByDescending(x => x.UpdatedAtUtc)
                        .FirstOrDefaultAsync(cancellationToken);

                if (completedActualSession is not null)
                {
                    var shadowActualWorkoutTypes =
                        await config.GetWorkoutTypesAsync(
                            shadowConfiguration.ConfigVersion.ConfigVersionId,
                            cancellationToken);

                    var shadowActualWorkoutDefinition =
                        shadowActualWorkoutTypes.Values.SingleOrDefault(
                            x => string.Equals(
                                x.WorkoutTypeCode,
                                Engine30LegacyWorkoutCompatibility.ToEngine2(
                                    completedActualSession.WorkoutTypeCode),
                                StringComparison.OrdinalIgnoreCase));

                    if (shadowActualWorkoutDefinition is null)
                    {
                        throw new InvalidOperationException(
                            $"Shadow workout definition not found for {completedActualSession.WorkoutTypeCode}.");
                    }

                    var shadowActualPlan =
                        await config.GetPlanAsync(
                            shadowConfiguration.ConfigVersion.ConfigVersionId,
                            planCode,
                            cancellationToken);

                    var nutritionSoFar =
                        await db.NutritionDays
                            .Where(x =>
                                x.ClientId == client.ClientId &&
                                x.ProgramId == program.ProgramId &&
                                x.NutritionDate == actionDate)
                            .OrderByDescending(x => x.UpdatedAtUtc)
                            .FirstOrDefaultAsync(cancellationToken);

                    shadowActualWorkout =
                        actualWorkoutAnalysis.Calculate(
                            shadowInput.CurrentWeightLb,
                            shadowActualPlan,
                            shadowResult,
                            completedActualSession,
                            shadowActualWorkoutDefinition,
                            nutritionSoFar,
                            shadowSettings);
                }
            }
            catch (Exception ex)
            {
                shadowActualWorkoutError = ex.Message;
            }
            engine2Diagnostics = new
            {
                actualWorkout = shadowActualWorkout,
                actualWorkoutError = shadowActualWorkoutError,

                mode = "AUTHORITATIVE",

                configVersion =
                    shadowConfiguration.ConfigVersion.VersionCode,

                configStatus =
                    shadowConfiguration.ConfigVersion.StatusCode,

                configurationValid =
                    shadowConfiguration.IsValid,

                decision = new
                {
                    plannedWorkout =
                        shadowResult.Decision.PlannedWorkout.WorkoutTypeCode,

                    recommendedWorkout =
                        shadowResult.Decision.RecommendedWorkout.WorkoutTypeCode,

                    basis =
                        shadowResult.Decision.BasisCode,

                    scheduleChange =
                        shadowResult.Decision.ScheduleChangeRecommended
                },

                trainingPrescription = new
                {
                    mode =
                        shadowResult.TrainingPrescription.PrescriptionModeCode,

                    purpose =
                        shadowResult.TrainingPrescription.TrainingPurpose,

                    targetArea =
                        shadowResult.TrainingPrescription.TargetArea,

                    minutes =
                        shadowResult.TrainingPrescription.RecommendedMinutes,

                    workingSets =
                        shadowResult.TrainingPrescription.ApproximateWorkingSets,

                    rpeMin =
                        shadowResult.TrainingPrescription.TargetRpeMinimum,

                    rpeMax =
                        shadowResult.TrainingPrescription.TargetRpeMaximum,

                    rpeScope =
                        shadowResult.TrainingPrescription.RpeScopeCode
                },

                decisionFuel = new
                {
                    preRescueReadiness =
                        shadowResult.Fuel.PreRescueReadinessCode,

                    preRescueAchievement =
                        shadowResult.Fuel.PreRescueAchievementRatio,

                    rescueAllocationG =
                        shadowResult.Rescue.AllocationG,

                    preWorkoutG =
                        shadowResult.Rescue.PreWorkoutG,

                    projectedReadiness =
                        shadowResult.Rescue.ProjectedReadinessCode,

                    projectedAchievement =
                        shadowResult.Rescue.ProjectedAchievementRatio
                },

                finalFuel = new
                {
                    readiness =
                        shadowResult.FinalFuel.PreRescueReadinessCode,

                    functionalTarget =
                        shadowResult.FinalFuel.FunctionalTargetRatio,

                    recoveryAllocationG =
                        shadowResult.FinalRescue.AllocationG,

                    preWorkoutG =
                        shadowResult.FinalRescue.PreWorkoutG
                },

                nutrition = new
                {
                    calories =
                        shadowResult.Nutrition.RecommendedCalories,

                    proteinG =
                        shadowResult.Nutrition.ProteinG,

                    carbsG =
                        shadowResult.Nutrition.RecommendedCarbsG,

                    fatG =
                        shadowResult.Nutrition.FatG
                },

                guidance = new
                {
                    workout =
                        shadowResult.Guidance.WorkoutScenarioCode,

                    nutrition =
                        shadowResult.Guidance.NutritionScenarioCode,

                    recovery =
                        shadowResult.Guidance.RecoveryScenarioCode,

                    key =
                        shadowResult.Guidance.DailyGuidanceKey
                },

                explanation =
                    shadowResult.Explanation
            };
        }
        catch (Exception ex)
        {
            // Engine 2.0 is authoritative. Capture the diagnostic error, then fail closed below.
            engine2Error =
                $"{ex.GetType().Name}: {ex.Message}";
        }
        if (engine2AuthoritativeResult is null)
        {
            throw new InvalidOperationException(
                $"Engine 2.0 authoritative calculation failed. {engine2Error}");
        }

        if (engine2AuthoritativeConfigVersionId is null)
        {
            throw new InvalidOperationException(
                "Engine 2.0 did not provide an authoritative configuration version.");
        }

        if (engine2AuthoritativeBaseTargets is null)
        {
            throw new InvalidOperationException(
                "Engine 2.0 did not provide authoritative base macro targets.");
        }

        var authoritativeResult = engine2AuthoritativeResult;

        var authoritativeFuelWindow =
            new FuelWindow(
                authoritativeResult.FinalFuel.DemandG,
                authoritativeResult.FinalFuel.RestorationCreditG,
                authoritativeResult.FinalFuel.AbsoluteRestorationRatio,
                authoritativeResult.FinalFuel.ModerateHardDays,
                authoritativeResult.FinalFuel.HardDays);

        var engineVersion =
            configuration["WebRecomp:EngineVersion"] ?? "2000.0";

        var run = new AnalysisRun
        {
            AnalysisRunId = Guid.NewGuid(),
            ClientId = client.ClientId,
            ProgramId = program.ProgramId,
            ActionDate = actionDate,
            InputCutoffAtUtc = DateTime.UtcNow,
            EngineVersion = engineVersion,
            ConfigVersionId = engine2AuthoritativeConfigVersionId.Value,
            RunStatusCode = "SUCCEEDED",
            DataConfidenceCode = DetermineConfidence(authoritativeFuelWindow),
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            DiagnosticJson = JsonSerializer.Serialize(new
            {
                weekly = authoritativeResult.WeeklyStrategy,
                regionRecovery = authoritativeResult.RegionRecovery,
                fuelWindow = authoritativeFuelWindow,

                plannedBaselineWorkout =
                    authoritativeResult.Decision.PlannedWorkout.WorkoutTypeCode,

                recommendedWorkout =
                    authoritativeResult.Decision.RecommendedWorkout.WorkoutTypeCode,

                engine2 = engine2Diagnostics,
                engine2Error = engine2Error
            })
        };

        var recommendation = new Recommendation
        {
            RecommendationId = Guid.NewGuid(),
            AnalysisRunId = run.AnalysisRunId,
            ClientId = client.ClientId,
            ProgramId = program.ProgramId,
            ActionDate = actionDate,

            PlannedWorkoutTypeCode =
                authoritativeResult.Decision.PlannedWorkout.WorkoutTypeCode,

            UserSelectedWorkoutTypeCode = null,

            RecommendedWorkoutTypeCode =
                authoritativeResult.Decision.RecommendedWorkout.WorkoutTypeCode,

            ScheduleChangeRecommended =
                authoritativeResult.Decision.ScheduleChangeRecommended,

            RecommendationBasisCode =
                authoritativeResult.Decision.BasisCode,

            BaseCalories =
                authoritativeResult.Nutrition.BaseCalories,

            RecommendedCalories =
                authoritativeResult.Nutrition.RecommendedCalories,

            RecommendedProteinG =
                authoritativeResult.Nutrition.ProteinG,

            BaseCarbsG =
                authoritativeResult.Nutrition.BaseCarbsG,

            RecoveryAllocationG =
                authoritativeResult.Nutrition.RecoveryAllocationG,

            PreWorkoutPortionG =
                authoritativeResult.Nutrition.PreWorkoutG,

            RecommendedCarbsG =
                authoritativeResult.Nutrition.RecommendedCarbsG,

            RecommendedFatG =
                authoritativeResult.Nutrition.FatG,

            FourDayGlycogenDemandG =
                authoritativeResult.FinalFuel.DemandG,

            FourDayRestorationCreditG =
                authoritativeResult.FinalFuel.RestorationCreditG,

            AbsoluteRestorationRatio =
                authoritativeResult.FinalFuel.AbsoluteRestorationRatio,

            FunctionalTargetRatio =
                authoritativeResult.FinalFuel.FunctionalTargetRatio,

            TargetAchievementRatio =
                authoritativeResult.FinalRescue.ProjectedAchievementRatio,

            GlycogenReadinessCode =
                authoritativeResult.FinalRescue.ProjectedReadinessCode,

            HardSessionCount4d =
                authoritativeResult.FinalFuel.HardDays,

            RegionPriorityCode =
                authoritativeResult.RegionRecovery.RegionPriorityCode,

            CardioNeedCode =
                authoritativeResult.RegionRecovery.CardioNeedCode,

            WeeklyStrategyStatusCode =
                authoritativeResult.WeeklyStrategy.StatusCode,

            WeeklyStrategyNote =
                authoritativeResult.WeeklyStrategy.Note,

            TimingNote =
                authoritativeResult.Nutrition.TimingNote,

            ShortExplanation =
                authoritativeResult.Explanation,

            CreatedAtUtc = DateTime.UtcNow
        };

        var weeklySnapshot = new WeeklyStrategySnapshot
        {
            WeeklyStrategySnapshotId = Guid.NewGuid(),
            AnalysisRunId = run.AnalysisRunId,

            WeekStartDate =
                authoritativeResult.WeeklyStrategy.WeekStart,

            MajorPushCount =
                authoritativeResult.WeeklyStrategy.MajorPushCount,

            MajorPullCount =
                authoritativeResult.WeeklyStrategy.MajorPullCount,

            MajorLegsCount =
                authoritativeResult.WeeklyStrategy.MajorLegsCount,

            LightStrengthCount =
                authoritativeResult.WeeklyStrategy.LightStrengthCount,

            PrimaryConditioningCount =
                authoritativeResult.WeeklyStrategy.PrimaryConditioningCount,

            RecoveryCount =
                authoritativeResult.WeeklyStrategy.RecoveryCount,

            HeavyGenericCount =
                authoritativeResult.WeeklyStrategy.HeavyGenericCount,

            StatusCode =
                authoritativeResult.WeeklyStrategy.StatusCode,

            StrategyNote =
                authoritativeResult.WeeklyStrategy.Note
        };

        db.AnalysisRuns.Add(run);
        db.Recommendations.Add(recommendation);
        db.WeeklyStrategySnapshots.Add(weeklySnapshot);

        short sort = 1;
        foreach (var alert in authoritativeResult.Decision.Alerts)
        {
            db.RecommendationAlerts.Add(new RecommendationAlert
            {
                RecommendationAlertId = Guid.NewGuid(),
                RecommendationId = recommendation.RecommendationId,
                AlertCode = alert.Code,
                SeverityCode = alert.Severity,
                Message = alert.Message,
                SortOrder = sort++
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return new RecommendationResult(
            recommendation,
            authoritativeResult.WeeklyStrategy,
            authoritativeResult.RegionRecovery,
            authoritativeResult.Decision.PlannedWorkout,
            authoritativeResult.Decision.PlannedWorkout,
            authoritativeResult.Decision.RecommendedWorkout,
            engine2AuthoritativeBaseTargets);
    }

    private async Task<FuelWindow> CalculateFuelWindowAsync(
        ProgramRecord program,
        Guid configVersionId,
        DateOnly actionDate,
        IReadOnlyDictionary<string, WorkoutTypeDefinition> workoutTypes,
        IReadOnlyDictionary<string, decimal> settings,
        CancellationToken cancellationToken)
    {
        var start = actionDate.AddDays(-4);
        var end = actionDate.AddDays(-1);

        var sessions = await db.WorkoutSessions
            .Where(x => x.ProgramId == program.ProgramId
                        && x.CompletedFlag
                        && x.WorkoutDate >= start
                        && x.WorkoutDate <= end)
            .ToListAsync(cancellationToken);

        var nutrition = await db.NutritionDays
            .Where(x => x.ProgramId == program.ProgramId
                        && x.NutritionDate >= start
                        && x.NutritionDate <= end)
            .ToDictionaryAsync(x => x.NutritionDate, cancellationToken);

        var backgroundPct = ConfigurationService.Setting(settings, "BACKGROUND_BASE_CARB_CREDIT_PCT", 0.20m);

        decimal totalDemand = 0;
        decimal totalCredit = 0;
        short moderateHard = 0;
        short hard = 0;

        foreach (var date in Enumerable.Range(0, 4).Select(i => start.AddDays(i)))
        {
            var daySessions = sessions.Where(x => x.WorkoutDate == date).ToList();
            var dayDemand = daySessions.Sum(x => x.CalculatedScaledGlycogenBurnG ?? 0m);
            totalDemand += dayDemand;

            var maxFuel = daySessions
                .Select(x =>
                    Engine30LegacyWorkoutCompatibility.TryResolve(
                        workoutTypes,
                        x.WorkoutTypeCode,
                        out var wt)
                            ? wt.FuelScore
                            : 0m)
                .DefaultIfEmpty(0m)
                .Max();

            if (maxFuel >= 2m) moderateHard++;
            if (maxFuel >= 3m) hard++;

            if (dayDemand <= 0 || !nutrition.TryGetValue(date, out var dayNutrition) || !dayNutrition.ActualCarbsG.HasValue)
                continue;

            var carbs = dayNutrition.ActualCarbsG.Value;
            var dayPlanCode = await planSelection.GetPlanCodeForDateAsync(program, date, cancellationToken);
            var dayPlan = await config.GetPlanAsync(configVersionId, dayPlanCode, cancellationToken);
            var credit = Math.Min(
                dayDemand,
                Math.Max(0m, carbs - dayPlan.BaseRestCarbsG)
                + Math.Min(carbs, dayPlan.BaseRestCarbsG) * backgroundPct);

            totalCredit += WorkoutMathService.RoundToIncrement(
                credit,
                ConfigurationService.Setting(settings, "CARB_ROUNDING_G", 5m));
        }

        var ratio = totalDemand <= 0 ? 1m : Math.Min(1m, totalCredit / totalDemand);

        return new FuelWindow(totalDemand, totalCredit, ratio, moderateHard, hard);
    }

    private async Task<WorkoutTypeDefinition?> FindHeavySwapAsync(
        Guid programId,
        DateOnly actionDate,
        WorkoutTypeDefinition current,
        IReadOnlyDictionary<string, WorkoutTypeDefinition> workoutTypes,
        RegionRecovery recovery,
        CancellationToken cancellationToken)
    {
        var weekStart = PlanningWeek.Start(actionDate);
        var weekEnd = weekStart.AddDays(6);

        var future = await db.PlannedWorkouts
            .Where(x => x.ProgramId == programId
                        && x.PlannedDate > actionDate
                        && x.PlannedDate <= weekEnd)
            .OrderBy(x => x.PlannedDate)
            .ToListAsync(cancellationToken);

        foreach (var plan in future)
        {
            if (!Engine30LegacyWorkoutCompatibility.TryResolve(
                    workoutTypes,
                    plan.WorkoutTypeCode,
                    out var type)
                || !type.CountsDedicatedHeavy)
                continue;

            if (string.Equals(type.WorkoutTypeCode, current.WorkoutTypeCode, StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsRegionRecovered(type.RegionCode, recovery))
                return type;
        }

        return null;
    }

    private async Task<WorkoutTypeDefinition?> FindRecoverySwapAsync(
        Guid programId,
        DateOnly actionDate,
        IReadOnlyDictionary<string, WorkoutTypeDefinition> workoutTypes,
        CancellationToken cancellationToken)
    {
        var weekEnd = PlanningWeek.Start(actionDate).AddDays(6);

        var future = await db.PlannedWorkouts
            .Where(x => x.ProgramId == programId
                        && x.PlannedDate > actionDate
                        && x.PlannedDate <= weekEnd)
            .OrderBy(x => x.PlannedDate)
            .ToListAsync(cancellationToken);

        foreach (var item in future)
        {
            if (Engine30LegacyWorkoutCompatibility.TryResolve(
                    workoutTypes,
                    item.WorkoutTypeCode,
                    out var type)
                && (type.CountsRecovery || type.WeeklyRoleCode == "RECOVERY_CARDIO"))
                return type;
        }

        return null;
    }

    private static bool IsRegionRecovered(string? regionCode, RegionRecovery recovery)
        => regionCode?.ToUpperInvariant() switch
        {
            "PUSH" => recovery.PushRecovered,
            "PULL" => recovery.PullRecovered,
            "LEGS" => recovery.LegsRecovered,
            _ => true
        };

    private static WorkoutTypeDefinition Recovery(IReadOnlyDictionary<string, WorkoutTypeDefinition> workoutTypes)
        => workoutTypes.TryGetValue("RECOVERY_0_RPE2", out var type)
            ? type
            : workoutTypes.Values.First(x => x.CountsRecovery);

    private static WorkoutTypeDefinition RecoveryCardioOrRecovery(IReadOnlyDictionary<string, WorkoutTypeDefinition> workoutTypes)
        => workoutTypes.TryGetValue("ZONE_2", out var zone)
            ? zone
            : workoutTypes.TryGetValue("Z2_40_RPE4", out var legacyZone)
                ? legacyZone
                : Recovery(workoutTypes);

    private static WorkoutTypeDefinition FindLightAlternative(
        string? regionCode,
        IReadOnlyDictionary<string, WorkoutTypeDefinition> workoutTypes)
    {
        string[] preferred = regionCode?.ToUpperInvariant() switch
        {
            "LEGS" => ["ACCESSORY_UPPER", "ACCESSORY_MIXED"],
            "PUSH" or "PULL" => ["ACCESSORY_LOWER", "ACCESSORY_MIXED"],
            _ => ["ACCESSORY_MIXED"]
        };

        foreach (var code in preferred)
            if (workoutTypes.TryGetValue(code, out var type))
                return type;

        return workoutTypes.Values.First(x => x.CountsLightStrength);
    }

    private static string DetermineConfidence(FuelWindow fuel)
        => fuel.DemandG <= 0 ? "MODERATE"
            : fuel.RestorationCreditG > 0 ? "MODERATE"
            : "LOW";
}













