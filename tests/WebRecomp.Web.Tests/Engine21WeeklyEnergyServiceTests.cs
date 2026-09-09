using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine21WeeklyEnergyServiceTests
{
    private readonly Engine21WeeklyEnergyService _weekly = new();
    private readonly Engine21EnergyService _energy = new();

    [Fact]
    public void WeeklyBudget_UsesNormalizedCategoryWeights_AndPreservesWeeklyTotal()
    {
        var days = ExampleWeek();

        var result =
            _weekly.Calculate(
                provisionalNonWorkoutEnergyKcal: 2740m,
                restingEnergyKcal: 1935m,
                targetDeficitPct: 0.15m,
                plannedDays: days,
                settings: Settings());

        Assert.Equal(
            21043.91m,
            result.WeeklyMaintenanceEnergyKcal);

        Assert.Equal(
            17890m,
            result.WeeklyPrescribedEnergyKcal);

        Assert.Equal(
            2555.71m,
            result.AveragePrescribedEnergyKcal);

        Assert.Equal(
            17890m,
            result.Days.Sum(x => x.IntendedEnergyTargetKcal));

        // Major day receives more of the fixed weekly budget.
        Assert.Equal(
            3220m,
            result.Days[0].IntendedEnergyTargetKcal);

        // True rest day receives substantially less.
        Assert.Equal(
            1930m,
            result.Days[3].IntendedEnergyTargetKcal);

        Assert.True(
            result.Days[0].IntendedEnergyTargetKcal
            > result.Days[3].IntendedEnergyTargetKcal);
    }

    [Fact]
    public void WorkoutEnergy_AddsOnlyEnergyAboveRestingCost()
    {
        var result =
            _weekly.Calculate(
                provisionalNonWorkoutEnergyKcal: 2740m,
                restingEnergyKcal: 1935m,
                targetDeficitPct: 0.15m,
                plannedDays: ExampleWeek(),
                settings: Settings());

        var majorLegs =
            result.Days[0];

        Assert.Equal(
            650m,
            majorLegs.GrossWorkoutEnergyKcal);

        // 1935 / 1440 * 60 = 80.625 resting kcal
        // 650 - 80.625 = 569.375 net workout kcal
        Assert.Equal(
            569.38m,
            majorLegs.NetWorkoutEnergyKcal);

        Assert.Equal(
            3309.38m,
            majorLegs.EstimatedMaintenanceEnergyKcal);

        Assert.True(
            majorLegs.NetWorkoutEnergyKcal
            < majorLegs.GrossWorkoutEnergyKcal);
    }

    [Fact]
    public void CategoryWeights_ChangeDistribution_NotWeeklyEnergyBudget()
    {
        var start =
            new DateOnly(2026, 9, 7);

        Engine21PlannedEnergyDay[] days =
        [
            new(start, "MAJOR_STRENGTH", 0m, 0m),
            new(start.AddDays(1), "ACCESSORY_STRENGTH", 0m, 0m),
            new(start.AddDays(2), "CONDITIONING_CARDIO", 0m, 0m),
            new(start.AddDays(3), "REST_RECOVERY_OTHER", 0m, 0m),
            new(start.AddDays(4), "REST_RECOVERY_OTHER", 0m, 0m),
            new(start.AddDays(5), "REST_RECOVERY_OTHER", 0m, 0m),
            new(start.AddDays(6), "REST_RECOVERY_OTHER", 0m, 0m)
        ];

        var result =
            _weekly.Calculate(
                provisionalNonWorkoutEnergyKcal: 2000m,
                restingEnergyKcal: 1500m,
                targetDeficitPct: 0.10m,
                plannedDays: days,
                settings: Settings());

        // 2000 * 7 * 90%
        Assert.Equal(
            12600m,
            result.WeeklyPrescribedEnergyKcal);

        Assert.Equal(
            12600m,
            result.Days.Sum(x => x.IntendedEnergyTargetKcal));

        Assert.True(
            result.Days[0].IntendedEnergyTargetKcal
            > result.Days[1].IntendedEnergyTargetKcal);

        Assert.True(
            result.Days[1].IntendedEnergyTargetKcal
            > result.Days[2].IntendedEnergyTargetKcal);

        Assert.True(
            result.Days[2].IntendedEnergyTargetKcal
            > result.Days[3].IntendedEnergyTargetKcal);
    }

    [Fact]
    public void WeeklyAverageFeedsStableFatTarget()
    {
        var week =
            _weekly.Calculate(
                provisionalNonWorkoutEnergyKcal: 2740m,
                restingEnergyKcal: 1935m,
                targetDeficitPct: 0.15m,
                plannedDays: ExampleWeek(),
                settings: Settings());

        var fat =
            _energy.CalculateFatTarget(
                currentWeightLb: 210m,
                averagePrescribedEnergyKcal:
                    week.AveragePrescribedEnergyKcal,
                settings:
                    Settings());

        Assert.Equal(
            2555.71m,
            fat.AveragePrescribedEnergyKcal);

        Assert.Equal(
            55m,
            fat.RecommendedFatG);

        Assert.Equal(
            "ENERGY_PERCENT",
            fat.GoverningRuleCode);
    }

    [Fact]
    public void EnergyReference_CalculatesBmiAsContext()
    {
        var result =
            _energy.CalculateReference(
                actionDate:
                    new DateOnly(2026, 9, 1),

                dateOfBirth:
                    new DateOnly(1963, 1, 1),

                sexCode:
                    "M",

                heightIn:
                    73m,

                weightLb:
                    210m,

                baselineStepsDay:
                    7000,

                recentDailySteps:
                    []);

        Assert.Equal(
            27.71m,
            result.Bmi);
    }

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
            ["FAT_ROUNDING_G"] = 5m
        };
}
