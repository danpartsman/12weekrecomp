using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30IntervalGlycogenWorkloadCalculatorTests
{
    private readonly Engine30IntervalGlycogenWorkloadCalculator _calculator = new();
    private readonly ITestOutputHelper _output;

    public Engine30IntervalGlycogenWorkloadCalculatorTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void IntervalGlycogen_ModelsWorkAndRecoverySeparately_AndUsesPhaseSpecificEvidence()
    {
        /*
            DRAFT interval calibration.

            Reference protocol:
            6 x 60 sec work
            5 x 120 sec recovery
            work RPE 8
            recovery RPE 2
            depletion fraction 0.20

            This test validates architecture and directional behavior only.
            It does NOT freeze physiological coefficients.
        */

        var config =
            new Engine30IntervalGlycogenWorkloadConfig(
                ReferenceWorkIntervalSeconds:
                    60m,

                ReferenceRecoveryIntervalSeconds:
                    120m,

                ReferenceRepetitions:
                    6,

                ReferenceRecoveryPeriods:
                    5,

                ReferenceDepletionFraction:
                    0.20m,

                ReferenceWorkRpe:
                    8m,

                ReferenceRecoveryRpe:
                    2m,

                ReferenceObjectiveRelativeWorkIntensity:
                    1.00m,

                ReferenceObjectiveRelativeRecoveryIntensity:
                    0.50m,

                WorkIntensitySensitivity:
                    0.50m,

                RecoveryIntensitySensitivity:
                    0.25m,

                RecoveryContributionWeight:
                    0.25m,

                MinimumDepletionFraction:
                    0.01m,

                MaximumDepletionFraction:
                    0.70m);

        var reference =
            Calculate(
                config,
                workSeconds:
                    60m,
                recoverySeconds:
                    120m,
                repetitions:
                    6,
                recoveryPeriods:
                    5,
                workRpe:
                    8m,
                recoveryRpe:
                    2m);

        var harderWork =
            Calculate(
                config,
                workSeconds:
                    60m,
                recoverySeconds:
                    120m,
                repetitions:
                    6,
                recoveryPeriods:
                    5,
                workRpe:
                    10m,
                recoveryRpe:
                    2m);

        var moreWorkBouts =
            Calculate(
                config,
                workSeconds:
                    60m,
                recoverySeconds:
                    120m,
                repetitions:
                    8,
                recoveryPeriods:
                    7,
                workRpe:
                    8m,
                recoveryRpe:
                    2m);

        var objectiveWork =
            Calculate(
                config,
                workSeconds:
                    60m,
                recoverySeconds:
                    120m,
                repetitions:
                    6,
                recoveryPeriods:
                    5,
                workRpe:
                    2m,
                recoveryRpe:
                    2m,
                objectiveWork:
                    1.20m);

        var objectiveBoth =
            Calculate(
                config,
                workSeconds:
                    60m,
                recoverySeconds:
                    120m,
                repetitions:
                    6,
                recoveryPeriods:
                    5,
                workRpe:
                    2m,
                recoveryRpe:
                    9m,
                objectiveWork:
                    1.20m,
                objectiveRecovery:
                    0.60m);

        var zeroWork =
            Calculate(
                config,
                workSeconds:
                    0m,
                recoverySeconds:
                    0m,
                repetitions:
                    0,
                recoveryPeriods:
                    0,
                workRpe:
                    0m,
                recoveryRpe:
                    0m);

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 - INTERVAL / HIIT GLYCOGEN WORKLOAD");

        _output.WriteLine(
            "DRAFT coefficients; validates work/recovery separation and evidence hierarchy only.");

        _output.WriteLine("");
        _output.WriteLine(
            new string('-', 156));

        _output.WriteLine(
            $"{"Case",-20} " +
            $"{"WorkSrc",-18} " +
            $"{"RecSrc",-18} " +
            $"{"WorkMin",8} " +
            $"{"RecMin",7} " +
            $"{"WorkFx",8} " +
            $"{"RecFx",8} " +
            $"{"Combined",9} " +
            $"{"Deplete",9}");

        _output.WriteLine(
            new string('-', 156));

        Print(
            "Reference",
            reference);

        Print(
            "Harder work",
            harderWork);

        Print(
            "More work bouts",
            moreWorkBouts);

        Print(
            "Objective work",
            objectiveWork);

        Print(
            "Objective both",
            objectiveBoth);

        Print(
            "Zero work",
            zeroWork);

        _output.WriteLine(
            new string('-', 156));

        // Complete reference protocol must resolve exactly to workload 1.00.
        Assert.Equal(
            1.0000m,
            reference.CombinedWorkloadFactor);

        Assert.Equal(
            0.2000m,
            reference.ResolvedDepletionFraction);

        Assert.Equal(
            Engine30IntervalGlycogenIntensitySourceCodes.StructuredRpe,
            reference.WorkPhase.SourceCode);

        Assert.Equal(
            Engine30IntervalGlycogenIntensitySourceCodes.StructuredRpe,
            reference.RecoveryPhase.SourceCode);

        // Harder work phase at otherwise identical exposure increases demand.
        Assert.True(
            harderWork.ResolvedDepletionFraction
            > reference.ResolvedDepletionFraction);

        // More completed work/recovery bouts increase total interval exposure.
        Assert.True(
            moreWorkBouts.ResolvedDepletionFraction
            > reference.ResolvedDepletionFraction);

        // Objective-relative work intensity outranks contradictory work RPE,
        // while recovery can still independently use structured RPE.
        Assert.Equal(
            Engine30IntervalGlycogenIntensitySourceCodes.ObjectiveRelative,
            objectiveWork.WorkPhase.SourceCode);

        Assert.Equal(
            Engine30IntervalGlycogenIntensitySourceCodes.StructuredRpe,
            objectiveWork.RecoveryPhase.SourceCode);

        Assert.True(
            objectiveWork.ResolvedDepletionFraction
            > reference.ResolvedDepletionFraction);

        // When objective-relative evidence exists for both phases, each phase
        // independently uses the higher-authority source.
        Assert.Equal(
            Engine30IntervalGlycogenIntensitySourceCodes.ObjectiveRelative,
            objectiveBoth.WorkPhase.SourceCode);

        Assert.Equal(
            Engine30IntervalGlycogenIntensitySourceCodes.ObjectiveRelative,
            objectiveBoth.RecoveryPhase.SourceCode);

        // Zero completed work means no modeled interval depletion.
        Assert.Equal(
            0m,
            zeroWork.CombinedWorkloadFactor);

        Assert.Equal(
            0m,
            zeroWork.ResolvedDepletionFraction);

        Assert.Equal(
            0m,
            zeroWork.WorkPhase.ActualMinutes);

        Assert.Equal(
            0m,
            zeroWork.RecoveryPhase.ActualMinutes);
    }

    private Engine30IntervalGlycogenWorkloadResult Calculate(
        Engine30IntervalGlycogenWorkloadConfig config,
        decimal workSeconds,
        decimal recoverySeconds,
        int repetitions,
        int recoveryPeriods,
        decimal workRpe,
        decimal recoveryRpe,
        decimal? objectiveWork = null,
        decimal? objectiveRecovery = null)
    {
        return _calculator.Calculate(
            new Engine30IntervalGlycogenWorkloadInput(
                ModalityCode:
                    "CYCLING",

                WorkIntervalSeconds:
                    workSeconds,

                RecoveryIntervalSeconds:
                    recoverySeconds,

                Repetitions:
                    repetitions,

                RecoveryPeriods:
                    recoveryPeriods,

                WorkRpe:
                    workRpe,

                RecoveryRpe:
                    recoveryRpe,

                ObjectiveRelativeWorkIntensity:
                    objectiveWork,

                ObjectiveRelativeRecoveryIntensity:
                    objectiveRecovery),

            config);
    }

    private void Print(
        string label,
        Engine30IntervalGlycogenWorkloadResult result)
    {
        _output.WriteLine(
            $"{label,-20} " +
            $"{result.WorkPhase.SourceCode,-18} " +
            $"{result.RecoveryPhase.SourceCode,-18} " +
            $"{result.WorkPhase.ActualMinutes,8:0.00} " +
            $"{result.RecoveryPhase.ActualMinutes,7:0.00} " +
            $"{result.WorkPhase.ExposureFactor,8:0.000} " +
            $"{result.RecoveryPhase.ExposureFactor,8:0.000} " +
            $"{result.CombinedWorkloadFactor,9:0.000} " +
            $"{result.ResolvedDepletionFraction,9:0.0000}");
    }
}