using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30OrangetheoryCompositeGlycogenIntegrationTests
{
    private readonly Engine30CardioGlycogenWorkloadCalculator _cardio = new();
    private readonly Engine30IntervalGlycogenWorkloadCalculator _interval = new();
    private readonly Engine30ResistanceGlycogenWorkloadCalculator _resistance = new();
    private readonly Engine30CardioGlycogenMuscleProfileResolver _profile = new();
    private readonly Engine30SequentialCompositeGlycogenCalculator _sequential = new();
    private readonly Engine30GlycogenDemandCalculator _demand = new();
    private readonly ITestOutputHelper _output;

    public Engine30OrangetheoryCompositeGlycogenIntegrationTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Orangetheory_Tread_Row_Floor_UsesSegmentSpecificModels_AndSharedGlycogenState()
    {
        /*
            DRAFT OTF integration scenario.

            This is NOT a claim that every Orangetheory template is exactly:
                20 min tread
                20 min row
                20 min floor

            The purpose is architectural:
            prove one composite workout can route different segments through
            different physiological models and then carry shared muscle-region
            glycogen forward without treating each segment as a fresh pool.

            Segment model choices in this test:
                TREAD -> continuous cardio
                ROW   -> structured intervals
                FLOOR -> resistance hard-set model

            All calibration/anatomy coefficients remain DRAFT test anchors.
        */

        const decimal fatFreeMassLb = 144m;
        const decimal skeletalMuscleFractionOfFfm = 0.528m;
        const decimal baselineGlycogenGPerKgMuscle = 23m;

        // ------------------------------------------------------------
        // 1. TREADMILL: continuous-cardio workload -> lower-body profile.
        // ------------------------------------------------------------

        var treadWorkload =
            _cardio.Calculate(
                new Engine30CardioGlycogenWorkloadInput(
                    ModalityCode:
                        "TREADMILL",

                    DurationMinutes:
                        20m,

                    SessionRpe:
                        7m),

                new Engine30CardioGlycogenWorkloadConfig(
                    ReferenceDurationMinutes:
                        20m,

                    ReferenceDepletionFraction:
                        0.14m,

                    ReferenceRpe:
                        7m,

                    ReferenceObjectiveRelativeIntensity:
                        null,

                    ReferenceHeartRateZoneIndex:
                        null,

                    IntensitySensitivity:
                        0.50m,

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
                        0.01m,

                    MaximumDepletionFraction:
                        0.60m));

        var treadProfile =
            _profile.Resolve(
                new Engine30GlycogenMuscleProfileExposure(
                    ModalityCode:
                        "TREADMILL",

                    DurationMinutes:
                        20m,

                    ResolvedDepletionFraction:
                        treadWorkload.ResolvedDepletionFraction),

                new Engine30CardioGlycogenModalityProfile(
                    ProfileCode:
                        "TEST_OTF_TREAD",

                    ModalityCode:
                        "TREADMILL",

                    SourceCode:
                        Engine30CardioMuscleProfileSourceCodes.ConfigDefault,

                    ConfidenceCode:
                        Engine30CardioMuscleProfileConfidenceCodes.Medium,

                    MuscleRegions:
                        new[]
                        {
                            new Engine30CardioMuscleRegionProfile(
                                MuscleGroupCode:
                                    "LOWER_BODY",

                                WholeBodySkeletalMuscleFraction:
                                    0.50m,

                                RecruitmentFraction:
                                    0.70m)
                        }));

        // ------------------------------------------------------------
        // 2. ROWER: interval workload -> lower + upper-body profile.
        // ------------------------------------------------------------

        var rowWorkload =
            _interval.Calculate(
                new Engine30IntervalGlycogenWorkloadInput(
                    ModalityCode:
                        "ROWING",

                    WorkIntervalSeconds:
                        60m,

                    RecoveryIntervalSeconds:
                        60m,

                    Repetitions:
                        10,

                    RecoveryPeriods:
                        10,

                    WorkRpe:
                        8m,

                    RecoveryRpe:
                        3m),

                new Engine30IntervalGlycogenWorkloadConfig(
                    ReferenceWorkIntervalSeconds:
                        60m,

                    ReferenceRecoveryIntervalSeconds:
                        60m,

                    ReferenceRepetitions:
                        10,

                    ReferenceRecoveryPeriods:
                        10,

                    ReferenceDepletionFraction:
                        0.18m,

                    ReferenceWorkRpe:
                        8m,

                    ReferenceRecoveryRpe:
                        3m,

                    ReferenceObjectiveRelativeWorkIntensity:
                        null,

                    ReferenceObjectiveRelativeRecoveryIntensity:
                        null,

                    WorkIntensitySensitivity:
                        0.50m,

                    RecoveryIntensitySensitivity:
                        0.25m,

                    RecoveryContributionWeight:
                        0.25m,

                    MinimumDepletionFraction:
                        0.01m,

                    MaximumDepletionFraction:
                        0.70m));

        var rowTotalMinutes =
            rowWorkload.WorkPhase.ActualMinutes
            + rowWorkload.RecoveryPhase.ActualMinutes;

        var rowProfile =
            _profile.Resolve(
                new Engine30GlycogenMuscleProfileExposure(
                    ModalityCode:
                        "ROWING",

                    DurationMinutes:
                        rowTotalMinutes,

                    ResolvedDepletionFraction:
                        rowWorkload.ResolvedDepletionFraction),

                new Engine30CardioGlycogenModalityProfile(
                    ProfileCode:
                        "TEST_OTF_ROW",

                    ModalityCode:
                        "ROWING",

                    SourceCode:
                        Engine30CardioMuscleProfileSourceCodes.ConfigDefault,

                    ConfidenceCode:
                        Engine30CardioMuscleProfileConfidenceCodes.Medium,

                    MuscleRegions:
                        new[]
                        {
                            new Engine30CardioMuscleRegionProfile(
                                MuscleGroupCode:
                                    "LOWER_BODY",

                                WholeBodySkeletalMuscleFraction:
                                    0.50m,

                                RecruitmentFraction:
                                    0.75m),

                            new Engine30CardioMuscleRegionProfile(
                                MuscleGroupCode:
                                    "UPPER_BODY",

                                WholeBodySkeletalMuscleFraction:
                                    0.20m,

                                RecruitmentFraction:
                                    0.45m)
                        }));

        // ------------------------------------------------------------
        // 3. FLOOR: resistance hard-set workload.
        //
        // Hard sets are known, so duration is diagnostic and does not
        // independently multiply resistance glycogen depletion.
        // ------------------------------------------------------------

        var floorWorkload =
            _resistance.Calculate(
                new Engine30ResistanceGlycogenWorkloadInput(
                    DurationMinutes:
                        20m,

                    TrainingStatusCode:
                        Engine30ResistanceGlycogenTrainingStatusCodes.Trained,

                    MuscleGroups:
                        new[]
                        {
                            new Engine30ResistanceMuscleGlycogenWorkInput(
                                MuscleGroupCode:
                                    "LOWER_BODY",

                                WholeBodySkeletalMuscleFraction:
                                    0.50m,

                                RecruitmentFraction:
                                    0.60m,

                                HardSets:
                                    4m,

                                PercentOneRepMax:
                                    null),

                            new Engine30ResistanceMuscleGlycogenWorkInput(
                                MuscleGroupCode:
                                    "UPPER_BODY",

                                WholeBodySkeletalMuscleFraction:
                                    0.20m,

                                RecruitmentFraction:
                                    0.70m,

                                HardSets:
                                    4m,

                                PercentOneRepMax:
                                    null)
                        }),

                new Engine30ResistanceGlycogenWorkloadConfig(
                    ReferenceDurationMinutes:
                        60m,

                    ReferenceHardSetsPerMuscleGroup:
                        8m,

                    ReferenceDepletionFraction:
                        0.28m,

                    HardSetSensitivity:
                        0.50m,

                    DurationSensitivity:
                        0.25m,

                    TrainedFactor:
                        1.00m,

                    UntrainedFactor:
                        1.10m,

                    ReferencePercentOneRepMax:
                        70m,

                    PercentOneRepMaxSensitivity:
                        0.25m,

                    MinimumDepletionFraction:
                        0.05m,

                    MaximumDepletionFraction:
                        0.60m));

        // ------------------------------------------------------------
        // 4. Sequential composite glycogen state.
        // ------------------------------------------------------------

        var composite =
            _sequential.Calculate(
                new Engine30SequentialCompositeGlycogenInput(
                    FatFreeMassLb:
                        fatFreeMassLb,

                    SkeletalMuscleFractionOfFfm:
                        skeletalMuscleFractionOfFfm,

                    BaselineGlycogenGPerKgMuscle:
                        baselineGlycogenGPerKgMuscle,

                    TargetRestorationFraction:
                        1.00m,

                    CarbohydratePerGlycogenG:
                        1.00m,

                    Segments:
                        new[]
                        {
                            new Engine30CompositeGlycogenSegmentInput(
                                SegmentCode:
                                    "TREAD",

                                MuscleGroups:
                                    treadProfile.ResolvedMuscleGroups),

                            new Engine30CompositeGlycogenSegmentInput(
                                SegmentCode:
                                    "ROW",

                                MuscleGroups:
                                    rowProfile.ResolvedMuscleGroups),

                            new Engine30CompositeGlycogenSegmentInput(
                                SegmentCode:
                                    "FLOOR",

                                MuscleGroups:
                                    floorWorkload.ResolvedMuscleGroups)
                        }));

        /*
            Comparison case: calculate every segment independently against
            a completely fresh glycogen pool.

            This is the behavior the sequential composite calculator exists
            to prevent when anatomical regions overlap.
        */
        var naiveFreshPoolTotal =
            FreshDemand(
                fatFreeMassLb,
                skeletalMuscleFractionOfFfm,
                baselineGlycogenGPerKgMuscle,
                treadProfile.ResolvedMuscleGroups)
            +
            FreshDemand(
                fatFreeMassLb,
                skeletalMuscleFractionOfFfm,
                baselineGlycogenGPerKgMuscle,
                rowProfile.ResolvedMuscleGroups)
            +
            FreshDemand(
                fatFreeMassLb,
                skeletalMuscleFractionOfFfm,
                baselineGlycogenGPerKgMuscle,
                floorWorkload.ResolvedMuscleGroups);

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 - ORANGETHEORY COMPOSITE GLYCOGEN");

        _output.WriteLine(
            "DRAFT segment/config anchors; validates treadmill -> rower -> floor orchestration.");

        _output.WriteLine("");
        _output.WriteLine(
            new string('-', 142));

        _output.WriteLine(
            $"{"Segment",-10} " +
            $"{"Region",-14} " +
            $"{"BeforeG",10} " +
            $"{"RecruitFx",10} " +
            $"{"DepleteFx",10} " +
            $"{"SegmentG",10} " +
            $"{"AfterG",10}");

        _output.WriteLine(
            new string('-', 142));

        foreach (var exposure in composite.Exposures)
        {
            _output.WriteLine(
                $"{exposure.SegmentCode,-10} " +
                $"{exposure.MuscleGroupCode,-14} " +
                $"{exposure.RegionRemainingBeforeG,10:0.00} " +
                $"{exposure.RecruitmentFraction,10:0.000} " +
                $"{exposure.DepletionFraction,10:0.0000} " +
                $"{exposure.SegmentGlycogenDepletedG,10:0.00} " +
                $"{exposure.RegionRemainingAfterG,10:0.00}");
        }

        _output.WriteLine(
            new string('-', 142));

        _output.WriteLine(
            $"Tread depletion fraction    = {treadWorkload.ResolvedDepletionFraction:0.0000}");

        _output.WriteLine(
            $"Row depletion fraction      = {rowWorkload.ResolvedDepletionFraction:0.0000}");

        _output.WriteLine(
            $"Floor lower depletion       = {floorWorkload.MuscleResults.Single(x => x.MuscleGroupCode == "LOWER_BODY").ResolvedDepletionFraction:0.0000}");

        _output.WriteLine(
            $"Floor upper depletion       = {floorWorkload.MuscleResults.Single(x => x.MuscleGroupCode == "UPPER_BODY").ResolvedDepletionFraction:0.0000}");

        _output.WriteLine(
            $"Sequential OTF depleted     = {composite.EstimatedGlycogenDepletedG:0.00} g");

        _output.WriteLine(
            $"Naive fresh-pool total      = {naiveFreshPoolTotal:0.00} g");

        _output.WriteLine(
            $"Carbohydrate reload         = {composite.CarbohydrateReloadRequirementG:0.00} g");

        _output.WriteLine(
            $"Reload diagnostic energy    = {composite.CarbohydrateReloadEnergyKcal:0.00} kcal");

        // ------------------------------------------------------------
        // Assertions.
        // ------------------------------------------------------------

        Assert.Equal(
            5,
            composite.Exposures.Count);

        Assert.Equal(
            new[]
            {
                "TREAD",
                "ROW",
                "ROW",
                "FLOOR",
                "FLOOR"
            },
            composite.Exposures.Select(
                x => x.SegmentCode).ToArray());

        var treadLower =
            composite.Exposures.Single(
                x =>
                    x.SegmentCode == "TREAD"
                    && x.MuscleGroupCode == "LOWER_BODY");

        var rowLower =
            composite.Exposures.Single(
                x =>
                    x.SegmentCode == "ROW"
                    && x.MuscleGroupCode == "LOWER_BODY");

        var floorLower =
            composite.Exposures.Single(
                x =>
                    x.SegmentCode == "FLOOR"
                    && x.MuscleGroupCode == "LOWER_BODY");

        Assert.Equal(
            treadLower.RegionRemainingAfterG,
            rowLower.RegionRemainingBeforeG);

        Assert.Equal(
            rowLower.RegionRemainingAfterG,
            floorLower.RegionRemainingBeforeG);

        Assert.True(
            composite.EstimatedGlycogenDepletedG
            < naiveFreshPoolTotal);

        Assert.Equal(
            composite.EstimatedGlycogenDepletedG,
            composite.CarbohydrateReloadRequirementG);

        Assert.InRange(
            Math.Abs(
                composite.CarbohydrateReloadEnergyKcal
                - composite.CarbohydrateReloadRequirementG * 4m),
            0m,
            0.03m);

        /*
            Architectural guardrail:
            this composite is not represented by one full-hour RPE/HIIT
            glycogen calculation. It contains separate upstream model outputs
            for continuous cardio, intervals, and resistance.
        */
        Assert.Equal(
            Engine30CardioGlycogenIntensitySourceCodes.RpeFallback,
            treadWorkload.IntensitySourceCode);

        Assert.Equal(
            Engine30IntervalGlycogenIntensitySourceCodes.StructuredRpe,
            rowWorkload.WorkPhase.SourceCode);

        Assert.All(
            floorWorkload.MuscleResults,
            x =>
                Assert.Equal(
                    Engine30ResistanceGlycogenWorkloadSourceCodes.HardSets,
                    x.WorkloadSourceCode));
    }

    private decimal FreshDemand(
        decimal fatFreeMassLb,
        decimal skeletalMuscleFractionOfFfm,
        decimal baselineGlycogenGPerKgMuscle,
        IReadOnlyCollection<Engine30MuscleGlycogenInput> muscleGroups)
    {
        return _demand.Calculate(
                new Engine30GlycogenDemandInput(
                    FatFreeMassLb:
                        fatFreeMassLb,

                    SkeletalMuscleFractionOfFfm:
                        skeletalMuscleFractionOfFfm,

                    BaselineGlycogenGPerKgMuscle:
                        baselineGlycogenGPerKgMuscle,

                    TargetRestorationFraction:
                        1.00m,

                    CarbohydratePerGlycogenG:
                        1.00m,

                    MuscleGroups:
                        muscleGroups))
            .EstimatedGlycogenDepletedG;
    }
}