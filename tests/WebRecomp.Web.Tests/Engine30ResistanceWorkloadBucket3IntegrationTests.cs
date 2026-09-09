using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30ResistanceWorkloadBucket3IntegrationTests
{
    private const decimal BaseLivingKcal = 1716.8m;
    private const decimal WeightLb = 180m;
    private const decimal FatFreeMassLb = 144m;

    private readonly Engine30WorkoutTypeCatalog _catalog = new();
    private readonly Engine30WorkoutSessionResolver _resolver = new();
    private readonly Engine30WorkoutEnergyAdapter _adapter = new();
    private readonly ITestOutputHelper _output;

    public Engine30ResistanceWorkloadBucket3IntegrationTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void HardSetDensity_ChangesBucket3_WithoutDoubleCountingRest()
    {
        /*
            DRAFT integration calibration only.

            Reference:
            60 min
            RPE 8
            16 hard sets
            90 sec rest

            We isolate hard-set density by holding person, duration,
            RPE, modality, and reference calibration constant.
        */

        var twelveSets =
            Calculate(
                hardSets: 12m,
                restSeconds: 90m);

        var sixteenSets =
            Calculate(
                hardSets: 16m,
                restSeconds: 90m);

        var sixteenSetsShortRest =
            Calculate(
                hardSets: 16m,
                restSeconds: 60m);

        var twentySets =
            Calculate(
                hardSets: 20m,
                restSeconds: 90m);

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 - RESISTANCE HARD-SET DENSITY -> BUCKET #3");

        _output.WriteLine(
            "DRAFT calibration; validates directional integration only.");

        _output.WriteLine("");
        _output.WriteLine(
            new string('-', 102));

        _output.WriteLine(
            $"{"Case",-24} " +
            $"{"Sets",6} " +
            $"{"Rest",7} " +
            $"{"SetRatio",9} " +
            $"{"Workload",9} " +
            $"{"Gross",10} " +
            $"{"RestKcal",10} " +
            $"{"B3 Net",10}");

        _output.WriteLine(
            new string('-', 102));

        Print(
            "12 sets",
            twelveSets,
            12m,
            90m);

        Print(
            "16 sets reference",
            sixteenSets,
            16m,
            90m);

        Print(
            "16 sets short rest",
            sixteenSetsShortRest,
            16m,
            60m);

        Print(
            "20 sets",
            twentySets,
            20m,
            90m);

        _output.WriteLine(
            new string('-', 102));

        var twelveWork =
            AssertWorkload(
                twelveSets);

        var sixteenWork =
            AssertWorkload(
                sixteenSets);

        var sixteenShortWork =
            AssertWorkload(
                sixteenSetsShortRest);

        var twentyWork =
            AssertWorkload(
                twentySets);

        Assert.Equal(
            0.7500m,
            twelveWork.HardSetDensityRatio);

        Assert.Equal(
            0.8750m,
            twelveWork.WorkloadFactor);

        Assert.Equal(
            1.0000m,
            sixteenWork.HardSetDensityRatio);

        Assert.Equal(
            1.0000m,
            sixteenWork.WorkloadFactor);

        Assert.Equal(
            1.0000m,
            sixteenShortWork.HardSetDensityRatio);

        Assert.Equal(
            1.0000m,
            sixteenShortWork.WorkloadFactor);

        Assert.Equal(
            1.5000m,
            sixteenShortWork.RestDensityRatio);

        Assert.Equal(
            1.2500m,
            twentyWork.HardSetDensityRatio);

        Assert.Equal(
            1.1250m,
            twentyWork.WorkloadFactor);

        // Hard-set density changes Bucket #3 directionally.
        Assert.True(
            twelveSets.EngineNetWorkoutEnergyKcal
            < sixteenSets.EngineNetWorkoutEnergyKcal);

        Assert.True(
            sixteenSets.EngineNetWorkoutEnergyKcal
            < twentySets.EngineNetWorkoutEnergyKcal);

        // Same sets + same duration + same RPE:
        // shorter rest remains diagnostic but is not double-counted.
        Assert.Equal(
            sixteenSets.EngineNetWorkoutEnergyKcal,
            sixteenSetsShortRest.EngineNetWorkoutEnergyKcal);
    }

    private Engine30WorkoutEnergyAdapterResult Calculate(
        decimal hardSets,
        decimal restSeconds)
    {
        var profile =
            _catalog.GetProfile(
                "MAJOR_LEGS");

        var session =
            _resolver.Resolve(
                profile,
                new Engine30WorkoutSessionInput(
                    WorkoutTypeCode:
                        "MAJOR_LEGS",

                    PlannedDurationMinutes:
                        60m,

                    Segments:
                        new[]
                        {
                            new Engine30WorkoutSegmentInput(
                                SegmentCode:
                                    "LEGS",

                                ModalityCode:
                                    Engine30WorkoutModalityCodes.Resistance,

                                PlannedDurationMinutes:
                                    60m,

                                DefaultRpe:
                                    8m,

                                Resistance:
                                    new Engine30ResistanceSegmentDetails(
                                        TotalHardSets:
                                            hardSets,

                                        AverageRestSeconds:
                                            restSeconds))
                        }));

        var calibration =
            new Engine30WorkoutEnergyCalibration(
                CalibrationCode:
                    "TEST_RESISTANCE_MAJOR_WORKLOAD",

                ModalityCode:
                    Engine30WorkoutModalityCodes.Resistance,

                ReferenceWorkoutCaloriesPerMinute:
                    5.5m,

                ReferenceWeightLb:
                    180m,

                ScalingBasisCode:
                    "FAT_FREE_MASS",

                ReferenceFatFreeMassLb:
                    130m,

                DefaultRestSeconds:
                    90m,

                ReferenceRpe:
                    8m,

                ResistanceWorkloadConfig:
                    new Engine30ResistanceEnergyWorkloadConfig(
                        ReferenceDurationMinutes:
                            60m,

                        ReferenceHardSets:
                            16m,

                        ReferenceRestSeconds:
                            90m,

                        HardSetDensitySensitivity:
                            0.50m,

                        RestFallbackSensitivity:
                            0.25m,

                        MinimumWorkloadFactor:
                            0.85m,

                        MaximumWorkloadFactor:
                            1.15m));

        return _adapter.Calculate(
            new Engine30WorkoutEnergyAdapterInput(
                Session:
                    session,

                BaseLivingEnergyKcal:
                    BaseLivingKcal,

                WeightLb:
                    WeightLb,

                FatFreeMassLb:
                    FatFreeMassLb,

                SegmentCalibrations:
                    new[]
                    {
                        new Engine30WorkoutSegmentEnergyCalibration(
                            SegmentCode:
                                "LEGS",

                            Calibration:
                                calibration)
                    }));
    }

    private static Engine30ResistanceEnergyWorkloadResult AssertWorkload(
        Engine30WorkoutEnergyAdapterResult result)
    {
        Assert.Single(
            result.Segments);

        Assert.NotNull(
            result.Segments[0].ResistanceWorkload);

        Assert.Equal(
            Engine30ResistanceEnergyWorkloadSourceCodes.HardSetDensity,
            result.Segments[0].ResistanceWorkload!.SourceCode);

        return result.Segments[0].ResistanceWorkload!;
    }

    private void Print(
        string label,
        Engine30WorkoutEnergyAdapterResult result,
        decimal hardSets,
        decimal restSeconds)
    {
        var workload =
            AssertWorkload(
                result);

        _output.WriteLine(
            $"{label,-24} " +
            $"{hardSets,6:0} " +
            $"{restSeconds,7:0} " +
            $"{workload.HardSetDensityRatio,9:0.000} " +
            $"{workload.WorkloadFactor,9:0.000} " +
            $"{result.GrossWorkoutEnergyKcal,10:0.0} " +
            $"{result.RestingEnergyDuringWorkoutKcal,10:0.0} " +
            $"{result.EngineNetWorkoutEnergyKcal,10:0.0}");
    }
}