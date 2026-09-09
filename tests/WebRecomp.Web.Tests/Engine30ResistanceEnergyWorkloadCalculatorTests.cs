using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30ResistanceEnergyWorkloadCalculatorTests
{
    private readonly Engine30ResistanceEnergyWorkloadCalculator _calculator = new();
    private readonly ITestOutputHelper _output;

    public Engine30ResistanceEnergyWorkloadCalculatorTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ResistanceWorkload_UsesHardSetDensityBeforeRestFallback()
    {
        /*
            DRAFT engineering calibration for behavior testing only.

            Reference session:
            - 60 minutes
            - 16 hard sets
            - 90 sec average rest

            Hard-set density sensitivity = 0.50
            Rest fallback sensitivity = 0.25
            Workload clamp = 0.85 to 1.15

            These values are NOT frozen physiology.
        */

        var config =
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
                    1.15m);

        var reference =
            Calculate(
                duration:
                    60m,

                hardSets:
                    16m,

                rest:
                    90m,

                config:
                    config);

        var sameSetsShortRest =
            Calculate(
                duration:
                    60m,

                hardSets:
                    16m,

                rest:
                    60m,

                config:
                    config);

        var moreSets =
            Calculate(
                duration:
                    60m,

                hardSets:
                    20m,

                rest:
                    60m,

                config:
                    config);

        var fewerSets =
            Calculate(
                duration:
                    60m,

                hardSets:
                    12m,

                rest:
                    120m,

                config:
                    config);

        var restFallback =
            Calculate(
                duration:
                    60m,

                hardSets:
                    null,

                rest:
                    60m,

                config:
                    config);

        var neutral =
            Calculate(
                duration:
                    60m,

                hardSets:
                    null,

                rest:
                    null,

                config:
                    config);

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 - RESISTANCE ENERGY WORKLOAD / DENSITY");

        _output.WriteLine(
            "DRAFT behavior-test calibration; validates signal priority, not final physiological coefficients.");

        _output.WriteLine("");
        _output.WriteLine(
            new string('-', 108));

        _output.WriteLine(
            $"{"Case",-24} " +
            $"{"Source",-18} " +
            $"{"Sets",7} " +
            $"{"Min",6} " +
            $"{"Sets/min",9} " +
            $"{"SetRatio",9} " +
            $"{"Rest",7} " +
            $"{"RestRatio",10} " +
            $"{"Factor",8}");

        _output.WriteLine(
            new string('-', 108));

        Print(
            "Reference",
            reference,
            16m,
            60m);

        Print(
            "Same sets / short rest",
            sameSetsShortRest,
            16m,
            60m);

        Print(
            "More sets",
            moreSets,
            20m,
            60m);

        Print(
            "Fewer sets",
            fewerSets,
            12m,
            60m);

        Print(
            "Rest fallback",
            restFallback,
            null,
            60m);

        Print(
            "No density data",
            neutral,
            null,
            60m);

        _output.WriteLine(
            new string('-', 108));

        // Reference session is neutral.
        Assert.Equal(
            Engine30ResistanceEnergyWorkloadSourceCodes.HardSetDensity,
            reference.SourceCode);

        Assert.Equal(
            1.0000m,
            reference.WorkloadFactor);

        Assert.Equal(
            1.0000m,
            reference.HardSetDensityRatio);

        // Shorter rest alone does not raise the factor when hard sets are known.
        Assert.Equal(
            Engine30ResistanceEnergyWorkloadSourceCodes.HardSetDensity,
            sameSetsShortRest.SourceCode);

        Assert.Equal(
            1.0000m,
            sameSetsShortRest.WorkloadFactor);

        Assert.Equal(
            1.5000m,
            sameSetsShortRest.RestDensityRatio);

        // More hard sets in the same duration raises workload.
        Assert.Equal(
            1.2500m,
            moreSets.HardSetDensityRatio);

        Assert.Equal(
            1.1250m,
            moreSets.WorkloadFactor);

        // Fewer hard sets in the same duration lowers workload.
        Assert.Equal(
            0.7500m,
            fewerSets.HardSetDensityRatio);

        Assert.Equal(
            0.8750m,
            fewerSets.WorkloadFactor);

        // Rest is used as fallback only when set information is unavailable.
        Assert.Equal(
            Engine30ResistanceEnergyWorkloadSourceCodes.RestFallback,
            restFallback.SourceCode);

        Assert.Equal(
            1.5000m,
            restFallback.RestDensityRatio);

        Assert.Equal(
            1.1250m,
            restFallback.WorkloadFactor);

        // No density information -> neutral.
        Assert.Equal(
            Engine30ResistanceEnergyWorkloadSourceCodes.Neutral,
            neutral.SourceCode);

        Assert.Equal(
            1.0000m,
            neutral.WorkloadFactor);
    }

    private Engine30ResistanceEnergyWorkloadResult Calculate(
        decimal duration,
        decimal? hardSets,
        decimal? rest,
        Engine30ResistanceEnergyWorkloadConfig config)
        => _calculator.Calculate(
            new Engine30ResistanceEnergyWorkloadInput(
                DurationMinutes:
                    duration,

                TotalHardSets:
                    hardSets,

                AverageRestSeconds:
                    rest),

            config);

    private void Print(
        string label,
        Engine30ResistanceEnergyWorkloadResult result,
        decimal? hardSets,
        decimal duration)
    {
        var sets =
            hardSets.HasValue
                ? hardSets.Value.ToString("0.0")
                : "-";

        var setsPerMinute =
            result.ActualHardSetsPerMinute.HasValue
                ? result.ActualHardSetsPerMinute.Value.ToString("0.000")
                : "-";

        var setRatio =
            result.HardSetDensityRatio.HasValue
                ? result.HardSetDensityRatio.Value.ToString("0.000")
                : "-";

        var rest =
            result.ActualRestSeconds.HasValue
                ? result.ActualRestSeconds.Value.ToString("0")
                : "-";

        var restRatio =
            result.RestDensityRatio.HasValue
                ? result.RestDensityRatio.Value.ToString("0.000")
                : "-";

        _output.WriteLine(
            $"{label,-24} " +
            $"{result.SourceCode,-18} " +
            $"{sets,7} " +
            $"{duration,6:0} " +
            $"{setsPerMinute,9} " +
            $"{setRatio,9} " +
            $"{rest,7} " +
            $"{restRatio,10} " +
            $"{result.WorkloadFactor,8:0.000}");
    }
}