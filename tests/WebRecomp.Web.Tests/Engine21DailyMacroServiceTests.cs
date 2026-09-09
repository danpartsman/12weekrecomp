using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine21DailyMacroServiceTests
{
    private readonly Engine21DailyMacroService _service =
        new(
            new Engine21CarbRequirementService(
                new WorkoutMathService(),
                new RecompEngineService()),
            new Engine21MacroReconciliationService());

    private static readonly IReadOnlyDictionary<string, decimal> Settings =
        new Dictionary<string, decimal>
        {
            ["CARB_ROUNDING_G"] = 5m,
            ["REFERENCE_WEIGHT_LB"] = 210m,
            ["BODY_WEIGHT_GLYCOGEN_EXPONENT"] = 0.5m,
            ["GLYCOGEN_ESTIMATE_MULTIPLIER"] = 0.65m,
            ["CARB_STANDARD_RECOMP_MAX_G_PER_KG"] = 5m
        };

    [Fact]
    public void Physiological_floor_wins_when_energy_allocation_is_too_low()
    {
        var result =
            _service.Calculate(
                Input(
                    intendedEnergyKcal: 1900m),
                Settings);

        Assert.Equal(
            190m,
            result.CarbRequirement.PhysiologicalCarbFloorG);

        Assert.Equal(
            155m,
            result.Reconciliation.EnergyBalancedCarbsG);

        Assert.Equal(
            190m,
            result.Reconciliation.RecommendedCarbsG);

        Assert.Equal(
            "PHYSIOLOGICAL_FLOOR",
            result.Reconciliation.GoverningRuleCode);

        Assert.Equal(
            2040m,
            result.Reconciliation.DerivedCaloriesKcal);

        Assert.Equal(
            140m,
            result.Reconciliation.EnergyVarianceKcal);
    }

    [Fact]
    public void Energy_balance_wins_when_supported_carbs_are_inside_range()
    {
        var result =
            _service.Calculate(
                Input(
                    intendedEnergyKcal: 2480m),
                Settings);

        Assert.Equal(
            190m,
            result.CarbRequirement.PhysiologicalCarbFloorG);

        Assert.Equal(
            300m,
            result.Reconciliation.EnergyBalancedCarbsG);

        Assert.Equal(
            300m,
            result.Reconciliation.RecommendedCarbsG);

        Assert.Equal(
            "ENERGY_BALANCE",
            result.Reconciliation.GoverningRuleCode);

        Assert.Equal(
            2480m,
            result.Reconciliation.DerivedCaloriesKcal);

        Assert.Equal(
            0m,
            result.Reconciliation.EnergyVarianceKcal);
    }

    [Fact]
    public void Useful_max_prevents_carbs_from_becoming_calorie_filler()
    {
        var result =
            _service.Calculate(
                Input(
                    intendedEnergyKcal: 3500m),
                Settings);

        Assert.Equal(
            475m,
            result.Reconciliation.StandardCarbUpperBoundaryG);

        Assert.Equal(
            555m,
            result.Reconciliation.EnergyBalancedCarbsG);

        Assert.Equal(
            475m,
            result.Reconciliation.RecommendedCarbsG);

        Assert.Equal(
            "STANDARD_USEFUL_MAX",
            result.Reconciliation.GoverningRuleCode);

        Assert.Contains(
            "NO_FILLER_CARBS_TO_REACH_ENERGY_TARGET",
            result.Reconciliation.ReasonCodes);

        Assert.Equal(
            3180m,
            result.Reconciliation.DerivedCaloriesKcal);

        Assert.Equal(
            -320m,
            result.Reconciliation.EnergyVarianceKcal);
    }

    [Fact]
    public void Carryover_requirement_can_raise_floor_before_energy_reconciliation()
    {
        var result =
            _service.Calculate(
                Input(
                    intendedEnergyKcal: 1900m,
                    fuelDemandG: 150m,
                    restorationCreditG: 35m,
                    absoluteRatio: 35m / 150m),
                Settings);

        Assert.Equal(
            65m,
            result.CarbRequirement.RecoveryAllocationG);

        Assert.Equal(
            195m,
            result.CarbRequirement.PhysiologicalCarbFloorG);

        Assert.Contains(
            "CARRYOVER_RAISED_PHYSIOLOGICAL_FLOOR",
            result.CarbRequirement.ReasonCodes);

        Assert.Equal(
            195m,
            result.Reconciliation.RecommendedCarbsG);

        Assert.Equal(
            "PHYSIOLOGICAL_FLOOR",
            result.Reconciliation.GoverningRuleCode);

        Assert.Equal(
            2060m,
            result.Reconciliation.DerivedCaloriesKcal);
    }

    private static Engine21DailyMacroInput Input(
        decimal intendedEnergyKcal,
        decimal fuelDemandG = 100m,
        decimal restorationCreditG = 100m,
        decimal absoluteRatio = 1m,
        short moderateHardDays = 0,
        short hardDays = 0)
    {
        return new Engine21DailyMacroInput(
            WeightLb:
                210m,

            Plan:
                new PlanDefinition
                {
                    PlanCode = "PLAN_2",
                    DisplayName = "Fat-loss Recomp",
                    PlanCategoryCode = "RECOMP",
                    BaseRestCarbsG = 130m,
                    IsActive = true
                },

            Workout:
                new WorkoutTypeDefinition
                {
                    WorkoutTypeCode = "TEST_MAJOR",
                    DisplayName = "Test Major",
                    WeeklyRoleCode = "HEAVY",
                    FuelPriorityCode = "HIGH",
                    SchedulePrincipleCode = "TEST",
                    WorkoutCategoryCode = "MAJOR_STRENGTH",

                    FuelScore = 3m,
                    DefaultMinutes = 60m,
                    RawGlycogenGMin = 2m,
                    DefaultIntensityFactor = 1m,
                    PlannedRestorationPct = 0.75m,
                    IsActive = true
                },

            FuelDemandG:
                fuelDemandG,

            FuelRestorationCreditG:
                restorationCreditG,

            FuelAbsoluteRestorationRatio:
                absoluteRatio,

            ModerateHardDays:
                moderateHardDays,

            HardDays:
                hardDays,

            ProteinG:
                185m,

            FatG:
                60m,

            IntendedEnergyTargetKcal:
                intendedEnergyKcal);
    }
}
