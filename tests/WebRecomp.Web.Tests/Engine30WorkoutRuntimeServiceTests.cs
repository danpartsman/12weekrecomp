using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30WorkoutRuntimeServiceTests
{
    private const decimal BaseLiving = 1900m;
    private const decimal WeightLb = 200m;
    private const decimal FfmLb = 160m;
    private const decimal SkeletalMuscleFraction = 0.528m;
    private const decimal BaselineGlycogen = 23m;

    [Fact]
    public async Task ActiveAdminEnergyCalibration_ChangesRuntimeEnergy()
    {
        var low = CardioProfile(kcalPerMinute: 5m, depletion: 0.12m);
        var high = CardioProfile(kcalPerMinute: 10m, depletion: 0.12m);

        var lowResult = await Calculate(low, "E30-ZONE2_CARDIO-V0002");
        var highResult = await Calculate(high, "E30-ZONE2_CARDIO-V0003");

        Assert.True(
            highResult.Energy.EngineNetWorkoutEnergyKcal
            > lowResult.Energy.EngineNetWorkoutEnergyKcal);

        Assert.Equal(
            "E30-ZONE2_CARDIO-V0003",
            highResult.ProfileProvenance.ProfileVersionCode);
    }

    [Fact]
    public async Task ActiveAdminGlycogenCalibration_ChangesRuntimeGlycogen()
    {
        var low = CardioProfile(kcalPerMinute: 6m, depletion: 0.08m);
        var high = CardioProfile(kcalPerMinute: 6m, depletion: 0.18m);

        var lowResult = await Calculate(low, "E30-ZONE2_CARDIO-V0004");
        var highResult = await Calculate(high, "E30-ZONE2_CARDIO-V0005");

        Assert.True(
            highResult.Glycogen.EstimatedGlycogenDepletedG
            > lowResult.Glycogen.EstimatedGlycogenDepletedG);
    }

    [Fact]
    public async Task GlycogenDepletion_CannotExceedNinetyPercentOfGrossExerciseEnergy()
    {
        var profile =
            CardioProfile(
                kcalPerMinute: 2m,
                depletion: 0.70m);

        var result =
            await Calculate(
                profile,
                "E30-ZONE2_CARDIO-V0014",
                plannedMinutes: 120m);

        var maximumGlycogenG =
            result.Energy.GrossWorkoutEnergyKcal
            * 0.90m
            / 4m;

        Assert.True(
            result.Glycogen.EstimatedGlycogenDepletedG
            <= maximumGlycogenG + 0.01m);

        Assert.Equal(
            result.Glycogen.CarbohydrateReloadRequirementG * 4m,
            result.Glycogen.CarbohydrateReloadEnergyKcal);
    }

    [Fact]
    public async Task SubstratePartition_SeparatesMuscleGlycogenFromOtherWorkoutFuels()
    {
        var profile =
            CardioProfile(
                kcalPerMinute: 7m,
                depletion: 0.70m);

        var result =
            await Calculate(
                profile,
                "E30-ZONE2_CARDIO-V0015",
                plannedMinutes: 120m);

        Assert.NotNull(
            result.SubstratePartition);

        var substrate =
            result.SubstratePartition!;

        Assert.True(
            substrate.MuscleGlycogenEnergyKcal > 0m);

        Assert.True(
            substrate.BloodGlucoseEnergyKcal > 0m);

        Assert.True(
            substrate.FatEnergyKcal > 0m);

        Assert.True(
            substrate.ProteinEnergyKcal > 0m);

        Assert.True(
            substrate.MuscleGlycogenEnergyKcal
            < substrate.GrossEnergyKcal);

        Assert.True(
            substrate.TotalCarbohydrateEnergyFraction < 0.90m);

        Assert.Equal(
            "SUBSTRATE_PARTITION_BOUND_APPLIED",
            result.GlycogenReconciliationCode);

        var substrateCeilingG =
            substrate.EstimatedMuscleGlycogenG
            * 1.20m;

        Assert.True(
            result.Glycogen.EstimatedGlycogenDepletedG
            <= substrateCeilingG + 0.02m);
    }

    [Fact]
    public async Task LongModerateCardio_ShiftsAwayFromMuscleGlycogenAsDurationAccumulates()
    {
        var result =
            await Calculate(
                CardioProfile(
                    kcalPerMinute: 7m,
                    depletion: 0.12m),
                "E30-ZONE2_CARDIO-V0016",
                plannedMinutes: 120m);

        var blocks =
            Assert.Single(
                result.SubstratePartition!.Segments)
                .Blocks;

        Assert.True(blocks.Count >= 10);

        var first = blocks.First();
        var last = blocks.Last();

        Assert.True(
            last.MuscleGlycogenFraction
            < first.MuscleGlycogenFraction);

        Assert.True(
            last.FatFraction
            > first.FatFraction);

        Assert.True(
            last.BloodGlucoseFraction
            > first.BloodGlucoseFraction);
    }

    [Fact]
    public async Task HeartRateZoneEvidence_IsPreferredOverRpeForSubstrateIntensity()
    {
        var profile =
            CardioProfile(
                kcalPerMinute: 7m,
                depletion: 0.12m);

        var provider =
            new FakeProfileProvider(
                profile,
                "E30-ZONE2_CARDIO-V0017");

        var service =
            new Engine30WorkoutRuntimeService(
                provider);

        var result =
            await service.CalculateAsync(
                new Engine30WorkoutRuntimeRequest(
                    Session:
                        new Engine30WorkoutSessionInput(
                            WorkoutTypeCode: "ZONE2_CARDIO",
                            PlannedDurationMinutes: 60m,
                            MonitorData:
                                new Engine30WorkoutMonitorData(
                                    MinutesZone1: 5m,
                                    MinutesZone2: 45m,
                                    MinutesZone3: 10m)),
                    BaseLivingEnergyKcal: BaseLiving,
                    WeightLb: WeightLb,
                    FatFreeMassLb: FfmLb,
                    SkeletalMuscleFractionOfFfm: SkeletalMuscleFraction,
                    BaselineGlycogenGPerKgMuscle: BaselineGlycogen));

        Assert.All(
            result.SubstratePartition!.Segments.Single().Blocks,
            block =>
                Assert.Equal(
                    Engine30SubstratePartitionSourceCodes.HeartRateZones,
                    block.IntensitySourceCode));
    }

    [Fact]
    public async Task RuntimeResult_PreservesActiveProfileProvenance()
    {
        var result =
            await Calculate(
                CardioProfile(6m, 0.12m),
                "E30-ZONE2_CARDIO-V0012");

        Assert.Equal(
            Engine30WorkoutRuntimeProfileSourceCodes.AdminActiveProfile,
            result.ProfileProvenance.SourceCode);

        Assert.Equal(12, result.ProfileProvenance.ProfileVersionNumber);
        Assert.Equal("CFG-3000.0", result.ProfileProvenance.ConfigVersionCode);
    }

    [Fact]
    public async Task DefaultDuration_ComesFromAdminProfile_WhenPlanDoesNotOverrideIt()
    {
        var profile = CardioProfile(6m, 0.12m);
        profile.DefaultDurationMinutes = 50m;

        var result =
            await Calculate(
                profile,
                "E30-ZONE2_CARDIO-V0013",
                plannedMinutes: 0m);

        Assert.Equal(
            50m,
            result.ResolvedSession.AnalysisDuration.Minutes);

        Assert.Equal(
            50m,
            result.Energy.ModeledDurationMinutes);
    }

    private static Task<Engine30WorkoutRuntimeResult> Calculate(
        Engine30WorkoutAdminProfile profile,
        string versionCode,
        decimal plannedMinutes = 60m)
    {
        var provider =
            new FakeProfileProvider(
                profile,
                versionCode);

        var service =
            new Engine30WorkoutRuntimeService(provider);

        return service.CalculateAsync(
            new Engine30WorkoutRuntimeRequest(
                Session: new Engine30WorkoutSessionInput(
                    WorkoutTypeCode: "ZONE2_CARDIO",
                    PlannedDurationMinutes: plannedMinutes),
                BaseLivingEnergyKcal: BaseLiving,
                WeightLb: WeightLb,
                FatFreeMassLb: FfmLb,
                SkeletalMuscleFractionOfFfm: SkeletalMuscleFraction,
                BaselineGlycogenGPerKgMuscle: BaselineGlycogen));
    }

    private static Engine30WorkoutAdminProfile CardioProfile(
        decimal kcalPerMinute,
        decimal depletion)
        => new()
        {
            WorkoutFamilyCode = Engine30WorkoutFamilyCodes.Cardio,
            DefaultModalityCode = Engine30WorkoutModalityCodes.GeneralCardio,
            TrainingPatternCode = Engine30TrainingPatternCodes.Zone2,
            SessionStructureCode = Engine30WorkoutSessionCodes.StructureContinuous,
            DefaultDurationMinutes = 45m,
            DefaultRpe = 3m,
            ReferenceWorkoutCaloriesPerMinute = kcalPerMinute,
            ReferenceRpe = 3m,
            ReferenceWeightLb = 180m,
            ScalingBasisCode = "BODY_WEIGHT",
            GlycogenModelCode = "CONTINUOUS",
            GlycogenReferenceDurationMinutes = 45m,
            GlycogenReferenceDepletionFraction = depletion,
            GlycogenIntensitySensitivity = 0.50m,
            GlycogenMinimumDepletionFraction = 0.01m,
            GlycogenMaximumDepletionFraction = 0.70m,
            TargetRestorationFraction = 1.00m,
            CarbohydratePerGlycogenG = 1.00m,
            MuscleRegions =
            [
                new Engine30WorkoutAdminMuscleRegion
                {
                    MuscleRegionCode = "LOWER_BODY",
                    WholeBodyMuscleFraction = 0.50m,
                    RecruitmentFraction = 0.70m,
                    HardSets = 0m
                }
            ]
        };

    private sealed class FakeProfileProvider(
        Engine30WorkoutAdminProfile profile,
        string versionCode)
        : IEngine30WorkoutRuntimeProfileProvider
    {
        public Task<Engine30WorkoutRuntimeProfileResolution> ResolveAsync(
            string workoutTypeCode,
            CancellationToken cancellationToken = default)
        {
            var versionNumber =
                int.Parse(versionCode[^4..]);

            return Task.FromResult(
                new Engine30WorkoutRuntimeProfileResolution(
                    profile,
                    new Engine30WorkoutRuntimeProfileProvenance(
                        ConfigVersionCode: "CFG-3000.0",
                        WorkoutTypeDefinitionId: Guid.NewGuid(),
                        WorkoutTypeCode: workoutTypeCode,
                        WorkoutDisplayName: "Zone 2 Cardio",
                        SourceCode: Engine30WorkoutRuntimeProfileSourceCodes.AdminActiveProfile,
                        ProfileVersionId: Guid.NewGuid(),
                        ProfileVersionCode: versionCode,
                        ProfileVersionNumber: versionNumber,
                        IsBaseline: false,
                        ProfileCreatedAtUtc: DateTime.UtcNow)));
        }
    }
}
