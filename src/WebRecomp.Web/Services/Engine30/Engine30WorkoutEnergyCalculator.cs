namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Engine 3.0 Bucket #3 - Workout Activity.
///
/// Estimates workout energy separately from Base Living and
/// Habitual Activity.
///
/// IMPORTANT:
/// Gross workout energy includes energy that would have been spent
/// at rest during the workout period.
///
/// Because Base Living already contains resting energy for the full
/// 24-hour day, Engine 3.0 subtracts resting energy during the
/// workout period before adding workout energy to maintenance.
/// </summary>
public sealed record Engine30WorkoutEnergyInput(
    string WorkoutTypeCode,
    decimal BaseLivingEnergyKcal,
    decimal WeightLb,
    decimal DurationMinutes,

    // Config-owned reference value for this workout type.
    decimal ReferenceWorkoutCaloriesPerMinute,

    // Body weight at which the reference kcal/min was defined.
    decimal ReferenceWeightLb,

    // Planned/default RPE or actual session RPE.
    decimal SessionRpe,

    // Optional resistance-training density inputs.
    // When absent, density factor = 1.00.
    decimal? DefaultRestSeconds = null,
    decimal? ActualRestSeconds = null,
    string ScalingBasisCode = "BODY_WEIGHT",
    decimal? FatFreeMassLb = null,
    decimal? ReferenceFatFreeMassLb = null);

public sealed record Engine30WorkoutEnergyResult(
    string WorkoutTypeCode,

    decimal BaseLivingEnergyKcal,
    decimal WeightLb,
    decimal DurationMinutes,

    decimal ReferenceWorkoutCaloriesPerMinute,
    decimal ReferenceWeightLb,

    string ScalingBasisCode,
    decimal? FatFreeMassLb,
    decimal? ReferenceFatFreeMassLb,

    decimal WeightFactor,
    decimal BodySizeScalingFactor,
    decimal SessionRpe,
    decimal IntensityFactor,
    decimal DensityFactor,

    decimal GrossWorkoutEnergyKcal,
    decimal RestingEnergyDuringWorkoutKcal,
    decimal NetWorkoutEnergyKcal);

public sealed class Engine30WorkoutEnergyCalculator
{
    /*
        Existing Engine design:

        RPE 0  -> .70 minimum
        RPE 5  -> .90
        RPE 7  -> .98
        RPE 8  -> 1.02
        RPE 9  -> 1.06
        RPE 10 -> 1.10

        Upper clamp remains available for future calibrated inputs.
    */
    private const decimal MinimumIntensityFactor = 0.70m;
    private const decimal MaximumIntensityFactor = 1.20m;

    /*
        Resistance-session density adjustment.

        Longer-than-reference rest lowers estimated gross expenditure.
        Shorter-than-reference rest raises it.

        Clamp prevents rest timing from dominating the estimate.
    */
    private const decimal MinimumDensityFactor = 0.75m;
    private const decimal MaximumDensityFactor = 1.25m;

    private const decimal MinutesPerDay = 1440m;

    public Engine30WorkoutEnergyResult Calculate(
        Engine30WorkoutEnergyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        Validate(input);

        var workoutTypeCode =
            input.WorkoutTypeCode
                .Trim()
                .ToUpperInvariant();

        var weightFactor =
            input.WeightLb
            / input.ReferenceWeightLb;

        if (string.IsNullOrWhiteSpace(input.ScalingBasisCode))
        {
            throw new ArgumentException(
                "Workout scaling basis is required.",
                nameof(input.ScalingBasisCode));
        }

        var scalingBasisCode =
            input.ScalingBasisCode
                .Trim()
                .ToUpperInvariant();

        decimal bodySizeScalingFactor;

        if (scalingBasisCode == "BODY_WEIGHT")
        {
            bodySizeScalingFactor =
                weightFactor;
        }
        else if (scalingBasisCode == "FAT_FREE_MASS")
        {
            if (!input.FatFreeMassLb.HasValue ||
                !input.ReferenceFatFreeMassLb.HasValue)
            {
                throw new ArgumentException(
                    "FatFreeMassLb and ReferenceFatFreeMassLb are required " +
                    "when FAT_FREE_MASS scaling is selected.");
            }

            if (input.FatFreeMassLb.Value <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input.FatFreeMassLb),
                    "Fat-free mass must be greater than zero.");
            }

            if (input.ReferenceFatFreeMassLb.Value <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input.ReferenceFatFreeMassLb),
                    "Reference fat-free mass must be greater than zero.");
            }

            bodySizeScalingFactor =
                input.FatFreeMassLb.Value
                / input.ReferenceFatFreeMassLb.Value;
        }
        else
        {
            throw new ArgumentException(
                $"Unsupported workout scaling basis '{input.ScalingBasisCode}'.",
                nameof(input.ScalingBasisCode));
        }

        var intensityFactor =
            Clamp(
                0.70m
                + input.SessionRpe * 0.04m,
                MinimumIntensityFactor,
                MaximumIntensityFactor);

        var densityFactor =
            CalculateDensityFactor(
                input.DefaultRestSeconds,
                input.ActualRestSeconds);

        var grossWorkoutEnergy =
            input.DurationMinutes
            * input.ReferenceWorkoutCaloriesPerMinute
            * bodySizeScalingFactor
            * intensityFactor
            * densityFactor;

        /*
            Base Living already includes resting expenditure for
            every minute of the day.

            Remove the resting amount corresponding to the workout
            duration before adding workout activity to maintenance.
        */
        var restingEnergyPerMinute =
            input.BaseLivingEnergyKcal
            / MinutesPerDay;

        var restingEnergyDuringWorkout =
            restingEnergyPerMinute
            * input.DurationMinutes;

        var netWorkoutEnergy =
            Math.Max(
                0m,
                grossWorkoutEnergy
                - restingEnergyDuringWorkout);

        return new Engine30WorkoutEnergyResult(
            WorkoutTypeCode:
                workoutTypeCode,

            BaseLivingEnergyKcal:
                Round(input.BaseLivingEnergyKcal),

            WeightLb:
                Round(input.WeightLb),

            DurationMinutes:
                Round(input.DurationMinutes),

            ReferenceWorkoutCaloriesPerMinute:
                Round(input.ReferenceWorkoutCaloriesPerMinute),

            ReferenceWeightLb:
                Round(input.ReferenceWeightLb),

            ScalingBasisCode:
                scalingBasisCode,

            FatFreeMassLb:
                input.FatFreeMassLb.HasValue
                    ? Round(input.FatFreeMassLb.Value)
                    : null,

            ReferenceFatFreeMassLb:
                input.ReferenceFatFreeMassLb.HasValue
                    ? Round(input.ReferenceFatFreeMassLb.Value)
                    : null,

            WeightFactor:
                Round(weightFactor, 4),

            BodySizeScalingFactor:
                Round(bodySizeScalingFactor, 4),

            SessionRpe:
                Round(input.SessionRpe, 1),

            IntensityFactor:
                Round(intensityFactor, 4),

            DensityFactor:
                Round(densityFactor, 4),

            GrossWorkoutEnergyKcal:
                Round(grossWorkoutEnergy),

            RestingEnergyDuringWorkoutKcal:
                Round(restingEnergyDuringWorkout),

            NetWorkoutEnergyKcal:
                Round(netWorkoutEnergy));
    }

    private static decimal CalculateDensityFactor(
        decimal? defaultRestSeconds,
        decimal? actualRestSeconds)
    {
        /*
            Cardio/conditioning or sessions without meaningful
            inter-set rest simply use 1.00.
        */
        if (!defaultRestSeconds.HasValue &&
            !actualRestSeconds.HasValue)
        {
            return 1.00m;
        }

        if (!defaultRestSeconds.HasValue ||
            !actualRestSeconds.HasValue)
        {
            throw new ArgumentException(
                "Default and actual rest seconds must either both " +
                "be supplied or both be omitted.");
        }

        if (defaultRestSeconds.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultRestSeconds),
                "Default rest seconds must be greater than zero.");
        }

        if (actualRestSeconds.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(actualRestSeconds),
                "Actual rest seconds must be greater than zero.");
        }

        return Clamp(
            defaultRestSeconds.Value
            / actualRestSeconds.Value,
            MinimumDensityFactor,
            MaximumDensityFactor);
    }

    private static void Validate(
        Engine30WorkoutEnergyInput input)
    {
        if (string.IsNullOrWhiteSpace(
            input.WorkoutTypeCode))
        {
            throw new ArgumentException(
                "Workout type code is required.",
                nameof(input.WorkoutTypeCode));
        }

        if (input.BaseLivingEnergyKcal <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.BaseLivingEnergyKcal),
                "Base Living energy must be greater than zero.");
        }

        if (input.WeightLb <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.WeightLb),
                "Weight must be greater than zero.");
        }

        if (input.DurationMinutes < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.DurationMinutes),
                "Workout duration cannot be negative.");
        }

        if (input.ReferenceWorkoutCaloriesPerMinute < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.ReferenceWorkoutCaloriesPerMinute),
                "Reference workout calories per minute cannot be negative.");
        }

        if (input.ReferenceWeightLb <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.ReferenceWeightLb),
                "Reference weight must be greater than zero.");
        }

        if (input.SessionRpe < 0m ||
            input.SessionRpe > 10m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.SessionRpe),
                "Session RPE must be between 0 and 10.");
        }
    }

    private static decimal Clamp(
        decimal value,
        decimal minimum,
        decimal maximum)
        => Math.Min(
            maximum,
            Math.Max(minimum, value));

    private static decimal Round(
        decimal value,
        int decimals = 2)
        => Math.Round(
            value,
            decimals,
            MidpointRounding.AwayFromZero);
}
