using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record RecompEngineInputSnapshot(
    DateOnly ActionDate,
    decimal CurrentWeightLb,
    PlanDefinition Plan,
    WorkoutTypeDefinition PlannedWorkout,
    WeeklyStrategyAssessment WeeklyStrategy,
    RegionRecovery RegionRecovery,

    decimal FuelDemandG,
    decimal FuelRestorationCreditG,
    decimal FuelAbsoluteRestorationRatio,
    short ModerateHardDays,
    short HardDays,

    bool PainFlag,
    bool IllnessFlag,

    WorkoutTypeDefinition RecoveryWorkout,
    WorkoutTypeDefinition? HeavySwapWorkout,
    WorkoutTypeDefinition? LightAlternativeWorkout,
    WorkoutTypeDefinition? RecoverySwapWorkout,
    WorkoutTypeDefinition? RecoveryCardioWorkout,

    IReadOnlyDictionary<string, decimal> Settings);

public sealed class RecompEngineOrchestrator(
    RecompEngineService engine,
    WorkoutMathService math,
    RecompTrainingPrescriptionService trainingPrescription)
{
    public RecompEngineResult Calculate(
        RecompEngineInputSnapshot input)
    {
        // -----------------------------------------------------
        // 1. Evaluate fuel readiness for the workout the person
        // actually planned to perform.
        // -----------------------------------------------------

        var decisionFuel = engine.BuildFuelState(
            input.FuelDemandG,
            input.FuelRestorationCreditG,
            input.FuelAbsoluteRestorationRatio,
            input.PlannedWorkout.PlannedRestorationPct,
            input.ModerateHardDays,
            input.HardDays,
            input.Settings);

        // -----------------------------------------------------
        // 2. Attempt a realistic pre-workout carbohydrate rescue
        // BEFORE considering a fuel-driven downgrade.
        // -----------------------------------------------------

        var decisionRescue = engine.BuildRescuePlan(
            decisionFuel,
            input.PlannedWorkout.FuelScore,
            input.Settings);

        // -----------------------------------------------------
        // 3. Safety, regional recovery and projected fuel state
        // determine the final recommended workout.
        // -----------------------------------------------------

        var decision = engine.BuildWorkoutDecision(
            input.PlannedWorkout,
            input.RegionRecovery,
            input.PainFlag,
            input.IllnessFlag,
            decisionFuel,
            decisionRescue,
            input.RecoveryWorkout,
            input.HeavySwapWorkout,
            input.LightAlternativeWorkout,
            input.RecoverySwapWorkout,
            input.RecoveryCardioWorkout);

        // -----------------------------------------------------
        // 4. Training prescription follows the FINAL workout.
        // -----------------------------------------------------

        var prescription =
            trainingPrescription.BuildPlanned(
                decision.RecommendedWorkout,
                input.Settings);

        // -----------------------------------------------------
        // 5. Re-evaluate fueling against the FINAL workout.
        //
        // This may differ from the decision fuel state if the
        // engine swapped or lightened the planned session.
        // -----------------------------------------------------

        var finalFuel = engine.BuildFuelState(
            input.FuelDemandG,
            input.FuelRestorationCreditG,
            input.FuelAbsoluteRestorationRatio,
            decision.RecommendedWorkout.PlannedRestorationPct,
            input.ModerateHardDays,
            input.HardDays,
            input.Settings);

        var finalRescue = engine.BuildRescuePlan(
            finalFuel,
            decision.RecommendedWorkout.FuelScore,
            input.Settings);

        // -----------------------------------------------------
        // 6. Calculate base nutrition for the FINAL workout.
        // -----------------------------------------------------

        var baseTargets = math.CalculatePlannedTargets(
            input.CurrentWeightLb,
            input.Plan,
            decision.RecommendedWorkout,
            input.Settings);

        var carbRounding =
            ConfigurationService.Setting(
                input.Settings,
                "CARB_ROUNDING_G",
                5m);

        // IMPORTANT:
        //
        // CalculatePlannedTargets already includes today's
        // planned workout glycogen restoration:
        //
        // BaseRestCarbs
        // + PlannedBurn * PlannedRestorationPct
        //
        // Therefore carryover/recovery allocation is used as
        // an alternate carbohydrate floor, NOT blindly added
        // to baseTargets.CarbsG.
        var recoveryCarbFloor =
            input.Plan.BaseRestCarbsG
            + finalRescue.AllocationG;

        var recommendedCarbs =
            WorkoutMathService.RoundToIncrement(
                Math.Max(
                    baseTargets.CarbsG,
                    recoveryCarbFloor),
                carbRounding);

        var recommendedCalories =
            Math.Round(
                baseTargets.ProteinG * 4m
                + recommendedCarbs * 4m
                + baseTargets.FatG * 9m,
                0,
                MidpointRounding.AwayFromZero);

        var nutrition =
            new RecompNutritionTargets(
                baseTargets.Calories,
                recommendedCalories,
                baseTargets.ProteinG,
                baseTargets.CarbsG,
                finalRescue.AllocationG,
                finalRescue.PreWorkoutG,
                recommendedCarbs,
                baseTargets.FatG,
                baseTargets.TimingNote);

        // -----------------------------------------------------
        // 7. Guidance currently uses the decision-stage fuel
        // evaluation because that explains WHY the workout
        // was kept or changed.
        // -----------------------------------------------------

        var guidance = engine.BuildGuidance(
            input.PlannedWorkout,
            decision.RecommendedWorkout,
            decision.BasisCode,
            decisionFuel,
            decisionRescue,
            input.RegionRecovery);

        var explanation =
            BuildExplanation(
                input,
                decision,
                prescription,
                decisionFuel,
                decisionRescue,
                finalFuel,
                finalRescue,
                nutrition);

        return new RecompEngineResult(
            input.ActionDate,
            input.WeeklyStrategy,
            input.RegionRecovery,
            decisionFuel,
            decisionRescue,
            decision,
            prescription,
            finalFuel,
            finalRescue,
            nutrition,
            guidance,
            explanation);
    }


    private static string BuildExplanation(
        RecompEngineInputSnapshot input,
        RecompWorkoutDecision decision,
        RecompTrainingPrescription prescription,
        RecompFuelState decisionFuel,
        RecompRescuePlan decisionRescue,
        RecompFuelState finalFuel,
        RecompRescuePlan finalRescue,
        RecompNutritionTargets nutrition)
    {
        var parts = new List<string>();

        if (decision.BasisCode == "SAFETY_PAIN")
        {
            parts.Add(
                "Pain/injury safety status overrides the planned workout.");
        }
        else if (decision.BasisCode == "SAFETY_ILLNESS")
        {
            parts.Add(
                "Illness/systemic-fatigue safety status overrides the planned workout.");
        }
        else if (decision.BasisCode == "SWAP_HEAVY_DAY")
        {
            parts.Add(
                "The planned region is not sufficiently recovered, so another major region is recommended.");
        }
        else if (decision.BasisCode == "REGION_NOT_RECOVERED")
        {
            parts.Add(
                "The planned region is not sufficiently recovered for the planned major session.");
        }
        else if (decision.BasisCode == "LOW_FUEL_AFTER_RESCUE")
        {
            parts.Add(
                $"Fuel readiness remained {decisionRescue.ProjectedReadinessCode} after the available pre-workout rescue.");
        }
        else if (decision.BasisCode == "VERY_LOW_FUEL_READINESS")
        {
            parts.Add(
                "Fuel readiness is too low for the planned high-output workout and a meaningful pre-workout rescue is not available.");
        }
        else if (decisionRescue.RescueIsMeaningful)
        {
            parts.Add(
                $"The planned workout can be preserved with approximately {decisionRescue.PreWorkoutG:0} g of carbohydrate available before training.");
        }
        else
        {
            parts.Add(
                "The planned workout can be preserved.");
        }

        parts.Add(
            $"Recommendation: {decision.RecommendedWorkout.DisplayName}.");

        if (prescription.ApproximateWorkingSets.HasValue)
        {
            parts.Add(
                $"Training dose: approximately {prescription.ApproximateWorkingSets.Value} working sets over {prescription.RecommendedMinutes:0} minutes.");
        }
        else
        {
            parts.Add(
                $"Training dose: approximately {prescription.RecommendedMinutes:0} minutes.");
        }

        if (prescription.TargetRpeMinimum.HasValue
            && prescription.TargetRpeMaximum.HasValue)
        {
            var scope =
                prescription.RpeScopeCode == "WORKING_SET"
                    ? "working-set"
                    : "session";

            parts.Add(
                $"Target {scope} RPE: {prescription.TargetRpeMinimum:0}-{prescription.TargetRpeMaximum:0}.");
        }

        parts.Add(
            $"Fuel readiness before rescue: {decisionFuel.PreRescueReadinessCode}; projected after available pre-workout rescue: {decisionRescue.ProjectedReadinessCode}.");

        if (!string.Equals(
                input.PlannedWorkout.WorkoutTypeCode,
                decision.RecommendedWorkout.WorkoutTypeCode,
                StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(
                $"Final workout fuel state was recalculated for {decision.RecommendedWorkout.DisplayName}: {finalFuel.PreRescueReadinessCode}.");
        }

        if (finalRescue.AllocationG > 0m)
        {
            parts.Add(
                $"Recovery carbohydrate allocation: {finalRescue.AllocationG:0} g, with approximately {finalRescue.PreWorkoutG:0} g targeted before training.");
        }

        parts.Add(
            $"Final nutrition target: {nutrition.RecommendedCalories:0} calories, {nutrition.ProteinG:0} g protein, {nutrition.RecommendedCarbsG:0} g carbohydrate and {nutrition.FatG:0} g fat.");

        parts.Add(
            $"Weekly structure: {input.WeeklyStrategy.StatusCode}. {input.WeeklyStrategy.Note}");

        return string.Join(" ", parts);
    }
}
