using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine2DecisionTests
{
    private readonly RecompEngineService _engine = new();
    private readonly RecompTrainingPrescriptionService _training = new();

    private static IReadOnlyDictionary<string, decimal> TrainingSettings() =>
        RecompTrainingPrescriptionCatalog.All
            .ToDictionary(
                x => x.Key,
                x => x.DefaultValue,
                StringComparer.OrdinalIgnoreCase);


    [Fact]
    public void Pain_Overrides_Major_Workout()
    {
        var planned = MajorPush();
        var recovery = Recovery();

        var decision = _engine.BuildWorkoutDecision(
            planned,
            FullyRecovered(),
            painFlag: true,
            illnessFlag: false,
            VeryLowFuel(),
            AdequateRescue(),
            recovery,
            heavySwapWorkout: MajorPull(),
            lightAlternativeWorkout: AccessoryUpper(),
            recoverySwapWorkout: Zone2(),
            recoveryCardioWorkout: Zone2());

        Assert.Equal("SAFETY_PAIN", decision.BasisCode);
        Assert.Equal(
            recovery.WorkoutTypeCode,
            decision.RecommendedWorkout.WorkoutTypeCode);

        Assert.True(decision.ScheduleChangeRecommended);

        Assert.Contains(
            decision.Alerts,
            x => x.Code == "PAIN_OVERRIDE");
    }


    [Fact]
    public void Illness_Overrides_Major_Workout()
    {
        var planned = MajorPush();
        var recovery = Recovery();

        var decision = _engine.BuildWorkoutDecision(
            planned,
            FullyRecovered(),
            painFlag: false,
            illnessFlag: true,
            VeryLowFuel(),
            AdequateRescue(),
            recovery,
            heavySwapWorkout: MajorPull(),
            lightAlternativeWorkout: AccessoryUpper(),
            recoverySwapWorkout: Zone2(),
            recoveryCardioWorkout: Zone2());

        Assert.Equal("SAFETY_ILLNESS", decision.BasisCode);

        Assert.Equal(
            recovery.WorkoutTypeCode,
            decision.RecommendedWorkout.WorkoutTypeCode);

        Assert.True(decision.ScheduleChangeRecommended);
    }


    [Fact]
    public void Unrecovered_Push_Can_Swap_To_Another_Major_Region()
    {
        var planned = MajorPush();
        var swap = MajorPull();

        var decision = _engine.BuildWorkoutDecision(
            planned,
            PushNotRecovered(),
            painFlag: false,
            illnessFlag: false,
            AdequateFuel(),
            AdequateNoRescue(),
            Recovery(),
            heavySwapWorkout: swap,
            lightAlternativeWorkout: AccessoryUpper(),
            recoverySwapWorkout: Zone2(),
            recoveryCardioWorkout: Zone2());

        Assert.Equal("SWAP_HEAVY_DAY", decision.BasisCode);

        Assert.Equal(
            swap.WorkoutTypeCode,
            decision.RecommendedWorkout.WorkoutTypeCode);

        Assert.True(decision.ScheduleChangeRecommended);
    }


    [Fact]
    public void Unrecovered_Push_Uses_Light_Alternative_When_No_Major_Swap()
    {
        var planned = MajorPush();
        var light = AccessoryUpper();

        var decision = _engine.BuildWorkoutDecision(
            planned,
            PushNotRecovered(),
            painFlag: false,
            illnessFlag: false,
            AdequateFuel(),
            AdequateNoRescue(),
            Recovery(),
            heavySwapWorkout: null,
            lightAlternativeWorkout: light,
            recoverySwapWorkout: Zone2(),
            recoveryCardioWorkout: Zone2());

        Assert.Equal(
            "REGION_NOT_RECOVERED",
            decision.BasisCode);

        Assert.Equal(
            light.WorkoutTypeCode,
            decision.RecommendedWorkout.WorkoutTypeCode);
    }


    [Fact]
    public void Very_Low_PreRescue_Fuel_Does_Not_Downgrade_When_Rescue_Works()
    {
        var planned = MajorPush();

        var decision = _engine.BuildWorkoutDecision(
            planned,
            FullyRecovered(),
            painFlag: false,
            illnessFlag: false,
            VeryLowFuel(),
            AdequateRescue(),
            Recovery(),
            heavySwapWorkout: null,
            lightAlternativeWorkout: AccessoryUpper(),
            recoverySwapWorkout: Zone2(),
            recoveryCardioWorkout: Zone2());

        Assert.Equal("KEEP_PLAN", decision.BasisCode);

        Assert.Equal(
            planned.WorkoutTypeCode,
            decision.RecommendedWorkout.WorkoutTypeCode);

        Assert.False(decision.ScheduleChangeRecommended);
    }


    [Fact]
    public void Workout_Downgrades_When_Fuel_Remains_Low_After_Rescue()
    {
        var planned = MajorPush();
        var recoveryCardio = Zone2();

        var rescue = new RecompRescuePlan(
            ShortfallG: 70m,
            AllocationG: 55m,
            PreWorkoutG: 35m,
            ProjectedAchievementRatio: 0.60m,
            ProjectedReadinessCode: "LOW",
            RescueIsMeaningful: true);

        var decision = _engine.BuildWorkoutDecision(
            planned,
            FullyRecovered(),
            painFlag: false,
            illnessFlag: false,
            VeryLowFuel(),
            rescue,
            Recovery(),
            heavySwapWorkout: null,
            lightAlternativeWorkout: AccessoryUpper(),
            recoverySwapWorkout: recoveryCardio,
            recoveryCardioWorkout: recoveryCardio);

        Assert.Equal(
            "LOW_FUEL_AFTER_RESCUE",
            decision.BasisCode);

        Assert.Equal(
            recoveryCardio.WorkoutTypeCode,
            decision.RecommendedWorkout.WorkoutTypeCode);

        Assert.True(decision.ScheduleChangeRecommended);
    }


    [Fact]
    public void Prescription_Uses_Final_Recommended_Workout()
    {
        var planned = MajorPush();
        var light = AccessoryUpper();

        var decision = new RecompWorkoutDecision(
            PlannedWorkout: planned,
            RecommendedWorkout: light,
            ScheduleChangeRecommended: true,
            BasisCode: "REGION_NOT_RECOVERED",
            Alerts: Array.Empty<RecompEngineAlert>());

        var prescription =
            _engine.BuildTrainingPrescription(
                decision,
                TrainingSettings(),
                _training);

        Assert.Equal(
            light.WorkoutTypeCode,
            prescription.WorkoutTypeCode);

        Assert.Equal(
            "RESISTANCE_SETS",
            prescription.PrescriptionModeCode);

        Assert.Equal(5m, prescription.TargetRpeMinimum);
        Assert.Equal(7m, prescription.TargetRpeMaximum);
    }


    [Fact]
    public void Major_Push_Produces_Approximately_23_Sets_At_Rpe_7_To_8()
    {
        var prescription =
            _training.BuildPlanned(
                MajorPush(),
                TrainingSettings());

        Assert.Equal(
            "RESISTANCE_SETS",
            prescription.PrescriptionModeCode);

        Assert.Equal(
            23,
            prescription.ApproximateWorkingSets);

        Assert.Equal(
            7m,
            prescription.TargetRpeMinimum);

        Assert.Equal(
            8m,
            prescription.TargetRpeMaximum);

        Assert.Equal(
            "WORKING_SET",
            prescription.RpeScopeCode);

        Assert.Equal(
            60m,
            prescription.RecommendedMinutes);
    }


    [Fact]
    public void Zone2_Uses_Duration_And_Session_Rpe_Not_Working_Sets()
    {
        var prescription =
            _training.BuildPlanned(
                Zone2(),
                TrainingSettings());

        Assert.Equal(
            "AEROBIC_DURATION",
            prescription.PrescriptionModeCode);

        Assert.Null(
            prescription.ApproximateWorkingSets);

        Assert.Equal(
            3m,
            prescription.TargetRpeMinimum);

        Assert.Equal(
            4m,
            prescription.TargetRpeMaximum);

        Assert.Equal(
            "SESSION",
            prescription.RpeScopeCode);

        Assert.Equal(
            40m,
            prescription.RecommendedMinutes);

        Assert.Contains(
            "mitochondrial",
            prescription.PhysiologicalPurpose,
            StringComparison.OrdinalIgnoreCase);
    }


    // ---------------------------------------------------------
    // Test fixtures
    // ---------------------------------------------------------

    private static WorkoutTypeDefinition MajorPush() =>
        Workout(
            "MAJOR_PUSH",
            "Major Push",
            weeklyRole: "HEAVY",
            region: "PUSH",
            focus: "Push",
            heavy: true,
            light: false,
            conditioning: false,
            recovery: false,
            fuelScore: 5m,
            minutes: 60m,
            restSeconds: 75,
            pushLoad: 3m,
            pullLoad: 0.25m,
            legLoad: 0m,
            cardioLoad: 0.25m,
            strengthStress: 3m,
            restorationPct: 0.80m);


    private static WorkoutTypeDefinition MajorPull() =>
        Workout(
            "MAJOR_PULL",
            "Major Pull",
            weeklyRole: "HEAVY",
            region: "PULL",
            focus: "Pull",
            heavy: true,
            light: false,
            conditioning: false,
            recovery: false,
            fuelScore: 5m,
            minutes: 60m,
            restSeconds: 75,
            pushLoad: 0.25m,
            pullLoad: 3m,
            legLoad: 0.50m,
            cardioLoad: 0.25m,
            strengthStress: 3m,
            restorationPct: 0.80m);


    private static WorkoutTypeDefinition AccessoryUpper() =>
        Workout(
            "ACCESSORY_UPPER",
            "Accessory Upper",
            weeklyRole: "LIGHT_STRENGTH",
            region: "UPPER",
            focus: "Upper Accessory",
            heavy: false,
            light: true,
            conditioning: false,
            recovery: false,
            fuelScore: 2m,
            minutes: 45m,
            restSeconds: 60,
            pushLoad: 1.50m,
            pullLoad: 1.50m,
            legLoad: 0m,
            cardioLoad: 0.25m,
            strengthStress: 2m,
            restorationPct: 0.60m);


    private static WorkoutTypeDefinition Zone2() =>
        Workout(
            "ZONE_2",
            "Zone 2",
            weeklyRole: "RECOVERY_CARDIO",
            region: null,
            focus: "Aerobic",
            heavy: false,
            light: false,
            conditioning: false,
            recovery: false,
            fuelScore: 1m,
            minutes: 40m,
            restSeconds: 0,
            pushLoad: 0m,
            pullLoad: 0m,
            legLoad: 0.25m,
            cardioLoad: 2m,
            strengthStress: 0.25m,
            restorationPct: 0.40m);


    private static WorkoutTypeDefinition Recovery() =>
        Workout(
            "RECOVERY_0_RPE2",
            "Recovery",
            weeklyRole: "RECOVERY",
            region: null,
            focus: "Recovery",
            heavy: false,
            light: false,
            conditioning: false,
            recovery: true,
            fuelScore: 0m,
            minutes: 0m,
            restSeconds: 0,
            pushLoad: 0m,
            pullLoad: 0m,
            legLoad: 0m,
            cardioLoad: 0m,
            strengthStress: 0m,
            restorationPct: 0m);


    private static WorkoutTypeDefinition Workout(
        string code,
        string name,
        string weeklyRole,
        string? region,
        string focus,
        bool heavy,
        bool light,
        bool conditioning,
        bool recovery,
        decimal fuelScore,
        decimal minutes,
        short restSeconds,
        decimal pushLoad,
        decimal pullLoad,
        decimal legLoad,
        decimal cardioLoad,
        decimal strengthStress,
        decimal restorationPct)
    {
        return new WorkoutTypeDefinition
        {
            WorkoutTypeDefinitionId = Guid.NewGuid(),
            ConfigVersionId = Guid.NewGuid(),

            WorkoutTypeCode = code,
            DisplayName = name,

            WorkoutCategoryCode =
                weeklyRole switch
                {
                    "HEAVY" or "HEAVY_GENERIC" =>
                        "MAJOR_STRENGTH",

                    "LIGHT_STRENGTH" =>
                        "ACCESSORY_STRENGTH",

                    "CONDITIONING" or "RECOVERY_CARDIO" =>
                        "CONDITIONING_CARDIO",

                    "RECOVERY" or "OTHER_LOAD" =>
                        "REST_RECOVERY_OTHER",

                    _ => throw new InvalidOperationException(
                        $"Unknown test weekly role '{weeklyRole}'.")
                },

            WeeklyRoleCode = weeklyRole,
            RegionCode = region,

            CountsDedicatedHeavy = heavy,
            CountsLightStrength = light,
            CountsPrimaryConditioning = conditioning,
            CountsRecovery = recovery,

            FuelPriorityCode =
                fuelScore >= 4m
                    ? "HIGH"
                    : fuelScore >= 2m
                        ? "MODERATE"
                        : "LOW",

            SchedulePrincipleCode = "TEST",

            MacroClassCode = weeklyRole,
            PrimaryFocusCode = focus,

            FuelScore = fuelScore,
            DefaultMinutes = minutes,

            RawGlycogenGMin =
                fuelScore == 0m
                    ? 0m
                    : 1m,

            DefaultIntensityFactor = 1m,
            DefaultRestSec = restSeconds,

            ReferenceWorkoutCalPerMin =
                fuelScore == 0m
                    ? 0m
                    : 8m,

            GlycogenCarbShare = 1m,

            PushLoad = pushLoad,
            PullLoad = pullLoad,
            LegLoad = legLoad,
            CardioLoad = cardioLoad,

            StrengthStress = strengthStress,
            PlannedRestorationPct = restorationPct,

            ProteinAdjustmentG = 0m,
            TargetFatG = 50m,
            TypicalRecoveryHours = 48m,

            IsActive = true
        };
    }


    private static RegionRecovery FullyRecovered() =>
        new(
            0m, 0m, 0m, 0m,
            true, true, true, true,
            0m, 0m, 0m, 0m,
            0m, 0m, 0m, 0m,
            "BALANCED",
            "ADEQUATE");


    private static RegionRecovery PushNotRecovered() =>
        new(
            2m, 0m, 0m, 0m,
            false, true, true, true,
            2m, 0m, 0m, 0m,
            3m, 0m, 0m, 0m,
            "PULL",
            "ADEQUATE");


    private static RecompFuelState AdequateFuel() =>
        new(
            DemandG: 100m,
            RestorationCreditG: 100m,
            AbsoluteRestorationRatio: 1m,
            FunctionalTargetRatio: 0.80m,
            PreRescueAchievementRatio: 1.25m,
            PreRescueReadinessCode: "ADEQUATE",
            ModerateHardDays: 1,
            HardDays: 1);


    private static RecompFuelState VeryLowFuel() =>
        new(
            DemandG: 100m,
            RestorationCreditG: 30m,
            AbsoluteRestorationRatio: 0.30m,
            FunctionalTargetRatio: 0.80m,
            PreRescueAchievementRatio: 0.375m,
            PreRescueReadinessCode: "VERY_LOW",
            ModerateHardDays: 2,
            HardDays: 2);


    private static RecompRescuePlan AdequateRescue() =>
        new(
            ShortfallG: 50m,
            AllocationG: 50m,
            PreWorkoutG: 50m,
            ProjectedAchievementRatio: 1m,
            ProjectedReadinessCode: "ADEQUATE",
            RescueIsMeaningful: true);


    private static RecompRescuePlan AdequateNoRescue() =>
        new(
            ShortfallG: 0m,
            AllocationG: 0m,
            PreWorkoutG: 0m,
            ProjectedAchievementRatio: 1.25m,
            ProjectedReadinessCode: "ADEQUATE",
            RescueIsMeaningful: false);
}

