namespace WebRecomp.Web.Services.Engine30;

public sealed record Engine30WorkoutGlycogenAdapterInput(
    Engine30ResolvedWorkoutSession Session,
    Engine30WorkoutAdminProfile Profile,
    decimal FatFreeMassLb,
    decimal SkeletalMuscleFractionOfFfm,
    decimal BaselineGlycogenGPerKgMuscle,
    string ResistanceTrainingStatusCode = Engine30ResistanceGlycogenTrainingStatusCodes.Trained);

public sealed record Engine30WorkoutGlycogenRegionResult(
    string MuscleGroupCode,
    decimal WholeBodySkeletalMuscleFraction,
    decimal RegionBaselineGlycogenG,
    decimal EffectiveRegionalDepletionFraction,
    decimal EstimatedGlycogenDepletedG);

public sealed record Engine30WorkoutGlycogenAdapterResult(
    string ModelCode,
    decimal EstimatedGlycogenDepletedG,
    decimal TargetGlycogenRestorationG,
    decimal CarbohydrateReloadRequirementG,
    decimal CarbohydrateReloadEnergyKcal,
    IReadOnlyList<Engine30WorkoutGlycogenRegionResult>? Regions = null);

/// <summary>
/// Bridges the authoritative resolved workout session and the active
/// administrator-owned Engine 3.0 workout profile into the existing glycogen
/// workload calculators.
///
/// Workout calories are never used as a glycogen proxy.
/// </summary>
public sealed class Engine30WorkoutGlycogenAdapter
{
    private const decimal ReferenceHardSetsPerMuscleGroup = 8m;
    private const decimal ResistanceHardSetSensitivity = 0.50m;
    private const decimal ResistanceDurationSensitivity = 0.25m;

    private readonly Engine30GlycogenDemandCalculator _demand = new();
    private readonly Engine30CardioGlycogenWorkloadCalculator _cardio = new();
    private readonly Engine30ResistanceGlycogenWorkloadCalculator _resistance = new();
    private readonly Engine30IntervalGlycogenWorkloadCalculator _interval = new();
    private readonly Engine30SequentialCompositeGlycogenCalculator _sequential = new();

    public Engine30WorkoutGlycogenAdapterResult Calculate(
        Engine30WorkoutGlycogenAdapterInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Session);
        ArgumentNullException.ThrowIfNull(input.Profile);

        ValidatePerson(input);

        var profile = input.Profile;

        if (IsRecovery(profile, input.Session))
        {
            return new Engine30WorkoutGlycogenAdapterResult(
                ModelCode: "ZERO",
                EstimatedGlycogenDepletedG: 0m,
                TargetGlycogenRestorationG: 0m,
                CarbohydrateReloadRequirementG: 0m,
                CarbohydrateReloadEnergyKcal: 0m);
        }

        return profile.WorkoutFamilyCode.Trim().ToUpperInvariant() switch
        {
            Engine30WorkoutFamilyCodes.Resistance =>
                CalculateResistance(input),

            Engine30WorkoutFamilyCodes.Intervals =>
                CalculateIntervals(input),

            Engine30WorkoutFamilyCodes.Composite =>
                CalculateComposite(input),

            _ =>
                CalculateContinuous(input)
        };
    }

    private Engine30WorkoutGlycogenAdapterResult CalculateContinuous(
        Engine30WorkoutGlycogenAdapterInput input)
    {
        var profile = input.Profile;
        var session = input.Session;
        var segment = FirstSegment(session);

        var groups =
            ValidRegions(profile)
                .Select(x =>
                    new Engine30MuscleGlycogenInput(
                        x.MuscleRegionCode,
                        x.WholeBodyMuscleFraction,
                        x.RecruitmentFraction,
                        0m))
                .ToArray();

        if (groups.Length == 0)
        {
            return Zero(profile.GlycogenModelCode);
        }

        var workload =
            _cardio.Calculate(
                new Engine30CardioGlycogenWorkloadInput(
                    ModalityCode: segment?.ModalityCode ?? session.AnalysisModality.Value,
                    DurationMinutes: SegmentDuration(segment, session.AnalysisDuration.Minutes),
                    SessionRpe: SegmentRpe(segment, session.AnalysisRpe.Value),
                    ObjectiveRelativeIntensity: null,
                    MonitorData: session.MonitorData),
                new Engine30CardioGlycogenWorkloadConfig(
                    ReferenceDurationMinutes: Positive(
                        profile.GlycogenReferenceDurationMinutes,
                        profile.DefaultDurationMinutes,
                        45m),
                    ReferenceDepletionFraction: profile.GlycogenReferenceDepletionFraction,
                    ReferenceRpe: Math.Max(1m, profile.DefaultRpe),
                    ReferenceObjectiveRelativeIntensity: null,
                    ReferenceHeartRateZoneIndex: null,
                    IntensitySensitivity: profile.GlycogenIntensitySensitivity,
                    Zone1Weight: 1m,
                    Zone2Weight: 2m,
                    Zone3Weight: 3m,
                    Zone4Weight: 4m,
                    Zone5Weight: 5m,
                    MinimumDepletionFraction: profile.GlycogenMinimumDepletionFraction,
                    MaximumDepletionFraction: profile.GlycogenMaximumDepletionFraction));

        var resolvedGroups =
            groups
                .Select(x => x with
                {
                    DepletionFraction = workload.ResolvedDepletionFraction
                })
                .ToArray();

        return FromDemand(
            profile.GlycogenModelCode,
            CalculateDemand(input, resolvedGroups),
            resolvedGroups,
            input.BaselineGlycogenGPerKgMuscle);
    }

    private Engine30WorkoutGlycogenAdapterResult CalculateResistance(
        Engine30WorkoutGlycogenAdapterInput input)
    {
        var profile = input.Profile;
        var session = input.Session;
        var segment = FirstSegment(session);
        var details = segment?.Resistance;

        var duration =
            SegmentDuration(segment, session.AnalysisDuration.Minutes);

        var referenceDuration =
            Positive(
                profile.GlycogenReferenceDurationMinutes,
                profile.DefaultDurationMinutes,
                60m);

        var durationScale =
            referenceDuration > 0m
                ? duration / referenceDuration
                : 1m;

        var totalSetScale =
            details?.TotalHardSets is > 0m
            && profile.ResistanceReferenceHardSets > 0m
                ? details.TotalHardSets.Value / profile.ResistanceReferenceHardSets
                : durationScale;

        var muscleOverrides =
            details?.MuscleGroups?
                .ToDictionary(
                    x => x.MuscleGroupCode.Trim().ToUpperInvariant(),
                    StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, Engine30WorkoutMuscleWork>(
                StringComparer.OrdinalIgnoreCase);

        var groups =
            ValidRegions(profile)
                .Select(region =>
                {
                    muscleOverrides.TryGetValue(
                        region.MuscleRegionCode,
                        out var overrideWork);

                    return new Engine30ResistanceMuscleGlycogenWorkInput(
                        MuscleGroupCode: region.MuscleRegionCode,
                        WholeBodySkeletalMuscleFraction: region.WholeBodyMuscleFraction,
                        RecruitmentFraction: overrideWork?.RecruitmentFraction ?? region.RecruitmentFraction,
                        HardSets: overrideWork?.HardSets ?? region.HardSets * totalSetScale,
                        PercentOneRepMax: details?.AveragePercentOneRepMax);
                })
                .ToArray();

        if (groups.Length == 0)
        {
            return Zero(profile.GlycogenModelCode);
        }

        var workload =
            _resistance.Calculate(
                new Engine30ResistanceGlycogenWorkloadInput(
                    DurationMinutes: duration,
                    TrainingStatusCode: input.ResistanceTrainingStatusCode,
                    MuscleGroups: groups),
                new Engine30ResistanceGlycogenWorkloadConfig(
                    ReferenceDurationMinutes: referenceDuration,
                    ReferenceHardSetsPerMuscleGroup: ReferenceHardSetsPerMuscleGroup,
                    ReferenceDepletionFraction: profile.GlycogenReferenceDepletionFraction,
                    HardSetSensitivity: ResistanceHardSetSensitivity,
                    DurationSensitivity: ResistanceDurationSensitivity,
                    TrainedFactor: 1.00m,
                    UntrainedFactor: 1.10m,
                    ReferencePercentOneRepMax: null,
                    PercentOneRepMaxSensitivity: 0m,
                    MinimumDepletionFraction: profile.GlycogenMinimumDepletionFraction,
                    MaximumDepletionFraction: profile.GlycogenMaximumDepletionFraction));

        return FromDemand(
            profile.GlycogenModelCode,
            CalculateDemand(input, workload.ResolvedMuscleGroups),
            workload.ResolvedMuscleGroups,
            input.BaselineGlycogenGPerKgMuscle);
    }

    private Engine30WorkoutGlycogenAdapterResult CalculateIntervals(
        Engine30WorkoutGlycogenAdapterInput input)
    {
        var profile = input.Profile;
        var session = input.Session;
        var segment = FirstSegment(session);
        var details = segment?.Intervals;

        var duration =
            SegmentDuration(segment, session.AnalysisDuration.Minutes);

        var workSeconds =
            details?.WorkIntervalSeconds
            ?? Positive(profile.IntervalWorkSeconds, 60m);

        var recoverySeconds =
            details?.RecoveryIntervalSeconds
            ?? Math.Max(0m, profile.IntervalRecoverySeconds);

        var repetitions =
            details?.Repetitions
            ?? RepetitionsForDuration(
                duration,
                workSeconds,
                recoverySeconds);

        var referenceRepetitions =
            profile.IntervalRepetitions > 0
                ? profile.IntervalRepetitions
                : RepetitionsForDuration(
                    Positive(profile.DefaultDurationMinutes, 30m),
                    Positive(profile.IntervalWorkSeconds, 60m),
                    Math.Max(0m, profile.IntervalRecoverySeconds));

        var workload =
            _interval.Calculate(
                new Engine30IntervalGlycogenWorkloadInput(
                    ModalityCode: segment?.ModalityCode ?? session.AnalysisModality.Value,
                    WorkIntervalSeconds: workSeconds,
                    RecoveryIntervalSeconds: recoverySeconds,
                    Repetitions: repetitions,
                    RecoveryPeriods: recoverySeconds > 0m ? repetitions : 0,
                    WorkRpe: details?.WorkRpe ?? profile.IntervalWorkRpe,
                    RecoveryRpe: details?.RecoveryRpe ?? profile.IntervalRecoveryRpe),
                new Engine30IntervalGlycogenWorkloadConfig(
                    ReferenceWorkIntervalSeconds: Positive(profile.IntervalWorkSeconds, 60m),
                    ReferenceRecoveryIntervalSeconds: Math.Max(0m, profile.IntervalRecoverySeconds),
                    ReferenceRepetitions: referenceRepetitions,
                    ReferenceRecoveryPeriods: profile.IntervalRecoverySeconds > 0m ? referenceRepetitions : 0,
                    ReferenceDepletionFraction: profile.GlycogenReferenceDepletionFraction,
                    ReferenceWorkRpe: Math.Max(1m, profile.IntervalWorkRpe),
                    ReferenceRecoveryRpe: Math.Max(1m, profile.IntervalRecoveryRpe),
                    ReferenceObjectiveRelativeWorkIntensity: null,
                    ReferenceObjectiveRelativeRecoveryIntensity: null,
                    WorkIntensitySensitivity: profile.IntervalWorkIntensitySensitivity,
                    RecoveryIntensitySensitivity: profile.IntervalRecoveryIntensitySensitivity,
                    RecoveryContributionWeight: profile.IntervalRecoveryContributionWeight,
                    MinimumDepletionFraction: profile.GlycogenMinimumDepletionFraction,
                    MaximumDepletionFraction: profile.GlycogenMaximumDepletionFraction));

        var groups =
            ValidRegions(profile)
                .Select(x =>
                    new Engine30MuscleGlycogenInput(
                        x.MuscleRegionCode,
                        x.WholeBodyMuscleFraction,
                        x.RecruitmentFraction,
                        workload.ResolvedDepletionFraction))
                .ToArray();

        if (groups.Length == 0)
        {
            return Zero(profile.GlycogenModelCode);
        }

        return FromDemand(
            profile.GlycogenModelCode,
            CalculateDemand(input, groups),
            groups,
            input.BaselineGlycogenGPerKgMuscle);
    }

    private Engine30WorkoutGlycogenAdapterResult CalculateComposite(
        Engine30WorkoutGlycogenAdapterInput input)
    {
        var profile = input.Profile;
        var session = input.Session;

        var profileSegments =
            profile.Segments
                .Where(x => x.Enabled && x.DurationMinutes > 0m)
                .OrderBy(x => x.DisplayOrder)
                .ToArray();

        if (profileSegments.Length < 2)
        {
            return Zero(profile.GlycogenModelCode);
        }

        var runtimeByCode =
            session.Segments.ToDictionary(
                x => x.SegmentCode.Trim().ToUpperInvariant(),
                StringComparer.OrdinalIgnoreCase);

        var sequentialSegments =
            new List<Engine30CompositeGlycogenSegmentInput>();

        foreach (var configured in profileSegments)
        {
            if (!runtimeByCode.TryGetValue(
                configured.SegmentCode.Trim().ToUpperInvariant(),
                out var runtime))
            {
                throw new InvalidOperationException(
                    $"Resolved composite workout is missing segment '{configured.SegmentCode}'.");
            }

            var resolvedGroups =
                ResolveCompositeSegment(
                    input,
                    configured,
                    runtime);

            sequentialSegments.Add(
                new Engine30CompositeGlycogenSegmentInput(
                    configured.SegmentCode,
                    resolvedGroups));
        }

        var result =
            _sequential.Calculate(
                new Engine30SequentialCompositeGlycogenInput(
                    FatFreeMassLb: input.FatFreeMassLb,
                    SkeletalMuscleFractionOfFfm: input.SkeletalMuscleFractionOfFfm,
                    BaselineGlycogenGPerKgMuscle: input.BaselineGlycogenGPerKgMuscle,
                    TargetRestorationFraction: profile.TargetRestorationFraction,
                    CarbohydratePerGlycogenG: profile.CarbohydratePerGlycogenG,
                    Segments: sequentialSegments));

        return new Engine30WorkoutGlycogenAdapterResult(
            ModelCode: profile.GlycogenModelCode,
            EstimatedGlycogenDepletedG: result.EstimatedGlycogenDepletedG,
            TargetGlycogenRestorationG: Round(
                result.EstimatedGlycogenDepletedG * profile.TargetRestorationFraction),
            CarbohydrateReloadRequirementG: result.CarbohydrateReloadRequirementG,
            CarbohydrateReloadEnergyKcal: result.CarbohydrateReloadEnergyKcal,
            Regions:
                result.Regions
                    .Select(region =>
                        new Engine30WorkoutGlycogenRegionResult(
                            MuscleGroupCode: region.MuscleGroupCode,
                            WholeBodySkeletalMuscleFraction: region.WholeBodySkeletalMuscleFraction,
                            RegionBaselineGlycogenG: region.BaselineGlycogenG,
                            EffectiveRegionalDepletionFraction:
                                region.BaselineGlycogenG > 0m
                                    ? region.TotalDepletedG / region.BaselineGlycogenG
                                    : 0m,
                            EstimatedGlycogenDepletedG: region.TotalDepletedG))
                    .ToArray());
    }

    private IReadOnlyCollection<Engine30MuscleGlycogenInput> ResolveCompositeSegment(
        Engine30WorkoutGlycogenAdapterInput input,
        Engine30WorkoutAdminSegment configured,
        Engine30WorkoutSegmentInput runtime)
    {
        var profile = input.Profile;
        var model =
            (configured.GlycogenModelCode ?? "CONTINUOUS")
                .Trim()
                .ToUpperInvariant();

        var duration =
            SegmentDuration(runtime, configured.DurationMinutes);

        if (model == "RESISTANCE")
        {
            var details = runtime.Resistance;
            var scale =
                configured.DurationMinutes > 0m
                    ? duration / configured.DurationMinutes
                    : 1m;

            var configuredRegionCount =
                (configured.LowerBodyRecruitmentFraction > 0m ? 1 : 0)
                + (configured.UpperBodyRecruitmentFraction > 0m ? 1 : 0);

            var configuredTotalHardSets =
                configured.HardSetsPerRegion > 0m
                && configuredRegionCount > 0
                    ? configured.HardSetsPerRegion
                      * configuredRegionCount
                    : 0m;

            var totalSetScale =
                details?.TotalHardSets is > 0m
                && configuredTotalHardSets > 0m
                    ? details.TotalHardSets.Value
                      / configuredTotalHardSets
                    : scale;

            var source =
                SegmentResistanceGroups(
                    configured,
                    details,
                    totalSetScale)
                    .ToArray();

            if (source.Length == 0)
            {
                return [];
            }

            var workload =
                _resistance.Calculate(
                    new Engine30ResistanceGlycogenWorkloadInput(
                        DurationMinutes: duration,
                        TrainingStatusCode: input.ResistanceTrainingStatusCode,
                        MuscleGroups: source),
                    new Engine30ResistanceGlycogenWorkloadConfig(
                        ReferenceDurationMinutes: configured.DurationMinutes,
                        ReferenceHardSetsPerMuscleGroup: ReferenceHardSetsPerMuscleGroup,
                        ReferenceDepletionFraction: configured.GlycogenReferenceDepletionFraction,
                        HardSetSensitivity: ResistanceHardSetSensitivity,
                        DurationSensitivity: ResistanceDurationSensitivity,
                        TrainedFactor: 1.00m,
                        UntrainedFactor: 1.10m,
                        ReferencePercentOneRepMax: null,
                        PercentOneRepMaxSensitivity: 0m,
                        MinimumDepletionFraction: profile.GlycogenMinimumDepletionFraction,
                        MaximumDepletionFraction: profile.GlycogenMaximumDepletionFraction));

            return workload.ResolvedMuscleGroups;
        }

        if (model == "INTERVAL")
        {
            var details = runtime.Intervals;

            var workSeconds =
                details?.WorkIntervalSeconds
                ?? Positive(profile.IntervalWorkSeconds, 60m);

            var recoverySeconds =
                details?.RecoveryIntervalSeconds
                ?? Math.Max(0m, profile.IntervalRecoverySeconds);

            var repetitions =
                details?.Repetitions
                ?? RepetitionsForDuration(
                    duration,
                    workSeconds,
                    recoverySeconds);

            var referenceRepetitions =
                RepetitionsForDuration(
                    configured.DurationMinutes,
                    Positive(profile.IntervalWorkSeconds, 60m),
                    Math.Max(0m, profile.IntervalRecoverySeconds));

            var workload =
                _interval.Calculate(
                    new Engine30IntervalGlycogenWorkloadInput(
                        ModalityCode: runtime.ModalityCode,
                        WorkIntervalSeconds: workSeconds,
                        RecoveryIntervalSeconds: recoverySeconds,
                        Repetitions: repetitions,
                        RecoveryPeriods: recoverySeconds > 0m ? repetitions : 0,
                        WorkRpe: details?.WorkRpe ?? profile.IntervalWorkRpe,
                        RecoveryRpe: details?.RecoveryRpe ?? profile.IntervalRecoveryRpe),
                    new Engine30IntervalGlycogenWorkloadConfig(
                        ReferenceWorkIntervalSeconds: Positive(profile.IntervalWorkSeconds, 60m),
                        ReferenceRecoveryIntervalSeconds: Math.Max(0m, profile.IntervalRecoverySeconds),
                        ReferenceRepetitions: Math.Max(1, referenceRepetitions),
                        ReferenceRecoveryPeriods: profile.IntervalRecoverySeconds > 0m ? Math.Max(1, referenceRepetitions) : 0,
                        ReferenceDepletionFraction: configured.GlycogenReferenceDepletionFraction,
                        ReferenceWorkRpe: Math.Max(1m, profile.IntervalWorkRpe),
                        ReferenceRecoveryRpe: Math.Max(1m, profile.IntervalRecoveryRpe),
                        ReferenceObjectiveRelativeWorkIntensity: null,
                        ReferenceObjectiveRelativeRecoveryIntensity: null,
                        WorkIntensitySensitivity: profile.IntervalWorkIntensitySensitivity,
                        RecoveryIntensitySensitivity: profile.IntervalRecoveryIntensitySensitivity,
                        RecoveryContributionWeight: profile.IntervalRecoveryContributionWeight,
                        MinimumDepletionFraction: profile.GlycogenMinimumDepletionFraction,
                        MaximumDepletionFraction: profile.GlycogenMaximumDepletionFraction));

            return SegmentDemandGroups(
                configured,
                workload.ResolvedDepletionFraction);
        }

        var cardio =
            _cardio.Calculate(
                new Engine30CardioGlycogenWorkloadInput(
                    ModalityCode: runtime.ModalityCode,
                    DurationMinutes: duration,
                    SessionRpe: SegmentRpe(runtime, configured.DefaultRpe),
                    ObjectiveRelativeIntensity: null,
                    MonitorData: null),
                new Engine30CardioGlycogenWorkloadConfig(
                    ReferenceDurationMinutes: configured.DurationMinutes,
                    ReferenceDepletionFraction: configured.GlycogenReferenceDepletionFraction,
                    ReferenceRpe: Math.Max(1m, configured.DefaultRpe),
                    ReferenceObjectiveRelativeIntensity: null,
                    ReferenceHeartRateZoneIndex: null,
                    IntensitySensitivity: profile.GlycogenIntensitySensitivity,
                    Zone1Weight: 1m,
                    Zone2Weight: 2m,
                    Zone3Weight: 3m,
                    Zone4Weight: 4m,
                    Zone5Weight: 5m,
                    MinimumDepletionFraction: profile.GlycogenMinimumDepletionFraction,
                    MaximumDepletionFraction: profile.GlycogenMaximumDepletionFraction));

        return SegmentDemandGroups(
            configured,
            cardio.ResolvedDepletionFraction);
    }

    private Engine30GlycogenDemandResult CalculateDemand(
        Engine30WorkoutGlycogenAdapterInput input,
        IReadOnlyCollection<Engine30MuscleGlycogenInput> groups)
        => _demand.Calculate(
            new Engine30GlycogenDemandInput(
                FatFreeMassLb: input.FatFreeMassLb,
                SkeletalMuscleFractionOfFfm: input.SkeletalMuscleFractionOfFfm,
                BaselineGlycogenGPerKgMuscle: input.BaselineGlycogenGPerKgMuscle,
                TargetRestorationFraction: input.Profile.TargetRestorationFraction,
                CarbohydratePerGlycogenG: input.Profile.CarbohydratePerGlycogenG,
                MuscleGroups: groups));

    private static Engine30WorkoutGlycogenAdapterResult FromDemand(
        string modelCode,
        Engine30GlycogenDemandResult result,
        IReadOnlyCollection<Engine30MuscleGlycogenInput> inputs,
        decimal baselineGlycogenGPerKgMuscle)
    {
        var inputByCode =
            inputs.ToDictionary(
                x => x.MuscleGroupCode.Trim().ToUpperInvariant(),
                StringComparer.OrdinalIgnoreCase);

        var regions =
            result.MuscleGroups
                .Select(muscle =>
                {
                    var input =
                        inputByCode[muscle.MuscleGroupCode];

                    var regionBaseline =
                        muscle.EstimatedMuscleMassKg
                        * baselineGlycogenGPerKgMuscle;

                    return new Engine30WorkoutGlycogenRegionResult(
                        MuscleGroupCode:
                            muscle.MuscleGroupCode,
                        WholeBodySkeletalMuscleFraction:
                            input.WholeBodySkeletalMuscleFraction,
                        RegionBaselineGlycogenG:
                            Round(regionBaseline),
                        EffectiveRegionalDepletionFraction:
                            regionBaseline > 0m
                                ? Math.Round(
                                    muscle.EstimatedGlycogenDepletedG
                                    / regionBaseline,
                                    4,
                                    MidpointRounding.AwayFromZero)
                                : 0m,
                        EstimatedGlycogenDepletedG:
                            muscle.EstimatedGlycogenDepletedG);
                })
                .ToArray();

        return new Engine30WorkoutGlycogenAdapterResult(
            ModelCode: modelCode,
            EstimatedGlycogenDepletedG: result.EstimatedGlycogenDepletedG,
            TargetGlycogenRestorationG: result.TargetGlycogenRestorationG,
            CarbohydrateReloadRequirementG: result.CarbohydrateReloadRequirementG,
            CarbohydrateReloadEnergyKcal: result.CarbohydrateReloadEnergyKcal,
            Regions: regions);
    }

    private static Engine30WorkoutGlycogenAdapterResult Zero(string modelCode)
        => new(
            ModelCode: string.IsNullOrWhiteSpace(modelCode) ? "ZERO" : modelCode,
            EstimatedGlycogenDepletedG: 0m,
            TargetGlycogenRestorationG: 0m,
            CarbohydrateReloadRequirementG: 0m,
            CarbohydrateReloadEnergyKcal: 0m);

    private static Engine30WorkoutSegmentInput? FirstSegment(
        Engine30ResolvedWorkoutSession session)
        => session.Segments.FirstOrDefault();

    private static decimal SegmentDuration(
        Engine30WorkoutSegmentInput? segment,
        decimal fallback)
        => segment is null
            ? fallback
            : segment.ActualDurationMinutes
              ?? segment.PlannedDurationMinutes;

    private static decimal SegmentRpe(
        Engine30WorkoutSegmentInput? segment,
        decimal fallback)
        => segment is null
            ? fallback
            : segment.ActualRpe
              ?? segment.PlannedRpeOverride
              ?? segment.DefaultRpe;

    private static IEnumerable<Engine30WorkoutAdminMuscleRegion> ValidRegions(
        Engine30WorkoutAdminProfile profile)
        => profile.MuscleRegions.Where(x =>
            !string.IsNullOrWhiteSpace(x.MuscleRegionCode)
            && x.WholeBodyMuscleFraction > 0m
            && x.RecruitmentFraction > 0m);

    private static IEnumerable<Engine30ResistanceMuscleGlycogenWorkInput> SegmentResistanceGroups(
        Engine30WorkoutAdminSegment configured,
        Engine30ResistanceSegmentDetails? details,
        decimal defaultSetScale)
    {
        var overrides =
            details?.MuscleGroups?
                .ToDictionary(
                    x => x.MuscleGroupCode.Trim().ToUpperInvariant(),
                    StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, Engine30WorkoutMuscleWork>(
                StringComparer.OrdinalIgnoreCase);

        if (configured.LowerBodyMuscleFraction > 0m
            && configured.LowerBodyRecruitmentFraction > 0m)
        {
            overrides.TryGetValue("LOWER_BODY", out var lower);

            yield return new Engine30ResistanceMuscleGlycogenWorkInput(
                MuscleGroupCode: "LOWER_BODY",
                WholeBodySkeletalMuscleFraction: configured.LowerBodyMuscleFraction,
                RecruitmentFraction: lower?.RecruitmentFraction ?? configured.LowerBodyRecruitmentFraction,
                HardSets: lower?.HardSets ?? configured.HardSetsPerRegion * defaultSetScale,
                PercentOneRepMax: details?.AveragePercentOneRepMax);
        }

        if (configured.UpperBodyMuscleFraction > 0m
            && configured.UpperBodyRecruitmentFraction > 0m)
        {
            overrides.TryGetValue("UPPER_BODY", out var upper);

            yield return new Engine30ResistanceMuscleGlycogenWorkInput(
                MuscleGroupCode: "UPPER_BODY",
                WholeBodySkeletalMuscleFraction: configured.UpperBodyMuscleFraction,
                RecruitmentFraction: upper?.RecruitmentFraction ?? configured.UpperBodyRecruitmentFraction,
                HardSets: upper?.HardSets ?? configured.HardSetsPerRegion * defaultSetScale,
                PercentOneRepMax: details?.AveragePercentOneRepMax);
        }
    }

    private static IReadOnlyCollection<Engine30MuscleGlycogenInput> SegmentDemandGroups(
        Engine30WorkoutAdminSegment configured,
        decimal depletionFraction)
    {
        var groups = new List<Engine30MuscleGlycogenInput>();

        if (configured.LowerBodyMuscleFraction > 0m
            && configured.LowerBodyRecruitmentFraction > 0m)
        {
            groups.Add(new Engine30MuscleGlycogenInput(
                "LOWER_BODY",
                configured.LowerBodyMuscleFraction,
                configured.LowerBodyRecruitmentFraction,
                depletionFraction));
        }

        if (configured.UpperBodyMuscleFraction > 0m
            && configured.UpperBodyRecruitmentFraction > 0m)
        {
            groups.Add(new Engine30MuscleGlycogenInput(
                "UPPER_BODY",
                configured.UpperBodyMuscleFraction,
                configured.UpperBodyRecruitmentFraction,
                depletionFraction));
        }

        return groups;
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

    private static bool IsRecovery(
        Engine30WorkoutAdminProfile profile,
        Engine30ResolvedWorkoutSession session)
        => profile.WorkoutFamilyCode.Equals(
               Engine30WorkoutFamilyCodes.Recovery,
               StringComparison.OrdinalIgnoreCase)
           || profile.GlycogenModelCode.Equals(
               "ZERO",
               StringComparison.OrdinalIgnoreCase)
           || session.AnalysisDuration.Minutes == 0m;

    private static decimal Positive(decimal first, decimal second)
        => first > 0m ? first : second;

    private static decimal Positive(decimal first, decimal second, decimal third)
        => first > 0m ? first : second > 0m ? second : third;

    private static decimal Round(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static void ValidatePerson(
        Engine30WorkoutGlycogenAdapterInput input)
    {
        if (input.FatFreeMassLb <= 0m)
            throw new ArgumentOutOfRangeException(nameof(input.FatFreeMassLb));

        if (input.SkeletalMuscleFractionOfFfm <= 0m
            || input.SkeletalMuscleFractionOfFfm > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.SkeletalMuscleFractionOfFfm));
        }

        if (input.BaselineGlycogenGPerKgMuscle <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.BaselineGlycogenGPerKgMuscle));
        }
    }
}
