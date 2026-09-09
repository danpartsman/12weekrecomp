using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30CardioGlycogenMuscleProfileResolverTests
{
    private readonly Engine30CardioGlycogenMuscleProfileResolver _resolver = new();
    private readonly ITestOutputHelper _output;

    public Engine30CardioGlycogenMuscleProfileResolverTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CardioMuscleProfile_ResolvesConfiguredRegions_WithProvenance()
    {
        var workload =
            new Engine30CardioGlycogenWorkloadResult(
                ModalityCode:
                    "CYCLING",

                DurationMinutes:
                    45m,

                IntensitySourceCode:
                    Engine30CardioGlycogenIntensitySourceCodes.RpeFallback,

                ActualIntensityIndex:
                    3m,

                ReferenceIntensityIndex:
                    3m,

                IntensityRatio:
                    1m,

                DurationRatio:
                    1m,

                IntensityFactor:
                    1m,

                WorkloadFactor:
                    1m,

                ResolvedDepletionFraction:
                    0.12m,

                HeartRateZoneMinutesUsed:
                    null);

        var profile =
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
                                0.70m),

                        new Engine30CardioMuscleRegionProfile(
                            MuscleGroupCode:
                                "UPPER_BODY",

                            WholeBodySkeletalMuscleFraction:
                                0.10m,

                            RecruitmentFraction:
                                0.20m)
                    });

        var result =
            _resolver.Resolve(
                new Engine30CardioGlycogenMuscleProfileInput(
                    Workload:
                        workload,

                    ModalityProfile:
                        profile));

        Assert.Equal(
            "TEST_CYCLING_PROFILE",
            result.ProfileCode);

        Assert.Equal(
            "CYCLING",
            result.ModalityCode);

        Assert.Equal(
            Engine30CardioMuscleProfileSourceCodes.ConfigDefault,
            result.SourceCode);

        Assert.Equal(
            Engine30CardioMuscleProfileConfidenceCodes.Medium,
            result.ConfidenceCode);

        Assert.Equal(
            2,
            result.ResolvedMuscleGroups.Count);

        Assert.All(
            result.ResolvedMuscleGroups,
            x =>
                Assert.Equal(
                    0.12m,
                    x.DepletionFraction));

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 - CARDIO GLYCOGEN MUSCLE PROFILE RESOLVER");

        _output.WriteLine(
            "DRAFT profile values; validates anatomy/recruitment separation only.");

        _output.WriteLine("");
        _output.WriteLine(
            new string('-', 104));

        _output.WriteLine(
            $"{"Region",-18} " +
            $"{"WholeBodyFx",12} " +
            $"{"RecruitFx",10} " +
            $"{"DepleteFx",10}");

        _output.WriteLine(
            new string('-', 104));

        foreach (var region in result.ResolvedMuscleGroups)
        {
            _output.WriteLine(
                $"{region.MuscleGroupCode,-18} " +
                $"{region.WholeBodySkeletalMuscleFraction,12:0.000} " +
                $"{region.RecruitmentFraction,10:0.000} " +
                $"{region.DepletionFraction,10:0.0000}");
        }

        _output.WriteLine(
            new string('-', 104));

        _output.WriteLine(
            $"Profile={result.ProfileCode} | Source={result.SourceCode} | Confidence={result.ConfidenceCode}");
    }

    [Fact]
    public void ZeroDuration_Resolves_NoMuscleGroups()
    {
        var workload =
            new Engine30CardioGlycogenWorkloadResult(
                ModalityCode:
                    "CYCLING",

                DurationMinutes:
                    0m,

                IntensitySourceCode:
                    Engine30CardioGlycogenIntensitySourceCodes.RpeFallback,

                ActualIntensityIndex:
                    3m,

                ReferenceIntensityIndex:
                    3m,

                IntensityRatio:
                    1m,

                DurationRatio:
                    0m,

                IntensityFactor:
                    1m,

                WorkloadFactor:
                    0m,

                ResolvedDepletionFraction:
                    0m,

                HeartRateZoneMinutesUsed:
                    null);

        var result =
            _resolver.Resolve(
                new Engine30CardioGlycogenMuscleProfileInput(
                    Workload:
                        workload,

                    ModalityProfile:
                        CreateCyclingProfile(
                            Engine30CardioMuscleProfileSourceCodes.ConfigDefault,
                            Engine30CardioMuscleProfileConfidenceCodes.Medium)));

        Assert.Empty(
            result.ResolvedMuscleGroups);
    }

    [Fact]
    public void PersonalizedProfile_Preserves_Source_And_Confidence()
    {
        var workload =
            ReferenceWorkload();

        var result =
            _resolver.Resolve(
                new Engine30CardioGlycogenMuscleProfileInput(
                    Workload:
                        workload,

                    ModalityProfile:
                        CreateCyclingProfile(
                            Engine30CardioMuscleProfileSourceCodes.Personalized,
                            Engine30CardioMuscleProfileConfidenceCodes.High)));

        Assert.Equal(
            Engine30CardioMuscleProfileSourceCodes.Personalized,
            result.SourceCode);

        Assert.Equal(
            Engine30CardioMuscleProfileConfidenceCodes.High,
            result.ConfidenceCode);
    }

    [Fact]
    public void ModalityMismatch_IsRejected()
    {
        var workload =
            ReferenceWorkload();

        var profile =
            new Engine30CardioGlycogenModalityProfile(
                ProfileCode:
                    "TEST_RUNNING",

                ModalityCode:
                    "RUNNING",

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
                                0.75m)
                    });

        Assert.Throws<ArgumentException>(
            () =>
                _resolver.Resolve(
                    new Engine30CardioGlycogenMuscleProfileInput(
                        Workload:
                            workload,

                        ModalityProfile:
                            profile)));
    }

    private static Engine30CardioGlycogenWorkloadResult ReferenceWorkload()
        => new(
            ModalityCode:
                "CYCLING",

            DurationMinutes:
                45m,

            IntensitySourceCode:
                Engine30CardioGlycogenIntensitySourceCodes.RpeFallback,

            ActualIntensityIndex:
                3m,

            ReferenceIntensityIndex:
                3m,

            IntensityRatio:
                1m,

            DurationRatio:
                1m,

            IntensityFactor:
                1m,

            WorkloadFactor:
                1m,

            ResolvedDepletionFraction:
                0.12m,

            HeartRateZoneMinutesUsed:
                null);

    private static Engine30CardioGlycogenModalityProfile CreateCyclingProfile(
        string source,
        string confidence)
        => new(
            ProfileCode:
                "TEST_CYCLING_PROFILE",

            ModalityCode:
                "CYCLING",

            SourceCode:
                source,

            ConfidenceCode:
                confidence,

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
}