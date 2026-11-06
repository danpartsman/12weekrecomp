using System.Net;
using System.Text;
using System.Text.Json;
using WebRecomp.NutritionIntelligence.Models;
using WebRecomp.NutritionIntelligence.Providers.FatSecret;
using WebRecomp.Web.Controllers;

namespace WebRecomp.Web.Tests;

public sealed class RestaurantFoodLookupTests
{
    private sealed class Handler(Func<HttpRequestMessage, object> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = request.Method == HttpMethod.Post ? new { access_token = "restaurant-test-token", expires_in = 3600 } : reply(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") });
        }
    }
    private static FatSecretFoodProvider Provider(HttpClient client) => new(client, new FatSecretOptions { ClientId = "restaurant-tests", ClientSecret = "test-only" });
    private static object Food(string id, string brand) => new {
        food_id = id, food_name = "Chicken sandwich", brand_name = brand, food_type = "Brand",
        servings = new { serving = new { serving_id = "10", serving_description = "1 sandwich", metric_serving_amount = "200", metric_serving_unit = "g",
            number_of_units = "1", measurement_description = "serving", is_default = "1", calories = "400", protein = "30", carbohydrate = "45", fat = "12" } }
    };

    [Fact]
    public async Task DirectoryUsesPremierRestaurantEndpointAndHandlesPunctuationAndSingletons()
    {
        using var client = new HttpClient(new Handler(request => {
            Assert.Contains("/brands/v2?", request.RequestUri!.AbsoluteUri);
            Assert.Contains("brand_type=restaurant", request.RequestUri.Query);
            Assert.Contains("starts_with=C", request.RequestUri.Query);
            Assert.NotNull(request.Headers.Authorization);
            return new { food_brands = new { food_brand = "Chick-fil-A" } };
        }));
        var names = await Provider(client).SearchRestaurantsAsync("Chick fila");
        Assert.Equal(new[] { "Chick-fil-A" }, names);
    }

    [Fact]
    public async Task MenuKeepsOnlyTheSelectedRestaurantAndLoadsBeyondFirstProviderPage()
    {
        var pages = new List<int>();
        using var client = new HttpClient(new Handler(request => {
            var uri = request.RequestUri!;
            Assert.Contains("Bob & Grill chicken", Uri.UnescapeDataString(uri.Query));
            var page = uri.Query.Contains("page_number=0") ? 0 : uri.Query.Contains("page_number=1") ? 1 : 2;
            pages.Add(page);
            return new { foods_search = new { total_results = "150", page_number = page.ToString(), max_results = "50",
                results = new { food = new[] { Food("restaurant-" + page, "Bob & Grill"), Food("wrong-" + page, "Bob & Grill Express") } } } };
        }));
        var menu = await Provider(client).RestaurantMenuAsync("Bob & Grill", "chicken");
        Assert.Equal(new[] { 0, 1, 2 }, pages);
        Assert.Equal(3, menu.Foods.Count); Assert.All(menu.Foods, food => Assert.Equal("Bob & Grill", food.BrandOwner));
        Assert.Null(menu.NextPage);
        var mapped = JsonSerializer.SerializeToElement(NutritionRestaurantController.MenuItem(menu.Foods[0]));
        Assert.Equal("1 sandwich", mapped.GetProperty("serving").GetString());
        Assert.Equal(400m, mapped.GetProperty("energyKcal").GetDecimal());
        Assert.Equal(30m, mapped.GetProperty("proteinG").GetDecimal());
        Assert.Equal(45m, mapped.GetProperty("carbohydrateG").GetDecimal());
        Assert.Equal(12m, mapped.GetProperty("fatG").GetDecimal());
    }

    [Fact]
    public async Task LargeMenusExposeNextPageInsteadOfSilentlyTruncatingToOnePage()
    {
        using var client = new HttpClient(new Handler(_ => new { foods_search = new { total_results = "200", page_number = "0", max_results = "50",
            results = new { food = Enumerable.Range(0, 12).Select(i => Food("large-menu-" + i, "Chipotle")).ToArray() } } }));
        var menu = await Provider(client).RestaurantMenuAsync("Chipotle", null);
        Assert.Equal(12, menu.Foods.Count); Assert.Equal(2, menu.NextPage);
    }

    [Fact]
    public async Task MissingPremierAccessIsAnErrorRatherThanAnEmptyDirectory()
    {
        using var client = new HttpClient(new Handler(_ => new { error = new { code = 14, message = "Missing scope" } }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Provider(client).SearchRestaurantsAsync("Chipotle"));
    }
}
