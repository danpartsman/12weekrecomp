namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Engine 3.0 continuous-cardio glycogen workload resolver.
///
/// PURPOSE
///
/// Resolve a session-specific glycogen depletion fraction for continuous
/// cardio without using workout calories as a glycogen proxy.
///
/// Evidence hierarchy:
///
/// 1. Individual-relative objective intensity, when already available.
///    Example: a modality-specific upstream resolver may eventually provide
///    power relative to FTP, speed relative to an individual threshold, etc.
///
/// 2. Personalized/device HR-zone distribution.
///
/// 3. Session RPE as fallback.
///
/// IMPORTANT
///
/// - Raw monitor calories are never used here.
/// - RPE is not converted to %VO2max.
/// - Raw watts / speed / pace are not interpreted without an individual
///   reference benchmark.
/// - Duration and intensity are both legitimate cardio workload dimensions.
/// - All coefficients remain configuration-owned and DRAFT.
/// - Baseline glycogen quantity remains downstream in
///   Engine30GlycogenDemandCalculator.
/// </summary>

public static class Engine30CardioGlycogenIntensitySourceCodes
{
    public const string ObjectiveRelative = "OBJECTIVE_RELATIVE";
    public const string HeartRateZones = "HEART_RATE_ZONES";
    public const string RpeFallback = "RPE_FALLBACK";
}

public sealed record Engine30CardioGlycogenWorkloadConfig(
    decimal ReferenceDurationMinutes,
    decimal ReferenceDepletionFraction,

    /// <summary>
    /// Reference RPE for the modality / training pattern.
    /// Used only when RPE is the selected evidence source.
    /// </summary>
    decimal ReferenceRpe,

    /// <summary>
    /// Optional reference for an upstream individual-relative objective
    /// intensity score. Example: 0.70 could represent 70% of a specific
    /// personal benchmark if that upstream resolver defines it that way.
    /// </summary>
    decimal? ReferenceObjectiveRelativeIntensity,

    /// <summary>
    /// Reference weighted HR-zone index for this modality / pattern.
    /// </summary>
    decimal? ReferenceHeartRateZoneIndex,

    /// <summary>
    /// Sensitivity applied to the ratio between actual and reference
    /// intensity evidence. This does not define a physiological %VO2 mapping.
    /// </summary>
    decimal IntensitySensitivity,

    decimal Zone1Weight,
    decimal Zone2Weight,
    decimal Zone3Weight,
    decimal Zone4Weight,
    decimal Zone5Weight,

    decimal MinimumDepletionFraction,
    decimal MaximumDepletionFraction);

public sealed record Engine30CardioGlycogenWorkloadInput(
    string ModalityCode,
    decimal DurationMinutes,

    /// <summary>
    /// Resolved session RPE. Actual reported RPE should be used when
    /// available; otherwise planned/default RPE.
    /// </summary>
    decimal SessionRpe,

    /// <summary>
    /// Optional upstream individual-relative objective intensity score.
    /// Raw watts, speed, pace, or distance do NOT belong here.
    /// </summary>
    decimal? ObjectiveRelativeIntensity = null,

    /// <summary>
    /// Optional monitor evidence. Only HR-zone minutes are considered.
    /// Reported monitor calories are intentionally ignored.
    /// </summary>
    Engine30WorkoutMonitorData? MonitorData = null);

public sealed record Engine30CardioGlycogenWorkloadResult(
    string ModalityCode,
    decimal DurationMinutes,

    string IntensitySourceCode,
    decimal ActualIntensityIndex,
    decimal ReferenceIntensityIndex,
    decimal IntensityRatio,

    decimal DurationRatio,
    decimal IntensityFactor,
    decimal WorkloadFactor,

    decimal ResolvedDepletionFraction,

    decimal? HeartRateZoneMinutesUsed);

public sealed class Engine30CardioGlycogenWorkloadCalculator
{
    public Engine30CardioGlycogenWorkloadResult Calculate(
        Engine30CardioGlycogenWorkloadInput input,
        Engine30CardioGlycogenWorkloadConfig config)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(config);

        Validate(
            input,
            config);

        var modality =
            NormalizeRequiredCode(
                input.ModalityCode,
                nameof(input.ModalityCode));

        var durationRatio =
            input.DurationMinutes
            / config.ReferenceDurationMinutes;

        var intensity =
            ResolveIntensity(
                input,
                config);

        var intensityRatio =
            intensity.ActualIntensityIndex
            / intensity.ReferenceIntensityIndex;

        var intensityFactor =
            1m
            + (intensityRatio - 1m)
              * config.IntensitySensitivity;

        intensityFactor =
            Math.Max(
                0m,
                intensityFactor);

        /*
            Continuous-cardio workload:

            duration is direct exposure time;
            intensity adjusts the rate of glycogen use relative to the
            modality / training-pattern reference condition.

            This is deliberately different from the resistance pathway,
            where known hard sets already summarize much of the workload and
            elapsed duration is therefore only a fallback.
        */
        var workloadFactor =
            durationRatio
            * intensityFactor;

        var depletionFraction =
            config.ReferenceDepletionFraction
            * workloadFactor;

        if (input.DurationMinutes == 0m)
        {
            depletionFraction = 0m;
        }
        else
        {
            depletionFraction =
                Clamp(
                    depletionFraction,
                    config.MinimumDepletionFraction,
                    config.MaximumDepletionFraction);
        }

        return new Engine30CardioGlycogenWorkloadResult(
            ModalityCode:
                modality,

            DurationMinutes:
                Round(
                    input.DurationMinutes,
                    2),

            IntensitySourceCode:
                intensity.SourceCode,

            ActualIntensityIndex:
                Round(
                    intensity.ActualIntensityIndex,
                    4),

            ReferenceIntensityIndex:
                Round(
                    intensity.ReferenceIntensityIndex,
                    4),

            IntensityRatio:
                Round(
                    intensityRatio,
                    4),

            DurationRatio:
                Round(
                    durationRatio,
                    4),

            IntensityFactor:
                Round(
                    intensityFactor,
                    4),

            WorkloadFactor:
                Round(
                    workloadFactor,
                    4),

            ResolvedDepletionFraction:
                Round(
                    depletionFraction,
                    4),

            HeartRateZoneMinutesUsed:
                intensity.HeartRateZoneMinutesUsed.HasValue
                    ? Round(
                        intensity.HeartRateZoneMinutesUsed.Value,
                        2)
                    : null);
    }

    private static IntensityResolution ResolveIntensity(
        Engine30CardioGlycogenWorkloadInput input,
        Engine30CardioGlycogenWorkloadConfig config)
    {
        if (input.ObjectiveRelativeIntensity.HasValue &&
            config.ReferenceObjectiveRelativeIntensity.HasValue)
        {
            return new IntensityResolution(
                SourceCode:
                    Engine30CardioGlycogenIntensitySourceCodes.ObjectiveRelative,

                ActualIntensityIndex:
                    input.ObjectiveRelativeIntensity.Value,

                ReferenceIntensityIndex:
                    config.ReferenceObjectiveRelativeIntensity.Value,

                HeartRateZoneMinutesUsed:
                    null);
        }

        var zoneResult =
            ResolveHeartRateZoneIndex(
                input.MonitorData,
                config);

        if (zoneResult is not null &&
            config.ReferenceHeartRateZoneIndex.HasValue)
        {
            return new IntensityResolution(
                SourceCode:
                    Engine30CardioGlycogenIntensitySourceCodes.HeartRateZones,

                ActualIntensityIndex:
                    zoneResult.Value.Index,

                ReferenceIntensityIndex:
                    config.ReferenceHeartRateZoneIndex.Value,

                HeartRateZoneMinutesUsed:
                    zoneResult.Value.TotalMinutes);
        }

        return new IntensityResolution(
            SourceCode:
                Engine30CardioGlycogenIntensitySourceCodes.RpeFallback,

            ActualIntensityIndex:
                input.SessionRpe,

            ReferenceIntensityIndex:
                config.ReferenceRpe,

            HeartRateZoneMinutesUsed:
                null);
    }

    private static (decimal Index, decimal TotalMinutes)? ResolveHeartRateZoneIndex(
        Engine30WorkoutMonitorData? monitor,
        Engine30CardioGlycogenWorkloadConfig config)
    {
        if (monitor is null)
        {
            return null;
        }

        var z1 =
            NormalizeZoneMinutes(
                monitor.MinutesZone1);

        var z2 =
            NormalizeZoneMinutes(
                monitor.MinutesZone2);

        var z3 =
            NormalizeZoneMinutes(
                monitor.MinutesZone3);

        var z4 =
            NormalizeZoneMinutes(
                monitor.MinutesZone4);

        var z5 =
            NormalizeZoneMinutes(
                monitor.MinutesZone5);

        var total =
            z1 + z2 + z3 + z4 + z5;

        if (total <= 0m)
        {
            return null;
        }

        var weighted =
            z1 * config.Zone1Weight
            + z2 * config.Zone2Weight
            + z3 * config.Zone3Weight
            + z4 * config.Zone4Weight
            + z5 * config.Zone5Weight;

        return (
            Index:
                weighted / total,

            TotalMinutes:
                total);
    }

    private static decimal NormalizeZoneMinutes(
        decimal? value)
        => value.HasValue
            ? Math.Max(
                0m,
                value.Value)
            : 0m;

    private static void Validate(
        Engine30CardioGlycogenWorkloadInput input,
        Engine30CardioGlycogenWorkloadConfig config)
    {
        NormalizeRequiredCode(
            input.ModalityCode,
            nameof(input.ModalityCode));

        if (input.DurationMinutes < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.DurationMinutes),
                "Duration cannot be negative.");
        }

        if (input.SessionRpe < 0m ||
            input.SessionRpe > 10m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.SessionRpe),
                "Session RPE must be between 0 and 10.");
        }

        if (input.ObjectiveRelativeIntensity.HasValue &&
            input.ObjectiveRelativeIntensity.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.ObjectiveRelativeIntensity),
                "Objective relative intensity must be greater than zero.");
        }

        if (config.ReferenceDurationMinutes <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.ReferenceDurationMinutes),
                "Reference duration must be greater than zero.");
        }

        ValidateFraction(
            config.ReferenceDepletionFraction,
            nameof(config.ReferenceDepletionFraction),
            allowZero:
                false);

        if (config.ReferenceRpe <= 0m ||
            config.ReferenceRpe > 10m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.ReferenceRpe),
                "Reference RPE must be greater than 0 and no more than 10.");
        }

        if (config.ReferenceObjectiveRelativeIntensity.HasValue &&
            config.ReferenceObjectiveRelativeIntensity.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.ReferenceObjectiveRelativeIntensity),
                "Reference objective relative intensity must be greater than zero.");
        }

        if (config.ReferenceHeartRateZoneIndex.HasValue &&
            config.ReferenceHeartRateZoneIndex.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.ReferenceHeartRateZoneIndex),
                "Reference HR-zone index must be greater than zero.");
        }

        if (config.IntensitySensitivity < 0m ||
            config.IntensitySensitivity > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.IntensitySensitivity),
                "Intensity sensitivity must be between 0 and 1.");
        }

        ValidatePositiveWeight(
            config.Zone1Weight,
            nameof(config.Zone1Weight));

        ValidatePositiveWeight(
            config.Zone2Weight,
            nameof(config.Zone2Weight));

        ValidatePositiveWeight(
            config.Zone3Weight,
            nameof(config.Zone3Weight));

        ValidatePositiveWeight(
            config.Zone4Weight,
            nameof(config.Zone4Weight));

        ValidatePositiveWeight(
            config.Zone5Weight,
            nameof(config.Zone5Weight));

        if (!(config.Zone1Weight
              < config.Zone2Weight &&
              config.Zone2Weight
              < config.Zone3Weight &&
              config.Zone3Weight
              < config.Zone4Weight &&
              config.Zone4Weight
              < config.Zone5Weight))
        {
            throw new ArgumentException(
                "HR-zone weights must increase monotonically from Zone 1 through Zone 5.",
                nameof(config));
        }

        ValidateFraction(
            config.MinimumDepletionFraction,
            nameof(config.MinimumDepletionFraction),
            allowZero:
                true);

        ValidateFraction(
            config.MaximumDepletionFraction,
            nameof(config.MaximumDepletionFraction),
            allowZero:
                false);

        if (config.MinimumDepletionFraction >
            config.MaximumDepletionFraction)
        {
            throw new ArgumentException(
                "Minimum depletion fraction cannot exceed maximum depletion fraction.");
        }
    }

    private static void ValidatePositiveWeight(
        decimal value,
        string parameterName)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "HR-zone weight must be greater than zero.");
        }
    }

    private static void ValidateFraction(
        decimal value,
        string parameterName,
        bool allowZero)
    {
        var minimum =
            allowZero
                ? 0m
                : 0.0000001m;

        if (value < minimum ||
            value > 1m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Fraction must be between 0 and 1.");
        }
    }

    private static string NormalizeRequiredCode(
        string code,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException(
                "Code is required.",
                parameterName);
        }

        return code
            .Trim()
            .ToUpperInvariant();
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

    private sealed record IntensityResolution(
        string SourceCode,
        decimal ActualIntensityIndex,
        decimal ReferenceIntensityIndex,
        decimal? HeartRateZoneMinutesUsed);
}