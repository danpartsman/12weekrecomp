using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30ResistanceGlycogenDemandIntegrationTests
{
    private readonly Engine30ResistanceGlycogenWorkloadCalculator _workload = new();
    private readonly Engine30GlycogenDemandCalculator _demand = new();
    private readonly ITestOutputHelper _output;

    public Engine30ResistanceGlycogenDemandIntegrationTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ResistanceWorkload_FlowsInto_GlycogenDemandCalculator()
    {
        /*
            DRAFT calibration values.

            Purpose:
            validate the data-flow boundary:

                actual resistance work
                -> resolved muscle-region depletion
                -> existing glycogen-demand calculator
                -> glycogen grams / carb restoration

            This is an integration test, not a freeze of physiology.
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

        var reference =
            Calculate(
                config,
                durationMinutes:
                    60m,
                hardSets:
                    8m);

        var moreSets =
            Calculate(
                config,
                durationMinutes:
                    60m,
                hardSets:
                    12m);

        var sameSetsLonger =
            Calculate(
                config,
                durationMinutes:
                    75m,
                hardSets:
                    8m);

        var durationFallback =
            Calculate(
                config,
                durationMinutes:
                    75m,
                hardSets:
                    null);

        var zeroSets =
            Calculate(
                config,
                durationMinutes:
                    60m,
                hardSets:
                    0m);

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 - RESISTANCE WORKLOAD -> GLYCOGEN DEMAND");

        _output.WriteLine(
            "DRAFT coefficients; validates resolver-to-demand integration only.");

        _output.WriteLine("");
        _output.WriteLine(
            new string('-', 118));

        _output.WriteLine(
            $"{"Case",-24} " +
            $"{"Sets",6} " +
            $"{"Source",-18} " +
            $"{"DepleteFx",10} " +
            $"{"RecruitKg",10} " +
            $"{"GlycogenG",11} " +
            $"{"ReloadG",10} " +
            $"{"ReloadKcal",11}");

        _output.WriteLine(
            new string('-', 118));

        Print(
            "Reference",
            reference);

        Print(
            "More sets",
            moreSets);

        Print(
            "Same sets / longer",
            sameSetsLonger);

        Print(
            "Duration fallback",
            durationFallback);

        Print(
            "Zero hard sets",
            zeroSets);

        _output.WriteLine(
            new string('-', 118));

        // More known hard sets increase downstream modeled glycogen demand.
        Assert.True(
            moreSets.Demand.EstimatedGlycogenDepletedG
            > reference.Demand.EstimatedGlycogenDepletedG);

        // Same hard sets: longer elapsed duration remains diagnostic only.
        Assert.Equal(
            reference.Demand.EstimatedGlycogenDepletedG,
            sameSetsLonger.Demand.EstimatedGlycogenDepletedG);

        // If hard sets are unavailable, duration fallback can increase demand.
        Assert.True(
            durationFallback.Demand.EstimatedGlycogenDepletedG
            > reference.Demand.EstimatedGlycogenDepletedG);

        // Explicit zero resistance work must propagate to zero glycogen demand.
        Assert.Equal(
            0m,
            zeroSets.Demand.EstimatedGlycogenDepletedG);

        Assert.Equal(
            0m,
            zeroSets.Demand.CarbohydrateReloadRequirementG);

        Assert.Equal(
            0m,
            zeroSets.Demand.CarbohydrateReloadEnergyKcal);

        // With restoration fraction = 1 and carb:glycogen = 1,
        // downstream reload grams should equal modeled glycogen depletion.
        Assert.Equal(
            reference.Demand.EstimatedGlycogenDepletedG,
            reference.Demand.CarbohydrateReloadRequirementG);

        Assert.Equal(
            moreSets.Demand.EstimatedGlycogenDepletedG,
            moreSets.Demand.CarbohydrateReloadRequirementG);

        // Reload kcal is a macro-allocation diagnostic, not an energy bucket.
        Assert.InRange(
            Math.Abs(
                reference.Demand.CarbohydrateReloadEnergyKcal
                - reference.Demand.CarbohydrateReloadRequirementG * 4m),
            0m,
            0.03m);
    }

    private IntegrationResult Calculate(
        Engine30ResistanceGlycogenWorkloadConfig config,
        decimal durationMinutes,
        decimal? hardSets)
    {
        var workload =
            _workload.Calculate(
                new Engine30ResistanceGlycogenWorkloadInput(
                    DurationMinutes:
                        durationMinutes,

                    TrainingStatusCode:
                        Engine30ResistanceGlycogenTrainingStatusCodes.Trained,

                    MuscleGroups:
                        new[]
                        {
                            new Engine30ResistanceMuscleGlycogenWorkInput(
                                MuscleGroupCode:
                                    "LEGS",

                                WholeBodySkeletalMuscleFraction:
                                    0.50m,

                                RecruitmentFraction:
                                    0.85m,

                                HardSets:
                                    hardSets,

                                PercentOneRepMax:
                                    null)
                        }),

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

    private void Print(
        string label,
        IntegrationResult result)
    {
        Assert.Single(
            result.Workload.MuscleResults);

        Assert.Single(
            result.Demand.MuscleGroups);

        var work =
            result.Workload.MuscleResults[0];

        var demandMuscle =
            result.Demand.MuscleGroups[0];

        var sets =
            work.HardSets.HasValue
                ? work.HardSets.Value.ToString("0")
                : "-";

        _output.WriteLine(
            $"{label,-24} " +
            $"{sets,6} " +
            $"{work.WorkloadSourceCode,-18} " +
            $"{work.ResolvedDepletionFraction,10:0.0000} " +
            $"{demandMuscle.EffectiveRecruitedMuscleMassKg,10:0.00} " +
            $"{result.Demand.EstimatedGlycogenDepletedG,11:0.0} " +
            $"{result.Demand.CarbohydrateReloadRequirementG,10:0.0} " +
            $"{result.Demand.CarbohydrateReloadEnergyKcal,11:0.0}");
    }

    private sealed record IntegrationResult(
        Engine30ResistanceGlycogenWorkloadResult Workload,
        Engine30GlycogenDemandResult Demand);
}