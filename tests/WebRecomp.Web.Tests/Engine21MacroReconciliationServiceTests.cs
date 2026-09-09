using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine21MacroReconciliationServiceTests
{
    private static readonly IReadOnlyDictionary<string, decimal> Settings =
        new Dictionary<string, decimal>
        {
            ["CARB_ROUNDING_G"] = 5m,
            ["CARB_STANDARD_RECOMP_MAX_G_PER_KG"] = 5m
        };

    private readonly Engine21MacroReconciliationService _service =
        new();

    [Fact]
    public void Physiological_floor_wins_when_energy_target_is_too_low()
    {
        var result = _service.Calculate(
            new Engine21MacroReconciliationInput(
                WeightLb: 210m,
                ProteinG: 185m,
                FatG: 60m,
                PhysiologicalCarbFloorG: 250m,
                IntendedEnergyTargetKcal: 2100m),
            Settings);

        Assert.Equal(250m, result.RecommendedCarbsG);
        Assert.Equal("PHYSIOLOGICAL_FLOOR", result.GoverningRuleCode);

        Assert.Contains(
            "ENERGY_TARGET_BELOW_PHYSIOLOGICAL_CARB_REQUIREMENT",
            result.ReasonCodes);

        Assert.Equal(2280m, result.DerivedCaloriesKcal);
        Assert.Equal(180m, result.EnergyVarianceKcal);
    }

    [Fact]
    public void Energy_balance_wins_when_inside_useful_range()
    {
        var result = _service.Calculate(
            new Engine21MacroReconciliationInput(
                WeightLb: 210m,
                ProteinG: 185m,
                FatG: 60m,
                PhysiologicalCarbFloorG: 200m,
                IntendedEnergyTargetKcal: 2480m),
            Settings);

        Assert.Equal(300m, result.EnergyBalancedCarbsG);
        Assert.Equal(300m, result.RecommendedCarbsG);
        Assert.Equal("ENERGY_BALANCE", result.GoverningRuleCode);
        Assert.Equal(2480m, result.DerivedCaloriesKcal);
        Assert.Equal(0m, result.EnergyVarianceKcal);
    }

    [Fact]
    public void Useful_max_prevents_filler_carbohydrate()
    {
        var result = _service.Calculate(
            new Engine21MacroReconciliationInput(
                WeightLb: 210m,
                ProteinG: 185m,
                FatG: 60m,
                PhysiologicalCarbFloorG: 200m,
                IntendedEnergyTargetKcal: 3500m),
            Settings);

        Assert.Equal(475m, result.StandardCarbUpperBoundaryG);
        Assert.Equal(475m, result.RecommendedCarbsG);
        Assert.Equal("STANDARD_USEFUL_MAX", result.GoverningRuleCode);

        Assert.Contains(
            "NO_FILLER_CARBS_TO_REACH_ENERGY_TARGET",
            result.ReasonCodes);

        Assert.Equal(3180m, result.DerivedCaloriesKcal);
        Assert.Equal(-320m, result.EnergyVarianceKcal);
    }

    [Fact]
    public void Physiological_requirement_can_override_standard_max()
    {
        var result = _service.Calculate(
            new Engine21MacroReconciliationInput(
                WeightLb: 210m,
                ProteinG: 185m,
                FatG: 60m,
                PhysiologicalCarbFloorG: 500m,
                IntendedEnergyTargetKcal: 3000m),
            Settings);

        Assert.Equal(475m, result.StandardCarbUpperBoundaryG);
        Assert.Equal(500m, result.RecommendedCarbsG);

        Assert.Equal(
            "PHYSIOLOGICAL_FLOOR_EXCEEDS_STANDARD_MAX",
            result.GoverningRuleCode);

        Assert.Contains(
            "STANDARD_CARB_MAX_OVERRIDDEN_BY_PHYSIOLOGY",
            result.ReasonCodes);

        Assert.Equal(3280m, result.DerivedCaloriesKcal);
        Assert.Equal(280m, result.EnergyVarianceKcal);
    }
}
