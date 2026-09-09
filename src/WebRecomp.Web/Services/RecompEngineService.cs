using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed class RecompEngineService
{
    private static decimal Parameter(
        IReadOnlyDictionary<string, decimal> settings,
        string key)
    {
        return ConfigurationService.Setting(
            settings,
            key,
            RecompEngineParameterCatalog.Default(key));
    }

    public decimal CalculateFunctionalTarget(
        decimal baseRestorationTarget,
        short moderateHardDays,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var target = Math.Max(0m, baseRestorationTarget);

        if (moderateHardDays >= 4)
        {
            target += Parameter(
                settings,
                RecompEngineParameterCatalog.Hard4TargetBonus);
        }
        else if (moderateHardDays >= 3)
        {
            target += Parameter(
                settings,
                RecompEngineParameterCatalog.Hard3TargetBonus);
        }

        var maximum = Parameter(
            settings,
            RecompEngineParameterCatalog.MaxFunctionalRestorationTarget);

        return Math.Min(maximum, target);
    }

    public string ClassifyReadiness(
        decimal achievement,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var adequate = Parameter(
            settings,
            RecompEngineParameterCatalog.ReadinessAdequate);

        var slightlyReduced = Parameter(
            settings,
            RecompEngineParameterCatalog.ReadinessSlightlyReduced);

        var moderatelyLow = Parameter(
            settings,
            RecompEngineParameterCatalog.ReadinessModeratelyLow);

        var low = Parameter(
            settings,
            RecompEngineParameterCatalog.ReadinessLow);

        ValidateReadinessThresholdOrder(
            adequate,
            slightlyReduced,
            moderatelyLow,
            low);

        if (achievement >= adequate)
            return "ADEQUATE";

        if (achievement >= slightlyReduced)
            return "SLIGHTLY_REDUCED";

        if (achievement >= moderatelyLow)
            return "MODERATELY_LOW";

        if (achievement >= low)
            return "LOW";

        return "VERY_LOW";
    }

    public RecompFuelState BuildFuelState(
        decimal demandG,
        decimal restorationCreditG,
        decimal absoluteRestorationRatio,
        decimal baseFunctionalTargetRatio,
        short moderateHardDays,
        short hardDays,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var functionalTarget = CalculateFunctionalTarget(
            baseFunctionalTargetRatio,
            moderateHardDays,
            settings);

        var achievement = functionalTarget <= 0m
            ? 1m
            : Math.Min(
                1.5m,
                absoluteRestorationRatio / functionalTarget);

        return new RecompFuelState(
            demandG,
            restorationCreditG,
            absoluteRestorationRatio,
            functionalTarget,
            achievement,
            ClassifyReadiness(achievement, settings),
            moderateHardDays,
            hardDays);
    }

    public RecompRescuePlan BuildRescuePlan(
        RecompFuelState fuel,
        decimal fuelScore,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var carbRounding = ConfigurationService.Setting(
            settings,
            "CARB_ROUNDING_G",
            5m);

        var allocationCap = GetAllocationCap(
            fuelScore,
            settings);

        var minimumPreWorkoutFuelScore = Parameter(
            settings,
            RecompEngineParameterCatalog.PreWorkoutMinimumFuelScore);

        var preWorkoutCap = GetPreWorkoutCap(
            fuelScore,
            settings);

        var shortfall = Math.Max(
            0m,
            fuel.DemandG * fuel.FunctionalTargetRatio
            - fuel.RestorationCreditG);

        var allocation = WorkoutMathService.RoundToIncrement(
            Math.Min(shortfall, allocationCap),
            carbRounding);

        var preWorkout =
            fuelScore < minimumPreWorkoutFuelScore
            || allocation <= 0m
                ? 0m
                : WorkoutMathService.RoundToIncrement(
                    Math.Min(allocation, preWorkoutCap),
                    carbRounding);

        var targetDemand =
            fuel.DemandG * fuel.FunctionalTargetRatio;

        // Only carbohydrate available before training is credited
        // when estimating whether today's workout can be rescued.
        var projectedAchievement = targetDemand <= 0m
            ? 1m
            : Math.Min(
                1.5m,
                (fuel.RestorationCreditG + preWorkout)
                / targetDemand);

        var projectedReadiness =
            ClassifyReadiness(
                projectedAchievement,
                settings);

        return new RecompRescuePlan(
            shortfall,
            allocation,
            preWorkout,
            projectedAchievement,
            projectedReadiness,
            preWorkout > 0m);
    }

    public RecompWorkoutDecision BuildWorkoutDecision(
        WorkoutTypeDefinition plannedWorkout,
        RegionRecovery regionRecovery,
        bool painFlag,
        bool illnessFlag,
        RecompFuelState fuel,
        RecompRescuePlan rescue,
        WorkoutTypeDefinition recoveryWorkout,
        WorkoutTypeDefinition? heavySwapWorkout,
        WorkoutTypeDefinition? lightAlternativeWorkout,
        WorkoutTypeDefinition? recoverySwapWorkout,
        WorkoutTypeDefinition? recoveryCardioWorkout)
    {
        var recommended = plannedWorkout;
        var basisCode = "KEEP_PLAN";
        var alerts = new List<RecompEngineAlert>();

        // Protected safety gates always outrank
        // fueling and normal training-plan logic.
        if (painFlag)
        {
            recommended = recoveryWorkout;
            basisCode = "SAFETY_PAIN";

            alerts.Add(new RecompEngineAlert(
                "PAIN_OVERRIDE",
                "STOP",
                "Pain/injury is logged today; recovery is recommended instead of hard training."));
        }
        else if (illnessFlag)
        {
            recommended = recoveryWorkout;
            basisCode = "SAFETY_ILLNESS";

            alerts.Add(new RecompEngineAlert(
                "ILLNESS_OVERRIDE",
                "STOP",
                "Illness/systemic fatigue is logged today; recovery is recommended instead of hard training."));
        }

        // Regional recovery is evaluated before fuel because
        // carbohydrate cannot repair an unrecovered body region.
        else if (plannedWorkout.CountsDedicatedHeavy
                 && !IsRegionRecovered(
                     plannedWorkout.RegionCode,
                     regionRecovery))
        {
            recommended =
                heavySwapWorkout
                ?? lightAlternativeWorkout
                ?? recoveryWorkout;

            basisCode =
                heavySwapWorkout is not null
                    ? "SWAP_HEAVY_DAY"
                    : "REGION_NOT_RECOVERED";
        }

        // Engine 2.0 rescue-before-downgrade rule.
        // Fuel decisions use projected readiness AFTER only the
        // carbohydrate that can actually be consumed pre-workout.
        else if (plannedWorkout.FuelScore >= 3m
                 && IsLowReadiness(
                     rescue.ProjectedReadinessCode))
        {
            recommended =
                recoverySwapWorkout
                ?? recoveryCardioWorkout
                ?? recoveryWorkout;

            basisCode =
                rescue.RescueIsMeaningful
                    ? "LOW_FUEL_AFTER_RESCUE"
                    : "VERY_LOW_FUEL_READINESS";

            alerts.Add(new RecompEngineAlert(
                "FUEL_LIMIT_AFTER_RESCUE",
                "CAUTION",
                rescue.RescueIsMeaningful
                    ? "Fuel readiness remains too low for the selected high-output workout even after the available pre-workout carbohydrate rescue."
                    : "Fuel readiness is too low for the selected high-output workout and no meaningful pre-workout rescue is available."));
        }

        var scheduleChangeRecommended =
            !string.Equals(
                plannedWorkout.WorkoutTypeCode,
                recommended.WorkoutTypeCode,
                StringComparison.OrdinalIgnoreCase);

        return new RecompWorkoutDecision(
            plannedWorkout,
            recommended,
            scheduleChangeRecommended,
            basisCode,
            alerts);
    }


    public RecompTrainingPrescription BuildTrainingPrescription(
        RecompWorkoutDecision decision,
        IReadOnlyDictionary<string, decimal> settings,
        RecompTrainingPrescriptionService trainingPrescription)
    {
        // Prescription always follows the FINAL recommended
        // workout, not necessarily the originally planned one.
        return trainingPrescription.BuildPlanned(
            decision.RecommendedWorkout,
            settings);
    }


    private static bool IsLowReadiness(
        string readinessCode) =>
        string.Equals(
            readinessCode,
            "LOW",
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            readinessCode,
            "VERY_LOW",
            StringComparison.OrdinalIgnoreCase);


    private static bool IsRegionRecovered(
        string? regionCode,
        RegionRecovery recovery)
    {
        return regionCode?.ToUpperInvariant() switch
        {
            "PUSH" => recovery.PushRecovered,
            "PULL" => recovery.PullRecovered,
            "LEGS" or "LOWER" => recovery.LegsRecovered,
            "CARDIO" => recovery.CardioRecovered,
            _ => true
        };
    }


    public RecompGuidanceCodes BuildGuidance(
        WorkoutTypeDefinition plannedWorkout,
        WorkoutTypeDefinition recommendedWorkout,
        string basisCode,
        RecompFuelState fuel,
        RecompRescuePlan rescue,
        RegionRecovery recovery)
    {
        var workoutCode = ClassifyWorkoutScenario(
            plannedWorkout,
            recommendedWorkout,
            basisCode,
            rescue);

        var nutritionCode = ClassifyNutritionScenario(
            recommendedWorkout,
            fuel,
            rescue);

        var recoveryCode = ClassifyRecoveryScenario(
            basisCode,
            fuel);

        var tags = BuildTags(
            plannedWorkout,
            recommendedWorkout,
            basisCode,
            rescue,
            recovery);

        var dailyKey =
            RecompGuidanceTaxonomy.BuildDailyGuidanceKey(
                workoutCode,
                nutritionCode,
                recoveryCode);

        return new RecompGuidanceCodes(
            RecompGuidanceTaxonomy.Version,
            workoutCode,
            nutritionCode,
            recoveryCode,
            dailyKey,
            tags);
    }

    private static decimal GetAllocationCap(
        decimal fuelScore,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var key =
            fuelScore >= 5m
                ? RecompEngineParameterCatalog.AllocationCapFuel5
            : fuelScore >= 4m
                ? RecompEngineParameterCatalog.AllocationCapFuel4
            : fuelScore >= 3m
                ? RecompEngineParameterCatalog.AllocationCapFuel3
            : fuelScore >= 2.5m
                ? RecompEngineParameterCatalog.AllocationCapFuel25
            : fuelScore >= 2m
                ? RecompEngineParameterCatalog.AllocationCapFuel2
            : fuelScore >= 1m
                ? RecompEngineParameterCatalog.AllocationCapFuel1
            : RecompEngineParameterCatalog.AllocationCapDefault;

        return Parameter(settings, key);
    }

    private static decimal GetPreWorkoutCap(
        decimal fuelScore,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var key =
            fuelScore >= 5m
                ? RecompEngineParameterCatalog.PreWorkoutCapFuel5
            : fuelScore >= 4m
                ? RecompEngineParameterCatalog.PreWorkoutCapFuel4
            : fuelScore >= 3m
                ? RecompEngineParameterCatalog.PreWorkoutCapFuel3
            : RecompEngineParameterCatalog.PreWorkoutCapDefault;

        return Parameter(settings, key);
    }

    private static string ClassifyWorkoutScenario(
        WorkoutTypeDefinition plannedWorkout,
        WorkoutTypeDefinition recommendedWorkout,
        string basisCode,
        RecompRescuePlan rescue)
    {
        if (string.Equals(
                basisCode,
                "SAFETY_PAIN",
                StringComparison.OrdinalIgnoreCase))
        {
            return RecompGuidanceTaxonomy.Workout.RecoveryPain;
        }

        if (string.Equals(
                basisCode,
                "SAFETY_ILLNESS",
                StringComparison.OrdinalIgnoreCase))
        {
            return RecompGuidanceTaxonomy.Workout.RecoveryIllness;
        }

        if (string.Equals(
                basisCode,
                "SWAP_HEAVY_DAY",
                StringComparison.OrdinalIgnoreCase))
        {
            return RecompGuidanceTaxonomy.Workout.SwapHeavyRegion;
        }

        if (string.Equals(
                basisCode,
                "REGION_NOT_RECOVERED",
                StringComparison.OrdinalIgnoreCase))
        {
            return RecompGuidanceTaxonomy.Workout.LightAlternative;
        }

        if (string.Equals(
                basisCode,
                "VERY_LOW_FUEL_READINESS",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                basisCode,
                "LOW_FUEL_AFTER_RESCUE",
                StringComparison.OrdinalIgnoreCase))
        {
            return RecompGuidanceTaxonomy.Workout.LowFuelDowngrade;
        }

        var sameWorkout = string.Equals(
            plannedWorkout.WorkoutTypeCode,
            recommendedWorkout.WorkoutTypeCode,
            StringComparison.OrdinalIgnoreCase);

        if (sameWorkout && rescue.RescueIsMeaningful)
        {
            return RecompGuidanceTaxonomy.Workout.KeepWithFuelRescue;
        }

        if (!sameWorkout)
        {
            return RecompGuidanceTaxonomy.Workout.RecoveryLoad;
        }

        return RecompGuidanceTaxonomy.Workout.KeepPlan;
    }

    private static string ClassifyNutritionScenario(
        WorkoutTypeDefinition recommendedWorkout,
        RecompFuelState fuel,
        RecompRescuePlan rescue)
    {
        if (rescue.PreWorkoutG > 0m)
        {
            return RecompGuidanceTaxonomy.Nutrition.PreWorkoutRescue;
        }

        if (rescue.AllocationG > 0m)
        {
            return RecompGuidanceTaxonomy.Nutrition.Carryover;
        }

        if (recommendedWorkout.CountsRecovery)
        {
            return RecompGuidanceTaxonomy.Nutrition.RecoveryRefill;
        }

        if (recommendedWorkout.FuelScore >= 4m)
        {
            return RecompGuidanceTaxonomy.Nutrition.HighDemand;
        }

        if (recommendedWorkout.FuelScore <= 1m)
        {
            return RecompGuidanceTaxonomy.Nutrition.LowDemand;
        }

        return RecompGuidanceTaxonomy.Nutrition.Base;
    }

    private static string ClassifyRecoveryScenario(
        string basisCode,
        RecompFuelState fuel)
    {
        if (string.Equals(
                basisCode,
                "SAFETY_PAIN",
                StringComparison.OrdinalIgnoreCase))
        {
            return RecompGuidanceTaxonomy.Recovery.PainSafety;
        }

        if (string.Equals(
                basisCode,
                "SAFETY_ILLNESS",
                StringComparison.OrdinalIgnoreCase))
        {
            return RecompGuidanceTaxonomy.Recovery.IllnessSafety;
        }

        if (string.Equals(
                basisCode,
                "REGION_NOT_RECOVERED",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                basisCode,
                "SWAP_HEAVY_DAY",
                StringComparison.OrdinalIgnoreCase))
        {
            return RecompGuidanceTaxonomy.Recovery.RegionNotRecovered;
        }

        if (string.Equals(
                fuel.PreRescueReadinessCode,
                "VERY_LOW",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                fuel.PreRescueReadinessCode,
                "LOW",
                StringComparison.OrdinalIgnoreCase))
        {
            return RecompGuidanceTaxonomy.Recovery.FuelLimited;
        }

        if (fuel.HardDays >= 3)
        {
            return RecompGuidanceTaxonomy.Recovery.HighRecoveryNeed;
        }

        if (fuel.AbsoluteRestorationRatio < 1.00m)
        {
            return RecompGuidanceTaxonomy.Recovery.PartialFuelRecovery;
        }

        return RecompGuidanceTaxonomy.Recovery.Ready;
    }

    private static IReadOnlyList<string> BuildTags(
        WorkoutTypeDefinition plannedWorkout,
        WorkoutTypeDefinition recommendedWorkout,
        string basisCode,
        RecompRescuePlan rescue,
        RegionRecovery recovery)
    {
        var tags = new List<string>();

        if (recommendedWorkout.CountsDedicatedHeavy)
        {
            tags.Add(RecompGuidanceTaxonomy.Tags.HeavyDay);
        }
        else if (recommendedWorkout.CountsLightStrength)
        {
            tags.Add(RecompGuidanceTaxonomy.Tags.LightStrengthDay);
        }
        else if (recommendedWorkout.CountsPrimaryConditioning)
        {
            tags.Add(RecompGuidanceTaxonomy.Tags.ConditioningDay);
        }
        else if (recommendedWorkout.CountsRecovery)
        {
            tags.Add(RecompGuidanceTaxonomy.Tags.RecoveryDay);
        }

        var sameWorkout = string.Equals(
            plannedWorkout.WorkoutTypeCode,
            recommendedWorkout.WorkoutTypeCode,
            StringComparison.OrdinalIgnoreCase);

        tags.Add(
            sameWorkout
                ? RecompGuidanceTaxonomy.Tags.PlanPreserved
                : RecompGuidanceTaxonomy.Tags.PlanChanged);

        if (rescue.RescueIsMeaningful)
        {
            tags.Add(RecompGuidanceTaxonomy.Tags.FuelRescueUsed);
        }

        if (rescue.AllocationG > 0m)
        {
            tags.Add(RecompGuidanceTaxonomy.Tags.CarryoverPresent);
        }

        if (recovery.PushRecovered)
        {
            tags.Add(RecompGuidanceTaxonomy.Tags.PushRecovered);
        }

        if (recovery.PullRecovered)
        {
            tags.Add(RecompGuidanceTaxonomy.Tags.PullRecovered);
        }

        if (recovery.LegsRecovered)
        {
            tags.Add(RecompGuidanceTaxonomy.Tags.LegsRecovered);
        }

        if (recovery.CardioRecovered)
        {
            tags.Add(RecompGuidanceTaxonomy.Tags.CardioRecovered);
        }

        if (string.Equals(
                basisCode,
                "SAFETY_PAIN",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                basisCode,
                "SAFETY_ILLNESS",
                StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(RecompGuidanceTaxonomy.Tags.SafetyOverride);
        }

        return tags;
    }

    private static void ValidateReadinessThresholdOrder(
        decimal adequate,
        decimal slightlyReduced,
        decimal moderatelyLow,
        decimal low)
    {
        if (!(adequate > slightlyReduced
              && slightlyReduced > moderatelyLow
              && moderatelyLow > low))
        {
            throw new InvalidOperationException(
                "Recomp Engine readiness thresholds are invalid. " +
                "Required order: ADEQUATE > SLIGHTLY_REDUCED > MODERATELY_LOW > LOW.");
        }
    }
}
