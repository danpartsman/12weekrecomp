namespace WebRecomp.Web.Services.Engine30;

public sealed record Engine30WorkoutRuntimeRequest(
    Engine30WorkoutSessionInput Session,
    decimal BaseLivingEnergyKcal,
    decimal WeightLb,
    decimal FatFreeMassLb,
    decimal SkeletalMuscleFractionOfFfm,
    decimal BaselineGlycogenGPerKgMuscle,
    string ResistanceTrainingStatusCode = Engine30ResistanceGlycogenTrainingStatusCodes.Trained);

public sealed record Engine30WorkoutRuntimeResult(
    Engine30WorkoutRuntimeProfileProvenance ProfileProvenance,
    Engine30ResolvedWorkoutSession ResolvedSession,
    Engine30WorkoutEnergyAdapterResult Energy,
    Engine30WorkoutGlycogenAdapterResult Glycogen);

/// <summary>
/// Engine 3.0 runtime workout orchestrator.
///
/// This is the first runtime path that reads the persisted ACTIVE Admin
/// workout profile and feeds those values into both workout energy and
/// glycogen calculations for the same authoritative session.
///
/// Frozen Engine 2.0 is not referenced or modified here.
/// </summary>
public sealed class Engine30WorkoutRuntimeService(
    IEngine30WorkoutRuntimeProfileProvider profileProvider)
{
    private readonly Engine30WorkoutSessionResolver _resolver = new();
    private readonly Engine30WorkoutEnergyAdapter _energy = new();
    private readonly Engine30WorkoutGlycogenAdapter _glycogen = new();

    public async Task<Engine30WorkoutRuntimeResult> CalculateAsync(
        Engine30WorkoutRuntimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Session);

        ValidatePerson(request);

        var profileResolution =
            await profileProvider.ResolveAsync(
                request.Session.WorkoutTypeCode,
                cancellationToken);

        var admin = profileResolution.Profile;

        Engine30WorkoutProfileService.Validate(admin);

        var normalizedSessionInput =
            BuildRuntimeSessionInput(
                request.Session,
                admin);

        var typeProfile =
            new Engine30WorkoutTypeProfile(
                WorkoutTypeCode: profileResolution.Provenance.WorkoutTypeCode,
                WorkoutFamilyCode: admin.WorkoutFamilyCode,
                SessionStructureCode: admin.SessionStructureCode,
                DefaultRpe: admin.DefaultRpe,
                DefaultModalityCode: admin.DefaultModalityCode,
                TrainingPatternCode: admin.TrainingPatternCode);

        var resolved =
            _resolver.Resolve(
                typeProfile,
                normalizedSessionInput);

        var energy =
            _energy.Calculate(
                new Engine30WorkoutEnergyAdapterInput(
                    Session: resolved,
                    BaseLivingEnergyKcal: request.BaseLivingEnergyKcal,
                    WeightLb: request.WeightLb,
                    FatFreeMassLb: request.FatFreeMassLb,
                    SessionCalibration: null,
                    SegmentCalibrations: BuildEnergyCalibrations(
                        admin,
                        profileResolution.Provenance,
                        resolved)));

        var modeledGlycogen =
            _glycogen.Calculate(
                new Engine30WorkoutGlycogenAdapterInput(
                    Session: resolved,
                    Profile: admin,
                    FatFreeMassLb: request.FatFreeMassLb,
                    SkeletalMuscleFractionOfFfm: request.SkeletalMuscleFractionOfFfm,
                    BaselineGlycogenGPerKgMuscle: request.BaselineGlycogenGPerKgMuscle,
                    ResistanceTrainingStatusCode: request.ResistanceTrainingStatusCode));

        // Physiological consistency guardrail.
        //
        // Glycogen depletion is modeled independently from workout energy so
        // calories never become a glycogen proxy.  The independent estimates
        // must nevertheless obey conservation of energy: muscle glycogen
        // energy cannot exceed the carbohydrate share of total exercise
        // energy.  Ninety percent is deliberately a generous ceiling for
        // very carbohydrate-dominant high-intensity work; it is a safety
        // boundary, not an assertion that every workout is 90% carbohydrate.
        var glycogen =
            ApplyExerciseEnergyConsistencyGuardrail(
                modeledGlycogen,
                energy.GrossWorkoutEnergyKcal);

        return new Engine30WorkoutRuntimeResult(
            ProfileProvenance: profileResolution.Provenance,
            ResolvedSession: resolved,
            Energy: energy,
            Glycogen: glycogen);
    }

    private static Engine30WorkoutGlycogenAdapterResult ApplyExerciseEnergyConsistencyGuardrail(
        Engine30WorkoutGlycogenAdapterResult modeled,
        decimal grossWorkoutEnergyKcal)
    {
        if (grossWorkoutEnergyKcal <= 0m
            || modeled.EstimatedGlycogenDepletedG <= 0m)
        {
            return modeled;
        }

        const decimal kcalPerGramCarbohydrate = 4m;
        const decimal maximumCarbohydrateEnergyFraction = 0.90m;

        var maximumPhysiologicallyPossibleGlycogenG =
            grossWorkoutEnergyKcal
            * maximumCarbohydrateEnergyFraction
            / kcalPerGramCarbohydrate;

        if (modeled.EstimatedGlycogenDepletedG
            <= maximumPhysiologicallyPossibleGlycogenG)
        {
            return modeled;
        }

        var scale =
            maximumPhysiologicallyPossibleGlycogenG
            / modeled.EstimatedGlycogenDepletedG;

        var depleted =
            Round(maximumPhysiologicallyPossibleGlycogenG, 2);

        var restoration =
            Round(modeled.TargetGlycogenRestorationG * scale, 2);

        var reload =
            Round(modeled.CarbohydrateReloadRequirementG * scale, 2);

        return modeled with
        {
            EstimatedGlycogenDepletedG = depleted,
            TargetGlycogenRestorationG = restoration,
            CarbohydrateReloadRequirementG = reload,
            CarbohydrateReloadEnergyKcal = Round(reload * kcalPerGramCarbohydrate, 2)
        };
    }

    private static decimal Round(decimal value, int decimals)
        => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    private static Engine30WorkoutSessionInput BuildRuntimeSessionInput(
        Engine30WorkoutSessionInput source,
        Engine30WorkoutAdminProfile profile)
    {
        var plannedDuration =
            source.PlannedDurationMinutes > 0m
                ? source.PlannedDurationMinutes
                : profile.DefaultDurationMinutes;

        var enabledProfileSegments =
            profile.Segments
                .Where(x => x.Enabled && x.DurationMinutes > 0m)
                .OrderBy(x => x.DisplayOrder)
                .ToArray();

        if (profile.WorkoutFamilyCode.Equals(
                Engine30WorkoutFamilyCodes.Composite,
                StringComparison.OrdinalIgnoreCase))
        {
            if (enabledProfileSegments.Length < 2)
            {
                throw new InvalidOperationException(
                    "Composite Engine 3.0 workout requires at least two enabled administrator segments.");
            }

            var supplied =
                source.Segments?
                    .ToDictionary(
                        x => x.SegmentCode.Trim().ToUpperInvariant(),
                        StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, Engine30WorkoutSegmentInput>(
                    StringComparer.OrdinalIgnoreCase);

            var knownCodes =
                enabledProfileSegments
                    .Select(x => x.SegmentCode.Trim().ToUpperInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var unknown =
                supplied.Keys
                    .Where(x => !knownCodes.Contains(x))
                    .ToArray();

            if (unknown.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Session contains composite segment(s) not defined by the active Admin profile: {string.Join(", ", unknown)}.");
            }

            var profileTotal =
                enabledProfileSegments.Sum(x => x.DurationMinutes);

            if (profileTotal <= 0m)
            {
                throw new InvalidOperationException(
                    "Composite Engine 3.0 workout requires positive administrator segment duration.");
            }

            /*
                Composite segment DurationMinutes are absolute planned block minutes.

                Do not stretch 14 + 14 + 14 back to a whole-session 60-minute
                value. The Admin profile defines the physiological blocks.

                A caller may still override a specific planned block by supplying
                that segment explicitly. Actual whole-session duration remains
                observational truth and can proportionally resolve actual segment
                durations when segment-level actual duration is unavailable.
            */
            var segments =
                enabledProfileSegments
                    .Select(configured =>
                    {
                        supplied.TryGetValue(
                            configured.SegmentCode.Trim().ToUpperInvariant(),
                            out var actual);

                        var plannedSegmentDuration =
                            actual is not null
                            && actual.PlannedDurationMinutes > 0m
                                ? actual.PlannedDurationMinutes
                                : configured.DurationMinutes;

                        decimal? actualSegmentDuration =
                            actual?.ActualDurationMinutes;

                        if (!actualSegmentDuration.HasValue
                            && source.ActualDurationMinutes.HasValue
                            && actual is null)
                        {
                            actualSegmentDuration =
                                configured.DurationMinutes
                                * source.ActualDurationMinutes.Value
                                / profileTotal;
                        }

                        return new Engine30WorkoutSegmentInput(
                            SegmentCode: configured.SegmentCode,
                            ModalityCode: actual?.ModalityCode ?? configured.ModalityCode,
                            PlannedDurationMinutes: plannedSegmentDuration,
                            DefaultRpe: configured.DefaultRpe,

                            // Segment-specific RPE wins. If the client supplied
                            // only a whole-session planned/actual RPE, use that
                            // as the fallback for each composite segment.
                            PlannedRpeOverride:
                                actual?.PlannedRpeOverride
                                ?? source.PlannedRpeOverride,

                            ActualDurationMinutes: actualSegmentDuration,

                            ActualRpe:
                                actual?.ActualRpe
                                ?? source.ActualRpe,
                            ObjectiveWorkload: actual?.ObjectiveWorkload,
                            Resistance: MergeResistance(configured, actual?.Resistance),
                            Intervals: MergeIntervals(profile, configured, actual?.Intervals, plannedSegmentDuration));
                    })
                    .ToArray();

            var plannedTotal =
                segments.Sum(x => x.PlannedDurationMinutes);

            return source with
            {
                PlannedDurationMinutes = plannedTotal,
                Segments = segments
            };
        }

        var suppliedSegments =
            source.Segments?.ToArray()
            ?? Array.Empty<Engine30WorkoutSegmentInput>();

        if (suppliedSegments.Length > 1)
        {
            throw new InvalidOperationException(
                "A non-composite Engine 3.0 workout may supply at most one runtime segment.");
        }

        var suppliedSegment =
            suppliedSegments.FirstOrDefault();

        var modality =
            suppliedSegment?.ModalityCode
            ?? source.ActualModalityCode
            ?? source.PlannedModalityOverrideCode
            ?? profile.DefaultModalityCode;

        var segment =
            new Engine30WorkoutSegmentInput(
                SegmentCode: suppliedSegment?.SegmentCode ?? "SESSION",
                ModalityCode: modality,
                PlannedDurationMinutes: suppliedSegment is not null
                                        && suppliedSegment.PlannedDurationMinutes > 0m
                    ? suppliedSegment.PlannedDurationMinutes
                    : plannedDuration,
                DefaultRpe: profile.DefaultRpe,
                PlannedRpeOverride: suppliedSegment?.PlannedRpeOverride ?? source.PlannedRpeOverride,
                ActualDurationMinutes: suppliedSegment?.ActualDurationMinutes ?? source.ActualDurationMinutes,
                ActualRpe: suppliedSegment?.ActualRpe ?? source.ActualRpe,
                ObjectiveWorkload: suppliedSegment?.ObjectiveWorkload,
                Resistance: profile.WorkoutFamilyCode.Equals(
                        Engine30WorkoutFamilyCodes.Resistance,
                        StringComparison.OrdinalIgnoreCase)
                    ? MergeResistance(profile, suppliedSegment?.Resistance, plannedDuration)
                    : suppliedSegment?.Resistance,
                Intervals: profile.WorkoutFamilyCode.Equals(
                        Engine30WorkoutFamilyCodes.Intervals,
                        StringComparison.OrdinalIgnoreCase)
                    ? MergeIntervals(profile, suppliedSegment?.Intervals, plannedDuration)
                    : suppliedSegment?.Intervals);

        return source with
        {
            PlannedDurationMinutes = plannedDuration,
            Segments = new[] { segment }
        };
    }

    private static Engine30ResistanceSegmentDetails MergeResistance(
        Engine30WorkoutAdminProfile profile,
        Engine30ResistanceSegmentDetails? supplied,
        decimal plannedDuration)
    {
        var referenceDuration =
            profile.ResistanceReferenceDurationMinutes > 0m
                ? profile.ResistanceReferenceDurationMinutes
                : profile.DefaultDurationMinutes > 0m
                    ? profile.DefaultDurationMinutes
                    : Math.Max(1m, plannedDuration);

        var durationScale =
            plannedDuration > 0m
                ? plannedDuration / referenceDuration
                : 1m;

        var defaultTotalHardSets =
            profile.ResistanceReferenceHardSets > 0m
                ? profile.ResistanceReferenceHardSets * durationScale
                : (decimal?)null;

        var defaultMuscles =
            profile.MuscleRegions
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.MuscleRegionCode)
                    && x.WholeBodyMuscleFraction > 0m)
                .Select(x =>
                    new Engine30WorkoutMuscleWork(
                        MuscleGroupCode: x.MuscleRegionCode,
                        HardSets: x.HardSets > 0m ? x.HardSets * durationScale : null,
                        RecruitmentFraction: x.RecruitmentFraction))
                .ToArray();

        return new Engine30ResistanceSegmentDetails(
            TotalHardSets: supplied?.TotalHardSets ?? defaultTotalHardSets,
            AverageRestSeconds: supplied?.AverageRestSeconds ?? profile.DefaultRestSeconds,
            AverageRepsPerSet: supplied?.AverageRepsPerSet,
            AveragePercentOneRepMax: supplied?.AveragePercentOneRepMax,
            MuscleGroups: supplied?.MuscleGroups ?? defaultMuscles);
    }

    private static Engine30ResistanceSegmentDetails? MergeResistance(
        Engine30WorkoutAdminSegment configured,
        Engine30ResistanceSegmentDetails? supplied)
    {
        if (!configured.GlycogenModelCode.Equals(
                "RESISTANCE",
                StringComparison.OrdinalIgnoreCase)
            && !configured.ModalityCode.Equals(
                Engine30WorkoutModalityCodes.Resistance,
                StringComparison.OrdinalIgnoreCase))
        {
            return supplied;
        }

        var muscles = new List<Engine30WorkoutMuscleWork>();

        if (configured.LowerBodyMuscleFraction > 0m
            && configured.LowerBodyRecruitmentFraction > 0m)
        {
            muscles.Add(new Engine30WorkoutMuscleWork(
                "LOWER_BODY",
                HardSets: configured.HardSetsPerRegion > 0m ? configured.HardSetsPerRegion : null,
                RecruitmentFraction: configured.LowerBodyRecruitmentFraction));
        }

        if (configured.UpperBodyMuscleFraction > 0m
            && configured.UpperBodyRecruitmentFraction > 0m)
        {
            muscles.Add(new Engine30WorkoutMuscleWork(
                "UPPER_BODY",
                HardSets: configured.HardSetsPerRegion > 0m ? configured.HardSetsPerRegion : null,
                RecruitmentFraction: configured.UpperBodyRecruitmentFraction));
        }

        var defaultTotalHardSets =
            configured.HardSetsPerRegion > 0m
                ? configured.HardSetsPerRegion * Math.Max(1, muscles.Count)
                : (decimal?)null;

        return new Engine30ResistanceSegmentDetails(
            TotalHardSets: supplied?.TotalHardSets ?? defaultTotalHardSets,
            AverageRestSeconds: supplied?.AverageRestSeconds ?? configured.DefaultRestSeconds,
            AverageRepsPerSet: supplied?.AverageRepsPerSet,
            AveragePercentOneRepMax: supplied?.AveragePercentOneRepMax,
            MuscleGroups: supplied?.MuscleGroups ?? muscles);
    }

    private static Engine30IntervalSegmentDetails MergeIntervals(
        Engine30WorkoutAdminProfile profile,
        Engine30IntervalSegmentDetails? supplied,
        decimal durationMinutes)
        => new(
            WorkIntervalSeconds: supplied?.WorkIntervalSeconds ?? profile.IntervalWorkSeconds,
            RecoveryIntervalSeconds: supplied?.RecoveryIntervalSeconds ?? profile.IntervalRecoverySeconds,
            Repetitions: supplied?.Repetitions ?? RepetitionsForDuration(
                durationMinutes,
                profile.IntervalWorkSeconds,
                profile.IntervalRecoverySeconds),
            WorkRpe: supplied?.WorkRpe ?? profile.IntervalWorkRpe,
            RecoveryRpe: supplied?.RecoveryRpe ?? profile.IntervalRecoveryRpe);

    private static Engine30IntervalSegmentDetails? MergeIntervals(
        Engine30WorkoutAdminProfile profile,
        Engine30WorkoutAdminSegment configured,
        Engine30IntervalSegmentDetails? supplied,
        decimal durationMinutes)
    {
        if (!configured.GlycogenModelCode.Equals(
                "INTERVAL",
                StringComparison.OrdinalIgnoreCase)
            && !configured.TrainingPatternCode.Equals(
                Engine30TrainingPatternCodes.Intervals,
                StringComparison.OrdinalIgnoreCase))
        {
            return supplied;
        }

        return MergeIntervals(
            profile,
            supplied,
            durationMinutes);
    }

    private static IReadOnlyCollection<Engine30WorkoutSegmentEnergyCalibration> BuildEnergyCalibrations(
        Engine30WorkoutAdminProfile profile,
        Engine30WorkoutRuntimeProfileProvenance provenance,
        Engine30ResolvedWorkoutSession session)
    {
        var configuredSegments =
            profile.Segments
                .Where(x => x.Enabled)
                .ToDictionary(
                    x => x.SegmentCode.Trim().ToUpperInvariant(),
                    StringComparer.OrdinalIgnoreCase);

        var result =
            new List<Engine30WorkoutSegmentEnergyCalibration>();

        foreach (var segment in session.Segments)
        {
            configuredSegments.TryGetValue(
                segment.SegmentCode.Trim().ToUpperInvariant(),
                out var configured);

            var isConfiguredCompositeSegment = configured is not null;

            var modality = segment.ModalityCode;

            var kcalPerMinute =
                isConfiguredCompositeSegment
                    ? configured!.ReferenceWorkoutCaloriesPerMinute
                    : profile.ReferenceWorkoutCaloriesPerMinute;

            var referenceRpe =
                isConfiguredCompositeSegment
                    ? configured!.ReferenceRpe
                    : profile.ReferenceRpe;

            var scalingBasis =
                isConfiguredCompositeSegment
                    ? configured!.ScalingBasisCode
                    : profile.ScalingBasisCode;

            var referenceFfm =
                isConfiguredCompositeSegment
                    ? configured!.ReferenceFatFreeMassLb
                    : profile.ReferenceFatFreeMassLb;

            var defaultRest =
                isConfiguredCompositeSegment
                    ? configured!.DefaultRestSeconds
                    : profile.DefaultRestSeconds;

            var isResistance =
                modality.Equals(
                    Engine30WorkoutModalityCodes.Resistance,
                    StringComparison.OrdinalIgnoreCase)
                || (configured?.GlycogenModelCode?.Equals(
                        "RESISTANCE",
                        StringComparison.OrdinalIgnoreCase)
                    ?? false);

            var configuredResistanceRegionCount =
                configured is null
                    ? 0
                    : (configured.LowerBodyRecruitmentFraction > 0m ? 1 : 0)
                      + (configured.UpperBodyRecruitmentFraction > 0m ? 1 : 0);

            var referenceResistanceDuration =
                configured is not null
                && configured.DurationMinutes > 0m
                    ? configured.DurationMinutes
                    : Positive(
                        profile.ResistanceReferenceDurationMinutes,
                        profile.DefaultDurationMinutes,
                        60m);

            var referenceResistanceHardSets =
                configured is not null
                && configured.HardSetsPerRegion > 0m
                && configuredResistanceRegionCount > 0
                    ? configured.HardSetsPerRegion
                      * configuredResistanceRegionCount
                    : Positive(
                        profile.ResistanceReferenceHardSets,
                        16m);

            var resistance =
                isResistance
                    ? new Engine30ResistanceEnergyWorkloadConfig(
                        ReferenceDurationMinutes: referenceResistanceDuration,
                        ReferenceHardSets: referenceResistanceHardSets,
                        ReferenceRestSeconds: Positive(
                            defaultRest ?? profile.DefaultRestSeconds ?? 90m,
                            90m),
                        HardSetDensitySensitivity: profile.ResistanceHardSetDensitySensitivity,
                        RestFallbackSensitivity: profile.ResistanceRestFallbackSensitivity,
                        MinimumWorkloadFactor: profile.ResistanceDensityMinimumFactor,
                        MaximumWorkloadFactor: profile.ResistanceDensityMaximumFactor)
                    : null;

            var calibrationCode =
                $"{provenance.ProfileVersionCode ?? "FALLBACK"}:{segment.SegmentCode}";

            result.Add(
                new Engine30WorkoutSegmentEnergyCalibration(
                    SegmentCode: segment.SegmentCode,
                    Calibration: new Engine30WorkoutEnergyCalibration(
                        CalibrationCode: calibrationCode,
                        ModalityCode: modality,
                        ReferenceWorkoutCaloriesPerMinute: kcalPerMinute,
                        ReferenceWeightLb: profile.ReferenceWeightLb,
                        ScalingBasisCode: scalingBasis,
                        ReferenceFatFreeMassLb: referenceFfm,
                        DefaultRestSeconds: defaultRest,
                        ReferenceRpe: referenceRpe,
                        ResistanceWorkloadConfig: resistance)));
        }

        return result;
    }

    private static int RepetitionsForDuration(
        decimal durationMinutes,
        decimal workSeconds,
        decimal recoverySeconds)
    {
        var cycleSeconds = workSeconds + recoverySeconds;

        if (durationMinutes <= 0m
            || workSeconds <= 0m
            || cycleSeconds <= 0m)
        {
            return 0;
        }

        return Math.Max(
            1,
            (int)Math.Round(
                durationMinutes * 60m / cycleSeconds,
                0,
                MidpointRounding.AwayFromZero));
    }

    private static decimal Positive(decimal first, decimal second)
        => first > 0m ? first : second;

    private static decimal Positive(decimal first, decimal second, decimal third)
        => first > 0m ? first : second > 0m ? second : third;

    private static void ValidatePerson(
        Engine30WorkoutRuntimeRequest request)
    {
        if (request.BaseLivingEnergyKcal <= 0m)
            throw new ArgumentOutOfRangeException(nameof(request.BaseLivingEnergyKcal));

        if (request.WeightLb <= 0m)
            throw new ArgumentOutOfRangeException(nameof(request.WeightLb));

        if (request.FatFreeMassLb <= 0m)
            throw new ArgumentOutOfRangeException(nameof(request.FatFreeMassLb));

        if (request.SkeletalMuscleFractionOfFfm <= 0m
            || request.SkeletalMuscleFractionOfFfm > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.SkeletalMuscleFractionOfFfm));
        }

        if (request.BaselineGlycogenGPerKgMuscle <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.BaselineGlycogenGPerKgMuscle));
        }
    }
}
