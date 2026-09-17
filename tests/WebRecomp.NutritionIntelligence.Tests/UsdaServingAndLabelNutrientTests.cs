using WebRecomp.NutritionIntelligence.Models;
using WebRecomp.NutritionIntelligence.Providers.Usda;

namespace WebRecomp.NutritionIntelligence.Tests;

public sealed class UsdaServingAndLabelNutrientTests
{
    [Fact]
    public void BrandedDetail_UsesLabelNutrients_WhenDetailRowsLackNutrientMetadata()
    {
        const string json = """
        {
          "fdcId": 463067,
          "description": "YOGURT",
          "dataType": "Branded",
          "servingSize": 170,
          "servingSizeUnit": "g",
          "householdServingFullText": "1 container",
          "foodNutrients": [
            { "id": 2998122, "amount": 0 },
            { "id": 2998121, "amount": 118 }
          ],
          "labelNutrients": {
            "calories": { "value": 100 },
            "protein": { "value": 10 },
            "carbohydrates": { "value": 12 },
            "fat": { "value": 2 },
            "fiber": { "value": 1 },
            "sugars": { "value": 8 },
            "sodium": { "value": 60 },
            "saturatedFat": { "value": 1 }
          }
        }
        """;

        var food =
            UsdaFoodDataCentralParser.ParseFoodDetails(json);

        Assert.Equal(170m, food.NutrientBasisAmount);
        Assert.Equal("g", food.NutrientBasisUnit);

        Assert.Equal(
            100m,
            Nutrient(food, CanonicalNutrientCodes.EnergyKcal));

        Assert.Equal(
            10m,
            Nutrient(food, CanonicalNutrientCodes.ProteinG));

        Assert.Equal(
            12m,
            Nutrient(food, CanonicalNutrientCodes.CarbohydrateG));

        Assert.Equal(
            2m,
            Nutrient(food, CanonicalNutrientCodes.FatG));

        var serving =
            Assert.Single(
                food.Measures!,
                x => x.Code == "serving");

        var servingFacts =
            NutritionFactsCalculator.ForMeasure(
                food,
                serving,
                1m);

        Assert.Equal(100m, servingFacts.EnergyKcal);
        Assert.Equal(10m, servingFacts.ProteinG);
        Assert.Equal(12m, servingFacts.CarbohydrateG);
        Assert.Equal(2m, servingFacts.FatG);

        Assert.Contains(
            food.Measures!,
            x => x.Code == "g");

        Assert.Contains(
            food.Measures!,
            x => x.Code == "oz");
    }

    [Fact]
    public void FullDetailWithoutCanonicalMacros_UsesAbridgedFallback()
    {
        const string fullJson = """
        {
          "fdcId": 2038064,
          "description": "CHICKEN",
          "dataType": "Branded",
          "servingSize": 56,
          "servingSizeUnit": "g",
          "householdServingFullText": "2 ONZ",
          "foodNutrients": [
            { "id": 500001, "amount": 0 },
            { "id": 500002, "amount": 120 }
          ]
        }
        """;

        const string abridgedJson = """
        {
          "fdcId": 2038064,
          "description": "CHICKEN",
          "dataType": "Branded",
          "servingSize": 56,
          "servingSizeUnit": "g",
          "householdServingFullText": "2 ONZ",
          "foodNutrients": [
            {
              "nutrientId": 1008,
              "nutrientName": "Energy",
              "nutrientNumber": "208",
              "unitName": "KCAL",
              "value": 180
            },
            {
              "nutrientId": 1003,
              "nutrientName": "Protein",
              "nutrientNumber": "203",
              "unitName": "G",
              "value": 30
            },
            {
              "nutrientId": 1005,
              "nutrientName": "Carbohydrate, by difference",
              "nutrientNumber": "205",
              "unitName": "G",
              "value": 0
            },
            {
              "nutrientId": 1004,
              "nutrientName": "Total lipid (fat)",
              "nutrientNumber": "204",
              "unitName": "G",
              "value": 6
            }
          ]
        }
        """;

        var fullFood =
            UsdaFoodDataCentralParser.ParseFoodDetails(fullJson);

        Assert.False(
            UsdaFoodDataCentralParser.HasCoreMacros(fullFood));

        var abridgedFood =
            UsdaFoodDataCentralParser.ParseAbridgedFoodDetails(
                abridgedJson);

        var merged =
            UsdaFoodDataCentralParser.MergeAbridgedFallback(
                fullFood,
                abridgedFood);

        Assert.True(
            UsdaFoodDataCentralParser.HasCoreMacros(merged));

        var serving =
            Assert.Single(
                merged.Measures!,
                x => x.Code == "serving");

        var facts =
            NutritionFactsCalculator.ForMeasure(
                merged,
                serving,
                1m);

        Assert.Equal(100.8m, facts.EnergyKcal);
        Assert.Equal(16.8m, facts.ProteinG);
        Assert.Equal(0m, facts.CarbohydrateG);
        Assert.Equal(3.36m, facts.FatG);
    }

    [Fact]
    public void GenericDetail_UsesUsdaFoodPortionGramWeight()
    {
        const string json = """
        {
          "fdcId": 123,
          "description": "Test generic food",
          "dataType": "Foundation",
          "foodNutrients": [
            {
              "amount": 100,
              "nutrient": {
                "id": 1008,
                "name": "Energy",
                "number": "208",
                "unitName": "kcal"
              }
            },
            {
              "amount": 10,
              "nutrient": {
                "id": 1003,
                "name": "Protein",
                "number": "203",
                "unitName": "g"
              }
            },
            {
              "amount": 20,
              "nutrient": {
                "id": 1005,
                "name": "Carbohydrate, by difference",
                "number": "205",
                "unitName": "g"
              }
            },
            {
              "amount": 5,
              "nutrient": {
                "id": 1004,
                "name": "Total lipid (fat)",
                "number": "204",
                "unitName": "g"
              }
            }
          ],
          "foodPortions": [
            {
              "id": 10,
              "amount": 1,
              "gramWeight": 240,
              "portionDescription": "1 cup",
              "measureUnit": {
                "name": "cup",
                "abbreviation": "cup"
              }
            }
          ]
        }
        """;

        var food =
            UsdaFoodDataCentralParser.ParseFoodDetails(json);

        Assert.Equal(100m, food.NutrientBasisAmount);
        Assert.Equal("g", food.NutrientBasisUnit);

        var cup =
            Assert.Single(
                food.Measures!,
                x => x.Code == "portion:10");

        Assert.Equal("cup", cup.UnitCode);
        Assert.Equal(240m, cup.BasisQuantityPerSelection);

        var facts =
            NutritionFactsCalculator.ForMeasure(
                food,
                cup,
                1m);

        Assert.Equal(240m, facts.EnergyKcal);
        Assert.Equal(24m, facts.ProteinG);
        Assert.Equal(48m, facts.CarbohydrateG);
        Assert.Equal(12m, facts.FatG);
    }

    [Fact]
    public void MilliliterBasis_AddsCupAndFluidOunceMeasures()
    {
        const string json = """
        {
          "fdcId": 456,
          "description": "Test liquid",
          "dataType": "Branded",
          "servingSize": 100,
          "servingSizeUnit": "ml",
          "householdServingFullText": "100 ml",
          "foodNutrients": [
            { "id": 900001, "amount": 50 }
          ],
          "labelNutrients": {
            "calories": { "value": 50 },
            "protein": { "value": 2 },
            "carbohydrates": { "value": 8 },
            "fat": { "value": 1 }
          }
        }
        """;

        var food =
            UsdaFoodDataCentralParser.ParseFoodDetails(json);

        Assert.Equal(100m, food.NutrientBasisAmount);
        Assert.Equal("ml", food.NutrientBasisUnit);

        var cup =
            Assert.Single(
                food.Measures!,
                x => x.Code == "cup");

        Assert.Equal("cup", cup.UnitCode);
        Assert.Equal(236.5882365m, cup.BasisQuantityPerSelection);

        var cupFacts =
            NutritionFactsCalculator.ForMeasure(
                food,
                cup,
                1m);

        Assert.Equal(118.2941m, cupFacts.EnergyKcal);
        Assert.Equal(4.7318m, cupFacts.ProteinG);
        Assert.Equal(18.9271m, cupFacts.CarbohydrateG);
        Assert.Equal(2.3659m, cupFacts.FatG);

        var teaspoon =
            Assert.Single(
                food.Measures!,
                x => x.Code == "tsp");

        Assert.Equal("tsp", teaspoon.UnitCode);
        Assert.Equal(
            4.92892159375m,
            teaspoon.BasisQuantityPerSelection);

        var tablespoon =
            Assert.Single(
                food.Measures!,
                x => x.Code == "tbsp");

        Assert.Equal("tbsp", tablespoon.UnitCode);
        Assert.Equal(
            14.78676478125m,
            tablespoon.BasisQuantityPerSelection);

        var fluidOunce =
            Assert.Single(
                food.Measures!,
                x => x.Code == "fl_oz");

        Assert.Equal("fl oz", fluidOunce.UnitCode);
    }

    private static decimal? Nutrient(
        FoodCatalogItem food,
        string canonicalCode)
        => food.Nutrients
            .Where(x => x.CanonicalCode == canonicalCode)
            .Select(x => x.Amount)
            .FirstOrDefault();
}
