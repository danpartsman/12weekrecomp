using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record RecompAdaptiveNutritionTargets(
    decimal OriginalCalories,
    decimal OriginalProteinG,
    decimal OriginalCarbsG,
    decimal OriginalFatG,

    decimal RecalculatedCalories,
    decimal RecalculatedProteinG,
    decimal RecalculatedCarbsG,
    decimal RecalculatedFatG,

    decimal CaloriesChange,
    decimal ProteinChangeG,
    decimal CarbsChangeG,
    decimal FatChangeG,

    decimal CarryoverAllocationG,

    decimal? ConsumedCalories,
    decimal? ConsumedProteinG,
    decimal? ConsumedCarbsG,
    decimal? ConsumedFatG,

    decimal? CaloriesBalance,
    decimal? ProteinBalanceG,
    decimal? CarbsBalanceG,
    decimal? FatBalanceG,

    string TimingNote);

public sealed record RecompActualWorkoutAnalysis(
    string PlannedWorkoutTypeCode,
    string PlannedWorkoutName,

    string ActualWorkoutTypeCode,
    string ActualWorkoutName,

    decimal ActualDurationMin,
    short? ActualTypicalRestSec,
    decimal? ActualSessionRpe,

    decimal PlannedGlycogenDemandG,
    decimal ActualGlycogenDemandG,
    decimal GlycogenDemandChangeG,

    decimal PlannedWorkoutCalories,
    decimal ActualWorkoutCalories,
    decimal WorkoutCaloriesChange,

    decimal ActualPushLoad,
    decimal ActualPullLoad,
    decimal ActualLegLoad,
    decimal ActualCardioLoad,

    int? EstimatedWorkingSets,

    RecompAdaptiveNutritionTargets Nutrition,

    string Explanation);

public sealed class RecompActualWorkoutAnalysisService(
    WorkoutMathService math,
    RecompTrainingPrescriptionService trainingPrescription)
{
    public RecompActualWorkoutAnalysis Calculate(
        decimal currentWeightLb,
        PlanDefinition plan,
        RecompEngineResult plannedEngineResult,
        WorkoutSession actualSession,
        WorkoutTypeDefinition actualWorkout,
        NutritionDay? nutritionSoFar,
        IReadOnlyDictionary<string, decimal> settings)
    {
        if (!actualSession.CompletedFlag)
        {
            throw new InvalidOperationException(
                "Actual-workout analysis requires a completed workout.");
        }

        if (!string.Equals(
                actualSession.WorkoutTypeCode,
                actualWorkout.WorkoutTypeCode,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "WorkoutSession and WorkoutTypeDefinition do not refer to the same workout type.");
        }

        var plannedWorkout =
            plannedEngineResult.Decision.RecommendedWorkout;

        // -----------------------------------------------------
        // Planned expenditure: what the morning prescription
        // expected to happen.
        // -----------------------------------------------------

        var plannedGlycogen =
            math.CalculatePlannedGlycogenBurn(
                currentWeightLb,
                plannedWorkout,
                settings);

        var plannedWorkoutCalories =
            math.CalculatePlannedWorkoutCalories(
                currentWeightLb,
                plannedWorkout,
                settings);

        // -----------------------------------------------------
        // Actual expenditure: authoritative completed-workout
        // inputs replace planned duration/rest/RPE assumptions.
        // -----------------------------------------------------

        var actualDerived =
            math.CalculateActualWorkout(
                currentWeightLb,
                actualSession,
                actualWorkout,
                settings);

        var estimatedWorkingSets =
            trainingPrescription.EstimateActualWorkingSets(
                actualWorkout,
                actualSession,
                settings);

        // -----------------------------------------------------
        // Rebuild today's workout-specific macro baseline using
        // the ACTUAL workout type.
        //
        // We use this for protein/fat configuration, but replace
        // its default-duration carbohydrate demand with the
        // actual glycogen demand calculated above.
        // -----------------------------------------------------

        var actualWorkoutBaseTargets =
            math.CalculatePlannedTargets(
                currentWeightLb,
                plan,
                actualWorkout,
                settings);

        var carbRounding =
            ConfigurationService.Setting(
                settings,
                "CARB_ROUNDING_G",
                5m);

        var actualWorkoutCarbTarget =
            WorkoutMathService.RoundToIncrement(
                plan.BaseRestCarbsG
                + actualDerived.ScaledGlycogenBurnG
                    * actualWorkout.PlannedRestorationPct,
                carbRounding);

        // Carryover/recovery allocation from the morning Engine
        // result remains a carbohydrate floor. It is historical
        // recovery demand and is not erased merely because the
        // current workout differed from plan.
        var recoveryCarbFloor =
            WorkoutMathService.RoundToIncrement(
                plan.BaseRestCarbsG
                + plannedEngineResult.FinalRescue.AllocationG,
                carbRounding);

        // Avoid double-counting:
        //
        // today's actual workout restoration and historical
        // carryover are alternate carbohydrate floors rather
        // than values blindly added together.
        var recalculatedCarbs =
            WorkoutMathService.RoundToIncrement(
                Math.Max(
                    actualWorkoutCarbTarget,
                    recoveryCarbFloor),
                carbRounding);

        var recalculatedProtein =
            actualWorkoutBaseTargets.ProteinG;

        var recalculatedFat =
            actualWorkoutBaseTargets.FatG;

        var recalculatedCalories =
            Math.Round(
                recalculatedProtein * 4m
                + recalculatedCarbs * 4m
                + recalculatedFat * 9m,
                0,
                MidpointRounding.AwayFromZero);

        var original =
            plannedEngineResult.Nutrition;

        var consumedCalories =
            GetConsumedCalories(nutritionSoFar);

        var consumedProtein =
            nutritionSoFar?.ActualProteinG;

        var consumedCarbs =
            nutritionSoFar?.ActualCarbsG;

        var consumedFat =
            nutritionSoFar?.ActualFatG;

        var adaptiveNutrition =
            new RecompAdaptiveNutritionTargets(
                original.RecommendedCalories,
                original.ProteinG,
                original.RecommendedCarbsG,
                original.FatG,

                recalculatedCalories,
                recalculatedProtein,
                recalculatedCarbs,
                recalculatedFat,

                recalculatedCalories
                    - original.RecommendedCalories,

                recalculatedProtein
                    - original.ProteinG,

                recalculatedCarbs
                    - original.RecommendedCarbsG,

                recalculatedFat
                    - original.FatG,

                plannedEngineResult.FinalRescue.AllocationG,

                consumedCalories,
                consumedProtein,
                consumedCarbs,
                consumedFat,

                Balance(
                    recalculatedCalories,
                    consumedCalories),

                Balance(
                    recalculatedProtein,
                    consumedProtein),

                Balance(
                    recalculatedCarbs,
                    consumedCarbs),

                Balance(
                    recalculatedFat,
                    consumedFat),

                WorkoutMathService.TimingNote(
                    actualWorkout.WeeklyRoleCode));

        var explanation =
            BuildExplanation(
                plannedWorkout,
                actualWorkout,
                actualSession,
                plannedGlycogen,
                actualDerived,
                plannedWorkoutCalories,
                estimatedWorkingSets,
                original,
                adaptiveNutrition);

        return new RecompActualWorkoutAnalysis(
            plannedWorkout.WorkoutTypeCode,
            plannedWorkout.DisplayName,

            actualWorkout.WorkoutTypeCode,
            actualWorkout.DisplayName,

            actualSession.DurationMin,
            actualSession.TypicalRestSec,
            actualSession.SessionRpe,

            plannedGlycogen,
            actualDerived.ScaledGlycogenBurnG,
            actualDerived.ScaledGlycogenBurnG
                - plannedGlycogen,

            plannedWorkoutCalories,
            actualDerived.WorkoutCalories,
            actualDerived.WorkoutCalories
                - plannedWorkoutCalories,

            actualDerived.PushLoad,
            actualDerived.PullLoad,
            actualDerived.LegLoad,
            actualDerived.CardioLoad,

            estimatedWorkingSets,

            adaptiveNutrition,

            explanation);
    }


    private static decimal? GetConsumedCalories(
        NutritionDay? nutrition)
    {
        if (nutrition is null)
            return null;

        if (nutrition.ActualCalories.HasValue)
            return nutrition.ActualCalories.Value;

        if (nutrition.ActualProteinG.HasValue
            && nutrition.ActualCarbsG.HasValue
            && nutrition.ActualFatG.HasValue)
        {
            return Math.Round(
                nutrition.ActualProteinG.Value * 4m
                + nutrition.ActualCarbsG.Value * 4m
                + nutrition.ActualFatG.Value * 9m,
                0,
                MidpointRounding.AwayFromZero);
        }

        return null;
    }


    private static decimal? Balance(
        decimal target,
        decimal? consumed)
    {
        return consumed.HasValue
            ? target - consumed.Value
            : null;
    }


    private static string BuildExplanation(
        WorkoutTypeDefinition plannedWorkout,
        WorkoutTypeDefinition actualWorkout,
        WorkoutSession actualSession,
        decimal plannedGlycogen,
        WorkoutDerived actualDerived,
        decimal plannedWorkoutCalories,
        int? estimatedWorkingSets,
        RecompNutritionTargets original,
        RecompAdaptiveNutritionTargets updated)
    {
        var parts = new List<string>();

        if (string.Equals(
                plannedWorkout.WorkoutTypeCode,
                actualWorkout.WorkoutTypeCode,
                StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(
                $"The planned {plannedWorkout.DisplayName} was completed using the actual recorded duration, rest and Session RPE.");
        }
        else
        {
            parts.Add(
                $"The planned workout was {plannedWorkout.DisplayName}, but the completed workout was {actualWorkout.DisplayName}; expenditure and nutrition were recalculated from the completed workout.");
        }

        parts.Add(
            $"Estimated glycogen demand changed from {plannedGlycogen:0} g planned to {actualDerived.ScaledGlycogenBurnG:0} g actual.");

        parts.Add(
            $"Estimated workout expenditure changed from {plannedWorkoutCalories:0} to {actualDerived.WorkoutCalories:0} calories.");

        if (actualSession.SessionRpe.HasValue)
        {
            parts.Add(
                $"Recorded Session RPE was {actualSession.SessionRpe.Value:0}.");
        }

        if (estimatedWorkingSets.HasValue)
        {
            parts.Add(
                $"Approximately {estimatedWorkingSets.Value} working sets were estimated from actual session duration and rest.");
        }

        if (updated.CarbsChangeG != 0m)
        {
            var direction =
                updated.CarbsChangeG > 0m
                    ? "increased"
                    : "decreased";

            parts.Add(
                $"Today's carbohydrate target {direction} from {original.RecommendedCarbsG:0} g to {updated.RecalculatedCarbsG:0} g.");
        }
        else
        {
            parts.Add(
                $"Today's carbohydrate target remains {updated.RecalculatedCarbsG:0} g.");
        }

        parts.Add(
            "Actual workout calories are reported as expenditure but are not added back to food intake one-for-one; the nutrition adjustment is driven by the macro and glycogen-restoration model.");

        if (updated.CarbsBalanceG.HasValue)
        {
            if (updated.CarbsBalanceG.Value >= 0m)
            {
                parts.Add(
                    $"{updated.CarbsBalanceG.Value:0} g of carbohydrate remains against the recalculated daily target based on currently entered nutrition.");
            }
            else
            {
                parts.Add(
                    $"Current carbohydrate intake is {-updated.CarbsBalanceG.Value:0} g above the recalculated daily target.");
            }
        }

        return string.Join(" ", parts);
    }
}
