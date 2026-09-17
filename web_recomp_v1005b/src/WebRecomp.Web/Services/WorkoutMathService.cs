using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record MacroTargets(
    decimal Calories,
    decimal ProteinG,
    decimal CarbsG,
    decimal FatG,
    decimal ScaledGlycogenBurnG,
    decimal PlannedWorkoutCalories,
    string TimingNote);

public sealed record WorkoutDerived(
    decimal WorkoutCalories,
    decimal ScaledGlycogenBurnG,
    decimal PushLoad,
    decimal PullLoad,
    decimal LegLoad,
    decimal CardioLoad);

public sealed class WorkoutMathService
{
    public MacroTargets CalculatePlannedTargets(
        decimal currentWeightLb,
        PlanDefinition plan,
        WorkoutTypeDefinition workout,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var proteinIncrement = ConfigurationService.Setting(settings, "PROTEIN_ROUNDING_G", 5m);
        var carbIncrement = ConfigurationService.Setting(settings, "CARB_ROUNDING_G", 5m);
        var proteinPerLb = ConfigurationService.Setting(settings, "PROTEIN_PER_LB", 0.88m);
        var proteinMin = ConfigurationService.Setting(settings, "PROTEIN_MIN_G", 175m);
        var proteinMax = ConfigurationService.Setting(settings, "PROTEIN_MAX_G", 195m);
        var minimumFat = ConfigurationService.Setting(settings, "MINIMUM_FAT_G", 35m);

        var baseProtein = RoundToIncrement(
            Math.Clamp(currentWeightLb * proteinPerLb, proteinMin, proteinMax),
            proteinIncrement);

        var protein = RoundToIncrement(baseProtein + workout.ProteinAdjustmentG, proteinIncrement);
        var burn = CalculatePlannedGlycogenBurn(currentWeightLb, workout, settings);

        var carbs = RoundToIncrement(
            plan.BaseRestCarbsG + burn * workout.PlannedRestorationPct,
            carbIncrement);

        var fat = Math.Max(minimumFat, workout.TargetFatG);
        var calories = Math.Round(protein * 4m + carbs * 4m + fat * 9m, 0, MidpointRounding.AwayFromZero);
        var workoutCalories = CalculatePlannedWorkoutCalories(currentWeightLb, workout, settings);

        return new MacroTargets(
            calories,
            protein,
            carbs,
            fat,
            burn,
            workoutCalories,
            TimingNote(workout.WeeklyRoleCode));
    }

    public decimal CalculatePlannedGlycogenBurn(
        decimal currentWeightLb,
        WorkoutTypeDefinition workout,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var referenceWeight = ConfigurationService.Setting(settings, "REFERENCE_WEIGHT_LB", 210m);
        var exponent = ConfigurationService.Setting(settings, "BODY_WEIGHT_GLYCOGEN_EXPONENT", 0.5m);
        var multiplier = ConfigurationService.Setting(settings, "GLYCOGEN_ESTIMATE_MULTIPLIER", 0.65m);
        var rounding = ConfigurationService.Setting(settings, "CARB_ROUNDING_G", 5m);

        var bodyScale = (decimal)Math.Pow((double)(currentWeightLb / referenceWeight), (double)exponent);
        var raw = workout.DefaultMinutes
                  * workout.RawGlycogenGMin
                  * bodyScale
                  * workout.DefaultIntensityFactor
                  * multiplier;

        return RoundToIncrement(Math.Max(0, raw), rounding);
    }

    public decimal CalculatePlannedWorkoutCalories(
        decimal currentWeightLb,
        WorkoutTypeDefinition workout,
        IReadOnlyDictionary<string, decimal> settings)
    {
        if (workout.FuelScore == 0)
            return 0m;

        var referenceWeight = ConfigurationService.Setting(settings, "REFERENCE_WEIGHT_LB", 210m);
        var raw = workout.DefaultMinutes
                  * workout.ReferenceWorkoutCalPerMin
                  * (currentWeightLb / referenceWeight)
                  * workout.DefaultIntensityFactor;

        return RoundToIncrement(Math.Max(0m, raw), 10m);
    }

    public WorkoutDerived CalculateActualWorkout(
        decimal currentWeightLb,
        WorkoutSession session,
        WorkoutTypeDefinition workout,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var referenceWeight = ConfigurationService.Setting(settings, "REFERENCE_WEIGHT_LB", 210m);
        var exponent = ConfigurationService.Setting(settings, "BODY_WEIGHT_GLYCOGEN_EXPONENT", 0.5m);
        var glycogenMultiplier = ConfigurationService.Setting(settings, "GLYCOGEN_ESTIMATE_MULTIPLIER", 0.65m);
        var carbRounding = ConfigurationService.Setting(settings, "CARB_ROUNDING_G", 5m);

        var duration = Math.Max(0m, session.DurationMin);
        var intensity = session.SessionRpe.HasValue
            ? Math.Clamp(0.7m + session.SessionRpe.Value * 0.04m, 0.7m, 1.2m)
            : workout.DefaultIntensityFactor;

        var density = 1m;
        if (workout.FuelScore >= 2m)
        {
            var rest = Math.Max(15m, session.TypicalRestSec ?? workout.DefaultRestSec);
            density = Math.Clamp(workout.DefaultRestSec / rest, 0.75m, 1.25m);
        }

        var bodyScale = (decimal)Math.Pow((double)(currentWeightLb / referenceWeight), (double)exponent);
        var glycogen = duration
                       * workout.RawGlycogenGMin
                       * bodyScale
                       * intensity
                       * density
                       * glycogenMultiplier;

        glycogen = RoundToIncrement(Math.Max(0, glycogen), carbRounding);

        var workoutCalories = workout.FuelScore == 0
            ? 0
            : RoundToIncrement(
                duration
                * workout.ReferenceWorkoutCalPerMin
                * (currentWeightLb / referenceWeight)
                * intensity,
                10m);

        var durationFactor = workout.DefaultMinutes <= 0
            ? 1m
            : Math.Clamp(duration / workout.DefaultMinutes, 0.5m, 1.5m);

        var rpeFactor = session.SessionRpe.HasValue && workout.DefaultIntensityFactor > 0
            ? Math.Clamp(intensity / workout.DefaultIntensityFactor, 0.75m, 1.25m)
            : 1m;

        decimal Adjust(decimal baseLoad) =>
            Math.Round(baseLoad * durationFactor * rpeFactor, 2, MidpointRounding.AwayFromZero);

        return new WorkoutDerived(
            workoutCalories,
            glycogen,
            Adjust(workout.PushLoad),
            Adjust(workout.PullLoad),
            Adjust(workout.LegLoad),
            Adjust(workout.CardioLoad));
    }

    public static decimal RoundToIncrement(decimal value, decimal increment)
    {
        if (increment <= 0)
            return value;

        return Math.Round(value / increment, 0, MidpointRounding.AwayFromZero) * increment;
    }

    public static string TimingNote(string weeklyRoleCode)
    {
        return weeklyRoleCode switch
        {
            "HEAVY" =>
                "Heavy day: protect the planned session. Put any carryover allocation before training. Aim about 25–35% of today's carbs 2–4 h pre-workout and 25–35% within 4 h after.",
            "LIGHT_STRENGTH" =>
                "Light strength: keep protein evenly distributed and place about 20–30% of daily carbs around the session.",
            "CONDITIONING" =>
                "HIIT/cardio: aim about 20–30% of daily carbs in the 2–3 h before training; have a normal protein/carbohydrate meal after.",
            "RECOVERY" or "RECOVERY_CARDIO" =>
                "Recovery day: normal meal timing; use the day to restore glycogen for the next heavy session rather than forcing another hard workout.",
            _ =>
                "Normal meal timing; monitor total workload and tomorrow's planned session."
        };
    }
}