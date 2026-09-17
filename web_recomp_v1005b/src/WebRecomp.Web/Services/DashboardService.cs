using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Services;

public sealed class DashboardService(
    RecompDbContext db,
    RecommendationService recommendationService,
    ConfigurationService configuration,
    WorkoutMathService math,
    ProgramPlanService planSelection)
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
        var result = await recommendationService.CalculateAndStoreAsync(context, actionDate, cancellationToken);

        var settings = await configuration.GetSettingsAsync(program.ConfigVersionId, cancellationToken);
        var workoutTypes = await configuration.GetWorkoutTypesAsync(program.ConfigVersionId, cancellationToken);
        var actionPlanCode = await planSelection.GetPlanCodeForDateAsync(program, actionDate, cancellationToken);
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

        WorkoutTypeDefinition todayPlannedType = result.ScheduledWorkout;
        if (todayPlan is not null && workoutTypes.TryGetValue(todayPlan.WorkoutTypeCode, out var explicitPlannedType))
            todayPlannedType = explicitPlannedType;

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

        WorkoutTypeDefinition? tomorrowType = null;
        MacroTargets? tomorrowTargets = null;
        if (tomorrowPlan is not null
            && workoutTypes.TryGetValue(tomorrowPlan.WorkoutTypeCode, out var plannedTomorrowType))
        {
            tomorrowType = plannedTomorrowType;
            if (currentMeasurement?.WeightLb is decimal currentWeight)
            {
                var tomorrowPlanCode = await planSelection.GetPlanCodeForDateAsync(program, tomorrowDate, cancellationToken);
                var tomorrowProgramPlan = await configuration.GetPlanAsync(
                    program.ConfigVersionId,
                    tomorrowPlanCode,
                    cancellationToken);
                tomorrowTargets = math.CalculatePlannedTargets(currentWeight, tomorrowProgramPlan, tomorrowType, settings);
            }
        }

        var previousDate = actionDate.AddDays(-1);
        var previousPlanCode = await planSelection.GetPlanCodeForDateAsync(program, previousDate, cancellationToken);
        var previousProgramPlan = await configuration.GetPlanAsync(
            program.ConfigVersionId,
            previousPlanCode,
            cancellationToken);

        var previousNutrition = await db.NutritionDays
            .SingleOrDefaultAsync(x => x.ClientId == client.ClientId
                                       && x.ProgramId == program.ProgramId
                                       && x.NutritionDate == previousDate,
                                  cancellationToken);

        var previousRecommendation = await db.Recommendations
            .Where(x => x.ClientId == client.ClientId
                        && x.ProgramId == program.ProgramId
                        && x.ActionDate == previousDate)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        decimal? previousTargetCalories = previousRecommendation?.RecommendedCalories;
        decimal? previousTargetProtein = previousRecommendation?.RecommendedProteinG;
        decimal? previousTargetCarbs = previousRecommendation?.RecommendedCarbsG;
        decimal? previousTargetFat = previousRecommendation?.RecommendedFatG;
        var previousTargetSource = previousRecommendation is null ? "Planned base target" : "Recomp recommendation";

        var previousMeasurement = await db.Measurements
            .Where(x => x.ClientId == client.ClientId
                        && !x.IsDeleted
                        && x.LocalMeasurementDate <= previousDate
                        && x.WeightLb != null)
            .OrderByDescending(x => x.LocalMeasurementDate)
            .ThenByDescending(x => x.MeasurementAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (previousRecommendation is null)
        {
            var previousPlan = await db.PlannedWorkouts
                .SingleOrDefaultAsync(x => x.ProgramId == program.ProgramId
                                           && x.PlannedDate == previousDate,
                                      cancellationToken);

            if (previousPlan is not null
                && previousMeasurement?.WeightLb is decimal previousWeight
                && workoutTypes.TryGetValue(previousPlan.WorkoutTypeCode, out var previousPlannedType))
            {
                var previousTargets = math.CalculatePlannedTargets(previousWeight, previousProgramPlan, previousPlannedType, settings);
                previousTargetCalories = previousTargets.Calories;
                previousTargetProtein = previousTargets.ProteinG;
                previousTargetCarbs = previousTargets.CarbsG;
                previousTargetFat = previousTargets.FatG;
            }
        }

        var previousSessions = await db.WorkoutSessions
            .Where(x => x.ClientId == client.ClientId
                        && x.ProgramId == program.ProgramId
                        && x.WorkoutDate == previousDate
                        && x.CompletedFlag)
            .ToListAsync(cancellationToken);

        var previousDemandG = 0m;
        foreach (var session in previousSessions)
        {
            if (session.CalculatedScaledGlycogenBurnG.HasValue)
            {
                previousDemandG += session.CalculatedScaledGlycogenBurnG.Value;
                continue;
            }

            if (previousMeasurement?.WeightLb is decimal previousWeight
                && workoutTypes.TryGetValue(session.WorkoutTypeCode, out var sessionType))
            {
                previousDemandG += math.CalculateActualWorkout(
                    previousWeight,
                    session,
                    sessionType,
                    settings).ScaledGlycogenBurnG;
            }
        }

        previousDemandG = WorkoutMathService.RoundToIncrement(
            previousDemandG,
            ConfigurationService.Setting(settings, "CARB_ROUNDING_G", 5m));

        decimal? previousRestorationCreditG = null;
        decimal? previousRestorationRatio = null;
        if (previousNutrition?.ActualCarbsG is decimal previousActualCarbs && previousDemandG > 0)
        {
            var backgroundPct = ConfigurationService.Setting(settings, "BACKGROUND_BASE_CARB_CREDIT_PCT", 0.20m);
            var credit = Math.Min(
                previousDemandG,
                Math.Max(0m, previousActualCarbs - previousProgramPlan.BaseRestCarbsG)
                + Math.Min(previousActualCarbs, previousProgramPlan.BaseRestCarbsG) * backgroundPct);

            previousRestorationCreditG = WorkoutMathService.RoundToIncrement(
                credit,
                ConfigurationService.Setting(settings, "CARB_ROUNDING_G", 5m));
            previousRestorationRatio = Math.Min(1m, previousRestorationCreditG.Value / previousDemandG);
        }

        var coach = BuildCoachRecommendation(result, todayCheckin);

        return new DashboardViewModel
        {
            ActionDate = actionDate,
            ClientName = client.ClientName,
            ProgramWeek = Math.Max(1, (actionDate.DayNumber - program.StartDate.DayNumber) / 7 + 1),
            PlanCode = actionPlanCode,

            TodayStatusLabel = BuildStatusLabel(result),
            TodayStatusClass = BuildStatusClass(result),

            PlannedWorkout = todayPlan is null ? "No workout planned" : todayPlannedType.DisplayName,
            RecommendedWorkout = result.RecommendedWorkout.DisplayName,
            ScheduleChangeRecommended = result.Recommendation.ScheduleChangeRecommended,
            WorkoutRecommendationLabel = todayPlan is null
                ? $"Use {result.RecommendedWorkout.DisplayName}"
                : result.Recommendation.ScheduleChangeRecommended
                    ? $"Change to {result.RecommendedWorkout.DisplayName}"
                    : $"Keep {todayPlannedType.DisplayName}",
            WorkoutFocus = BuildWorkoutFocus(todayPlannedType),
            WorkoutDescription = BuildWorkoutDescription(todayPlannedType),
            WorkoutDurationMin = todayPlannedType.DefaultMinutes,
            WorkoutEffort = BuildEffortLabel(todayPlannedType),
            WorkoutActual = workoutActual,
            WorkoutRecommendationRationale = BuildWorkoutRecommendationRationale(result, todayPlan is not null),

            Calories = result.Recommendation.RecommendedCalories,
            ProteinG = result.Recommendation.RecommendedProteinG,
            CarbsG = result.Recommendation.RecommendedCarbsG,
            FatG = result.Recommendation.RecommendedFatG,
            NutritionDayLabel = BuildNutritionDayLabel(result.RecommendedWorkout),
            NutritionTip = BuildNutritionTip(result.RecommendedWorkout, actionDate),
            NutritionActualLogged = nutritionActual is not null,
            ActualCalories = nutritionActual?.ActualCalories,
            ActualProteinG = nutritionActual?.ActualProteinG,
            ActualCarbsG = nutritionActual?.ActualCarbsG,
            ActualFatG = nutritionActual?.ActualFatG,

            CoachTitle = coach.Title,
            CoachRecommendation = coach.Body,
            CoachWhy = BuildCoachWhy(result, todayCheckin),
            EnergyScore = todayCheckin?.EnergyScore,
            SorenessScore = todayCheckin?.SorenessScore,
            SleepHours = todayCheckin?.SleepHours,
            PainFlag = todayCheckin?.PainFlag == true,
            IllnessFlag = todayCheckin?.IllnessFlag == true,

            BaseCarbsG = result.Recommendation.BaseCarbsG,
            CarryoverCarbsG = result.Recommendation.RecoveryAllocationG,
            PreWorkoutCarbsG = result.Recommendation.PreWorkoutPortionG,
            FourDayGlycogenDemandG = result.Recommendation.FourDayGlycogenDemandG,
            FourDayRestorationCreditG = result.Recommendation.FourDayRestorationCreditG,
            AbsoluteRestorationRatio = result.Recommendation.AbsoluteRestorationRatio,
            FunctionalTargetRatio = result.Recommendation.FunctionalTargetRatio,
            TargetAchievementRatio = result.Recommendation.TargetAchievementRatio,
            GlycogenReadiness = HumanizeReadiness(result.Recommendation.GlycogenReadinessCode),

            TomorrowDate = tomorrowDate,
            TomorrowPlanAvailable = tomorrowType is not null,
            TomorrowWorkout = tomorrowType?.DisplayName,
            TomorrowFocus = tomorrowType is null ? null : BuildWorkoutFocus(tomorrowType),
            TomorrowDescription = tomorrowType is null ? null : BuildWorkoutDescription(tomorrowType),
            TomorrowDurationMin = tomorrowType?.DefaultMinutes,
            TomorrowEffort = tomorrowType is null ? null : BuildEffortLabel(tomorrowType),
            TomorrowCalories = tomorrowTargets?.Calories,
            TomorrowProteinG = tomorrowTargets?.ProteinG,
            TomorrowCarbsG = tomorrowTargets?.CarbsG,
            TomorrowFatG = tomorrowTargets?.FatG,
            TomorrowTip = BuildTomorrowTip(tomorrowType, tomorrowDate),

            PreviousDate = previousDate,
            PreviousTargetAvailable = previousTargetCalories.HasValue
                                      || previousTargetProtein.HasValue
                                      || previousTargetCarbs.HasValue
                                      || previousTargetFat.HasValue,
            PreviousTargetSource = previousTargetSource,
            PreviousNutritionActualLogged = previousNutrition is not null,
            PreviousTargetCalories = previousTargetCalories,
            PreviousTargetProteinG = previousTargetProtein,
            PreviousTargetCarbsG = previousTargetCarbs,
            PreviousTargetFatG = previousTargetFat,
            PreviousActualCalories = previousNutrition?.ActualCalories,
            PreviousActualProteinG = previousNutrition?.ActualProteinG,
            PreviousActualCarbsG = previousNutrition?.ActualCarbsG,
            PreviousActualFatG = previousNutrition?.ActualFatG,
            PreviousCarbDifferenceG = previousNutrition?.ActualCarbsG is decimal previousActualCarbsForDifference
                                      && previousTargetCarbs is decimal previousTargetCarbsForDifference
                ? previousActualCarbsForDifference - previousTargetCarbsForDifference
                : null,
            PreviousGlycogenDemandG = previousDemandG,
            PreviousRestorationCreditG = previousRestorationCreditG,
            PreviousRestorationRatio = previousRestorationRatio,
            PreviousFuelSummary = BuildPreviousFuelSummary(
                previousNutrition,
                previousDemandG,
                previousRestorationCreditG,
                previousRestorationRatio),

            WeeklyStatus = HumanizeWeeklyStatus(result.WeeklyStrategy.StatusCode),
            WeeklyNote = HumanizeWeeklyNote(result.WeeklyStrategy),
            StrengthPriority = HumanizeStrengthPriority(result.Recommendation.RegionPriorityCode),
            CardioNeed = HumanizeCardioNeed(result.Recommendation.CardioNeedCode)
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
            var displayName = workoutTypes.TryGetValue(x.WorkoutTypeCode, out var type)
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
