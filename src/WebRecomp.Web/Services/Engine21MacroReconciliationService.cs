namespace WebRecomp.Web.Services;

public sealed record Engine21MacroReconciliationInput(
    decimal WeightLb,
    decimal ProteinG,
    decimal FatG,
    decimal PhysiologicalCarbFloorG,
    decimal IntendedEnergyTargetKcal);

public sealed record Engine21MacroReconciliationResult(
    decimal ProteinG,
    decimal FatG,
    decimal PhysiologicalCarbFloorG,
    decimal EnergyBalancedCarbsG,
    decimal StandardCarbUpperBoundaryG,
    decimal RecommendedCarbsG,
    decimal DerivedCaloriesKcal,
    decimal EnergyVarianceKcal,
    string GoverningRuleCode,
    IReadOnlyList<string> ReasonCodes);

public sealed class Engine21MacroReconciliationService
{
    private const decimal PoundsPerKg = 2.2046226218m;

    public Engine21MacroReconciliationResult Calculate(
        Engine21MacroReconciliationInput input,
        IReadOnlyDictionary<string, decimal> settings)
    {
        Validate(input);

        var carbRoundingG =
            ConfigurationService.Setting(
                settings,
                "CARB_ROUNDING_G",
                5m);

        var standardCarbMaxGPerKg =
            ConfigurationService.Setting(
                settings,
                "CARB_STANDARD_RECOMP_MAX_G_PER_KG",
                5m);

        if (carbRoundingG <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "CARB_ROUNDING_G must be greater than zero.");

        if (standardCarbMaxGPerKg <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "CARB_STANDARD_RECOMP_MAX_G_PER_KG must be greater than zero.");

        var weightKg =
            input.WeightLb / PoundsPerKg;

        var physiologicalFloor =
            RoundToIncrement(
                Math.Max(0m, input.PhysiologicalCarbFloorG),
                carbRoundingG);

        var standardUpperBoundary =
            RoundToIncrement(
                weightKg * standardCarbMaxGPerKg,
                carbRoundingG);

        var fixedMacroCalories =
            input.ProteinG * 4m +
            input.FatG * 9m;

        var rawEnergyBalancedCarbs =
            (input.IntendedEnergyTargetKcal - fixedMacroCalories) / 4m;

        var energyBalancedCarbs =
            RoundToIncrement(
                Math.Max(0m, rawEnergyBalancedCarbs),
                carbRoundingG);

        decimal recommendedCarbs;
        string governingRule;
        var reasons = new List<string>();

        if (physiologicalFloor > standardUpperBoundary)
        {
            recommendedCarbs = physiologicalFloor;

            governingRule =
                "PHYSIOLOGICAL_FLOOR_EXCEEDS_STANDARD_MAX";

            reasons.Add(
                "PHYSIOLOGICAL_CARB_REQUIREMENT_PROTECTED");

            reasons.Add(
                "STANDARD_CARB_MAX_OVERRIDDEN_BY_PHYSIOLOGY");
        }
        else if (energyBalancedCarbs < physiologicalFloor)
        {
            recommendedCarbs = physiologicalFloor;

            governingRule =
                "PHYSIOLOGICAL_FLOOR";

            reasons.Add(
                "ENERGY_TARGET_BELOW_PHYSIOLOGICAL_CARB_REQUIREMENT");
        }
        else if (energyBalancedCarbs > standardUpperBoundary)
        {
            recommendedCarbs = standardUpperBoundary;

            governingRule =
                "STANDARD_USEFUL_MAX";

            reasons.Add(
                "NO_FILLER_CARBS_TO_REACH_ENERGY_TARGET");
        }
        else
        {
            recommendedCarbs = energyBalancedCarbs;

            governingRule =
                "ENERGY_BALANCE";

            reasons.Add(
                "ENERGY_SUPPORTED_CARBOHYDRATE_WITHIN_PHYSIOLOGICAL_RANGE");
        }

        var derivedCalories =
            Math.Round(
                input.ProteinG * 4m +
                recommendedCarbs * 4m +
                input.FatG * 9m,
                0,
                MidpointRounding.AwayFromZero);

        var energyVariance =
            Math.Round(
                derivedCalories -
                input.IntendedEnergyTargetKcal,
                2,
                MidpointRounding.AwayFromZero);

        return new Engine21MacroReconciliationResult(
            ProteinG: input.ProteinG,
            FatG: input.FatG,
            PhysiologicalCarbFloorG: physiologicalFloor,
            EnergyBalancedCarbsG: energyBalancedCarbs,
            StandardCarbUpperBoundaryG: standardUpperBoundary,
            RecommendedCarbsG: recommendedCarbs,
            DerivedCaloriesKcal: derivedCalories,
            EnergyVarianceKcal: energyVariance,
            GoverningRuleCode: governingRule,
            ReasonCodes: reasons);
    }

    private static void Validate(
        Engine21MacroReconciliationInput input)
    {
        if (input.WeightLb <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.WeightLb));

        if (input.ProteinG < 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.ProteinG));

        if (input.FatG < 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.FatG));

        if (input.PhysiologicalCarbFloorG < 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.PhysiologicalCarbFloorG));

        if (input.IntendedEnergyTargetKcal <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.IntendedEnergyTargetKcal));
    }

    private static decimal RoundToIncrement(
        decimal value,
        decimal increment)
    {
        if (increment <= 0m)
            return value;

        return Math.Round(
                   value / increment,
                   0,
                   MidpointRounding.AwayFromZero)
               * increment;
    }
}
