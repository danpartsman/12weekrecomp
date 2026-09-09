namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Administrator-only apples-to-apples fuel comparison.
///
/// The displayed value is modeled skeletal-muscle glycogen depletion for a
/// standardized 60-minute session in a 200-lb male at 20% body fat. It uses
/// the active Engine 3.0 workout profile (RPE, depletion, recruitment, hard
/// sets, interval structure, and composite segments) and is NOT persisted as
/// an independent score.
/// </summary>
public sealed class Engine30AdminFuelComparisonService
{
    public const decimal ReferenceWeightLb = 200m;
    public const decimal ReferenceBodyFatFraction = 0.20m;
    public const decimal ReferenceDurationMinutes = 60m;

    private const decimal ReferenceFatFreeMassLb =
        ReferenceWeightLb * (1m - ReferenceBodyFatFraction);

    // Current Engine 3.0 male physiology anchors.
    private const decimal SkeletalMuscleFractionOfFfm = 0.528m;
    private const decimal BaselineGlycogenGPerKgMuscle = 23m;

    // Current resistance-glycogen workload references.
    private const decimal ReferenceHardSetsPerMuscleGroup = 8m;
    private const decimal ResistanceHardSetSensitivity = 0.50m;
    private const decimal ResistanceDurationSensitivity = 0.25m;

    private readonly Engine30GlycogenDemandCalculator _demand = new();
    private readonly Engine30CardioGlycogenWorkloadCalculator _cardio = new();
    private readonly Engine30ResistanceGlycogenWorkloadCalculator _resistance = new();
    private readonly Engine30IntervalGlycogenWorkloadCalculator _interval = new();
    private readonly Engine30SequentialCompositeGlycogenCalculator _sequential = new();

    public decimal? Calculate60MinuteGlycogen(
        Engine30WorkoutAdminProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        try
        {
            if (IsRecovery(profile))
                return 0m;

            var value =
                profile.WorkoutFamilyCode.ToUpperInvariant() switch
                {
                    Engine30WorkoutFamilyCodes.Resistance =>
                        CalculateResistance(profile),

                    Engine30WorkoutFamilyCodes.Intervals =>
                        CalculateIntervals(profile),

                    Engine30WorkoutFamilyCodes.Composite =>
                        CalculateComposite(profile),

                    Engine30WorkoutFamilyCodes.Cardio =>
                        CalculateContinuous(profile),

                    _ =>
                        CalculateContinuous(profile)
                };

            return value.HasValue
                ? Math.Round(
                    value.Value,
                    1,
                    MidpointRounding.AwayFromZero)
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private decimal? CalculateContinuous(
        Engine30WorkoutAdminProfile profile)
    {
        var groups =
            ValidRegions(profile)
                .ToArray();

        if (groups.Length == 0)
            return null;

        var referenceDuration =
            Positive(
                profile.GlycogenReferenceDurationMinutes,
                profile.DefaultDurationMinutes,
                ReferenceDurationMinutes);

        var referenceRpe =
            Math.Max(
                1m,
                profile.DefaultRpe);

        var workload =
            _cardio.Calculate(
                new Engine30CardioGlycogenWorkloadInput(
                    ModalityCode:
                        profile.DefaultModalityCode,

                    DurationMinutes:
                        ReferenceDurationMinutes,

                    SessionRpe:
                        profile.DefaultRpe),

                new Engine30CardioGlycogenWorkloadConfig(
                    ReferenceDurationMinutes:
                        referenceDuration,

                    ReferenceDepletionFraction:
                        profile.GlycogenReferenceDepletionFraction,

                    ReferenceRpe:
                        referenceRpe,

                    ReferenceObjectiveRelativeIntensity:
                        null,

                    ReferenceHeartRateZoneIndex:
                        null,

                    IntensitySensitivity:
                        profile.GlycogenIntensitySensitivity,

                    Zone1Weight:
                        1m,

                    Zone2Weight:
                        2m,

                    Zone3Weight:
                        3m,

                    Zone4Weight:
                        4m,

                    Zone5Weight:
                        5m,

                    MinimumDepletionFraction:
                        profile.GlycogenMinimumDepletionFraction,

                    MaximumDepletionFraction:
                        profile.GlycogenMaximumDepletionFraction));

        return Demand(
            groups.Select(
                x =>
                    new Engine30MuscleGlycogenInput(
                        x.MuscleRegionCode,
                        x.WholeBodyMuscleFraction,
                        x.RecruitmentFraction,
                        workload.ResolvedDepletionFraction))
                .ToArray());
    }

    private decimal? CalculateResistance(
        Engine30WorkoutAdminProfile profile)
    {
        var regions =
            ValidRegions(profile)
                .ToArray();

        if (regions.Length == 0)
            return null;

        var defaultDuration =
            Positive(
                profile.DefaultDurationMinutes,
                profile.GlycogenReferenceDurationMinutes,
                ReferenceDurationMinutes);

        var hardSetScale =
            ReferenceDurationMinutes
            / defaultDuration;

        var workload =
            _resistance.Calculate(
                new Engine30ResistanceGlycogenWorkloadInput(
                    DurationMinutes:
                        ReferenceDurationMinutes,

                    TrainingStatusCode:
                        Engine30ResistanceGlycogenTrainingStatusCodes.Trained,

                    MuscleGroups:
                        regions
                            .Select(
                                x =>
                                    new Engine30ResistanceMuscleGlycogenWorkInput(
                                        MuscleGroupCode:
                                            x.MuscleRegionCode,

                                        WholeBodySkeletalMuscleFraction:
                                            x.WholeBodyMuscleFraction,

                                        RecruitmentFraction:
                                            x.RecruitmentFraction,

                                        HardSets:
                                            x.HardSets * hardSetScale,

                                        PercentOneRepMax:
                                            null))
                            .ToArray()),

                new Engine30ResistanceGlycogenWorkloadConfig(
                    ReferenceDurationMinutes:
                        Positive(
                            profile.GlycogenReferenceDurationMinutes,
                            profile.DefaultDurationMinutes,
                            ReferenceDurationMinutes),

                    ReferenceHardSetsPerMuscleGroup:
                        ReferenceHardSetsPerMuscleGroup,

                    ReferenceDepletionFraction:
                        profile.GlycogenReferenceDepletionFraction,

                    HardSetSensitivity:
                        ResistanceHardSetSensitivity,

                    DurationSensitivity:
                        ResistanceDurationSensitivity,

                    TrainedFactor:
                        1.00m,

                    UntrainedFactor:
                        1.10m,

                    ReferencePercentOneRepMax:
                        null,

                    PercentOneRepMaxSensitivity:
                        0m,

                    MinimumDepletionFraction:
                        profile.GlycogenMinimumDepletionFraction,

                    MaximumDepletionFraction:
                        profile.GlycogenMaximumDepletionFraction));

        return Demand(
            workload.ResolvedMuscleGroups);
    }

    private decimal? CalculateIntervals(
        Engine30WorkoutAdminProfile profile)
    {
        var groups =
            ValidRegions(profile)
                .ToArray();

        if (groups.Length == 0)
            return null;

        var workSeconds =
            Positive(
                profile.IntervalWorkSeconds,
                60m);

        var recoverySeconds =
            Math.Max(
                0m,
                profile.IntervalRecoverySeconds);

        var repetitions =
            RepetitionsForDuration(
                ReferenceDurationMinutes,
                workSeconds,
                recoverySeconds);

        var referenceRepetitions =
            profile.IntervalRepetitions > 0
                ? profile.IntervalRepetitions
                : RepetitionsForDuration(
                    Positive(
                        profile.DefaultDurationMinutes,
                        30m),
                    workSeconds,
                    recoverySeconds);

        var workload =
            _interval.Calculate(
                new Engine30IntervalGlycogenWorkloadInput(
                    ModalityCode:
                        profile.DefaultModalityCode,

                    WorkIntervalSeconds:
                        workSeconds,

                    RecoveryIntervalSeconds:
                        recoverySeconds,

                    Repetitions:
                        repetitions,

                    RecoveryPeriods:
                        recoverySeconds > 0m
                            ? repetitions
                            : 0,

                    WorkRpe:
                        profile.IntervalWorkRpe,

                    RecoveryRpe:
                        profile.IntervalRecoveryRpe),

                new Engine30IntervalGlycogenWorkloadConfig(
                    ReferenceWorkIntervalSeconds:
                        workSeconds,

                    ReferenceRecoveryIntervalSeconds:
                        recoverySeconds,

                    ReferenceRepetitions:
                        referenceRepetitions,

                    ReferenceRecoveryPeriods:
                        recoverySeconds > 0m
                            ? referenceRepetitions
                            : 0,

                    ReferenceDepletionFraction:
                        profile.GlycogenReferenceDepletionFraction,

                    ReferenceWorkRpe:
                        Math.Max(
                            1m,
                            profile.IntervalWorkRpe),

                    ReferenceRecoveryRpe:
                        Math.Max(
                            1m,
                            profile.IntervalRecoveryRpe),

                    ReferenceObjectiveRelativeWorkIntensity:
                        null,

                    ReferenceObjectiveRelativeRecoveryIntensity:
                        null,

                    WorkIntensitySensitivity:
                        profile.IntervalWorkIntensitySensitivity,

                    RecoveryIntensitySensitivity:
                        profile.IntervalRecoveryIntensitySensitivity,

                    RecoveryContributionWeight:
                        profile.IntervalRecoveryContributionWeight,

                    MinimumDepletionFraction:
                        profile.GlycogenMinimumDepletionFraction,

                    MaximumDepletionFraction:
                        profile.GlycogenMaximumDepletionFraction));

        return Demand(
            groups.Select(
                x =>
                    new Engine30MuscleGlycogenInput(
                        x.MuscleRegionCode,
                        x.WholeBodyMuscleFraction,
                        x.RecruitmentFraction,
                        workload.ResolvedDepletionFraction))
                .ToArray());
    }

    private decimal? CalculateComposite(
        Engine30WorkoutAdminProfile profile)
    {
        var enabled =
            profile.Segments
                .Where(
                    x =>
                        x.Enabled
                        && x.DurationMinutes > 0m)
                .OrderBy(x => x.DisplayOrder)
                .ToArray();

        if (enabled.Length == 0)
            return null;

        var sourceDuration =
            enabled.Sum(x => x.DurationMinutes);

        if (sourceDuration <= 0m)
            return null;

        var durationScale =
            ReferenceDurationMinutes
            / sourceDuration;

        var segments =
            new List<Engine30CompositeGlycogenSegmentInput>();

        var hasAnyMuscleExposure = false;

        foreach (var segment in enabled)
        {
            var resolved =
                ResolveCompositeSegment(
                    profile,
                    segment,
                    durationScale);

            if (resolved.Count > 0)
                hasAnyMuscleExposure = true;

            segments.Add(
                new Engine30CompositeGlycogenSegmentInput(
                    segment.SegmentCode,
                    resolved));
        }

        if (!hasAnyMuscleExposure)
            return null;

        var result =
            _sequential.Calculate(
                new Engine30SequentialCompositeGlycogenInput(
                    FatFreeMassLb:
                        ReferenceFatFreeMassLb,

                    SkeletalMuscleFractionOfFfm:
                        SkeletalMuscleFractionOfFfm,

                    BaselineGlycogenGPerKgMuscle:
                        BaselineGlycogenGPerKgMuscle,

                    TargetRestorationFraction:
                        1.00m,

                    CarbohydratePerGlycogenG:
                        1.00m,

                    Segments:
                        segments));

        return result.EstimatedGlycogenDepletedG;
    }

    private IReadOnlyCollection<Engine30MuscleGlycogenInput> ResolveCompositeSegment(
        Engine30WorkoutAdminProfile profile,
        Engine30WorkoutAdminSegment segment,
        decimal durationScale)
    {
        var duration =
            segment.DurationMinutes
            * durationScale;

        var model =
            (segment.GlycogenModelCode ?? "CONTINUOUS")
                .Trim()
                .ToUpperInvariant();

        if (model == "RESISTANCE")
        {
            var source =
                SegmentResistanceRegions(
                    segment,
                    durationScale)
                    .ToArray();

            if (source.Length == 0)
                return [];

            var workload =
                _resistance.Calculate(
                    new Engine30ResistanceGlycogenWorkloadInput(
                        DurationMinutes:
                            duration,

                        TrainingStatusCode:
                            Engine30ResistanceGlycogenTrainingStatusCodes.Trained,

                        MuscleGroups:
                            source),

                    new Engine30ResistanceGlycogenWorkloadConfig(
                        ReferenceDurationMinutes:
                            segment.DurationMinutes,

                        ReferenceHardSetsPerMuscleGroup:
                            ReferenceHardSetsPerMuscleGroup,

                        ReferenceDepletionFraction:
                            segment.GlycogenReferenceDepletionFraction,

                        HardSetSensitivity:
                            ResistanceHardSetSensitivity,

                        DurationSensitivity:
                            ResistanceDurationSensitivity,

                        TrainedFactor:
                            1.00m,

                        UntrainedFactor:
                            1.10m,

                        ReferencePercentOneRepMax:
                            null,

                        PercentOneRepMaxSensitivity:
                            0m,

                        MinimumDepletionFraction:
                            profile.GlycogenMinimumDepletionFraction,

                        MaximumDepletionFraction:
                            profile.GlycogenMaximumDepletionFraction));

            return workload.ResolvedMuscleGroups;
        }

        if (model == "INTERVAL")
        {
            var workSeconds =
                Positive(
                    profile.IntervalWorkSeconds,
                    60m);

            var recoverySeconds =
                Math.Max(
                    0m,
                    profile.IntervalRecoverySeconds);

            var actualRepetitions =
                RepetitionsForDuration(
                    duration,
                    workSeconds,
                    recoverySeconds);

            var referenceRepetitions =
                RepetitionsForDuration(
                    segment.DurationMinutes,
                    workSeconds,
                    recoverySeconds);

            var workload =
                _interval.Calculate(
                    new Engine30IntervalGlycogenWorkloadInput(
                        ModalityCode:
                            segment.ModalityCode,

                        WorkIntervalSeconds:
                            workSeconds,

                        RecoveryIntervalSeconds:
                            recoverySeconds,

                        Repetitions:
                            actualRepetitions,

                        RecoveryPeriods:
                            recoverySeconds > 0m
                                ? actualRepetitions
                                : 0,

                        WorkRpe:
                            profile.IntervalWorkRpe,

                        RecoveryRpe:
                            profile.IntervalRecoveryRpe),

                    new Engine30IntervalGlycogenWorkloadConfig(
                        ReferenceWorkIntervalSeconds:
                            workSeconds,

                        ReferenceRecoveryIntervalSeconds:
                            recoverySeconds,

                        ReferenceRepetitions:
                            referenceRepetitions,

                        ReferenceRecoveryPeriods:
                            recoverySeconds > 0m
                                ? referenceRepetitions
                                : 0,

                        ReferenceDepletionFraction:
                            segment.GlycogenReferenceDepletionFraction,

                        ReferenceWorkRpe:
                            Math.Max(
                                1m,
                                profile.IntervalWorkRpe),

                        ReferenceRecoveryRpe:
                            Math.Max(
                                1m,
                                profile.IntervalRecoveryRpe),

                        ReferenceObjectiveRelativeWorkIntensity:
                            null,

                        ReferenceObjectiveRelativeRecoveryIntensity:
                            null,

                        WorkIntensitySensitivity:
                            profile.IntervalWorkIntensitySensitivity,

                        RecoveryIntensitySensitivity:
                            profile.IntervalRecoveryIntensitySensitivity,

                        RecoveryContributionWeight:
                            profile.IntervalRecoveryContributionWeight,

                        MinimumDepletionFraction:
                            profile.GlycogenMinimumDepletionFraction,

                        MaximumDepletionFraction:
                            profile.GlycogenMaximumDepletionFraction));

            return SegmentDemandGroups(
                segment,
                workload.ResolvedDepletionFraction);
        }

        var cardio =
            _cardio.Calculate(
                new Engine30CardioGlycogenWorkloadInput(
                    ModalityCode:
                        segment.ModalityCode,

                    DurationMinutes:
                        duration,

                    SessionRpe:
                        segment.DefaultRpe),

                new Engine30CardioGlycogenWorkloadConfig(
                    ReferenceDurationMinutes:
                        segment.DurationMinutes,

                    ReferenceDepletionFraction:
                        segment.GlycogenReferenceDepletionFraction,

                    ReferenceRpe:
                        Math.Max(
                            1m,
                            segment.DefaultRpe),

                    ReferenceObjectiveRelativeIntensity:
                        null,

                    ReferenceHeartRateZoneIndex:
                        null,

                    IntensitySensitivity:
                        profile.GlycogenIntensitySensitivity,

                    Zone1Weight:
                        1m,

                    Zone2Weight:
                        2m,

                    Zone3Weight:
                        3m,

                    Zone4Weight:
                        4m,

                    Zone5Weight:
                        5m,

                    MinimumDepletionFraction:
                        profile.GlycogenMinimumDepletionFraction,

                    MaximumDepletionFraction:
                        profile.GlycogenMaximumDepletionFraction));

        return SegmentDemandGroups(
            segment,
            cardio.ResolvedDepletionFraction);
    }

    private decimal Demand(
        IReadOnlyCollection<Engine30MuscleGlycogenInput> groups)
    {
        if (groups.Count == 0)
            return 0m;

        var result =
            _demand.Calculate(
                new Engine30GlycogenDemandInput(
                    FatFreeMassLb:
                        ReferenceFatFreeMassLb,

                    SkeletalMuscleFractionOfFfm:
                        SkeletalMuscleFractionOfFfm,

                    BaselineGlycogenGPerKgMuscle:
                        BaselineGlycogenGPerKgMuscle,

                    TargetRestorationFraction:
                        1.00m,

                    CarbohydratePerGlycogenG:
                        1.00m,

                    MuscleGroups:
                        groups));

        return result.EstimatedGlycogenDepletedG;
    }

    private static IEnumerable<Engine30WorkoutAdminMuscleRegion> ValidRegions(
        Engine30WorkoutAdminProfile profile)
        => profile.MuscleRegions.Where(
            x =>
                !string.IsNullOrWhiteSpace(x.MuscleRegionCode)
                && x.WholeBodyMuscleFraction > 0m
                && x.RecruitmentFraction > 0m);

    private static IEnumerable<Engine30ResistanceMuscleGlycogenWorkInput> SegmentResistanceRegions(
        Engine30WorkoutAdminSegment segment,
        decimal durationScale)
    {
        if (segment.LowerBodyMuscleFraction > 0m
            && segment.LowerBodyRecruitmentFraction > 0m)
        {
            yield return
                new Engine30ResistanceMuscleGlycogenWorkInput(
                    MuscleGroupCode:
                        "LOWER_BODY",

                    WholeBodySkeletalMuscleFraction:
                        segment.LowerBodyMuscleFraction,

                    RecruitmentFraction:
                        segment.LowerBodyRecruitmentFraction,

                    HardSets:
                        segment.HardSetsPerRegion
                        * durationScale,

                    PercentOneRepMax:
                        null);
        }

        if (segment.UpperBodyMuscleFraction > 0m
            && segment.UpperBodyRecruitmentFraction > 0m)
        {
            yield return
                new Engine30ResistanceMuscleGlycogenWorkInput(
                    MuscleGroupCode:
                        "UPPER_BODY",

                    WholeBodySkeletalMuscleFraction:
                        segment.UpperBodyMuscleFraction,

                    RecruitmentFraction:
                        segment.UpperBodyRecruitmentFraction,

                    HardSets:
                        segment.HardSetsPerRegion
                        * durationScale,

                    PercentOneRepMax:
                        null);
        }
    }

    private static IReadOnlyCollection<Engine30MuscleGlycogenInput> SegmentDemandGroups(
        Engine30WorkoutAdminSegment segment,
        decimal depletionFraction)
    {
        var groups =
            new List<Engine30MuscleGlycogenInput>();

        if (segment.LowerBodyMuscleFraction > 0m
            && segment.LowerBodyRecruitmentFraction > 0m)
        {
            groups.Add(
                new Engine30MuscleGlycogenInput(
                    "LOWER_BODY",
                    segment.LowerBodyMuscleFraction,
                    segment.LowerBodyRecruitmentFraction,
                    depletionFraction));
        }

        if (segment.UpperBodyMuscleFraction > 0m
            && segment.UpperBodyRecruitmentFraction > 0m)
        {
            groups.Add(
                new Engine30MuscleGlycogenInput(
                    "UPPER_BODY",
                    segment.UpperBodyMuscleFraction,
                    segment.UpperBodyRecruitmentFraction,
                    depletionFraction));
        }

        return groups;
    }

    private static int RepetitionsForDuration(
        decimal durationMinutes,
        decimal workSeconds,
        decimal recoverySeconds)
    {
        var cycleSeconds =
            workSeconds
            + recoverySeconds;

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

    private static bool IsRecovery(
        Engine30WorkoutAdminProfile profile)
        => profile.WorkoutFamilyCode.Equals(
               Engine30WorkoutFamilyCodes.Recovery,
               StringComparison.OrdinalIgnoreCase)
           || profile.GlycogenModelCode.Equals(
               "ZERO",
               StringComparison.OrdinalIgnoreCase)
           || profile.DefaultDurationMinutes == 0m;

    private static decimal Positive(
        decimal first,
        decimal second)
        => first > 0m
            ? first
            : second;

    private static decimal Positive(
        decimal first,
        decimal second,
        decimal third)
        => first > 0m
            ? first
            : second > 0m
                ? second
                : third;
}

