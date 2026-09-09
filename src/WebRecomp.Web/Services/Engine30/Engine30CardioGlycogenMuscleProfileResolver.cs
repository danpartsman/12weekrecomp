namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Engine 3.0 cardio glycogen muscle-profile resolver.
///
/// PURPOSE
///
/// The continuous-cardio workload calculator answers:
///
///     "How much of the reference glycogen pool is depleted?"
///
/// This component answers the separate anatomy/recruitment question:
///
///     "Which skeletal-muscle regions does this modality meaningfully recruit?"
///
/// The separation is intentional:
///
///     cardio intensity + duration
///         -> depletion fraction
///
///     modality profile
///         -> muscle regions + recruitment
///
///     both together
///         -> Engine30MuscleGlycogenInput[]
///
/// No modality-specific anatomical percentages are hard-coded here.
/// They remain configuration-owned so they can be calibrated, versioned,
/// overridden, and later personalized without changing engine structure.
/// </summary>

public static class Engine30CardioMuscleProfileSourceCodes
{
    public const string ConfigDefault = "CONFIG_DEFAULT";
    public const string UserOverride = "USER_OVERRIDE";
    public const string Personalized = "PERSONALIZED";
}

public static class Engine30CardioMuscleProfileConfidenceCodes
{
    public const string Low = "LOW";
    public const string Medium = "MEDIUM";
    public const string High = "HIGH";
}

public sealed record Engine30CardioMuscleRegionProfile(
    string MuscleGroupCode,

    /// <summary>
    /// Fraction of whole-body skeletal muscle represented by this
    /// non-overlapping modeled region.
    /// </summary>
    decimal WholeBodySkeletalMuscleFraction,

    /// <summary>
    /// Effective fraction of this region meaningfully recruited by
    /// the modality under the modeled condition.
    /// </summary>
    decimal RecruitmentFraction);

public sealed record Engine30CardioGlycogenModalityProfile(
    string ProfileCode,
    string ModalityCode,
    string SourceCode,
    string ConfidenceCode,
    IReadOnlyCollection<Engine30CardioMuscleRegionProfile> MuscleRegions);

/// <summary>
/// Generic glycogen-muscle-profile exposure.
/// This deliberately contains only the information the anatomy/recruitment
/// layer needs from an upstream workload model.
///
/// Continuous cardio, intervals/HIIT, and future conditioning models can all
/// use the same downstream muscle-profile logic without pretending they share
/// the same workload calculator.
/// </summary>
public sealed record Engine30GlycogenMuscleProfileExposure(
    string ModalityCode,
    decimal DurationMinutes,
    decimal ResolvedDepletionFraction);

public sealed record Engine30CardioGlycogenMuscleProfileInput(
    Engine30CardioGlycogenWorkloadResult Workload,
    Engine30CardioGlycogenModalityProfile ModalityProfile);

public sealed record Engine30CardioGlycogenMuscleProfileResult(
    string ProfileCode,
    string ModalityCode,
    string SourceCode,
    string ConfidenceCode,

    decimal DurationMinutes,
    decimal ResolvedDepletionFraction,

    IReadOnlyList<Engine30MuscleGlycogenInput> ResolvedMuscleGroups);

public sealed class Engine30CardioGlycogenMuscleProfileResolver
{
    /// <summary>
    /// Backward-compatible continuous-cardio entry point.
    /// Existing callers and tests keep using the continuous-cardio workload
    /// result, while the resolver internally delegates to the generic exposure
    /// contract used by intervals and future conditioning models.
    /// </summary>
    public Engine30CardioGlycogenMuscleProfileResult Resolve(
        Engine30CardioGlycogenMuscleProfileInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Workload);
        ArgumentNullException.ThrowIfNull(input.ModalityProfile);

        return Resolve(
            new Engine30GlycogenMuscleProfileExposure(
                ModalityCode:
                    input.Workload.ModalityCode,

                DurationMinutes:
                    input.Workload.DurationMinutes,

                ResolvedDepletionFraction:
                    input.Workload.ResolvedDepletionFraction),

            input.ModalityProfile);
    }

    /// <summary>
    /// Workload-model-agnostic entry point.
    ///
    /// The caller supplies only:
    /// - modality
    /// - actual modeled exposure duration
    /// - upstream resolved depletion fraction
    ///
    /// No calorie total, RPE, HR value, interval structure, watts, speed, or
    /// resistance-set detail belongs here. Those are resolved upstream by the
    /// appropriate workload calculator.
    /// </summary>
    public Engine30CardioGlycogenMuscleProfileResult Resolve(
        Engine30GlycogenMuscleProfileExposure exposure,
        Engine30CardioGlycogenModalityProfile modalityProfile)
    {
        ArgumentNullException.ThrowIfNull(exposure);
        ArgumentNullException.ThrowIfNull(modalityProfile);
        ArgumentNullException.ThrowIfNull(modalityProfile.MuscleRegions);

        Validate(
            exposure,
            modalityProfile);

        var profileCode =
            NormalizeRequiredCode(
                modalityProfile.ProfileCode,
                nameof(modalityProfile.ProfileCode));

        var modalityCode =
            NormalizeRequiredCode(
                modalityProfile.ModalityCode,
                nameof(modalityProfile.ModalityCode));

        var sourceCode =
            NormalizeSourceCode(
                modalityProfile.SourceCode);

        var confidenceCode =
            NormalizeConfidenceCode(
                modalityProfile.ConfidenceCode);

        /*
            No actual exposure -> no recruited muscle groups are sent
            downstream.

            This prevents diagnostics from reporting positive "recruited kg"
            for zero-duration / zero-depletion work.
        */
        if (exposure.DurationMinutes == 0m ||
            exposure.ResolvedDepletionFraction == 0m)
        {
            return new Engine30CardioGlycogenMuscleProfileResult(
                ProfileCode:
                    profileCode,

                ModalityCode:
                    modalityCode,

                SourceCode:
                    sourceCode,

                ConfidenceCode:
                    confidenceCode,

                DurationMinutes:
                    Round(
                        exposure.DurationMinutes,
                        2),

                ResolvedDepletionFraction:
                    Round(
                        exposure.ResolvedDepletionFraction,
                        4),

                ResolvedMuscleGroups:
                    Array.Empty<Engine30MuscleGlycogenInput>());
        }

        var resolved =
            modalityProfile.MuscleRegions
                .Select(
                    region =>
                        new Engine30MuscleGlycogenInput(
                            MuscleGroupCode:
                                NormalizeRequiredCode(
                                    region.MuscleGroupCode,
                                    nameof(region.MuscleGroupCode)),

                            WholeBodySkeletalMuscleFraction:
                                region.WholeBodySkeletalMuscleFraction,

                            RecruitmentFraction:
                                region.RecruitmentFraction,

                            DepletionFraction:
                                exposure.ResolvedDepletionFraction))
                .ToList();

        return new Engine30CardioGlycogenMuscleProfileResult(
            ProfileCode:
                profileCode,

            ModalityCode:
                modalityCode,

            SourceCode:
                sourceCode,

            ConfidenceCode:
                confidenceCode,

            DurationMinutes:
                Round(
                    exposure.DurationMinutes,
                    2),

            ResolvedDepletionFraction:
                Round(
                    exposure.ResolvedDepletionFraction,
                    4),

            ResolvedMuscleGroups:
                resolved);
    }

    private static void Validate(
        Engine30GlycogenMuscleProfileExposure exposure,
        Engine30CardioGlycogenModalityProfile modalityProfile)
    {
        var workloadModality =
            NormalizeRequiredCode(
                exposure.ModalityCode,
                nameof(exposure.ModalityCode));

        var profileModality =
            NormalizeRequiredCode(
                modalityProfile.ModalityCode,
                nameof(modalityProfile.ModalityCode));

        if (workloadModality != profileModality)
        {
            throw new ArgumentException(
                $"Workload modality '{workloadModality}' does not match muscle-profile modality '{profileModality}'.");
        }

        NormalizeRequiredCode(
            modalityProfile.ProfileCode,
            nameof(modalityProfile.ProfileCode));

        NormalizeSourceCode(
            modalityProfile.SourceCode);

        NormalizeConfidenceCode(
            modalityProfile.ConfidenceCode);

        if (exposure.DurationMinutes < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exposure.DurationMinutes));
        }

        if (exposure.ResolvedDepletionFraction < 0m ||
            exposure.ResolvedDepletionFraction > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exposure.ResolvedDepletionFraction),
                "Resolved depletion fraction must be between 0 and 1.");
        }

        if (modalityProfile.MuscleRegions.Count == 0)
        {
            throw new ArgumentException(
                "A modality profile must contain at least one muscle region.",
                nameof(modalityProfile.MuscleRegions));
        }

        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        decimal totalWholeBodyFraction = 0m;

        foreach (var region in modalityProfile.MuscleRegions)
        {
            var code =
                NormalizeRequiredCode(
                    region.MuscleGroupCode,
                    nameof(region.MuscleGroupCode));

            if (!seen.Add(code))
            {
                throw new ArgumentException(
                    $"Duplicate muscle-region code '{code}'.",
                    nameof(modalityProfile.MuscleRegions));
            }

            ValidateFraction(
                region.WholeBodySkeletalMuscleFraction,
                nameof(region.WholeBodySkeletalMuscleFraction),
                allowZero:
                    false);

            ValidateFraction(
                region.RecruitmentFraction,
                nameof(region.RecruitmentFraction),
                allowZero:
                    true);

            totalWholeBodyFraction +=
                region.WholeBodySkeletalMuscleFraction;
        }

        /*
            The current representation assumes the profile regions are
            non-overlapping anatomical pools.

            Overlapping regions require a future overlap-aware/sequential
            glycogen model and must not be silently summed here.
        */
        if (totalWholeBodyFraction > 1m)
        {
            throw new ArgumentException(
                "Muscle-profile whole-body fractions cannot sum above 1.00.",
                nameof(modalityProfile.MuscleRegions));
        }
    }

    private static string NormalizeSourceCode(
        string code)
    {
        var normalized =
            NormalizeRequiredCode(
                code,
                nameof(code));

        return normalized switch
        {
            Engine30CardioMuscleProfileSourceCodes.ConfigDefault =>
                Engine30CardioMuscleProfileSourceCodes.ConfigDefault,

            Engine30CardioMuscleProfileSourceCodes.UserOverride =>
                Engine30CardioMuscleProfileSourceCodes.UserOverride,

            Engine30CardioMuscleProfileSourceCodes.Personalized =>
                Engine30CardioMuscleProfileSourceCodes.Personalized,

            _ =>
                throw new ArgumentException(
                    $"Unsupported muscle-profile source '{code}'.",
                    nameof(code))
        };
    }

    private static string NormalizeConfidenceCode(
        string code)
    {
        var normalized =
            NormalizeRequiredCode(
                code,
                nameof(code));

        return normalized switch
        {
            Engine30CardioMuscleProfileConfidenceCodes.Low =>
                Engine30CardioMuscleProfileConfidenceCodes.Low,

            Engine30CardioMuscleProfileConfidenceCodes.Medium =>
                Engine30CardioMuscleProfileConfidenceCodes.Medium,

            Engine30CardioMuscleProfileConfidenceCodes.High =>
                Engine30CardioMuscleProfileConfidenceCodes.High,

            _ =>
                throw new ArgumentException(
                    $"Unsupported muscle-profile confidence '{code}'.",
                    nameof(code))
        };
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

    private static decimal Round(
        decimal value,
        int decimals = 2)
        => Math.Round(
            value,
            decimals,
            MidpointRounding.AwayFromZero);
}
