using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30ResistanceMultiRegionGlycogenIntegrationTests
{
    private readonly Engine30ResistanceGlycogenWorkloadCalculator _workload = new();
    private readonly Engine30GlycogenDemandCalculator _demand = new();
    private readonly ITestOutputHelper _output;

    public Engine30ResistanceMultiRegionGlycogenIntegrationTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void MultipleNonOverlappingMuscleRegions_CombineIntoOneDemand()
    {
        /*
            DRAFT architecture test.

            LOWER_BODY and UPPER_BODY are deliberately modeled as
            non-overlapping regions for this test.

            Purpose:
            prove that one resistance session can carry different actual
            hard-set volumes into different muscle regions, resolve separate
            depletion fractions, and then combine those regions in the
            existing glycogen-demand calculator.

            This test does NOT freeze the 0.50 / 0.25 anatomical fractions.
        */

        var config =
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
                    0.60m);

        var lowerOnly =
            ResolveAndDemand(
                config,
                new[]
                {
                    new Engine30ResistanceMuscleGlycogenWorkInput(
                        MuscleGroupCode:
                            "LOWER_BODY",

                        WholeBodySkeletalMuscleFraction:
                            0.50m,

                        RecruitmentFraction:
                            0.85m,

                        HardSets:
                            8m,

                        PercentOneRepMax:
                            null)
                });

        var multiRegion =
            ResolveAndDemand(
                config,
                new[]
                {
                    new Engine30ResistanceMuscleGlycogenWorkInput(
                        MuscleGroupCode:
                            "LOWER_BODY",

                        WholeBodySkeletalMuscleFraction:
                            0.50m,

                        RecruitmentFraction:
                            0.85m,

                        HardSets:
                            8m,

                        PercentOneRepMax:
                            null),

                    new Engine30ResistanceMuscleGlycogenWorkInput(
                        MuscleGroupCode:
                            "UPPER_BODY",

                        WholeBodySkeletalMuscleFraction:
                            0.25m,

                        RecruitmentFraction:
                            0.70m,

                        HardSets:
                            4m,

                        PercentOneRepMax:
                            null)
                });

        Assert.Single(
            lowerOnly.Workload.MuscleResults);

        Assert.Equal(
            2,
            multiRegion.Workload.MuscleResults.Count);

        Assert.Equal(
            2,
            multiRegion.Workload.ResolvedMuscleGroups.Count);

        Assert.Equal(
            2,
            multiRegion.Demand.MuscleGroups.Count);

        var lower =
            multiRegion.Workload.MuscleResults.Single(
                x => x.MuscleGroupCode == "LOWER_BODY");

        var upper =
            multiRegion.Workload.MuscleResults.Single(
                x => x.MuscleGroupCode == "UPPER_BODY");

        Assert.Equal(
            Engine30ResistanceGlycogenWorkloadSourceCodes.HardSets,
            lower.WorkloadSourceCode);

        Assert.Equal(
            Engine30ResistanceGlycogenWorkloadSourceCodes.HardSets,
            upper.WorkloadSourceCode);

        // LOWER_BODY is at the 8-set reference.
        Assert.Equal(
            1.0000m,
            lower.HardSetRatio);

        Assert.Equal(
            0.2800m,
            lower.ResolvedDepletionFraction);

        // UPPER_BODY has 4 sets against an 8-set reference:
        // ratio .50 -> workload .75 -> depletion .21.
        Assert.Equal(
            0.5000m,
            upper.HardSetRatio);

        Assert.Equal(
            0.7500m,
            upper.BaseWorkloadFactor);

        Assert.Equal(
            0.2100m,
            upper.ResolvedDepletionFraction);

        // Adding a separately modeled non-overlapping trained region
        // must increase total modeled glycogen demand.
        Assert.True(
            multiRegion.Demand.EstimatedGlycogenDepletedG
            > lowerOnly.Demand.EstimatedGlycogenDepletedG);

        Assert.True(
            multiRegion.Demand.CarbohydrateReloadRequirementG
            > lowerOnly.Demand.CarbohydrateReloadRequirementG);

        // Carb restoration remains an allocation diagnostic only.
        Assert.InRange(
            Math.Abs(
                multiRegion.Demand.CarbohydrateReloadEnergyKcal
                - multiRegion.Demand.CarbohydrateReloadRequirementG * 4m),
            0m,
            0.03m);

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 - MULTI-REGION RESISTANCE GLYCOGEN");

        _output.WriteLine(
            "DRAFT anatomical fractions and coefficients; validates multi-region data flow only.");

        _output.WriteLine("");
        _output.WriteLine(
            new string('-', 110));

        _output.WriteLine(
            $"{"Region",-18} " +
            $"{"Sets",6} " +
            $"{"SetRatio",9} " +
            $"{"Recruit",9} " +
            $"{"DepleteFx",10}");

        _output.WriteLine(
            new string('-', 110));

        foreach (var muscle in multiRegion.Workload.MuscleResults)
        {
            var sets =
                muscle.HardSets.HasValue
                    ? muscle.HardSets.Value.ToString("0")
                    : "-";

            _output.WriteLine(
                $"{muscle.MuscleGroupCode,-18} " +
                $"{sets,6} " +
                $"{muscle.HardSetRatio,9:0.000} " +
                $"{muscle.RecruitmentFraction,9:0.000} " +
                $"{muscle.ResolvedDepletionFraction,10:0.0000}");
        }

        _output.WriteLine(
            new string('-', 110));

        _output.WriteLine(
            $"{"LOWER_ONLY TOTAL",-44} " +
            $"Glycogen {lowerOnly.Demand.EstimatedGlycogenDepletedG,8:0.0} g | " +
            $"Reload {lowerOnly.Demand.CarbohydrateReloadRequirementG,8:0.0} g");

        _output.WriteLine(
            $"{"MULTI_REGION TOTAL",-44} " +
            $"Glycogen {multiRegion.Demand.EstimatedGlycogenDepletedG,8:0.0} g | " +
            $"Reload {multiRegion.Demand.CarbohydrateReloadRequirementG,8:0.0} g | " +
            $"Reload kcal {multiRegion.Demand.CarbohydrateReloadEnergyKcal,8:0.0}");
    }

    private IntegrationResult ResolveAndDemand(
        Engine30ResistanceGlycogenWorkloadConfig config,
        IReadOnlyCollection<Engine30ResistanceMuscleGlycogenWorkInput> muscleGroups)
    {
        var workload =
            _workload.Calculate(
                new Engine30ResistanceGlycogenWorkloadInput(
                    DurationMinutes:
                        60m,

                    TrainingStatusCode:
                        Engine30ResistanceGlycogenTrainingStatusCodes.Trained,

                    MuscleGroups:
                        muscleGroups),

                config);

        var demand =
            _demand.Calculate(
                new Engine30GlycogenDemandInput(
                    FatFreeMassLb:
                        144m,

                    SkeletalMuscleFractionOfFfm:
                        0.528m,

                    BaselineGlycogenGPerKgMuscle:
                        23m,

                    TargetRestorationFraction:
                        1.00m,

                    CarbohydratePerGlycogenG:
                        1.00m,

                    MuscleGroups:
                        workload.ResolvedMuscleGroups));

        return new IntegrationResult(
            workload,
            demand);
    }

    private sealed record IntegrationResult(
        Engine30ResistanceGlycogenWorkloadResult Workload,
        Engine30GlycogenDemandResult Demand);
}