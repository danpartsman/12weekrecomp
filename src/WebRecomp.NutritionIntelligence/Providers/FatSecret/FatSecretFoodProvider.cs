using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WebRecomp.NutritionIntelligence.Contracts;
using WebRecomp.NutritionIntelligence.Models;

namespace WebRecomp.NutritionIntelligence.Providers.FatSecret;

public sealed class FatSecretFoodProvider(
    HttpClient httpClient,
    FatSecretOptions options)
    : IFoodCompositionProvider, ICachedFoodCompositionParser
{
    private static readonly SemaphoreSlim TokenGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, SearchCacheItem>
        SearchFoods = new(StringComparer.Ordinal);

    private static string? cachedToken;
    private static DateTime tokenExpiresAtUtc = DateTime.MinValue;

    public string ProviderCode => NutritionProviderCodes.FatSecret;

    public async Task<FoodSearchPage> SearchAsync(
        FoodSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException(
                "Food search query is required.",
                nameof(request));

        options.Validate();

        var pageSize = Math.Min(50, Math.Max(1, request.PageSize));
        var zeroBasedPage = Math.Max(0, request.PageNumber - 1);

        var relative =
            "foods/search/v5?search_expression="
            + Uri.EscapeDataString(request.Query.Trim())
            + "&page_number="
            + zeroBasedPage.ToString(CultureInfo.InvariantCulture)
            + "&max_results="
            + pageSize.ToString(CultureInfo.InvariantCulture)
            + "&flag_default_serving=true"
            + "&region="
            + Uri.EscapeDataString(options.Region)
            + "&format=json";

        var json =
            await GetJsonAsync(
                relative,
                cancellationToken);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!TryGetProperty(root, "foods_search", out var searchNode))
            return new FoodSearchPage(
                request.PageNumber,
                pageSize,
                0,
                0,
                []);

        var totalHits =
            ReadInt(searchNode, "total_results")
            ?? 0;

        var foods =
            new List<FoodCatalogItem>();

        if (TryGetProperty(searchNode, "results", out var resultsNode)
            && TryGetProperty(resultsNode, "food", out var foodNode))
        {
            foreach (var item in EnumerateObjectOrArray(foodNode))
            {
                try
                {
                    var wrapped =
                        "{\"food\":"
                        + item.GetRawText()
                        + "}";

                    var composition =
                        ParseCachedFood(wrapped);

                    foods.Add(composition.Food);

                    SearchFoods[composition.Food.SourceFoodId] =
                        new SearchCacheItem(
                            composition,
                            DateTime.UtcNow.AddMinutes(15));
                }
                catch
                {
                    // Skip malformed provider rows; do not fail the whole search.
                }
            }
        }

        CleanupSearchCache();

        var totalPages =
            totalHits <= 0
                ? 0
                : (int)Math.Ceiling(totalHits / (double)pageSize);

        return new FoodSearchPage(
            request.PageNumber,
            pageSize,
            totalHits,
            totalPages,
            foods);
    }

    public async Task<FoodComposition> GetFoodAsync(
        string sourceFoodId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFoodId))
            throw new ArgumentException(
                "FatSecret food ID is required.",
                nameof(sourceFoodId));

        options.Validate();

        var normalizedId =
            sourceFoodId.Trim();

        if (SearchFoods.TryGetValue(
                normalizedId,
                out var searchCached)
            && searchCached.ExpiresAtUtc > DateTime.UtcNow)
        {
            return searchCached.Food;
        }

        var relative =
            "food/v5?food_id="
            + Uri.EscapeDataString(normalizedId)
            + "&flag_default_serving=true"
            + "&region="
            + Uri.EscapeDataString(options.Region)
            + "&format=json";

        var json =
            await GetJsonAsync(
                relative,
                cancellationToken);

        return ParseCachedFood(json);
    }

    public FoodComposition ParseCachedFood(
        string rawSourceJson)
    {
        if (string.IsNullOrWhiteSpace(rawSourceJson))
            throw new InvalidOperationException(
                "FatSecret cached payload is empty.");

        using var document =
            JsonDocument.Parse(rawSourceJson);

        if (!TryGetProperty(
                document.RootElement,
                "food",
                out var foodNode))
        {
            throw new InvalidOperationException(
                "FatSecret response did not contain a food object.");
        }

        return new FoodComposition(
            ParseDetailedFood(foodNode),
            rawSourceJson);
    }

    private async Task<string> GetJsonAsync(
        string relative,
        CancellationToken cancellationToken)
    {
        var token =
            await GetAccessTokenAsync(
                cancellationToken);

        var uri =
            new Uri(
                options.ApiBaseUrl.TrimEnd('/')
                + "/"
                + relative.TrimStart('/'));

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                uri);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        using var response =
            await httpClient.SendAsync(
                request,
                cancellationToken);

        var body =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                "FatSecret request failed with HTTP "
                + (int)response.StatusCode
                + ".");
        }

        return body;
    }

    private async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(cachedToken)
            && tokenExpiresAtUtc > DateTime.UtcNow.AddMinutes(5))
        {
            return cachedToken;
        }

        await TokenGate.WaitAsync(cancellationToken);

        try
        {
            if (!string.IsNullOrWhiteSpace(cachedToken)
                && tokenExpiresAtUtc > DateTime.UtcNow.AddMinutes(5))
            {
                return cachedToken;
            }

            options.Validate();

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    options.TokenUrl);

            var rawCredentials =
                Encoding.ASCII.GetBytes(
                    options.ClientId
                    + ":"
                    + options.ClientSecret);

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(rawCredentials));

            request.Content =
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["grant_type"] = "client_credentials",
                        ["scope"] = options.Scope
                    });

            using var response =
                await httpClient.SendAsync(
                    request,
                    cancellationToken);

            var json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    "FatSecret OAuth request failed with HTTP "
                    + (int)response.StatusCode
                    + ".");
            }

            using var document =
                JsonDocument.Parse(json);

            var token =
                ReadString(
                    document.RootElement,
                    "access_token");

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException(
                    "FatSecret OAuth response did not contain an access token.");
            }

            var expiresIn =
                ReadInt(
                    document.RootElement,
                    "expires_in")
                ?? 3600;

            cachedToken =
                token;

            tokenExpiresAtUtc =
                DateTime.UtcNow.AddSeconds(
                    Math.Max(300, expiresIn));

            return token;
        }
        finally
        {
            TokenGate.Release();
        }
    }

    private static FoodCatalogItem ParseDetailedFood(
        JsonElement food)
    {
        var foodId =
            ReadString(food, "food_id")
            ?? throw new InvalidOperationException(
                "FatSecret food_id is missing.");

        var name =
            ReadString(food, "food_name")
            ?? "(description unavailable)";

        var foodType =
            NormalizeFoodType(
                ReadString(food, "food_type"));

        var brand =
            ReadString(food, "brand_name");

        var servings =
            ReadServings(food);

        if (servings.Count == 0)
        {
            throw new InvalidOperationException(
                "FatSecret food has no servings.");
        }

        var anchor =
            servings.FirstOrDefault(x =>
                HasCoreNutrition(x) && IsMetric(x, "g"))
            ?? servings.FirstOrDefault(x =>
                HasCoreNutrition(x) && IsMetric(x, "oz"))
            ?? servings.FirstOrDefault(x =>
                HasCoreNutrition(x) && IsMetric(x, "ml"))
            ?? servings.FirstOrDefault(x =>
                HasCoreNutrition(x) && x.IsDefault)
            ?? servings.FirstOrDefault(HasCoreNutrition)
            ?? servings.FirstOrDefault(x => IsMetric(x, "g"))
            ?? servings.FirstOrDefault(x => IsMetric(x, "oz"))
            ?? servings.FirstOrDefault(x => IsMetric(x, "ml"))
            ?? servings.FirstOrDefault(x => x.IsDefault)
            ?? servings[0];

        string basisUnit;
        decimal divisor;

        if (IsMetric(anchor, "g"))
        {
            basisUnit = "g";
            divisor = anchor.MetricAmount!.Value;
        }
        else if (IsMetric(anchor, "oz"))
        {
            basisUnit = "g";
            divisor =
                anchor.MetricAmount!.Value
                * 28.349523125m;
        }
        else if (IsMetric(anchor, "ml"))
        {
            basisUnit = "ml";
            divisor = anchor.MetricAmount!.Value;
        }
        else
        {
            basisUnit = "serving";
            divisor = 1m;
        }

        var measures =
            BuildMeasures(
                servings,
                basisUnit);

        var nutrients =
            BuildBasisNutrients(
                anchor,
                divisor);

        var servingSize =
            basisUnit switch
            {
                "g" => divisor,
                "ml" => divisor,
                _ => 1m
            };

        return new FoodCatalogItem(
            NutritionProviderCodes.FatSecret,
            foodId,
            name,
            foodType,
            brand,
            null,
            servingSize,
            basisUnit,
            anchor.Description,
            nutrients,
            1m,
            basisUnit,
            measures);
    }

    private static IReadOnlyList<FoodMeasureOption> BuildMeasures(
        IReadOnlyList<FatSecretServing> servings,
        string basisUnit)
    {
        var result =
            new List<FoodMeasureOption>();

        foreach (
            var serving in
            servings
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.SortOrder))
        {
            decimal? basisQuantity =
                null;

            if (basisUnit == "g")
            {
                if (IsMetric(serving, "g"))
                {
                    basisQuantity =
                        serving.MetricAmount;
                }
                else if (IsMetric(serving, "oz"))
                {
                    basisQuantity =
                        serving.MetricAmount!.Value
                        * 28.349523125m;
                }
            }
            else if (basisUnit == "ml")
            {
                if (IsMetric(serving, "ml"))
                {
                    basisQuantity =
                        serving.MetricAmount;
                }
            }
            else
            {
                basisQuantity =
                    1m;
            }

            if (!basisQuantity.HasValue
                || basisQuantity.Value <= 0m)
            {
                continue;
            }

            var quantityPerSelection =
                serving.NumberOfUnits.HasValue
                && serving.NumberOfUnits.Value > 0m
                    ? serving.NumberOfUnits.Value
                    : 1m;

            result.Add(
                new FoodMeasureOption(
                    "serving:" + serving.ServingId,
                    serving.Description,
                    NormalizeUnitCode(
                        serving.MeasurementDescription,
                        serving.Description),
                    quantityPerSelection,
                    basisQuantity.Value,
                    basisUnit));
        }

        if (result.Count == 0)
        {
            var first = servings[0];

            result.Add(
                new FoodMeasureOption(
                    "serving:" + first.ServingId,
                    first.Description,
                    "serving",
                    1m,
                    1m,
                    basisUnit));
        }

        return result;
    }

    private static IReadOnlyList<NutrientAmount> BuildBasisNutrients(
        FatSecretServing serving,
        decimal divisor)
    {
        if (divisor <= 0m)
            divisor = 1m;

        var result =
            new List<NutrientAmount>();

        AddNutrient(
            result,
            "Energy",
            serving.Calories,
            "kcal",
            CanonicalNutrientCodes.EnergyKcal,
            divisor);

        AddNutrient(
            result,
            "Protein",
            serving.Protein,
            "g",
            CanonicalNutrientCodes.ProteinG,
            divisor);

        AddNutrient(
            result,
            "Carbohydrate",
            serving.Carbohydrate,
            "g",
            CanonicalNutrientCodes.CarbohydrateG,
            divisor);

        AddNutrient(
            result,
            "Fat",
            serving.Fat,
            "g",
            CanonicalNutrientCodes.FatG,
            divisor);

        AddNutrient(
            result,
            "Fiber",
            serving.Fiber,
            "g",
            CanonicalNutrientCodes.FiberG,
            divisor);

        AddNutrient(
            result,
            "Sugar",
            serving.Sugar,
            "g",
            CanonicalNutrientCodes.SugarG,
            divisor);

        AddNutrient(
            result,
            "Sodium",
            serving.Sodium,
            "mg",
            CanonicalNutrientCodes.SodiumMg,
            divisor);

        AddNutrient(
            result,
            "Saturated Fat",
            serving.SaturatedFat,
            "g",
            CanonicalNutrientCodes.SaturatedFatG,
            divisor);

        return result;
    }

    private static void AddNutrient(
        ICollection<NutrientAmount> target,
        string name,
        decimal? value,
        string unit,
        string canonicalCode,
        decimal divisor)
    {
        if (!value.HasValue)
            return;

        target.Add(
            new NutrientAmount(
                null,
                name,
                null,
                decimal.Round(
                    value.Value / divisor,
                    8,
                    MidpointRounding.AwayFromZero),
                unit,
                canonicalCode));
    }

    private static IReadOnlyList<FatSecretServing> ReadServings(
        JsonElement food)
    {
        if (!TryGetProperty(
                food,
                "servings",
                out var servingsNode)
            || !TryGetProperty(
                servingsNode,
                "serving",
                out var servingNode))
        {
            return [];
        }

        var result =
            new List<FatSecretServing>();

        var index = 0;

        foreach (var item in EnumerateObjectOrArray(servingNode))
        {
            index++;

            var id =
                ReadString(
                    item,
                    "serving_id");

            if (string.IsNullOrWhiteSpace(id))
                continue;

            result.Add(
                new FatSecretServing(
                    id,
                    ReadString(
                        item,
                        "serving_description")
                        ?? "1 serving",
                    ReadDecimal(
                        item,
                        "metric_serving_amount"),
                    NormalizeMetricUnit(
                        ReadString(
                            item,
                            "metric_serving_unit")),
                    ReadDecimal(
                        item,
                        "number_of_units"),
                    ReadString(
                        item,
                        "measurement_description"),
                    ReadDecimal(
                        item,
                        "calories"),
                    ReadDecimal(
                        item,
                        "protein"),
                    ReadDecimal(
                        item,
                        "carbohydrate"),
                    ReadDecimal(
                        item,
                        "fat"),
                    ReadDecimal(
                        item,
                        "fiber"),
                    ReadDecimal(
                        item,
                        "sugar"),
                    ReadDecimal(
                        item,
                        "sodium"),
                    ReadDecimal(
                        item,
                        "saturated_fat"),
                    ReadBool(
                        item,
                        "is_default"),
                    index));
        }

        return result;
    }

    private static IEnumerable<JsonElement> EnumerateObjectOrArray(
        JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
                yield return item;

            yield break;
        }

        if (node.ValueKind == JsonValueKind.Object)
            yield return node;
    }

    private static bool HasCoreNutrition(
        FatSecretServing serving)
        => serving.Calories.HasValue
           && serving.Protein.HasValue
           && serving.Carbohydrate.HasValue
           && serving.Fat.HasValue;

    private static bool IsMetric(
        FatSecretServing serving,
        string unit)
        => serving.MetricAmount.HasValue
           && serving.MetricAmount.Value > 0m
           && string.Equals(
               serving.MetricUnit,
               unit,
               StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFoodType(
        string? value)
        => string.Equals(
            value,
            "Brand",
            StringComparison.OrdinalIgnoreCase)
            ? "Branded"
            : "Generic";

    private static string? NormalizeMetricUnit(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var unit =
            value.Trim().ToLowerInvariant();

        return unit switch
        {
            "gram" or "grams" => "g",
            "milliliter" or "milliliters" => "ml",
            "ounce" or "ounces" => "oz",
            _ => unit
        };
    }

    private static string NormalizeUnitCode(
        string? measurementDescription,
        string servingDescription)
    {
        var value =
            string.IsNullOrWhiteSpace(measurementDescription)
                ? servingDescription
                : measurementDescription;

        var lower =
            value.Trim().ToLowerInvariant();

        if (lower.Contains("tablespoon", StringComparison.Ordinal))
            return "tbsp";

        if (lower.Contains("teaspoon", StringComparison.Ordinal))
            return "tsp";

        if (lower.Contains("cup", StringComparison.Ordinal))
            return "cup";

        if (lower.Contains("fluid ounce", StringComparison.Ordinal)
            || lower.Contains("fl oz", StringComparison.Ordinal))
        {
            return "fl_oz";
        }

        if (lower.Contains("ounce", StringComparison.Ordinal)
            || lower == "oz")
        {
            return "oz";
        }

        if (lower == "g"
            || lower.Contains("gram", StringComparison.Ordinal))
        {
            return "g";
        }

        if (lower == "ml"
            || lower.Contains("milliliter", StringComparison.Ordinal))
        {
            return "ml";
        }

        return "serving";
    }

    private static bool TryGetProperty(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(
                        name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(
        JsonElement element,
        string name)
    {
        if (!TryGetProperty(
                element,
                name,
                out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int? ReadInt(
        JsonElement element,
        string name)
    {
        var raw =
            ReadString(
                element,
                name);

        return int.TryParse(
            raw,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static decimal? ReadDecimal(
        JsonElement element,
        string name)
    {
        var raw =
            ReadString(
                element,
                name);

        return decimal.TryParse(
            raw,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static bool ReadBool(
        JsonElement element,
        string name)
    {
        var raw =
            ReadString(
                element,
                name);

        return string.Equals(
                   raw,
                   "1",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   raw,
                   "true",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void CleanupSearchCache()
    {
        var now =
            DateTime.UtcNow;

        foreach (var item in SearchFoods)
        {
            if (item.Value.ExpiresAtUtc <= now)
                SearchFoods.TryRemove(item.Key, out _);
        }
    }

    private sealed record SearchCacheItem(
        FoodComposition Food,
        DateTime ExpiresAtUtc);

    private sealed record FatSecretServing(
        string ServingId,
        string Description,
        decimal? MetricAmount,
        string? MetricUnit,
        decimal? NumberOfUnits,
        string? MeasurementDescription,
        decimal? Calories,
        decimal? Protein,
        decimal? Carbohydrate,
        decimal? Fat,
        decimal? Fiber,
        decimal? Sugar,
        decimal? Sodium,
        decimal? SaturatedFat,
        bool IsDefault,
        int SortOrder);
}
