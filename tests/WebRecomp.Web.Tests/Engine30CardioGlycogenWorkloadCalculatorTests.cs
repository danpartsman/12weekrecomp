using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30CardioGlycogenWorkloadCalculatorTests
{
    private readonly Engine30CardioGlycogenWorkloadCalculator _calculator = new();
    private readonly ITestOutputHelper _output;

    public Engine30CardioGlycogenWorkloadCalculatorTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ContinuousCardioGlycogen_UsesEvidenceHierarchy_AndDirectionallyResponds()
    {
        /*
            DRAFT continuous-cardio calibration.

            Reference condition:
            45 minutes
            RPE 3
            depletion fraction 0.12

            This test validates architecture and directional behavior only.
            It does NOT freeze these physiological coefficients.
        */

        var config =
            new Engine30CardioGlycogenWorkloadConfig(
                ReferenceDurationMinutes:
                    45m,

                ReferenceDepletionFraction:
                    0.12m,

                ReferenceRpe:
                    3m,

                ReferenceObjectiveRelativeIntensity:
                    0.70m,

                ReferenceHeartRateZoneIndex:
                    2.00m,

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
                    0.60m);

        var rpeReference =
            Calculate(
                config,
                durationMinutes:
                    45m,
                rpe:
                    3m);

        var rpeHigher =
            Calculate(
                config,
                durationMinutes:
                    45m,
                rpe:
                    5m);

        var longerReference =
            Calculate(
                config,
                durationMinutes:
                    60m,
                rpe:
                    3m);

        var hrZoneReference =
            Calculate(
                config,
                durationMinutes:
                    45m,
                rpe:
                    9m,
                monitor:
                    new Engine30WorkoutMonitorData(
                        ReportedCalories:
                            9999m,

                        ReportedCalorieTypeCode:
                            "GROSS",

                        MinutesZone2:
                            45m));

        var hrZoneHigher =
            Calculate(
                config,
                durationMinutes:
                    45m,
                rpe:
                    2m,
                monitor:
                    new Engine30WorkoutMonitorData(
                        ReportedCalories:
                            1m,

                        ReportedCalorieTypeCode:
                            "ACTIVE",

                        MinutesZone2:
                            15m,

                        MinutesZone3:
                            30m));

        var objective =
            Calculate(
                config,
                durationMinutes:
                    45m,
                rpe:
                    1m,
                objectiveRelativeIntensity:
                    0.84m,
                monitor:
                    new Engine30WorkoutMonitorData(
                        ReportedCalories:
                            5000m,

                        MinutesZone5:
                            45m));

        var zeroDuration =
            Calculate(
                config,
                durationMinutes:
                    0m,
                rpe:
                    3m);

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 - CONTINUOUS CARDIO GLYCOGEN WORKLOAD");

        _output.WriteLine(
            "DRAFT coefficients; validates evidence hierarchy and directional behavior only.");

        _output.WriteLine("");
        _output.WriteLine(
            new string('-', 128));

        _output.WriteLine(
            $"{"Case",-24} " +
            $"{"Min",6} " +
            $"{"RPE",5} " +
            $"{"Source",-20} " +
            $"{"ActualIdx",10} " +
            $"{"RefIdx",8} " +
            $"{"IntRatio",9} " +
            $"{"DurRatio",9} " +
            $"{"Work",8} " +
            $"{"Deplete",9}");

        _output.WriteLine(
            new string('-', 128));

        Print(
            "RPE reference",
            rpeReference,
            3m);

        Print(
            "RPE higher",
            rpeHigher,
            5m);

        Print(
            "Longer same RPE",
            longerReference,
            3m);

        Print(
            "HR Zone2 reference",
            hrZoneReference,
            9m);

        Print(
            "HR higher mix",
            hrZoneHigher,
            2m);

        Print(
            "Objective relative",
            objective,
            1m);

        Print(
            "Zero duration",
            zeroDuration,
            3m);

        _output.WriteLine(
            new string('-', 128));

        // RPE fallback: reference matches configured reference condition.
        Assert.Equal(
            Engine30CardioGlycogenIntensitySourceCodes.RpeFallback,
            rpeReference.IntensitySourceCode);

        Assert.Equal(
            1.0000m,
            rpeReference.IntensityRatio);

        Assert.Equal(
            1.0000m,
            rpeReference.DurationRatio);

        Assert.Equal(
            0.1200m,
            rpeReference.ResolvedDepletionFraction);

        // Higher RPE at same duration raises modeled depletion.
        Assert.Equal(
            Engine30CardioGlycogenIntensitySourceCodes.RpeFallback,
            rpeHigher.IntensitySourceCode);

        Assert.True(
            rpeHigher.ResolvedDepletionFraction
            > rpeReference.ResolvedDepletionFraction);

        // Longer continuous exposure at the same intensity raises depletion.
        Assert.True(
            longerReference.ResolvedDepletionFraction
            > rpeReference.ResolvedDepletionFraction);

        // HR zones outrank RPE when usable HR-zone evidence exists.
        Assert.Equal(
            Engine30CardioGlycogenIntensitySourceCodes.HeartRateZones,
            hrZoneReference.IntensitySourceCode);

        Assert.Equal(
            2.0000m,
            hrZoneReference.ActualIntensityIndex);

        Assert.Equal(
            0.1200m,
            hrZoneReference.ResolvedDepletionFraction);

        Assert.Equal(
            Engine30CardioGlycogenIntensitySourceCodes.HeartRateZones,
            hrZoneHigher.IntensitySourceCode);

        Assert.True(
            hrZoneHigher.ResolvedDepletionFraction
            > hrZoneReference.ResolvedDepletionFraction);

        // Objective individual-relative intensity outranks HR zones and RPE.
        Assert.Equal(
            Engine30CardioGlycogenIntensitySourceCodes.ObjectiveRelative,
            objective.IntensitySourceCode);

        Assert.Equal(
            0.8400m,
            objective.ActualIntensityIndex);

        Assert.Equal(
            0.7000m,
            objective.ReferenceIntensityIndex);

        Assert.True(
            objective.ResolvedDepletionFraction
            > rpeReference.ResolvedDepletionFraction);

        // Monitor calorie totals are intentionally irrelevant to glycogen.
        // The HR-zone source was selected despite absurdly different calorie
        // values because only HR-zone distribution was consumed.
        Assert.Equal(
            45m,
            hrZoneReference.HeartRateZoneMinutesUsed);

        Assert.Equal(
            45m,
            hrZoneHigher.HeartRateZoneMinutesUsed);

        // No exercise duration -> no modeled depletion.
        Assert.Equal(
            0m,
            zeroDuration.ResolvedDepletionFraction);
    }

    private Engine30CardioGlycogenWorkloadResult Calculate(
        Engine30CardioGlycogenWorkloadConfig config,
        decimal durationMinutes,
        decimal rpe,
        decimal? objectiveRelativeIntensity = null,
        Engine30WorkoutMonitorData? monitor = null)
    {
        return _calculator.Calculate(
            new Engine30CardioGlycogenWorkloadInput(
                ModalityCode:
                    "CYCLING",

                DurationMinutes:
                    durationMinutes,

                SessionRpe:
                    rpe,

                ObjectiveRelativeIntensity:
                    objectiveRelativeIntensity,

                MonitorData:
                    monitor),

            config);
    }

    private void Print(
        string label,
        Engine30CardioGlycogenWorkloadResult result,
        decimal rpe)
    {
        _output.WriteLine(
            $"{label,-24} " +
            $"{result.DurationMinutes,6:0} " +
            $"{rpe,5:0.0} " +
            $"{result.IntensitySourceCode,-20} " +
            $"{result.ActualIntensityIndex,10:0.000} " +
            $"{result.ReferenceIntensityIndex,8:0.000} " +
            $"{result.IntensityRatio,9:0.000} " +
            $"{result.DurationRatio,9:0.000} " +
            $"{result.WorkloadFactor,8:0.000} " +
            $"{result.ResolvedDepletionFraction,9:0.0000}");
    }
}