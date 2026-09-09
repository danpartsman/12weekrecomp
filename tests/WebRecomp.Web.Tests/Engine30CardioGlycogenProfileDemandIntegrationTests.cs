using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30CardioGlycogenProfileDemandIntegrationTests
{
    private readonly Engine30CardioGlycogenWorkloadCalculator _workload = new();
    private readonly Engine30CardioGlycogenMuscleProfileResolver _profile = new();
    private readonly Engine30GlycogenDemandCalculator _demand = new();
    private readonly ITestOutputHelper _output;

    public Engine30CardioGlycogenProfileDemandIntegrationTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CardioWorkload_Profile_AndDemand_FormOneEndToEndPath()
    {
        /*
            DRAFT integration anchors only.

            This test validates the complete cardio glycogen path:

                intensity + duration
                -> resolved depletion fraction
                -> modality muscle profile
                -> recruited muscle mass
                -> glycogen depletion
                -> carbohydrate restoration

            Anatomy/recruitment values remain configuration-owned and are
            deliberately not frozen by this test.
        */

        var workloadConfig =
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

        var cyclingProfile =
            new Engine30CardioGlycogenModalityProfile(
                ProfileCode:
                    "TEST_CYCLING_PROFILE",

                ModalityCode:
                    "CYCLING",

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
                    });

        var reference =
            Calculate(
                workloadConfig,
                cyclingProfile,
                durationMinutes:
                    45m,
                rpe:
                    3m);

        var higherRpe =
            Calculate(
                workloadConfig,
                cyclingProfile,
                durationMinutes:
                    45m,
                rpe:
                    5m);

        var longer =
            Calculate(
                workloadConfig,
                cyclingProfile,
                durationMinutes:
                    60m,
                rpe:
                    3m);

        var zeroDuration =
            Calculate(
                workloadConfig,
                cyclingProfile,
                durationMinutes:
                    0m,
                rpe:
                    3m);

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 - CARDIO WORKLOAD -> PROFILE -> GLYCOGEN DEMAND");

        _output.WriteLine(
            "DRAFT coefficients/profile; validates full cardio glycogen data flow only.");

        _output.WriteLine("");
        _output.WriteLine(
            new string('-', 126));

        _output.WriteLine(
            $"{"Case",-22} " +
            $"{"DepleteFx",10} " +
            $"{"Regions",8} " +
            $"{"RecruitKg",10} " +
            $"{"GlycogenG",11} " +
            $"{"ReloadG",10} " +
            $"{"ReloadKcal",11} " +
            $"{"ProfileSource",-14}");

        _output.WriteLine(
            new string('-', 126));

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
            "Zero duration",
            zeroDuration);

        _output.WriteLine(
            new string('-', 126));

        Assert.Single(
            reference.Profile.ResolvedMuscleGroups);

        Assert.Equal(
            Engine30CardioMuscleProfileSourceCodes.ConfigDefault,
            reference.Profile.SourceCode);

        Assert.Equal(
            Engine30CardioMuscleProfileConfidenceCodes.Medium,
            reference.Profile.ConfidenceCode);

        Assert.True(
            higherRpe.Demand.EstimatedGlycogenDepletedG
            > reference.Demand.EstimatedGlycogenDepletedG);

        Assert.True(
            longer.Demand.EstimatedGlycogenDepletedG
            > reference.Demand.EstimatedGlycogenDepletedG);

        /*
            Critical zero-duration behavior:
            no cardio exposure -> no resolved recruited muscle groups
            -> no downstream recruited muscle mass
            -> no glycogen demand.
        */
        Assert.Empty(
            zeroDuration.Profile.ResolvedMuscleGroups);

        Assert.Empty(
            zeroDuration.Demand.MuscleGroups);

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
        Engine30CardioGlycogenWorkloadConfig workloadConfig,
        Engine30CardioGlycogenModalityProfile modalityProfile,
        decimal durationMinutes,
        decimal rpe)
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
                        null),

                workloadConfig);

        var profile =
            _profile.Resolve(
                new Engine30CardioGlycogenMuscleProfileInput(
                    Workload:
                        workload,

                    ModalityProfile:
                        modalityProfile));

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
                        profile.ResolvedMuscleGroups));

        return new IntegrationResult(
            workload,
            profile,
            demand);
    }

    private void Print(
        string label,
        IntegrationResult result)
    {
        var recruitedKg =
            result.Demand.MuscleGroups.Sum(
                x => x.EffectiveRecruitedMuscleMassKg);

        _output.WriteLine(
            $"{label,-22} " +
            $"{result.Workload.ResolvedDepletionFraction,10:0.0000} " +
            $"{result.Profile.ResolvedMuscleGroups.Count,8} " +
            $"{recruitedKg,10:0.00} " +
            $"{result.Demand.EstimatedGlycogenDepletedG,11:0.0} " +
            $"{result.Demand.CarbohydrateReloadRequirementG,10:0.0} " +
            $"{result.Demand.CarbohydrateReloadEnergyKcal,11:0.0} " +
            $"{result.Profile.SourceCode,-14}");
    }

    private sealed record IntegrationResult(
        Engine30CardioGlycogenWorkloadResult Workload,
        Engine30CardioGlycogenMuscleProfileResult Profile,
        Engine30GlycogenDemandResult Demand);
}