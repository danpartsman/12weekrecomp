using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine21CarbRequirementServiceTests
{
    private readonly Engine21CarbRequirementService _service =
        new(
            new WorkoutMathService(),
            new RecompEngineService());

    private static readonly IReadOnlyDictionary<string, decimal> Settings =
        new Dictionary<string, decimal>
        {
            ["CARB_ROUNDING_G"] = 5m,
            ["REFERENCE_WEIGHT_LB"] = 210m,
            ["BODY_WEIGHT_GLYCOGEN_EXPONENT"] = 0.5m,
            ["GLYCOGEN_ESTIMATE_MULTIPLIER"] = 0.65m
        };

    [Fact]
    public void Normal_workout_restoration_establishes_floor()
    {
        var result = _service.Calculate(
            Input(
                demandG: 100m,
                restorationCreditG: 100m,
                absoluteRatio: 1m),
            Settings);

        Assert.Equal(80m, result.PlannedGlycogenBurnG);
        Assert.Equal(60m, result.PlannedWorkoutRestorationG);
        Assert.Equal(190m, result.BaseWorkoutCarbsG);

        Assert.Equal(0m, result.RecoveryAllocationG);
        Assert.Equal(190m, result.PhysiologicalCarbFloorG);
    }

    [Fact]
    public void Small_carryover_is_earmarked_inside_existing_workout_target()
    {
        var result = _service.Calculate(
            Input(
                demandG: 100m,
                restorationCreditG: 45m,
                absoluteRatio: 0.45m),
            Settings);

        Assert.Equal(30m, result.RecoveryShortfallG);
        Assert.Equal(30m, result.RecoveryAllocationG);
        Assert.Equal(30m, result.PreWorkoutG);

        // Base workout target = 190 g.
        // Base-rest 130 + allocation 30 = 160 g.
        // The carryover is therefore already covered by 190 g.
        Assert.Equal(190m, result.BaseWorkoutCarbsG);
        Assert.Equal(190m, result.PhysiologicalCarbFloorG);

        Assert.Contains(
            "CARRYOVER_ALREADY_COVERED_WITHIN_BASE_TARGET",
            result.ReasonCodes);
    }

    [Fact]
    public void Large_carryover_can_raise_physiological_floor()
    {
        var result = _service.Calculate(
            Input(
                demandG: 150m,
                restorationCreditG: 35m,
                absoluteRatio: 35m / 150m),
            Settings);

        Assert.Equal(65m, result.RecoveryAllocationG);
        Assert.Equal(35m, result.PreWorkoutG);

        // Base workout target = 190 g.
        // Base-rest 130 + capped allocation 65 = 195 g.
        Assert.Equal(190m, result.BaseWorkoutCarbsG);
        Assert.Equal(195m, result.PhysiologicalCarbFloorG);

        Assert.Contains(
            "CARRYOVER_RAISED_PHYSIOLOGICAL_FLOOR",
            result.ReasonCodes);
    }

    [Fact]
    public void Recent_training_load_raises_functional_restoration_target()
    {
        var result = _service.Calculate(
            Input(
                demandG: 100m,
                restorationCreditG: 45m,
                absoluteRatio: 0.45m,
                moderateHardDays: 3),
            Settings);

        Assert.Equal(0.80m, result.FunctionalTargetRatio);

        Assert.Contains(
            "FUNCTIONAL_TARGET_ELEVATED_FOR_RECENT_LOAD",
            result.ReasonCodes);
    }

    private static Engine21CarbRequirementInput Input(
        decimal demandG,
        decimal restorationCreditG,
        decimal absoluteRatio,
        short moderateHardDays = 0,
        short hardDays = 0)
    {
        return new Engine21CarbRequirementInput(
            WeightLb: 210m,

            Plan: new PlanDefinition
            {
                PlanCode = "PLAN_2",
                DisplayName = "Fat-loss Recomp",
                PlanCategoryCode = "RECOMP",
                BaseRestCarbsG = 130m,
                IsActive = true
            },

            Workout: new WorkoutTypeDefinition
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

            FuelDemandG: demandG,
            FuelRestorationCreditG: restorationCreditG,
            FuelAbsoluteRestorationRatio: absoluteRatio,
            ModerateHardDays: moderateHardDays,
            HardDays: hardDays);
    }
}
