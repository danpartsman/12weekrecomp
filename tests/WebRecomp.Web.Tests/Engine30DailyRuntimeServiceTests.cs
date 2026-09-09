using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30DailyRuntimeServiceTests
{
    private static readonly DateOnly ActionDate = new(2026, 9, 3);
    private static readonly DateOnly Dob = new(1986, 1, 1);

    [Fact]
    public async Task NoWorkout_MaintenanceIsBasePlusHabitual_AndGlycogenIsZero()
    {
        var service =
            CreateService(
                CardioProfile());

        var result =
            await service.CalculateAsync(
                BaseRequest(workout: null));

        Assert.Null(result.Workout);
        Assert.False(result.Glycogen.HasWorkout);
        Assert.Equal(0m, result.Glycogen.EstimatedGlycogenDepletedG);
        Assert.Equal(0m, result.Energy.NetWorkoutActivityEnergyKcal);

        Assert.Equal(
            Round(
                result.Energy.BaseLivingEnergyKcal
                + result.Energy.HabitualActivityEnergyKcal),
            result.Energy.MaintenanceEnergyKcal);
    }

    [Fact]
    public async Task Workout_MaintenanceAddsOnlyNetWorkoutEnergy()
    {
        var service =
            CreateService(
                CardioProfile());

        var result =
            await service.CalculateAsync(
                BaseRequest(
                    workout: new Engine30WorkoutSessionInput(
                        WorkoutTypeCode: "ZONE2_CARDIO",
                        PlannedDurationMinutes: 60m)));

        Assert.NotNull(result.Workout);

        Assert.Equal(
            result.Workout!.Energy.EngineNetWorkoutEnergyKcal,
            result.Energy.NetWorkoutActivityEnergyKcal);

        Assert.Equal(
            Round(
                result.Energy.BaseLivingEnergyKcal
                + result.Energy.HabitualActivityEnergyKcal
                + result.Workout.Energy.EngineNetWorkoutEnergyKcal),
            result.Energy.MaintenanceEnergyKcal);
    }

    [Fact]
    public async Task GlycogenReloadEnergy_IsDiagnosticOnly_NotMaintenanceBucket()
    {
        var service =
            CreateService(
                CardioProfile(depletion: 0.18m));

        var result =
            await service.CalculateAsync(
                BaseRequest(
                    workout: new Engine30WorkoutSessionInput(
                        WorkoutTypeCode: "ZONE2_CARDIO",
                        PlannedDurationMinutes: 60m)));

        Assert.True(
            result.Glycogen.CarbohydrateReloadEnergyKcalDiagnostic > 0m);

        Assert.False(
            result.Glycogen.ReloadEnergyIncludedInMaintenance);

        var wrongMaintenance =
            Round(
                result.Energy.BaseLivingEnergyKcal
                + result.Energy.HabitualActivityEnergyKcal
                + result.Energy.NetWorkoutActivityEnergyKcal
                + result.Glycogen.CarbohydrateReloadEnergyKcalDiagnostic);

        Assert.NotEqual(
            wrongMaintenance,
            result.Energy.MaintenanceEnergyKcal);
    }

    [Fact]
    public async Task BodyFatObservation_DrivesFatFreeMassUsedByWorkout()
    {
        var service =
            CreateService(
                CardioProfile());

        var result =
            await service.CalculateAsync(
                BaseRequest(
                    workout: new Engine30WorkoutSessionInput(
                        WorkoutTypeCode: "ZONE2_CARDIO",
                        PlannedDurationMinutes: 60m)));

        Assert.Equal(
            "BODY_FAT_DERIVED",
            result.BodyComposition.BodyCompositionSourceCode);

        Assert.Equal(
            160m,
            result.BodyComposition.FatFreeMassLb);

        Assert.Equal(
            0.528m,
            result.Provenance.SkeletalMuscleFractionOfFfm);
    }

    [Fact]
    public async Task DailyResult_PreservesWorkoutProfileVersionProvenance()
    {
        var profile =
            CardioProfile();

        var service =
            CreateService(
                profile,
                versionCode: "E30-ZONE2_CARDIO-V0042");

        var result =
            await service.CalculateAsync(
                BaseRequest(
                    workout: new Engine30WorkoutSessionInput(
                        WorkoutTypeCode: "ZONE2_CARDIO",
                        PlannedDurationMinutes: 60m)));

        Assert.NotNull(
            result.Provenance.WorkoutProfile);

        Assert.Equal(
            "E30-ZONE2_CARDIO-V0042",
            result.Provenance.WorkoutProfile!.ProfileVersionCode);

        Assert.Equal(
            Engine30WorkoutRuntimeProfileSourceCodes.AdminActiveProfile,
            result.Provenance.WorkoutProfile.SourceCode);
    }

    [Fact]
    public async Task DailyResult_ExposesThreeBucketComposition()
    {
        var service =
            CreateService(
                CardioProfile());

        var result =
            await service.CalculateAsync(
                BaseRequest(
                    workout: new Engine30WorkoutSessionInput(
                        WorkoutTypeCode: "ZONE2_CARDIO",
                        PlannedDurationMinutes: 45m)));

        Assert.True(
            result.Energy.BaseLivingEnergyKcal > 0m);

        Assert.True(
            result.Energy.HabitualActivityEnergyKcal > 0m);

        Assert.True(
            result.Energy.NetWorkoutActivityEnergyKcal > 0m);

        Assert.Equal(
            "BASE_LIVING_PLUS_HABITUAL_PLUS_NET_WORKOUT",
            result.Energy.FormulaCode);
    }

    private static Engine30DailyRuntimeRequest BaseRequest(
        Engine30WorkoutSessionInput? workout)
        => new(
            ActionDate: ActionDate,
            DateOfBirth: Dob,
            SexCode: "MALE",
            HeightIn: 72m,
            WeightLb: 200m,
            BodyFatFraction: 0.20m,
            OccupationalRoles:
            [
                new Engine30OccupationalRoleInput(
                    OccupationName: "Office",
                    OccupationActivityCode: "SEDENTARY",
                    TypicalHoursPerWeek: 40m,
                    EffectiveOccupationalActivityFactor: 0.20m)
            ],
            LifestyleActivityCode: "LIGHT",
            WorkoutSession: workout);

    private static Engine30DailyRuntimeService CreateService(
        Engine30WorkoutAdminProfile profile,
        string versionCode = "E30-ZONE2_CARDIO-V0007")
    {
        var provider =
            new FakeProfileProvider(
                profile,
                versionCode);

        var workoutRuntime =
            new Engine30WorkoutRuntimeService(
                provider);

        return new Engine30DailyRuntimeService(
            workoutRuntime);
    }

    private static Engine30WorkoutAdminProfile CardioProfile(
        decimal kcalPerMinute = 6m,
        decimal depletion = 0.12m)
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
            var digits =
                new string(
                    versionCode
                        .Reverse()
                        .TakeWhile(char.IsDigit)
                        .Reverse()
                        .ToArray());

            var versionNumber =
                string.IsNullOrWhiteSpace(digits)
                    ? 1
                    : int.Parse(digits);

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

    private static decimal Round(decimal value)
        => Math.Round(
            value,
            2,
            MidpointRounding.AwayFromZero);
}
