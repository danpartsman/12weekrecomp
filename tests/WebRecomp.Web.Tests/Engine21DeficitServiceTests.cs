using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine21DeficitServiceTests
{
    private readonly Engine21DeficitService _service = new();

    [Fact]
    public void FatLossRecomp_StartsAtFifteenPercent()
    {
        var result =
            _service.Calculate(
                planCode: "PLAN_2",
                weeklyMaintenanceEnergyKcal: 21043.91m,
                bmi: 27.71m,
                settings: Settings());

        Assert.Equal(
            0.15m,
            result.RequestedDeficitPct);

        Assert.Equal(
            0.15m,
            result.EffectiveDeficitPct);

        Assert.Equal(
            3156.59m,
            result.TargetWeeklyDeficitKcal);

        Assert.Equal(
            450.94m,
            result.TargetAverageDailyDeficitKcal);

        Assert.Equal(
            17890m,
            result.WeeklyPrescribedEnergyKcal);

        Assert.Contains(
            "PLAN_FAT_LOSS_RECOMP",
            result.ReasonCodes);

        Assert.DoesNotContain(
            "DEFICIT_ABSOLUTE_CAP_APPLIED",
            result.ReasonCodes);
    }

    [Fact]
    public void MuscleRecomp_UsesSmallerStartingDeficit()
    {
        var result =
            _service.Calculate(
                planCode: "PLAN_3",
                weeklyMaintenanceEnergyKcal: 21043.91m,
                bmi: 27.71m,
                settings: Settings());

        Assert.Equal(
            0.075m,
            result.RequestedDeficitPct);

        Assert.Equal(
            1578.29m,
            result.TargetWeeklyDeficitKcal);

        Assert.Equal(
            225.47m,
            result.TargetAverageDailyDeficitKcal);

        Assert.Contains(
            "PLAN_MUSCLE_RECOMP",
            result.ReasonCodes);
    }

    [Fact]
    public void LargeEnergyRequirement_IsCappedAtFiveHundredPerDay()
    {
        var result =
            _service.Calculate(
                planCode: "PLAN_2",
                weeklyMaintenanceEnergyKcal: 30000m,
                bmi: 31m,
                settings: Settings());

        Assert.Equal(
            0.15m,
            result.RequestedDeficitPct);

        // 15% would be 4,500 kcal/week.
        // Engine 2.1 constrains that to 500 x 7 = 3,500.
        Assert.Equal(
            3500m,
            result.TargetWeeklyDeficitKcal);

        Assert.Equal(
            500m,
            result.TargetAverageDailyDeficitKcal);

        Assert.Equal(
            0.116667m,
            result.EffectiveDeficitPct);

        Assert.Equal(
            26500m,
            result.WeeklyPrescribedEnergyKcal);

        Assert.Contains(
            "DEFICIT_ABSOLUTE_CAP_APPLIED",
            result.ReasonCodes);
    }

    [Fact]
    public void HighBmi_DoesNotAutomaticallyIncreaseDeficit()
    {
        var result =
            _service.Calculate(
                planCode: "PLAN_2",
                weeklyMaintenanceEnergyKcal: 21000m,
                bmi: 36m,
                settings: Settings());

        Assert.Equal(
            0.15m,
            result.RequestedDeficitPct);

        Assert.Equal(
            0.15m,
            result.EffectiveDeficitPct);

        Assert.Contains(
            "BMI_HIGH_CONTEXT_NO_AUTO_AGGRESSION",
            result.ReasonCodes);
    }

    [Fact]
    public void UnderweightBmi_DoesNotReceiveWeightLossDeficit()
    {
        var result =
            _service.Calculate(
                planCode: "PLAN_2",
                weeklyMaintenanceEnergyKcal: 18000m,
                bmi: 17.8m,
                settings: Settings());

        Assert.Equal(
            0m,
            result.EffectiveDeficitPct);

        Assert.Equal(
            0m,
            result.TargetWeeklyDeficitKcal);

        Assert.Equal(
            0m,
            result.TargetAverageDailyDeficitKcal);

        Assert.Equal(
            18000m,
            result.WeeklyPrescribedEnergyKcal);

        Assert.Contains(
            "BMI_BELOW_DEFICIT_THRESHOLD",
            result.ReasonCodes);
    }

    private static IReadOnlyDictionary<string, decimal>
        Settings()
        => new Dictionary<string, decimal>
        {
            ["ENERGY_ROUNDING_KCAL"] = 10m,

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
