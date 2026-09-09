using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine21PipelineServiceTests
{
    private readonly Engine21EnergyService _energy = new();
    private readonly Engine21WeeklyEnergyService _weekly = new();
    private readonly Engine21DeficitService _deficit = new();

    [Fact]
    public void FatLossProfile_ComposesEntireEnergyPipeline()
    {
        var result =
            Pipeline().Calculate(
                StandardMale(
                    planCode:
                        "PLAN_2"),

                ExampleWeek(),

                Settings());

        Assert.Equal(
            27.71m,
            result.EnergyReference.Bmi);

        Assert.Equal(
            2740m,
            result.EnergyReference.ProvisionalNonWorkoutEnergyKcal);

        Assert.Equal(
            21043.91m,
            result.WeeklyBudget.WeeklyMaintenanceEnergyKcal);

        Assert.Equal(
            0.15m,
            result.Deficit.EffectiveDeficitPct);

        Assert.Equal(
            3156.59m,
            result.Deficit.TargetWeeklyDeficitKcal);

        Assert.Equal(
            17890m,
            result.WeeklyBudget.WeeklyPrescribedEnergyKcal);

        Assert.Equal(
            2555.71m,
            result.WeeklyBudget.AveragePrescribedEnergyKcal);

        Assert.Equal(
            55m,
            result.FatTarget.RecommendedFatG);

        Assert.Equal(
            result.Deficit.WeeklyPrescribedEnergyKcal,
            result.WeeklyBudget.WeeklyPrescribedEnergyKcal);
    }

    [Fact]
    public void MuscleFocusedPlan_PreservesMoreWeeklyEnergy()
    {
        var fatLoss =
            Pipeline().Calculate(
                StandardMale(
                    "PLAN_2"),

                ExampleWeek(),

                Settings());

        var muscle =
            Pipeline().Calculate(
                StandardMale(
                    "PLAN_3"),

                ExampleWeek(),

                Settings());

        Assert.Equal(
            0.15m,
            fatLoss.Deficit.RequestedDeficitPct);

        Assert.Equal(
            0.075m,
            muscle.Deficit.RequestedDeficitPct);

        Assert.True(
            muscle.WeeklyBudget.WeeklyPrescribedEnergyKcal
            > fatLoss.WeeklyBudget.WeeklyPrescribedEnergyKcal);

        Assert.True(
            muscle.WeeklyBudget.AveragePrescribedEnergyKcal
            > fatLoss.WeeklyBudget.AveragePrescribedEnergyKcal);

        // Fat can rise slightly because the stable fat calculation is
        // based partly on average prescribed energy rather than workout type.
        Assert.True(
            muscle.FatTarget.RecommendedFatG
            >= fatLoss.FatTarget.RecommendedFatG);
    }

    [Fact]
    public void HighBmiWoman_DoesNotReceiveAutomaticExtraAggression()
    {
        var person =
            new Engine21PersonEnergyInput(
                ActionDate:
                    new DateOnly(2026, 9, 1),

                DateOfBirth:
                    new DateOnly(1976, 1, 1),

                SexCode:
                    "F",

                HeightIn:
                    64m,

                WeightLb:
                    220m,

                BaselineStepsDay:
                    3500,

                RecentDailySteps:
                    [],

                PlanCode:
                    "PLAN_2");

        var result =
            Pipeline().Calculate(
                person,
                ExampleWeek(),
                Settings());

        Assert.True(
            result.EnergyReference.Bmi
            >= 30m);

        Assert.Equal(
            0.15m,
            result.Deficit.RequestedDeficitPct);

        Assert.Equal(
            0.15m,
            result.Deficit.EffectiveDeficitPct);

        Assert.Contains(
            "BMI_HIGH_CONTEXT_NO_AUTO_AGGRESSION",
            result.Deficit.ReasonCodes);

        Assert.DoesNotContain(
            "DEFICIT_PERCENT_CAP_APPLIED",
            result.Deficit.ReasonCodes);

        Assert.True(
            result.FatTarget.RecommendedFatG
            >= 50m);
    }

    [Fact]
    public void UnderweightProfile_ReceivesNoWeightLossDeficit()
    {
        var person =
            new Engine21PersonEnergyInput(
                ActionDate:
                    new DateOnly(2026, 9, 1),

                DateOfBirth:
                    new DateOnly(1996, 1, 1),

                SexCode:
                    "F",

                HeightIn:
                    67m,

                WeightLb:
                    110m,

                BaselineStepsDay:
                    7000,

                RecentDailySteps:
                    [],

                PlanCode:
                    "PLAN_2");

        var result =
            Pipeline().Calculate(
                person,
                ExampleWeek(),
                Settings());

        Assert.True(
            result.EnergyReference.Bmi
            < 18.5m);

        Assert.Equal(
            0m,
            result.Deficit.EffectiveDeficitPct);

        Assert.Equal(
            0m,
            result.Deficit.TargetWeeklyDeficitKcal);

        Assert.Contains(
            "BMI_BELOW_DEFICIT_THRESHOLD",
            result.Deficit.ReasonCodes);

        // With no deficit, the weekly prescription should simply be
        // the rounded weekly estimated maintenance requirement.
        Assert.Equal(
            result.Deficit.WeeklyPrescribedEnergyKcal,
            result.WeeklyBudget.WeeklyPrescribedEnergyKcal);
    }

    private Engine21PipelineService Pipeline()
        => new(
            _energy,
            _weekly,
            _deficit);

    private static Engine21PersonEnergyInput StandardMale(
        string planCode)
        => new(
            ActionDate:
                new DateOnly(2026, 9, 1),

            DateOfBirth:
                new DateOnly(1963, 1, 1),

            SexCode:
                "M",

            HeightIn:
                73m,

            WeightLb:
                210m,

            BaselineStepsDay:
                6500,

            RecentDailySteps:
            [
                6000,
                6500,
                7000,
                7500,
                8000,
                7000,
                7000
            ],

            PlanCode:
                planCode);

    private static Engine21PlannedEnergyDay[] ExampleWeek()
    {
        var start =
            new DateOnly(2026, 9, 7);

        return
        [
            new(
                start,
                "MAJOR_STRENGTH",
                650m,
                60m),

            new(
                start.AddDays(1),
                "ACCESSORY_STRENGTH",
                350m,
                45m),

            new(
                start.AddDays(2),
                "CONDITIONING_CARDIO",
                500m,
                60m),

            new(
                start.AddDays(3),
                "REST_RECOVERY_OTHER",
                0m,
                0m),

            new(
                start.AddDays(4),
                "MAJOR_STRENGTH",
                500m,
                60m),

            new(
                start.AddDays(5),
                "CONDITIONING_CARDIO",
                220m,
                40m),

            new(
                start.AddDays(6),
                "REST_RECOVERY_OTHER",
                0m,
                0m)
        ];
    }

    private static IReadOnlyDictionary<string, decimal>
        Settings()
        => new Dictionary<string, decimal>
        {
            ["ENERGY_ROUNDING_KCAL"] = 10m,

            ["ENERGY_WEIGHT_MAJOR_STRENGTH"] = 1.10m,
            ["ENERGY_WEIGHT_ACCESSORY_STRENGTH"] = 1.00m,
            ["ENERGY_WEIGHT_CONDITIONING_CARDIO"] = 0.95m,
            ["ENERGY_WEIGHT_REST_RECOVERY_OTHER"] = 0.80m,

            ["FAT_TARGET_ENERGY_PCT"] = 0.20m,
            ["FAT_MIN_G_PER_KG"] = 0.50m,
            ["FAT_ROUNDING_G"] = 5m,

            ["DEFICIT_PCT_PLAN_1"] = 0.10m,
            ["DEFICIT_PCT_PLAN_2"] = 0.15m,
            ["DEFICIT_PCT_PLAN_3"] = 0.075m,
            ["DEFICIT_PCT_PLAN_4"] = 0.10m,
            ["DEFICIT_PCT_PLAN_5"] = 0.10m,

            ["DEFICIT_MAX_DAILY_KCAL"] = 500m,
            ["DEFICIT_MAX_PCT"] = 0.20m,
            ["DEFICIT_MIN_BMI"] = 18.5m
        };
}
