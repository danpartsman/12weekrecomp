namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Engine 3.0 resistance-energy workload/density resolver.
///
/// PURPOSE
///
/// The existing workout-energy calculator already accounts for:
/// - duration
/// - RPE
/// - body-size scaling
/// - resting-energy subtraction
///
/// This component resolves the remaining resistance-session workload/density
/// signal without allowing rest time alone to dominate the estimate.
///
/// PRIORITY
///
/// 1. If hard sets are known:
///      use HARD SETS PER MINUTE relative to the configured reference session.
///
///    Duration is already used directly by the energy calculator, so this
///    factor measures training density within that duration rather than
///    multiplying duration a second time.
///
/// 2. If hard sets are unavailable but average rest is known:
///      use rest time only as a lower-confidence fallback density signal.
///
/// 3. If neither is known:
///      return a neutral factor of 1.00.
///
/// Average rest remains available as a diagnostic even when hard-set density
/// is the controlling signal. This avoids double-counting both shorter rest
/// and more sets when they describe the same increase in session density.
///
/// All reference values, sensitivities, and clamps are configuration-owned.
/// This calculator does not contain final scientific calibration values.
/// </summary>

public static class Engine30ResistanceEnergyWorkloadSourceCodes
{
    public const string HardSetDensity = "HARD_SET_DENSITY";
    public const string RestFallback = "REST_FALLBACK";
    public const string Neutral = "NEUTRAL";
}

public sealed record Engine30ResistanceEnergyWorkloadConfig(
    decimal ReferenceDurationMinutes,
    decimal ReferenceHardSets,
    decimal ReferenceRestSeconds,

    /// <summary>
    /// 0 = ignore deviation in hard-set density.
    /// 1 = use the full hard-set-density ratio.
    /// Intermediate values damp the response.
    /// </summary>
    decimal HardSetDensitySensitivity,

    /// <summary>
    /// Used only when hard sets are unavailable.
    /// </summary>
    decimal RestFallbackSensitivity,

    decimal MinimumWorkloadFactor,
    decimal MaximumWorkloadFactor);

public sealed record Engine30ResistanceEnergyWorkloadInput(
    decimal DurationMinutes,
    decimal? TotalHardSets = null,
    decimal? AverageRestSeconds = null);

public sealed record Engine30ResistanceEnergyWorkloadResult(
    string SourceCode,

    decimal WorkloadFactor,

    decimal ReferenceHardSetsPerMinute,
    decimal? ActualHardSetsPerMinute,
    decimal? HardSetDensityRatio,

    decimal ReferenceRestSeconds,
    decimal? ActualRestSeconds,
    decimal? RestDensityRatio,

    decimal HardSetDensitySensitivity,
    decimal RestFallbackSensitivity);

public sealed class Engine30ResistanceEnergyWorkloadCalculator
{
    public Engine30ResistanceEnergyWorkloadResult Calculate(
        Engine30ResistanceEnergyWorkloadInput input,
        Engine30ResistanceEnergyWorkloadConfig config)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(config);

        Validate(
            input,
            config);

        var referenceHardSetsPerMinute =
            config.ReferenceHardSets
            / config.ReferenceDurationMinutes;

        decimal? actualHardSetsPerMinute = null;
        decimal? hardSetDensityRatio = null;
        decimal? restDensityRatio = null;

        if (input.AverageRestSeconds.HasValue)
        {
            restDensityRatio =
                config.ReferenceRestSeconds
                / input.AverageRestSeconds.Value;
        }

        string sourceCode;
        decimal workloadFactor;

        if (input.TotalHardSets.HasValue)
        {
            actualHardSetsPerMinute =
                input.DurationMinutes > 0m
                    ? input.TotalHardSets.Value
                      / input.DurationMinutes
                    : 0m;

            hardSetDensityRatio =
                referenceHardSetsPerMinute > 0m
                    ? actualHardSetsPerMinute.Value
                      / referenceHardSetsPerMinute
                    : 1m;

            workloadFactor =
                1m
                + (hardSetDensityRatio.Value - 1m)
                * config.HardSetDensitySensitivity;

            sourceCode =
                Engine30ResistanceEnergyWorkloadSourceCodes.HardSetDensity;
        }
        else if (input.AverageRestSeconds.HasValue)
        {
            workloadFactor =
                1m
                + (restDensityRatio!.Value - 1m)
                * config.RestFallbackSensitivity;

            sourceCode =
                Engine30ResistanceEnergyWorkloadSourceCodes.RestFallback;
        }
        else
        {
            workloadFactor = 1m;

            sourceCode =
                Engine30ResistanceEnergyWorkloadSourceCodes.Neutral;
        }

        workloadFactor =
            Clamp(
                workloadFactor,
                config.MinimumWorkloadFactor,
                config.MaximumWorkloadFactor);

        return new Engine30ResistanceEnergyWorkloadResult(
            SourceCode:
                sourceCode,

            WorkloadFactor:
                Round(
                    workloadFactor,
                    4),

            ReferenceHardSetsPerMinute:
                Round(
                    referenceHardSetsPerMinute,
                    4),

            ActualHardSetsPerMinute:
                actualHardSetsPerMinute.HasValue
                    ? Round(
                        actualHardSetsPerMinute.Value,
                        4)
                    : null,

            HardSetDensityRatio:
                hardSetDensityRatio.HasValue
                    ? Round(
                        hardSetDensityRatio.Value,
                        4)
                    : null,

            ReferenceRestSeconds:
                Round(
                    config.ReferenceRestSeconds,
                    2),

            ActualRestSeconds:
                input.AverageRestSeconds.HasValue
                    ? Round(
                        input.AverageRestSeconds.Value,
                        2)
                    : null,

            RestDensityRatio:
                restDensityRatio.HasValue
                    ? Round(
                        restDensityRatio.Value,
                        4)
                    : null,

            HardSetDensitySensitivity:
                Round(
                    config.HardSetDensitySensitivity,
                    4),

            RestFallbackSensitivity:
                Round(
                    config.RestFallbackSensitivity,
                    4));
    }

    private static void Validate(
        Engine30ResistanceEnergyWorkloadInput input,
        Engine30ResistanceEnergyWorkloadConfig config)
    {
        if (input.DurationMinutes < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.DurationMinutes),
                "Duration cannot be negative.");
        }

        if (input.TotalHardSets.HasValue &&
            input.TotalHardSets.Value < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.TotalHardSets),
                "Hard sets cannot be negative.");
        }

        if (input.TotalHardSets.HasValue &&
            input.TotalHardSets.Value > 0m &&
            input.DurationMinutes <= 0m)
        {
            throw new ArgumentException(
                "A positive duration is required when hard sets are supplied.");
        }

        if (input.AverageRestSeconds.HasValue &&
            input.AverageRestSeconds.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.AverageRestSeconds),
                "Average rest seconds must be greater than zero.");
        }

        if (config.ReferenceDurationMinutes <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.ReferenceDurationMinutes),
                "Reference duration must be greater than zero.");
        }

        if (config.ReferenceHardSets <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.ReferenceHardSets),
                "Reference hard sets must be greater than zero.");
        }

        if (config.ReferenceRestSeconds <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.ReferenceRestSeconds),
                "Reference rest seconds must be greater than zero.");
        }

        ValidateSensitivity(
            config.HardSetDensitySensitivity,
            nameof(config.HardSetDensitySensitivity));

        ValidateSensitivity(
            config.RestFallbackSensitivity,
            nameof(config.RestFallbackSensitivity));

        if (config.MinimumWorkloadFactor <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.MinimumWorkloadFactor),
                "Minimum workload factor must be greater than zero.");
        }

        if (config.MaximumWorkloadFactor <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.MaximumWorkloadFactor),
                "Maximum workload factor must be greater than zero.");
        }

        if (config.MinimumWorkloadFactor >
            config.MaximumWorkloadFactor)
        {
            throw new ArgumentException(
                "Minimum workload factor cannot exceed maximum workload factor.");
        }

        if (config.MinimumWorkloadFactor > 1m ||
            config.MaximumWorkloadFactor < 1m)
        {
            throw new ArgumentException(
                "The workload-factor bounds must contain the neutral value 1.00.");
        }
    }

    private static void ValidateSensitivity(
        decimal value,
        string parameterName)
    {
        if (value < 0m ||
            value > 1m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Sensitivity must be between 0 and 1.");
        }
    }

    private static decimal Clamp(
        decimal value,
        decimal minimum,
        decimal maximum)
        => Math.Min(
            maximum,
            Math.Max(
                minimum,
                value));

    private static decimal Round(
        decimal value,
        int decimals = 2)
        => Math.Round(
            value,
            decimals,
            MidpointRounding.AwayFromZero);
}