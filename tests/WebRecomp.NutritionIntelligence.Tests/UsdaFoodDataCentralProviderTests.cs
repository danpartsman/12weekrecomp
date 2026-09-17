using WebRecomp.NutritionIntelligence.Models;
using WebRecomp.NutritionIntelligence.Providers.Usda;

namespace WebRecomp.NutritionIntelligence.Tests;

public sealed class UsdaFoodDataCentralProviderTests
{
    [Fact]
    public void ParseSearchResponse_MapsIdentityBarcodeServingAndCanonicalMacros()
    {
        const string json = """
        {
          "totalHits": 1,
          "currentPage": 1,
          "totalPages": 1,
          "foods": [
            {
              "fdcId": 123456,
              "description": "Greek yogurt, plain",
              "dataType": "Branded",
              "brandOwner": "Example Foods",
              "gtinUpc": "012345678905",
              "servingSize": 170,
              "servingSizeUnit": "g",
              "householdServingFullText": "1 container",
              "foodNutrients": [
                {
                  "nutrientId": 1008,
                  "nutrientName": "Energy",
                  "nutrientNumber": "208",
                  "unitName": "kcal",
                  "value": 100
                },
                {
                  "nutrientId": 1003,
                  "nutrientName": "Protein",
                  "nutrientNumber": "203",
                  "unitName": "g",
                  "value": 17
                }
              ]
            }
          ]
        }
        """;

        var result =
            UsdaFoodDataCentralParser.ParseSearchResponse(
                json,
                requestedPageNumber: 1,
                requestedPageSize: 25);

        var food = Assert.Single(result.Foods);

        Assert.Equal("USDA_FDC", food.ProviderCode);
        Assert.Equal("123456", food.SourceFoodId);
        Assert.Equal("012345678905", food.GtinUpc);
        Assert.Equal(170m, food.ServingSize);
        Assert.Equal("g", food.ServingSizeUnit);

        Assert.Contains(
            food.Nutrients,
            x => x.CanonicalCode == CanonicalNutrientCodes.EnergyKcal
                 && x.Amount == 100m);

        Assert.Contains(
            food.Nutrients,
            x => x.CanonicalCode == CanonicalNutrientCodes.ProteinG
                 && x.Amount == 17m);
    }

    [Fact]
    public void ParseFoodDetails_HandlesNestedNutrientShape()
    {
        const string json = """
        {
          "fdcId": 654321,
          "description": "Chicken breast, roasted",
          "dataType": "Foundation",
          "foodNutrients": [
            {
              "nutrient": {
                "id": 1003,
                "number": "203",
                "name": "Protein",
                "unitName": "g"
              },
              "amount": 31.02
            }
          ]
        }
        """;

        var food =
            UsdaFoodDataCentralParser.ParseFoodDetails(json);

        var protein =
            Assert.Single(
                food.Nutrients.Where(
                    x => x.CanonicalCode
                         == CanonicalNutrientCodes.ProteinG));

        Assert.Equal(31.02m, protein.Amount);
    }

    [Theory]
    [InlineData(1003, "Protein", "g", CanonicalNutrientCodes.ProteinG)]
    [InlineData(1004, "Total lipid (fat)", "g", CanonicalNutrientCodes.FatG)]
    [InlineData(1005, "Carbohydrate, by difference", "g", CanonicalNutrientCodes.CarbohydrateG)]
    [InlineData(1008, "Energy", "kcal", CanonicalNutrientCodes.EnergyKcal)]
    [InlineData(1079, "Fiber, total dietary", "g", CanonicalNutrientCodes.FiberG)]
    [InlineData(1093, "Sodium, Na", "mg", CanonicalNutrientCodes.SodiumMg)]
    [InlineData(1258, "Fatty acids, total saturated", "g", CanonicalNutrientCodes.SaturatedFatG)]
    [InlineData(2000, "Sugars, total including NLEA", "g", CanonicalNutrientCodes.SugarG)]
    public void CanonicalMap_MapsCoreNutritionFields(
        int nutrientId,
        string nutrientName,
        string unitName,
        string expected)
    {
        Assert.Equal(
            expected,
            CanonicalNutrientCodes.MapUsdaNutrient(
                nutrientId,
                nutrientName,
                unitName));
    }
}
