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
    WeeklyPlanService weeklyPlans)
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
        var configVersion = await db.ConfigVersions.SingleAsync(
            x => x.ConfigVersionId == program.ConfigVersionId,
            cancellationToken);

        var settings = await config.GetSettingsAsync(program.ConfigVersionId, cancellationToken);
        var workoutTypes = await config.GetWorkoutTypesAsync(program.ConfigVersionId, cancellationToken);
        var planCode = await planSelection.GetPlanCodeForDateAsync(program, actionDate, cancellationToken);
        var plan = await config.GetPlanAsync(program.ConfigVersionId, planCode, cancellationToken);

        var clientToday = AppUserContextService.GetClientToday(client);
        if (WeeklyStrategyService.WeekStart(actionDate) == WeeklyStrategyService.WeekStart(clientToday))
        {
            await weeklyPlans.EnsureCurrentWeekAsync(client, program, clientToday, cancellationToken);
        }

        var latestMeasurement = await db.Measurements
            .Where(x => x.ClientId == client.ClientId
                        && !x.IsDeleted
                        && x.LocalMeasurementDate <= actionDate
                        && x.WeightLb != null)
            .OrderByDescending(x => x.LocalMeasurementDate)
            .ThenByDescending(x => x.MeasurementAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("A current body weight is required.");

        var weight = latestMeasurement.WeightLb!.Value;

        var planned = await db.PlannedWorkouts
            .SingleOrDefaultAsync(x => x.ProgramId == program.ProgramId && x.PlannedDate == actionDate, cancellationToken);

        var checkin = await db.DailyCheckins
            .SingleOrDefaultAsync(x => x.ProgramId == program.ProgramId && x.CheckinDate == actionDate, cancellationToken);

        // v1002 workflow: Workout Plan is the user's intended workout.
        // Daily check-in/recovery data can cause Recomp to recommend a change,
        // but there is no second independent "intended workout" override.
        var chosenCode = planned?.WorkoutTypeCode;

        if (string.IsNullOrWhiteSpace(chosenCode))
            chosenCode = workoutTypes.ContainsKey("RECOVERY_0_RPE2") ? "RECOVERY_0_RPE2" : workoutTypes.Values.First().WorkoutTypeCode;

        if (!workoutTypes.TryGetValue(chosenCode, out var chosenWorkout))
            throw new InvalidOperationException($"Unknown workout type: {chosenCode}");

        var scheduledWorkout = planned is not null && workoutTypes.TryGetValue(planned.WorkoutTypeCode, out var scheduled)
            ? scheduled
            : chosenWorkout;

        var weekly = await strategy.AssessWeekAsync(
            program.ProgramId,
            program.ConfigVersionId,
            actionDate,
            planCode,
            program.AvailableTrainingDaysWeek,
            cancellationToken);
        var regionRecovery = await strategy.CalculateRegionRecoveryAsync(program.ProgramId, actionDate, settings, cancellationToken);
        var fuelWindow = await CalculateFuelWindowAsync(program, actionDate, workoutTypes, settings, cancellationToken);

        var functionalTarget = chosenWorkout.PlannedRestorationPct;
        if (fuelWindow.ModerateHardDays >= 4) functionalTarget += 0.10m;
        else if (fuelWindow.ModerateHardDays >= 3) functionalTarget += 0.05m;

        functionalTarget = Math.Min(
            ConfigurationService.Setting(settings, "MAX_FUNCTIONAL_RESTORATION_TARGET", 0.90m),
            functionalTarget);

        var achievement = functionalTarget <= 0
            ? 1m
            : Math.Min(1.5m, fuelWindow.AbsoluteRestorationRatio / functionalTarget);

        var readiness = ClassifyReadiness(achievement);

        var recommended = chosenWorkout;
        var scheduleChange = false;
        var basis = "KEEP_PLAN";
        var explanationParts = new List<string>();
        var alerts = new List<(string Code, string Severity, string Message)>();

        if (checkin?.PainFlag == true)
        {
            recommended = Recovery(workoutTypes);
            scheduleChange = true;
            basis = "SAFETY_PAIN";
            explanationParts.Add("Pain/injury flag overrides the planned session.");
            alerts.Add(("PAIN_OVERRIDE", "STOP", "Pain/injury is logged today; use recovery or obtain appropriate clinical guidance before hard training."));
        }
        else if (checkin?.IllnessFlag == true)
        {
            recommended = Recovery(workoutTypes);
            scheduleChange = true;
            basis = "SAFETY_ILLNESS";
            explanationParts.Add("Illness/systemic-fatigue flag overrides the planned session.");
            alerts.Add(("ILLNESS_OVERRIDE", "STOP", "Illness/systemic fatigue is logged today; hard training is not recommended."));
        }
        else if (chosenWorkout.CountsDedicatedHeavy && !IsRegionRecovered(chosenWorkout.RegionCode, regionRecovery))
        {
            var swap = await FindHeavySwapAsync(
                program.ProgramId,
                actionDate,
                chosenWorkout,
                workoutTypes,
                regionRecovery,
                cancellationToken);

            recommended = swap ?? FindLightAlternative(chosenWorkout.RegionCode, workoutTypes);
            scheduleChange = true;
            basis = swap is null ? "REGION_NOT_RECOVERED" : "SWAP_HEAVY_DAY";
            explanationParts.Add(
                swap is null
                    ? $"{chosenWorkout.RegionCode} is not recovered enough for the planned major session."
                    : $"{chosenWorkout.RegionCode} is not recovered; swap with {swap.DisplayName} to preserve the three-heavy-day weekly structure.");
        }
        else if (string.Equals(readiness, "VERY_LOW", StringComparison.OrdinalIgnoreCase)
                 && chosenWorkout.FuelScore >= 3m)
        {
            recommended = await FindRecoverySwapAsync(
                              program.ProgramId, actionDate, workoutTypes, cancellationToken)
                          ?? RecoveryCardioOrRecovery(workoutTypes);

            scheduleChange = true;
            basis = "VERY_LOW_FUEL_READINESS";
            explanationParts.Add("Workout-specific glycogen readiness is very low; use a planned recovery swap rather than forcing the high-output session.");
            alerts.Add(("VERY_LOW_FUEL", "CAUTION", "Fuel readiness is very low for the selected high-output workout."));
        }
        else
        {
            explanationParts.Add($"KEEP PLAN: {chosenWorkout.DisplayName}.");
        }

        // If the recommendation changed, calculate the fuel target against the actual recommendation.
        var finalFunctionalTarget = recommended.PlannedRestorationPct;
        if (fuelWindow.ModerateHardDays >= 4) finalFunctionalTarget += 0.10m;
        else if (fuelWindow.ModerateHardDays >= 3) finalFunctionalTarget += 0.05m;
        finalFunctionalTarget = Math.Min(
            ConfigurationService.Setting(settings, "MAX_FUNCTIONAL_RESTORATION_TARGET", 0.90m),
            finalFunctionalTarget);

        var finalAchievement = finalFunctionalTarget <= 0
            ? 1m
            : Math.Min(1.5m, fuelWindow.AbsoluteRestorationRatio / finalFunctionalTarget);

        var finalReadiness = ClassifyReadiness(finalAchievement);
        var baseTargets = math.CalculatePlannedTargets(weight, plan, recommended, settings);

        var shortfall = Math.Max(0m, fuelWindow.DemandG * finalFunctionalTarget - fuelWindow.RestorationCreditG);
        var allocation = WorkoutMathService.RoundToIncrement(
            Math.Min(shortfall, RecoveryAllocationCap(recommended.FuelScore)),
            ConfigurationService.Setting(settings, "CARB_ROUNDING_G", 5m));

        var preWorkout = recommended.FuelScore < 2m || allocation == 0
            ? 0
            : WorkoutMathService.RoundToIncrement(
                Math.Min(allocation, PreWorkoutCap(recommended.FuelScore)),
                ConfigurationService.Setting(settings, "CARB_ROUNDING_G", 5m));

        var totalCarbs = WorkoutMathService.RoundToIncrement(
            Math.Max(baseTargets.CarbsG, plan.BaseRestCarbsG + allocation),
            ConfigurationService.Setting(settings, "CARB_ROUNDING_G", 5m));

        var totalCalories = Math.Round(
            baseTargets.ProteinG * 4m + totalCarbs * 4m + baseTargets.FatG * 9m,
            0,
            MidpointRounding.AwayFromZero);

        if (fuelWindow.HardDays >= 3)
            explanationParts.Add($"{fuelWindow.HardDays} recent hard sessions are treated as a fueling/recovery warning, not an automatic downgrade.");

        explanationParts.Add($"Weekly structure: {weekly.StatusCode}. {weekly.Note}");

        if (allocation > 0)
            explanationParts.Add($"Carryover carbohydrate allocation: {allocation:0} g; target about {preWorkout:0} g before training.");

        var engineVersion = configuration["WebRecomp:EngineVersion"] ?? "1001.0";

        var run = new AnalysisRun
        {
            AnalysisRunId = Guid.NewGuid(),
            ClientId = client.ClientId,
            ProgramId = program.ProgramId,
            ActionDate = actionDate,
            InputCutoffAtUtc = DateTime.UtcNow,
            EngineVersion = engineVersion,
            ConfigVersionId = configVersion.ConfigVersionId,
            RunStatusCode = "SUCCEEDED",
            DataConfidenceCode = DetermineConfidence(fuelWindow),
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            DiagnosticJson = JsonSerializer.Serialize(new
            {
                weekly,
                regionRecovery,
                fuelWindow,
                plannedBaselineWorkout = chosenWorkout.WorkoutTypeCode,
                recommendedWorkout = recommended.WorkoutTypeCode
            })
        };

        var recommendation = new Recommendation
        {
            RecommendationId = Guid.NewGuid(),
            AnalysisRunId = run.AnalysisRunId,
            ClientId = client.ClientId,
            ProgramId = program.ProgramId,
            ActionDate = actionDate,
            PlannedWorkoutTypeCode = planned?.WorkoutTypeCode,
            UserSelectedWorkoutTypeCode = null,
            RecommendedWorkoutTypeCode = recommended.WorkoutTypeCode,
            ScheduleChangeRecommended = scheduleChange,
            RecommendationBasisCode = basis,

            BaseCalories = baseTargets.Calories,
            RecommendedCalories = totalCalories,
            RecommendedProteinG = baseTargets.ProteinG,
            BaseCarbsG = baseTargets.CarbsG,
            RecoveryAllocationG = allocation,
            PreWorkoutPortionG = preWorkout,
            RecommendedCarbsG = totalCarbs,
            RecommendedFatG = baseTargets.FatG,

            FourDayGlycogenDemandG = fuelWindow.DemandG,
            FourDayRestorationCreditG = fuelWindow.RestorationCreditG,
            AbsoluteRestorationRatio = fuelWindow.AbsoluteRestorationRatio,
            FunctionalTargetRatio = finalFunctionalTarget,
            TargetAchievementRatio = finalAchievement,
            GlycogenReadinessCode = finalReadiness,

            HardSessionCount4d = fuelWindow.HardDays,
            RegionPriorityCode = regionRecovery.RegionPriorityCode,
            CardioNeedCode = regionRecovery.CardioNeedCode,
            WeeklyStrategyStatusCode = weekly.StatusCode,
            WeeklyStrategyNote = weekly.Note,
            TimingNote = AddCarryoverToTiming(baseTargets.TimingNote, allocation, preWorkout),
            ShortExplanation = string.Join(" ", explanationParts),
            CreatedAtUtc = DateTime.UtcNow
        };

        var weeklySnapshot = new WeeklyStrategySnapshot
        {
            WeeklyStrategySnapshotId = Guid.NewGuid(),
            AnalysisRunId = run.AnalysisRunId,
            WeekStartDate = weekly.WeekStart,
            MajorPushCount = weekly.MajorPushCount,
            MajorPullCount = weekly.MajorPullCount,
            MajorLegsCount = weekly.MajorLegsCount,
            LightStrengthCount = weekly.LightStrengthCount,
            PrimaryConditioningCount = weekly.PrimaryConditioningCount,
            RecoveryCount = weekly.RecoveryCount,
            HeavyGenericCount = weekly.HeavyGenericCount,
            StatusCode = weekly.StatusCode,
            StrategyNote = weekly.Note
        };

        db.AnalysisRuns.Add(run);
        db.Recommendations.Add(recommendation);
        db.WeeklyStrategySnapshots.Add(weeklySnapshot);

        short sort = 1;
        foreach (var alert in alerts)
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
            weekly,
            regionRecovery,
            scheduledWorkout,
            chosenWorkout,
            recommended,
            baseTargets);
    }

    private async Task<FuelWindow> CalculateFuelWindowAsync(
        ProgramRecord program,
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
                .Select(x => workoutTypes.TryGetValue(x.WorkoutTypeCode, out var wt) ? wt.FuelScore : 0m)
                .DefaultIfEmpty(0m)
                .Max();

            if (maxFuel >= 2m) moderateHard++;
            if (maxFuel >= 3m) hard++;

            if (dayDemand <= 0 || !nutrition.TryGetValue(date, out var dayNutrition) || !dayNutrition.ActualCarbsG.HasValue)
                continue;

            var carbs = dayNutrition.ActualCarbsG.Value;
            var dayPlanCode = await planSelection.GetPlanCodeForDateAsync(program, date, cancellationToken);
            var dayPlan = await config.GetPlanAsync(program.ConfigVersionId, dayPlanCode, cancellationToken);
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
        var weekStart = WeeklyStrategyService.WeekStart(actionDate);
        var weekEnd = weekStart.AddDays(6);

        var future = await db.PlannedWorkouts
            .Where(x => x.ProgramId == programId
                        && x.PlannedDate > actionDate
                        && x.PlannedDate <= weekEnd)
            .OrderBy(x => x.PlannedDate)
            .ToListAsync(cancellationToken);

        foreach (var plan in future)
        {
            if (!workoutTypes.TryGetValue(plan.WorkoutTypeCode, out var type) || !type.CountsDedicatedHeavy)
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
        var weekEnd = WeeklyStrategyService.WeekStart(actionDate).AddDays(6);

        var future = await db.PlannedWorkouts
            .Where(x => x.ProgramId == programId
                        && x.PlannedDate > actionDate
                        && x.PlannedDate <= weekEnd)
            .OrderBy(x => x.PlannedDate)
            .ToListAsync(cancellationToken);

        foreach (var item in future)
        {
            if (workoutTypes.TryGetValue(item.WorkoutTypeCode, out var type)
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

    private static string ClassifyReadiness(decimal achievement)
    {
        if (achievement >= 1.00m) return "ADEQUATE";
        if (achievement >= 0.85m) return "SLIGHTLY_REDUCED";
        if (achievement >= 0.70m) return "MODERATELY_LOW";
        if (achievement >= 0.50m) return "LOW";
        return "VERY_LOW";
    }

    private static decimal RecoveryAllocationCap(decimal fuelScore)
        => fuelScore >= 5m ? 90m
            : fuelScore >= 4m ? 75m
            : fuelScore >= 3m ? 65m
            : fuelScore >= 2.5m ? 55m
            : fuelScore >= 2m ? 50m
            : fuelScore >= 1m ? 40m
            : 35m;

    private static decimal PreWorkoutCap(decimal fuelScore)
        => fuelScore >= 5m ? 55m
            : fuelScore >= 4m ? 45m
            : fuelScore >= 3m ? 35m
            : 25m;

    private static string AddCarryoverToTiming(string timing, decimal allocation, decimal preWorkout)
    {
        if (allocation <= 0)
            return timing;

        return $"{timing} Carryover allocation: {allocation:0} g; target {preWorkout:0} g before the session.";
    }

    private static string DetermineConfidence(FuelWindow fuel)
        => fuel.DemandG <= 0 ? "MODERATE"
            : fuel.RestorationCreditG > 0 ? "MODERATE"
            : "LOW";
}
