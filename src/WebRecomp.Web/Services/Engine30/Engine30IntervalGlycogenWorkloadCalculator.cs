namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Engine 3.0 interval / HIIT glycogen workload calculator.
///
/// PURPOSE
///
/// Continuous cardio and intervals are intentionally modeled separately.
///
/// Continuous cardio:
///     duration × one resolved session-intensity exposure
///
/// Intervals:
///     repeated work exposure + repeated recovery exposure
///
/// This calculator resolves only the interval session's glycogen-depletion
/// fraction. It does NOT calculate calories and it does NOT define the
/// modality's anatomical muscle pool. The existing cardio muscle-profile
/// resolver can supply anatomy/recruitment downstream.
///
/// IMPORTANT GUARDRAILS
///
/// - Workout/monitor calories are never used as a glycogen proxy.
/// - RPE is never converted to %VO2max.
/// - Objective intensity must already be expressed relative to the individual
///   (for example, fraction of personal cycling threshold/FTP).
/// - Work and recovery phases are resolved separately.
/// - All physiological coefficients remain configuration-owned and DRAFT.
/// </summary>

public static class Engine30IntervalGlycogenIntensitySourceCodes
{
    public const string ObjectiveRelative = "OBJECTIVE_RELATIVE";
    public const string StructuredRpe = "STRUCTURED_RPE";
}

public sealed record Engine30IntervalGlycogenWorkloadConfig(
    decimal ReferenceWorkIntervalSeconds,
    decimal ReferenceRecoveryIntervalSeconds,
    int ReferenceRepetitions,
    int ReferenceRecoveryPeriods,

    decimal ReferenceDepletionFraction,

    decimal ReferenceWorkRpe,
    decimal ReferenceRecoveryRpe,

    decimal? ReferenceObjectiveRelativeWorkIntensity,
    decimal? ReferenceObjectiveRelativeRecoveryIntensity,

    decimal WorkIntensitySensitivity,
    decimal RecoveryIntensitySensitivity,

    /// <summary>
    /// Relative physiological contribution of recovery exposure to the
    /// reference interval session. This is NOT a calorie multiplier.
    /// </summary>
    decimal RecoveryContributionWeight,

    decimal MinimumDepletionFraction,
    decimal MaximumDepletionFraction);

public sealed record Engine30IntervalGlycogenWorkloadInput(
    string ModalityCode,

    decimal WorkIntervalSeconds,
    decimal RecoveryIntervalSeconds,

    int Repetitions,
    int RecoveryPeriods,

    decimal WorkRpe,
    decimal RecoveryRpe,

    decimal? ObjectiveRelativeWorkIntensity = null,
    decimal? ObjectiveRelativeRecoveryIntensity = null);

public sealed record Engine30IntervalGlycogenPhaseResult(
    string SourceCode,

    decimal ActualIntensityIndex,
    decimal ReferenceIntensityIndex,

    decimal IntensityRatio,
    decimal IntensityFactor,

    decimal ActualMinutes,
    decimal ReferenceMinutes,

    decimal DurationRatio,
    decimal ExposureFactor);

public sealed record Engine30IntervalGlycogenWorkloadResult(
    string ModalityCode,

    int Repetitions,
    int RecoveryPeriods,

    Engine30IntervalGlycogenPhaseResult WorkPhase,
    Engine30IntervalGlycogenPhaseResult RecoveryPhase,

    decimal CombinedWorkloadFactor,
    decimal ResolvedDepletionFraction);

public sealed class Engine30IntervalGlycogenWorkloadCalculator
{
    public Engine30IntervalGlycogenWorkloadResult Calculate(
        Engine30IntervalGlycogenWorkloadInput input,
        Engine30IntervalGlycogenWorkloadConfig config)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(config);

        Validate(
            input,
            config);

        var modalityCode =
            NormalizeRequiredCode(
                input.ModalityCode,
                nameof(input.ModalityCode));

        /*
            No completed work bouts means no modeled interval glycogen demand.
            Recovery time by itself cannot create an interval workout.
        */
        if (input.Repetitions == 0 ||
            input.WorkIntervalSeconds == 0m)
        {
            return new Engine30IntervalGlycogenWorkloadResult(
                ModalityCode:
                    modalityCode,

                Repetitions:
                    input.Repetitions,

                RecoveryPeriods:
                    input.RecoveryPeriods,

                WorkPhase:
                    ZeroPhase(
                        Engine30IntervalGlycogenIntensitySourceCodes.StructuredRpe),

                RecoveryPhase:
                    ZeroPhase(
                        Engine30IntervalGlycogenIntensitySourceCodes.StructuredRpe),

                CombinedWorkloadFactor:
                    0m,

                ResolvedDepletionFraction:
                    0m);
        }

        var actualWorkMinutes =
            input.WorkIntervalSeconds
            * input.Repetitions
            / 60m;

        var referenceWorkMinutes =
            config.ReferenceWorkIntervalSeconds
            * config.ReferenceRepetitions
            / 60m;

        var actualRecoveryMinutes =
            input.RecoveryIntervalSeconds
            * input.RecoveryPeriods
            / 60m;

        var referenceRecoveryMinutes =
            config.ReferenceRecoveryIntervalSeconds
            * config.ReferenceRecoveryPeriods
            / 60m;

        var work =
            ResolvePhase(
                actualMinutes:
                    actualWorkMinutes,

                referenceMinutes:
                    referenceWorkMinutes,

                actualRpe:
                    input.WorkRpe,

                referenceRpe:
                    config.ReferenceWorkRpe,

                actualObjectiveRelativeIntensity:
                    input.ObjectiveRelativeWorkIntensity,

                referenceObjectiveRelativeIntensity:
                    config.ReferenceObjectiveRelativeWorkIntensity,

                sensitivity:
                    config.WorkIntensitySensitivity);

        var recovery =
            ResolvePhase(
                actualMinutes:
                    actualRecoveryMinutes,

                referenceMinutes:
                    referenceRecoveryMinutes,

                actualRpe:
                    input.RecoveryRpe,

                referenceRpe:
                    config.ReferenceRecoveryRpe,

                actualObjectiveRelativeIntensity:
                    input.ObjectiveRelativeRecoveryIntensity,

                referenceObjectiveRelativeIntensity:
                    config.ReferenceObjectiveRelativeRecoveryIntensity,

                sensitivity:
                    config.RecoveryIntensitySensitivity);

        /*
            Normalize the weighted actual exposure against the weighted
            REFERENCE exposure.

            Because each phase's ExposureFactor is 1.00 at its own reference
            condition, the denominator (1 + recovery weight) guarantees that
            the complete reference interval protocol resolves to workload 1.00.
        */
        var weightedExposure =
            work.ExposureFactor
            + config.RecoveryContributionWeight
              * recovery.ExposureFactor;

        var referenceWeightedExposure =
            1m
            + config.RecoveryContributionWeight;

        var combinedWorkloadFactor =
            weightedExposure
            / referenceWeightedExposure;

        combinedWorkloadFactor =
            Math.Max(
                0m,
                combinedWorkloadFactor);

        var depletionFraction =
            config.ReferenceDepletionFraction
            * combinedWorkloadFactor;

        depletionFraction =
            Clamp(
                depletionFraction,
                config.MinimumDepletionFraction,
                config.MaximumDepletionFraction);

        return new Engine30IntervalGlycogenWorkloadResult(
            ModalityCode:
                modalityCode,

            Repetitions:
                input.Repetitions,

            RecoveryPeriods:
                input.RecoveryPeriods,

            WorkPhase:
                work,

            RecoveryPhase:
                recovery,

            CombinedWorkloadFactor:
                Round(
                    combinedWorkloadFactor,
                    4),

            ResolvedDepletionFraction:
                Round(
                    depletionFraction,
                    4));
    }

    private static Engine30IntervalGlycogenPhaseResult ResolvePhase(
        decimal actualMinutes,
        decimal referenceMinutes,
        decimal actualRpe,
        decimal referenceRpe,
        decimal? actualObjectiveRelativeIntensity,
        decimal? referenceObjectiveRelativeIntensity,
        decimal sensitivity)
    {
        string sourceCode;
        decimal actualIntensityIndex;
        decimal referenceIntensityIndex;

        /*
            Objective-relative evidence wins only when both the actual and
            reference values exist for THIS PHASE.

            Raw watts, speed, pace, or grade must be normalized upstream
            against the individual's own reference before entering here.
        */
        if (actualObjectiveRelativeIntensity.HasValue &&
            referenceObjectiveRelativeIntensity.HasValue)
        {
            sourceCode =
                Engine30IntervalGlycogenIntensitySourceCodes.ObjectiveRelative;

            actualIntensityIndex =
                actualObjectiveRelativeIntensity.Value;

            referenceIntensityIndex =
                referenceObjectiveRelativeIntensity.Value;
        }
        else
        {
            sourceCode =
                Engine30IntervalGlycogenIntensitySourceCodes.StructuredRpe;

            actualIntensityIndex =
                actualRpe;

            referenceIntensityIndex =
                referenceRpe;
        }

        var intensityRatio =
            referenceIntensityIndex == 0m
                ? 0m
                : actualIntensityIndex
                  / referenceIntensityIndex;

        var intensityFactor =
            1m
            + (intensityRatio - 1m)
              * sensitivity;

        intensityFactor =
            Math.Max(
                0m,
                intensityFactor);

        decimal durationRatio;

        if (referenceMinutes == 0m)
        {
            durationRatio =
                actualMinutes == 0m
                    ? 1m
                    : 0m;
        }
        else
        {
            durationRatio =
                actualMinutes
                / referenceMinutes;
        }

        var exposureFactor =
            durationRatio
            * intensityFactor;

        return new Engine30IntervalGlycogenPhaseResult(
            SourceCode:
                sourceCode,

            ActualIntensityIndex:
                Round(
                    actualIntensityIndex,
                    4),

            ReferenceIntensityIndex:
                Round(
                    referenceIntensityIndex,
                    4),

            IntensityRatio:
                Round(
                    intensityRatio,
                    4),

            IntensityFactor:
                Round(
                    intensityFactor,
                    4),

            ActualMinutes:
                Round(
                    actualMinutes,
                    2),

            ReferenceMinutes:
                Round(
                    referenceMinutes,
                    2),

            DurationRatio:
                Round(
                    durationRatio,
                    4),

            ExposureFactor:
                Round(
                    exposureFactor,
                    4));
    }

    private static Engine30IntervalGlycogenPhaseResult ZeroPhase(
        string sourceCode)
        => new(
            SourceCode:
                sourceCode,

            ActualIntensityIndex:
                0m,

            ReferenceIntensityIndex:
                0m,

            IntensityRatio:
                0m,

            IntensityFactor:
                0m,

            ActualMinutes:
                0m,

            ReferenceMinutes:
                0m,

            DurationRatio:
                0m,

            ExposureFactor:
                0m);

    private static void Validate(
        Engine30IntervalGlycogenWorkloadInput input,
        Engine30IntervalGlycogenWorkloadConfig config)
    {
        NormalizeRequiredCode(
            input.ModalityCode,
            nameof(input.ModalityCode));

        ValidatePositive(
            config.ReferenceWorkIntervalSeconds,
            nameof(config.ReferenceWorkIntervalSeconds));

        ValidateNonNegative(
            config.ReferenceRecoveryIntervalSeconds,
            nameof(config.ReferenceRecoveryIntervalSeconds));

        if (config.ReferenceRepetitions <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.ReferenceRepetitions));
        }

        if (config.ReferenceRecoveryPeriods < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.ReferenceRecoveryPeriods));
        }

        ValidateFraction(
            config.ReferenceDepletionFraction,
            nameof(config.ReferenceDepletionFraction),
            allowZero:
                false);

        ValidateRpeReference(
            config.ReferenceWorkRpe,
            nameof(config.ReferenceWorkRpe));

        ValidateRpeReference(
            config.ReferenceRecoveryRpe,
            nameof(config.ReferenceRecoveryRpe));

        ValidateOptionalPositive(
            config.ReferenceObjectiveRelativeWorkIntensity,
            nameof(config.ReferenceObjectiveRelativeWorkIntensity));

        ValidateOptionalPositive(
            config.ReferenceObjectiveRelativeRecoveryIntensity,
            nameof(config.ReferenceObjectiveRelativeRecoveryIntensity));

        ValidateNonNegative(
            config.WorkIntensitySensitivity,
            nameof(config.WorkIntensitySensitivity));

        ValidateNonNegative(
            config.RecoveryIntensitySensitivity,
            nameof(config.RecoveryIntensitySensitivity));

        ValidateNonNegative(
            config.RecoveryContributionWeight,
            nameof(config.RecoveryContributionWeight));

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

        if (config.MaximumDepletionFraction
            < config.MinimumDepletionFraction)
        {
            throw new ArgumentException(
                "Maximum depletion fraction must be >= minimum depletion fraction.");
        }

        ValidateNonNegative(
            input.WorkIntervalSeconds,
            nameof(input.WorkIntervalSeconds));

        ValidateNonNegative(
            input.RecoveryIntervalSeconds,
            nameof(input.RecoveryIntervalSeconds));

        if (input.Repetitions < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.Repetitions));
        }

        if (input.RecoveryPeriods < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.RecoveryPeriods));
        }

        ValidateRpeActual(
            input.WorkRpe,
            nameof(input.WorkRpe));

        ValidateRpeActual(
            input.RecoveryRpe,
            nameof(input.RecoveryRpe));

        ValidateOptionalPositive(
            input.ObjectiveRelativeWorkIntensity,
            nameof(input.ObjectiveRelativeWorkIntensity));

        ValidateOptionalPositive(
            input.ObjectiveRelativeRecoveryIntensity,
            nameof(input.ObjectiveRelativeRecoveryIntensity));

        if (config.ReferenceRecoveryPeriods == 0 &&
            config.ReferenceRecoveryIntervalSeconds > 0m)
        {
            throw new ArgumentException(
                "Reference recovery interval seconds were supplied but reference recovery periods are zero.");
        }

        if (input.RecoveryPeriods == 0 &&
            input.RecoveryIntervalSeconds > 0m)
        {
            throw new ArgumentException(
                "Recovery interval seconds were supplied but recovery periods are zero.");
        }
    }

    private static void ValidateRpeReference(
        decimal value,
        string parameterName)
    {
        if (value <= 0m ||
            value > 10m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Reference RPE must be > 0 and <= 10.");
        }
    }

    private static void ValidateRpeActual(
        decimal value,
        string parameterName)
    {
        if (value < 0m ||
            value > 10m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Actual RPE must be between 0 and 10.");
        }
    }

    private static void ValidateOptionalPositive(
        decimal? value,
        string parameterName)
    {
        if (value.HasValue &&
            value.Value <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Relative objective intensity must be > 0 when supplied.");
        }
    }

    private static void ValidatePositive(
        decimal value,
        string parameterName)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName);
        }
    }

    private static void ValidateNonNegative(
        decimal value,
        string parameterName)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName);
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
}