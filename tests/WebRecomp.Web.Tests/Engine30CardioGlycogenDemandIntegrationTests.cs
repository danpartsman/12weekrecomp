using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30CardioGlycogenDemandIntegrationTests
{
    private readonly Engine30CardioGlycogenWorkloadCalculator _workload = new();
    private readonly Engine30GlycogenDemandCalculator _demand = new();
    private readonly ITestOutputHelper _output;

    public Engine30CardioGlycogenDemandIntegrationTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ContinuousCardioWorkload_FlowsInto_GlycogenDemandCalculator()
    {
        /*
            DRAFT integration test.

            Purpose:
            validate the data-flow boundary:

                cardio duration + intensity evidence
                -> resolved depletion fraction
                -> modality-specific recruited muscle pool
                -> existing glycogen-demand calculator
                -> glycogen grams / carb restoration

            The LOWER_BODY muscle fraction and recruitment fraction used here
            are test anchors only. This test does NOT freeze modality-specific
            anatomy/recruitment coefficients.
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

        var reference =
            Calculate(
                config,
                durationMinutes:
                    45m,
                rpe:
                    3m);

        var higherRpe =
            Calculate(
                config,
                durationMinutes:
                    45m,
                rpe:
                    5m);

        var longer =
            Calculate(
                config,
                durationMinutes:
                    60m,
                rpe:
                    3m);

        var hrReference =
            Calculate(
                config,
                durationMinutes:
                    45m,
                rpe:
                    9m,
                monitor:
                    new Engine30WorkoutMonitorData(
                        ReportedCalories:
                            9000m,

                        ReportedCalorieTypeCode:
                            "GROSS",

                        MinutesZone2:
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
            "ENGINE 3.0 - CARDIO WORKLOAD -> GLYCOGEN DEMAND");

        _output.WriteLine(
            "DRAFT coefficients and anatomy anchors; validates integration only.");

        _output.WriteLine("");
        _output.WriteLine(
            new string('-', 120));

        _output.WriteLine(
            $"{"Case",-22} " +
            $"{"Source",-20} " +
            $"{"DepleteFx",10} " +
            $"{"RecruitKg",10} " +
            $"{"GlycogenG",11} " +
            $"{"ReloadG",10} " +
            $"{"ReloadKcal",11}");

        _output.WriteLine(
            new string('-', 120));

        Print(
            "Reference",
            reference);

        Print(
            "Higher RPE",
            higherRpe);

        Print(
            "Longer same RPE",
            longer);

        Print(
            "HR Zone2 reference",
            hrReference);

        Print(
            "Zero duration",
            zeroDuration);

        _output.WriteLine(
            new string('-', 120));

        Assert.True(
            higherRpe.Demand.EstimatedGlycogenDepletedG
            > reference.Demand.EstimatedGlycogenDepletedG);

        Assert.True(
            longer.Demand.EstimatedGlycogenDepletedG
            > reference.Demand.EstimatedGlycogenDepletedG);

        // Same modeled reference intensity through HR-zone evidence should
        // produce the same depletion fraction and downstream demand as the
        // RPE reference condition.
        Assert.Equal(
            Engine30CardioGlycogenIntensitySourceCodes.HeartRateZones,
            hrReference.Workload.IntensitySourceCode);

        Assert.Equal(
            reference.Workload.ResolvedDepletionFraction,
            hrReference.Workload.ResolvedDepletionFraction);

        Assert.Equal(
            reference.Demand.EstimatedGlycogenDepletedG,
            hrReference.Demand.EstimatedGlycogenDepletedG);

        // Monitor calorie total is not part of cardio glycogen demand.
        Assert.Equal(
            0m,
            zeroDuration.Demand.EstimatedGlycogenDepletedG);

        Assert.Equal(
            0m,
            zeroDuration.Demand.CarbohydrateReloadRequirementG);

        Assert.Equal(
            0m,
            zeroDuration.Demand.CarbohydrateReloadEnergyKcal);

        Assert.Equal(
            reference.Demand.EstimatedGlycogenDepletedG,
            reference.Demand.CarbohydrateReloadRequirementG);

        Assert.InRange(
            Math.Abs(
                reference.Demand.CarbohydrateReloadEnergyKcal
                - reference.Demand.CarbohydrateReloadRequirementG * 4m),
            0m,
            0.03m);
    }

    private IntegrationResult Calculate(
        Engine30CardioGlycogenWorkloadConfig config,
        decimal durationMinutes,
        decimal rpe,
        Engine30WorkoutMonitorData? monitor = null)
    {
        var workload =
            _workload.Calculate(
                new Engine30CardioGlycogenWorkloadInput(
                    ModalityCode:
                        "CYCLING",

                    DurationMinutes:
                        durationMinutes,

                    SessionRpe:
                        rpe,

                    ObjectiveRelativeIntensity:
                        null,

                    MonitorData:
                        monitor),

                config);

        /*
            DRAFT cycling muscle-pool anchors for this integration test only.

            The cardio workload calculator resolves intensity/duration-driven
            depletion. A later modality profile component should own the
            anatomical muscle fraction and recruitment fraction.
        */
        var muscleGroups =
            new[]
            {
                new Engine30MuscleGlycogenInput(
                    MuscleGroupCode:
                        "LOWER_BODY",

                    WholeBodySkeletalMuscleFraction:
                        0.50m,

                    RecruitmentFraction:
                        0.70m,

                    DepletionFraction:
                        workload.ResolvedDepletionFraction)
            };

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
                        muscleGroups));

        return new IntegrationResult(
            workload,
            demand);
    }

    private void Print(
        string label,
        IntegrationResult result)
    {
        Assert.Single(
            result.Demand.MuscleGroups);

        var muscle =
            result.Demand.MuscleGroups[0];

        _output.WriteLine(
            $"{label,-22} " +
            $"{result.Workload.IntensitySourceCode,-20} " +
            $"{result.Workload.ResolvedDepletionFraction,10:0.0000} " +
            $"{muscle.EffectiveRecruitedMuscleMassKg,10:0.00} " +
            $"{result.Demand.EstimatedGlycogenDepletedG,11:0.0} " +
            $"{result.Demand.CarbohydrateReloadRequirementG,10:0.0} " +
            $"{result.Demand.CarbohydrateReloadEnergyKcal,11:0.0}");
    }

    private sealed record IntegrationResult(
        Engine30CardioGlycogenWorkloadResult Workload,
        Engine30GlycogenDemandResult Demand);
}