using System.Net;
using WebRecomp.NutritionIntelligence.Models;
using WebRecomp.NutritionIntelligence.Providers.FatSecret;

namespace WebRecomp.NutritionIntelligence.Tests;

public sealed class FatSecretFoodProviderTests
{
    [Fact]
    public void CachedFoodParser_MapsServingsAndCanonicalMacros()
    {
        using var client =
            new HttpClient(
                new NoopHandler());

        var provider =
            new FatSecretFoodProvider(
                client,
                new FatSecretOptions
                {
                    ClientId = "test",
                    ClientSecret = "test"
                });

        const string json = """
            {
              "food": {
                "food_id": "50953",
                "food_name": "Whole Grain Cheerios",
                "brand_name": "General Mills",
                "food_type": "Brand",
                "servings": {
                  "serving": [
                    {
                      "serving_id": "100675",
                      "serving_description": "1 cup",
                      "metric_serving_amount": "28",
                      "metric_serving_unit": "g",
                      "number_of_units": "1",
                      "measurement_description": "cup",
                      "is_default": "1",
                      "calories": "93.24",
                      "carbohydrate": "18.6676",
                      "protein": "2.8",
                      "fat": "1.8676",
                      "fiber": "2.8",
                      "sugar": "0.9324",
                      "sodium": "149.24"
                    },
                    {
                      "serving_id": "0",
                      "serving_description": "100 g",
                      "metric_serving_amount": "100",
                      "metric_serving_unit": "g",
                      "number_of_units": "100",
                      "measurement_description": "g",
                      "calories": "333",
                      "carbohydrate": "66.67",
                      "protein": "10",
                      "fat": "6.67",
                      "fiber": "10",
                      "sugar": "3.33",
                      "sodium": "533"
                    }
                  ]
                }
              }
            }
            """;

        var parsed =
            provider.ParseCachedFood(json);

        Assert.Equal(
            NutritionProviderCodes.FatSecret,
            parsed.Food.ProviderCode);

        Assert.Equal(
            "50953",
            parsed.Food.SourceFoodId);

        Assert.Equal(
            "General Mills",
            parsed.Food.BrandOwner);

        var cup =
            Assert.Single(
                parsed.Food.Measures!,
                x => x.Code == "serving:100675");

        Assert.Equal("cup", cup.UnitCode);

        var facts =
            NutritionFactsCalculator.ForMeasure(
                parsed.Food,
                cup,
                1m);

        Assert.InRange(
            facts.EnergyKcal ?? 0m,
            93.1m,
            93.4m);

        Assert.InRange(
            facts.ProteinG ?? 0m,
            2.7m,
            2.9m);
    }

    private sealed class NoopHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(
                new HttpResponseMessage(
                    HttpStatusCode.OK));
    }
}
