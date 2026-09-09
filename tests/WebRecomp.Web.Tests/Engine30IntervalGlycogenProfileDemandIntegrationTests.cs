using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30IntervalGlycogenProfileDemandIntegrationTests
{
    private readonly Engine30IntervalGlycogenWorkloadCalculator _interval = new();
    private readonly Engine30CardioGlycogenMuscleProfileResolver _profile = new();
    private readonly Engine30GlycogenDemandCalculator _demand = new();
    private readonly ITestOutputHelper _output;

    public Engine30IntervalGlycogenProfileDemandIntegrationTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void IntervalWorkload_FlowsThrough_GenericMuscleProfile_IntoGlycogenDemand()
    {
        /*
            DRAFT integration anchors only.

            Purpose:
            prove the generic muscle-profile exposure contract allows an
            interval/HIIT workload to use the same downstream anatomy and
            glycogen-demand layers as continuous cardio WITHOUT constructing
            a fake continuous-cardio workload result.

            The cycling anatomy/recruitment values remain configuration-owned
            test anchors and are NOT frozen here.
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

        var cyclingProfile =
            new Engine30CardioGlycogenModalityProfile(
                ProfileCode:
                    "TEST_CYCLING_INTERVAL_PROFILE",

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
                config,
                cyclingProfile,
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

        var harder =
            Calculate(
                config,
                cyclingProfile,
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

        var moreBouts =
            Calculate(
                config,
                cyclingProfile,
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

        var zero =
            Calculate(
                config,
                cyclingProfile,
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
            "ENGINE 3.0 - INTERVAL WORKLOAD -> GENERIC PROFILE -> GLYCOGEN DEMAND");

        _output.WriteLine(
            "DRAFT coefficients/profile; validates interval-to-shared-profile integration only.");

        _output.WriteLine("");
        _output.WriteLine(
            new string('-', 134));

        _output.WriteLine(
            $"{"Case",-18} " +
            $"{"WorkMin",8} " +
            $"{"RecMin",8} " +
            $"{"TotalMin",9} " +
            $"{"DepleteFx",10} " +
            $"{"Regions",8} " +
            $"{"RecruitKg",10} " +
            $"{"GlycogenG",11} " +
            $"{"ReloadG",10} " +
            $"{"ReloadKcal",11}");

        _output.WriteLine(
            new string('-', 134));

        Print(
            "Reference",
            reference);

        Print(
            "Harder work",
            harder);

        Print(
            "More bouts",
            moreBouts);

        Print(
            "Zero work",
            zero);

        _output.WriteLine(
            new string('-', 134));

        Assert.Single(
            reference.Profile.ResolvedMuscleGroups);

        Assert.Equal(
            0.2000m,
            reference.Interval.ResolvedDepletionFraction);

        Assert.True(
            harder.Demand.EstimatedGlycogenDepletedG
            > reference.Demand.EstimatedGlycogenDepletedG);

        Assert.True(
            moreBouts.Demand.EstimatedGlycogenDepletedG
            > reference.Demand.EstimatedGlycogenDepletedG);

        /*
            Critical integration guardrail:
            zero interval work must remain zero through the generic exposure
            layer, the muscle-profile resolver, and glycogen demand.
        */
        Assert.Empty(
            zero.Profile.ResolvedMuscleGroups);

        Assert.Empty(
            zero.Demand.MuscleGroups);

        Assert.Equal(
            0m,
            zero.Demand.EstimatedGlycogenDepletedG);

        Assert.Equal(
            0m,
            zero.Demand.CarbohydrateReloadRequirementG);

        Assert.Equal(
            0m,
            zero.Demand.CarbohydrateReloadEnergyKcal);

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
        Engine30IntervalGlycogenWorkloadConfig config,
        Engine30CardioGlycogenModalityProfile modalityProfile,
        decimal workSeconds,
        decimal recoverySeconds,
        int repetitions,
        int recoveryPeriods,
        decimal workRpe,
        decimal recoveryRpe)
    {
        var interval =
            _interval.Calculate(
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
                        recoveryRpe),

                config);

        var totalExposureMinutes =
            interval.WorkPhase.ActualMinutes
            + interval.RecoveryPhase.ActualMinutes;

        /*
            This is the key architecture boundary.

            We pass ONLY the resolved physiological exposure downstream:
            modality + exposure duration + depletion fraction.

            We do not manufacture an Engine30CardioGlycogenWorkloadResult.
        */
        var profile =
            _profile.Resolve(
                new Engine30GlycogenMuscleProfileExposure(
                    ModalityCode:
                        interval.ModalityCode,

                    DurationMinutes:
                        totalExposureMinutes,

                    ResolvedDepletionFraction:
                        interval.ResolvedDepletionFraction),

                modalityProfile);

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
            interval,
            profile,
            demand);
    }

    private void Print(
        string label,
        IntegrationResult result)
    {
        var totalMinutes =
            result.Interval.WorkPhase.ActualMinutes
            + result.Interval.RecoveryPhase.ActualMinutes;

        var recruitedKg =
            result.Demand.MuscleGroups.Sum(
                x => x.EffectiveRecruitedMuscleMassKg);

        _output.WriteLine(
            $"{label,-18} " +
            $"{result.Interval.WorkPhase.ActualMinutes,8:0.00} " +
            $"{result.Interval.RecoveryPhase.ActualMinutes,8:0.00} " +
            $"{totalMinutes,9:0.00} " +
            $"{result.Interval.ResolvedDepletionFraction,10:0.0000} " +
            $"{result.Profile.ResolvedMuscleGroups.Count,8} " +
            $"{recruitedKg,10:0.00} " +
            $"{result.Demand.EstimatedGlycogenDepletedG,11:0.0} " +
            $"{result.Demand.CarbohydrateReloadRequirementG,10:0.0} " +
            $"{result.Demand.CarbohydrateReloadEnergyKcal,11:0.0}");
    }

    private sealed record IntegrationResult(
        Engine30IntervalGlycogenWorkloadResult Interval,
        Engine30CardioGlycogenMuscleProfileResult Profile,
        Engine30GlycogenDemandResult Demand);
}