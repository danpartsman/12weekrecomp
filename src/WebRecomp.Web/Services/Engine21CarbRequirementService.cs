using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record Engine21CarbRequirementInput(
    decimal WeightLb,
    PlanDefinition Plan,
    WorkoutTypeDefinition Workout,
    decimal FuelDemandG,
    decimal FuelRestorationCreditG,
    decimal FuelAbsoluteRestorationRatio,
    short ModerateHardDays,
    short HardDays);

public sealed record Engine21CarbRequirementResult(
    decimal PlannedGlycogenBurnG,
    decimal PlannedWorkoutRestorationG,
    decimal BaseWorkoutCarbsG,
    decimal FunctionalTargetRatio,
    string PreRescueReadinessCode,
    decimal RecoveryShortfallG,
    decimal RecoveryAllocationG,
    decimal PreWorkoutG,
    string ProjectedReadinessCode,
    decimal PhysiologicalCarbFloorG,
    IReadOnlyList<string> ReasonCodes);

public sealed class Engine21CarbRequirementService(
    WorkoutMathService workoutMath,
    RecompEngineService recompEngine)
{
    public Engine21CarbRequirementResult Calculate(
        Engine21CarbRequirementInput input,
        IReadOnlyDictionary<string, decimal> settings)
    {
        Validate(input);

        var carbRounding =
            ConfigurationService.Setting(
                settings,
                "CARB_ROUNDING_G",
                5m);

        if (carbRounding <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "CARB_ROUNDING_G must be greater than zero.");

        // Preserve the existing Engine 2.0 glycogen-demand calculation.
        var plannedBurn =
            workoutMath.CalculatePlannedGlycogenBurn(
                input.WeightLb,
                input.Workout,
                settings);

        var plannedWorkoutRestoration =
            WorkoutMathService.RoundToIncrement(
                plannedBurn *
                input.Workout.PlannedRestorationPct,
                carbRounding);

        var baseWorkoutCarbs =
            WorkoutMathService.RoundToIncrement(
                input.Plan.BaseRestCarbsG +
                plannedWorkoutRestoration,
                carbRounding);

        // Reuse Engine 2.0 fuel/readiness logic.
        var fuel =
            recompEngine.BuildFuelState(
                demandG:
                    input.FuelDemandG,

                restorationCreditG:
                    input.FuelRestorationCreditG,

                absoluteRestorationRatio:
                    input.FuelAbsoluteRestorationRatio,

                baseFunctionalTargetRatio:
                    input.Workout.PlannedRestorationPct,

                moderateHardDays:
                    input.ModerateHardDays,

                hardDays:
                    input.HardDays,

                settings:
                    settings);

        var rescue =
            recompEngine.BuildRescuePlan(
                fuel,
                input.Workout.FuelScore,
                settings);

        // Preserve the existing rule that recovery allocation is earmarked
        // inside the normal target when that target already covers it.
        var carryoverFloor =
            WorkoutMathService.RoundToIncrement(
                input.Plan.BaseRestCarbsG +
                rescue.AllocationG,
                carbRounding);

        var physiologicalFloor =
            Math.Max(
                baseWorkoutCarbs,
                carryoverFloor);

        var reasons = new List<string>
        {
            "BASE_REST_CARBOHYDRATE",
            "WORKOUT_GLYCOGEN_RESTORATION"
        };

        if (input.ModerateHardDays >= 3)
        {
            reasons.Add(
                "FUNCTIONAL_TARGET_ELEVATED_FOR_RECENT_LOAD");
        }

        if (rescue.AllocationG > 0m)
        {
            reasons.Add(
                "RECENT_FUEL_SHORTFALL_REQUIRES_ALLOCATION");

            reasons.Add(
                carryoverFloor > baseWorkoutCarbs
                    ? "CARRYOVER_RAISED_PHYSIOLOGICAL_FLOOR"
                    : "CARRYOVER_ALREADY_COVERED_WITHIN_BASE_TARGET");
        }

        if (rescue.PreWorkoutG > 0m)
        {
            reasons.Add(
                "PRE_WORKOUT_CARB_RESCUE_AVAILABLE");
        }

        return new Engine21CarbRequirementResult(
            PlannedGlycogenBurnG:
                plannedBurn,

            PlannedWorkoutRestorationG:
                plannedWorkoutRestoration,

            BaseWorkoutCarbsG:
                baseWorkoutCarbs,

            FunctionalTargetRatio:
                fuel.FunctionalTargetRatio,

            PreRescueReadinessCode:
                fuel.PreRescueReadinessCode,

            RecoveryShortfallG:
                rescue.ShortfallG,

            RecoveryAllocationG:
                rescue.AllocationG,

            PreWorkoutG:
                rescue.PreWorkoutG,

            ProjectedReadinessCode:
                rescue.ProjectedReadinessCode,

            PhysiologicalCarbFloorG:
                physiologicalFloor,

            ReasonCodes:
                reasons);
    }

    private static void Validate(
        Engine21CarbRequirementInput input)
    {
        if (input.WeightLb <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.WeightLb));

        if (input.FuelDemandG < 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.FuelDemandG));

        if (input.FuelRestorationCreditG < 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.FuelRestorationCreditG));

        if (input.FuelAbsoluteRestorationRatio < 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.FuelAbsoluteRestorationRatio));

        if (input.ModerateHardDays < 0)
            throw new ArgumentOutOfRangeException(
                nameof(input.ModerateHardDays));

        if (input.HardDays < 0)
            throw new ArgumentOutOfRangeException(
                nameof(input.HardDays));
    }
}
