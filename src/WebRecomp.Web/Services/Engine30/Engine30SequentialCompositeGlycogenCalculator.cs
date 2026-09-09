namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Engine 3.0 sequential composite glycogen calculator.
///
/// PURPOSE
///
/// Composite sessions such as Orangetheory must NOT be modeled as one
/// artificial full-session HIIT exposure. Each segment should first be
/// resolved by the workload model appropriate to that segment:
///
///     treadmill/cardio
///     rowing/cardio or intervals
///     floor/resistance
///
/// Those resolved muscle-group exposures are then processed here IN ORDER.
///
/// The key problem solved by this calculator is overlapping muscle pools.
/// If treadmill and rowing both use LOWER_BODY, the second segment must not
/// behave as if the lower-body glycogen pool were completely fresh again.
///
/// CURRENT STATE MODEL
///
/// For each named, non-overlapping anatomical region:
///
///     baseline regional glycogen
///     -> segment 1 depletion
///     -> remaining regional glycogen
///     -> segment 2 depletion from the remaining pool
///     -> ...
///
/// Within a named region, remaining glycogen is modeled as proportionally
/// distributed across that region. This is an explicit engineering assumption,
/// not a claim that glycogen depletion is anatomically uniform in vivo.
///
/// IMPORTANT GUARDRAILS
///
/// - Uses individual FFM as the body-composition anchor.
/// - Uses resolved muscle-group exposures; it does not infer workload itself.
/// - Does not use workout calories.
/// - Does not turn carbohydrate restoration into an energy-expenditure bucket.
/// - Same named anatomical region must use the same whole-body muscle fraction
///   in every segment.
/// - Unique anatomical region fractions may not sum above 1.00.
/// - Segment order is preserved and materially affects overlapping pools.
/// </summary>

public sealed record Engine30CompositeGlycogenSegmentInput(
    string SegmentCode,
    IReadOnlyCollection<Engine30MuscleGlycogenInput> MuscleGroups);

public sealed record Engine30SequentialCompositeGlycogenInput(
    decimal FatFreeMassLb,
    decimal SkeletalMuscleFractionOfFfm,
    decimal BaselineGlycogenGPerKgMuscle,
    decimal TargetRestorationFraction,
    decimal CarbohydratePerGlycogenG,
    IReadOnlyCollection<Engine30CompositeGlycogenSegmentInput> Segments);

public sealed record Engine30SequentialGlycogenExposureResult(
    string SegmentCode,
    string MuscleGroupCode,

    decimal WholeBodySkeletalMuscleFraction,
    decimal RecruitmentFraction,
    decimal DepletionFraction,

    decimal RegionMuscleMassKg,
    decimal RegionBaselineGlycogenG,

    decimal RegionRemainingBeforeG,
    decimal RecruitedAvailableGlycogenG,
    decimal SegmentGlycogenDepletedG,
    decimal RegionRemainingAfterG);

public sealed record Engine30SequentialGlycogenRegionResult(
    string MuscleGroupCode,
    decimal WholeBodySkeletalMuscleFraction,
    decimal RegionMuscleMassKg,
    decimal BaselineGlycogenG,
    decimal TotalDepletedG,
    decimal RemainingGlycogenG,
    decimal RemainingFraction);

public sealed record Engine30SequentialCompositeGlycogenResult(
    decimal FatFreeMassKg,
    decimal WholeBodySkeletalMuscleMassKg,

    IReadOnlyList<Engine30SequentialGlycogenExposureResult> Exposures,
    IReadOnlyList<Engine30SequentialGlycogenRegionResult> Regions,

    decimal EstimatedGlycogenDepletedG,
    decimal CarbohydrateReloadRequirementG,
    decimal CarbohydrateReloadEnergyKcal);

public sealed class Engine30SequentialCompositeGlycogenCalculator
{
    private const decimal PoundsPerKg = 2.2046226218m;

    public Engine30SequentialCompositeGlycogenResult Calculate(
        Engine30SequentialCompositeGlycogenInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Segments);

        ValidateInput(input);

        var fatFreeMassKg =
            input.FatFreeMassLb
            / PoundsPerKg;

        var wholeBodySkeletalMuscleKg =
            fatFreeMassKg
            * input.SkeletalMuscleFractionOfFfm;

        var regionDefinitions =
            BuildRegionDefinitions(
                input.Segments);

        var regionStates =
            regionDefinitions.ToDictionary(
                x => x.Key,
                x =>
                {
                    var regionMuscleKg =
                        wholeBodySkeletalMuscleKg
                        * x.Value;

                    var baselineGlycogenG =
                        regionMuscleKg
                        * input.BaselineGlycogenGPerKgMuscle;

                    return new RegionState(
                        MuscleGroupCode:
                            x.Key,

                        WholeBodySkeletalMuscleFraction:
                            x.Value,

                        RegionMuscleMassKg:
                            regionMuscleKg,

                        BaselineGlycogenG:
                            baselineGlycogenG,

                        RemainingGlycogenG:
                            baselineGlycogenG);
                },
                StringComparer.OrdinalIgnoreCase);

        var exposures =
            new List<Engine30SequentialGlycogenExposureResult>();

        foreach (var segment in input.Segments)
        {
            var segmentCode =
                NormalizeRequiredCode(
                    segment.SegmentCode,
                    nameof(segment.SegmentCode));

            foreach (var muscle in segment.MuscleGroups)
            {
                var muscleCode =
                    NormalizeRequiredCode(
                        muscle.MuscleGroupCode,
                        nameof(muscle.MuscleGroupCode));

                var state =
                    regionStates[muscleCode];

                var remainingBefore =
                    state.RemainingGlycogenG;

                /*
                    Proportional-region state model:

                    If 70% of a region is effectively recruited, that exposure
                    can act on 70% of the glycogen currently remaining in the
                    named region. The requested depletion fraction is then
                    applied to that recruited available amount.

                    Because state.RemainingGlycogenG is updated after every
                    segment, later overlapping segments automatically see a
                    partially depleted pool.
                */
                var recruitedAvailable =
                    remainingBefore
                    * muscle.RecruitmentFraction;

                var segmentDepleted =
                    recruitedAvailable
                    * muscle.DepletionFraction;

                segmentDepleted =
                    Math.Min(
                        remainingBefore,
                        Math.Max(
                            0m,
                            segmentDepleted));

                var remainingAfter =
                    Math.Max(
                        0m,
                        remainingBefore
                        - segmentDepleted);

                state.RemainingGlycogenG =
                    remainingAfter;

                exposures.Add(
                    new Engine30SequentialGlycogenExposureResult(
                        SegmentCode:
                            segmentCode,

                        MuscleGroupCode:
                            muscleCode,

                        WholeBodySkeletalMuscleFraction:
                            Round(
                                muscle.WholeBodySkeletalMuscleFraction,
                                4),

                        RecruitmentFraction:
                            Round(
                                muscle.RecruitmentFraction,
                                4),

                        DepletionFraction:
                            Round(
                                muscle.DepletionFraction,
                                4),

                        RegionMuscleMassKg:
                            Round(
                                state.RegionMuscleMassKg,
                                2),

                        RegionBaselineGlycogenG:
                            Round(
                                state.BaselineGlycogenG,
                                2),

                        RegionRemainingBeforeG:
                            Round(
                                remainingBefore,
                                2),

                        RecruitedAvailableGlycogenG:
                            Round(
                                recruitedAvailable,
                                2),

                        SegmentGlycogenDepletedG:
                            Round(
                                segmentDepleted,
                                2),

                        RegionRemainingAfterG:
                            Round(
                                remainingAfter,
                                2)));
            }
        }

        var regions =
            regionStates.Values
                .OrderBy(
                    x => x.MuscleGroupCode,
                    StringComparer.OrdinalIgnoreCase)
                .Select(
                    x =>
                    {
                        var depleted =
                            x.BaselineGlycogenG
                            - x.RemainingGlycogenG;

                        var remainingFraction =
                            x.BaselineGlycogenG == 0m
                                ? 0m
                                : x.RemainingGlycogenG
                                  / x.BaselineGlycogenG;

                        return new Engine30SequentialGlycogenRegionResult(
                            MuscleGroupCode:
                                x.MuscleGroupCode,

                            WholeBodySkeletalMuscleFraction:
                                Round(
                                    x.WholeBodySkeletalMuscleFraction,
                                    4),

                            RegionMuscleMassKg:
                                Round(
                                    x.RegionMuscleMassKg,
                                    2),

                            BaselineGlycogenG:
                                Round(
                                    x.BaselineGlycogenG,
                                    2),

                            TotalDepletedG:
                                Round(
                                    depleted,
                                    2),

                            RemainingGlycogenG:
                                Round(
                                    x.RemainingGlycogenG,
                                    2),

                            RemainingFraction:
                                Round(
                                    remainingFraction,
                                    4));
                    })
                .ToList();

        var totalDepleted =
            regionStates.Values.Sum(
                x =>
                    x.BaselineGlycogenG
                    - x.RemainingGlycogenG);

        var restorationG =
            totalDepleted
            * input.TargetRestorationFraction;

        var carbohydrateReloadG =
            restorationG
            * input.CarbohydratePerGlycogenG;

        var reloadEnergyKcal =
            carbohydrateReloadG
            * 4m;

        return new Engine30SequentialCompositeGlycogenResult(
            FatFreeMassKg:
                Round(
                    fatFreeMassKg,
                    2),

            WholeBodySkeletalMuscleMassKg:
                Round(
                    wholeBodySkeletalMuscleKg,
                    2),

            Exposures:
                exposures,

            Regions:
                regions,

            EstimatedGlycogenDepletedG:
                Round(
                    totalDepleted,
                    2),

            CarbohydrateReloadRequirementG:
                Round(
                    carbohydrateReloadG,
                    2),

            CarbohydrateReloadEnergyKcal:
                Round(
                    reloadEnergyKcal,
                    2));
    }

    private static IReadOnlyDictionary<string, decimal> BuildRegionDefinitions(
        IReadOnlyCollection<Engine30CompositeGlycogenSegmentInput> segments)
    {
        var definitions =
            new Dictionary<string, decimal>(
                StringComparer.OrdinalIgnoreCase);

        decimal uniqueFractionTotal = 0m;

        foreach (var segment in segments)
        {
            ArgumentNullException.ThrowIfNull(segment);
            ArgumentNullException.ThrowIfNull(segment.MuscleGroups);

            NormalizeRequiredCode(
                segment.SegmentCode,
                nameof(segment.SegmentCode));

            var segmentSeen =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var muscle in segment.MuscleGroups)
            {
                ArgumentNullException.ThrowIfNull(muscle);

                var code =
                    NormalizeRequiredCode(
                        muscle.MuscleGroupCode,
                        nameof(muscle.MuscleGroupCode));

                if (!segmentSeen.Add(code))
                {
                    throw new ArgumentException(
                        $"Segment '{segment.SegmentCode}' contains duplicate muscle region '{code}'.");
                }

                ValidateFraction(
                    muscle.WholeBodySkeletalMuscleFraction,
                    nameof(muscle.WholeBodySkeletalMuscleFraction),
                    allowZero:
                        false);

                ValidateFraction(
                    muscle.RecruitmentFraction,
                    nameof(muscle.RecruitmentFraction),
                    allowZero:
                        true);

                ValidateFraction(
                    muscle.DepletionFraction,
                    nameof(muscle.DepletionFraction),
                    allowZero:
                        true);

                if (definitions.TryGetValue(
                        code,
                        out var existingFraction))
                {
                    if (existingFraction
                        != muscle.WholeBodySkeletalMuscleFraction)
                    {
                        throw new ArgumentException(
                            $"Muscle region '{code}' uses inconsistent whole-body skeletal-muscle fractions across segments.");
                    }
                }
                else
                {
                    definitions.Add(
                        code,
                        muscle.WholeBodySkeletalMuscleFraction);

                    uniqueFractionTotal +=
                        muscle.WholeBodySkeletalMuscleFraction;
                }
            }
        }

        if (uniqueFractionTotal > 1m)
        {
            throw new ArgumentException(
                "Unique composite muscle-region whole-body fractions cannot sum above 1.00.");
        }

        return definitions;
    }

    private static void ValidateInput(
        Engine30SequentialCompositeGlycogenInput input)
    {
        if (input.FatFreeMassLb <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.FatFreeMassLb));
        }

        ValidateFraction(
            input.SkeletalMuscleFractionOfFfm,
            nameof(input.SkeletalMuscleFractionOfFfm),
            allowZero:
                false);

        if (input.BaselineGlycogenGPerKgMuscle <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.BaselineGlycogenGPerKgMuscle));
        }

        ValidateFraction(
            input.TargetRestorationFraction,
            nameof(input.TargetRestorationFraction),
            allowZero:
                true);

        if (input.CarbohydratePerGlycogenG <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.CarbohydratePerGlycogenG));
        }

        if (input.Segments.Count == 0)
        {
            throw new ArgumentException(
                "Composite glycogen calculation requires at least one segment.",
                nameof(input.Segments));
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

    private static decimal Round(
        decimal value,
        int decimals = 2)
        => Math.Round(
            value,
            decimals,
            MidpointRounding.AwayFromZero);

    private sealed class RegionState
    {
        public RegionState(
            string MuscleGroupCode,
            decimal WholeBodySkeletalMuscleFraction,
            decimal RegionMuscleMassKg,
            decimal BaselineGlycogenG,
            decimal RemainingGlycogenG)
        {
            this.MuscleGroupCode =
                MuscleGroupCode;

            this.WholeBodySkeletalMuscleFraction =
                WholeBodySkeletalMuscleFraction;

            this.RegionMuscleMassKg =
                RegionMuscleMassKg;

            this.BaselineGlycogenG =
                BaselineGlycogenG;

            this.RemainingGlycogenG =
                RemainingGlycogenG;
        }

        public string MuscleGroupCode { get; }

        public decimal WholeBodySkeletalMuscleFraction { get; }

        public decimal RegionMuscleMassKg { get; }

        public decimal BaselineGlycogenG { get; }

        public decimal RemainingGlycogenG { get; set; }
    }
}