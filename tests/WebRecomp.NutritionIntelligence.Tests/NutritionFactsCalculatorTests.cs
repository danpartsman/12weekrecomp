using WebRecomp.NutritionIntelligence.Models;

namespace WebRecomp.NutritionIntelligence.Tests;

public sealed class NutritionFactsCalculatorTests
{
    [Fact]
    public void ForQuantity_ScalesCanonicalFactsFromHundredGramBasis()
    {
        var food =
            new FoodCatalogItem(
                NutritionProviderCodes.UsdaFoodDataCentral,
                "123",
                "Test food",
                "Foundation",
                null,
                null,
                null,
                null,
                null,
                [
                    new NutrientAmount(
                        1008,
                        "Energy",
                        "208",
                        200m,
                        "kcal",
                        CanonicalNutrientCodes.EnergyKcal),
                    new NutrientAmount(
                        1003,
                        "Protein",
                        "203",
                        20m,
                        "g",
                        CanonicalNutrientCodes.ProteinG),
                    new NutrientAmount(
                        1005,
                        "Carbohydrate",
                        "205",
                        30m,
                        "g",
                        CanonicalNutrientCodes.CarbohydrateG),
                    new NutrientAmount(
                        1004,
                        "Fat",
                        "204",
                        10m,
                        "g",
                        CanonicalNutrientCodes.FatG)
                ],
                NutrientBasisAmount: 100m,
                NutrientBasisUnit: "g");

        var facts =
            NutritionFactsCalculator.ForQuantity(
                food,
                150m,
                "g");

        Assert.Equal(300m, facts.EnergyKcal);
        Assert.Equal(30m, facts.ProteinG);
        Assert.Equal(45m, facts.CarbohydrateG);
        Assert.Equal(15m, facts.FatG);
    }

    [Fact]
    public void ForQuantity_RejectsMismatchedBasisUnit()
    {
        var food =
            new FoodCatalogItem(
                NutritionProviderCodes.UsdaFoodDataCentral,
                "123",
                "Test food",
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                NutrientBasisAmount: 100m,
                NutrientBasisUnit: "g");

        Assert.Throws<NotSupportedException>(
            () => NutritionFactsCalculator.ForQuantity(
                food,
                1m,
                "serving"));
    }
}
