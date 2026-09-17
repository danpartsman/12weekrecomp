using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using WebRecomp.NutritionIntelligence.Contracts;
using WebRecomp.NutritionIntelligence.Models;

namespace WebRecomp.NutritionIntelligence.Providers.Usda;

public sealed class UsdaFoodDataCentralProvider(
    HttpClient httpClient,
    UsdaFoodDataCentralOptions options)
    : IFoodCompositionProvider
{
    public string ProviderCode => NutritionProviderCodes.UsdaFoodDataCentral;

    public async Task<FoodSearchPage> SearchAsync(
        FoodSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException(
                "Food search query is required.",
                nameof(request));

        if (request.PageNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(request.PageNumber));

        if (request.PageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(request.PageSize));

        options.Validate();

        var payload = new Dictionary<string, object?>
        {
            ["query"] = request.Query.Trim(),
            ["pageNumber"] = request.PageNumber,
            ["pageSize"] = request.PageSize
        };

        if (request.DataTypes is { Count: > 0 })
            payload["dataType"] = request.DataTypes;

        using var response =
            await httpClient.PostAsJsonAsync(
                BuildUri("foods/search"),
                payload,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var json =
            await response.Content.ReadAsStringAsync(cancellationToken);

        return UsdaFoodDataCentralParser.ParseSearchResponse(
            json,
            request.PageNumber,
            request.PageSize);
    }

    public async Task<FoodComposition> GetFoodAsync(
        string sourceFoodId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFoodId))
            throw new ArgumentException(
                "USDA FDC food ID is required.",
                nameof(sourceFoodId));

        options.Validate();

        using var response =
            await httpClient.GetAsync(
                BuildUri(
                    $"food/{Uri.EscapeDataString(sourceFoodId.Trim())}?format=full"),
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var fullJson =
            await response.Content.ReadAsStringAsync(cancellationToken);

        var fullFood =
            UsdaFoodDataCentralParser.ParseFoodDetails(fullJson);

        if (UsdaFoodDataCentralParser.HasCoreMacros(fullFood))
        {
            return new FoodComposition(
                fullFood,
                fullJson);
        }

        using var abridgedResponse =
            await httpClient.GetAsync(
                BuildUri(
                    $"food/{Uri.EscapeDataString(sourceFoodId.Trim())}?format=abridged"),
                cancellationToken);

        abridgedResponse.EnsureSuccessStatusCode();

        var abridgedJson =
            await abridgedResponse.Content.ReadAsStringAsync(
                cancellationToken);

        var abridgedFood =
            UsdaFoodDataCentralParser.ParseAbridgedFoodDetails(
                abridgedJson);

        var mergedFood =
            UsdaFoodDataCentralParser.MergeAbridgedFallback(
                fullFood,
                abridgedFood);

        return new FoodComposition(
            mergedFood,
            CombineRawPayloads(fullJson, abridgedJson));
    }

    private static string CombineRawPayloads(
        string fullJson,
        string abridgedJson)
    {
        using var full = JsonDocument.Parse(fullJson);
        using var abridged = JsonDocument.Parse(abridgedJson);

        return JsonSerializer.Serialize(
            new Dictionary<string, JsonElement>
            {
                ["full"] = full.RootElement.Clone(),
                ["abridgedFallback"] = abridged.RootElement.Clone()
            });
    }

    private Uri BuildUri(string relative)
    {
        var baseUrl = options.BaseUrl.TrimEnd('/');
        var apiKey = Uri.EscapeDataString(options.ApiKey);

        var separator =
            relative.Contains("?", StringComparison.Ordinal)
                ? "&"
                : "?";

        return new Uri(
            $"{baseUrl}/{relative}{separator}api_key={apiKey}",
            UriKind.Absolute);
    }
}

internal static class UsdaFoodDataCentralParser
{
    public static FoodSearchPage ParseSearchResponse(
        string json,
        int requestedPageNumber,
        int requestedPageSize)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var foods = new List<FoodCatalogItem>();

        if (TryGetProperty(root, "foods", out var foodsElement)
            && foodsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var food in foodsElement.EnumerateArray())
                foods.Add(ParseSearchFood(food));
        }

        return new FoodSearchPage(
            ReadInt(root, "currentPage") ?? requestedPageNumber,
            requestedPageSize,
            ReadInt(root, "totalHits") ?? foods.Count,
            ReadInt(root, "totalPages") ?? 1,
            foods);
    }

    public static FoodCatalogItem ParseFoodDetails(string json)
    {
        using var document = JsonDocument.Parse(json);
        var food = document.RootElement;

        var fdcId =
            ReadLong(food, "fdcId")?.ToString(CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException(
                "USDA FDC response did not contain fdcId.");

        var description =
            ReadString(food, "description")
            ?? "(description unavailable)";

        var dataType = ReadString(food, "dataType");
        var servingSize = ReadDecimal(food, "servingSize");
        var servingSizeUnit =
            NormalizeUnit(
                ReadString(food, "servingSizeUnit"));
        var household =
            ReadString(food, "householdServingFullText");

        var nutrients = ReadNutrients(food);
        var basisAmount = 100m;
        var basisUnit = "g";

        if (!HasCoreMacros(nutrients))
        {
            var labelNutrients =
                ReadLabelNutrients(food);

            if (HasCoreMacros(labelNutrients))
            {
                nutrients = labelNutrients;

                basisAmount =
                    servingSize is > 0m
                        ? servingSize.Value
                        : 1m;

                basisUnit =
                    !string.IsNullOrWhiteSpace(servingSizeUnit)
                        ? servingSizeUnit!
                        : "serving";
            }
        }

        var measures =
            ReadMeasures(
                food,
                basisAmount,
                basisUnit,
                servingSize,
                servingSizeUnit,
                household);

        return new FoodCatalogItem(
            NutritionProviderCodes.UsdaFoodDataCentral,
            fdcId,
            description,
            dataType,
            ReadString(food, "brandOwner"),
            ReadString(food, "gtinUpc"),
            servingSize,
            servingSizeUnit,
            household,
            nutrients,
            basisAmount,
            basisUnit,
            measures);
    }

    internal static FoodCatalogItem ParseAbridgedFoodDetails(
        string json)
    {
        using var document = JsonDocument.Parse(json);
        return ParseSearchFood(document.RootElement);
    }

    internal static bool HasCoreMacros(FoodCatalogItem food)
    {
        ArgumentNullException.ThrowIfNull(food);
        return HasCoreMacros(food.Nutrients);
    }

    internal static FoodCatalogItem MergeAbridgedFallback(
        FoodCatalogItem fullFood,
        FoodCatalogItem abridgedFood)
    {
        ArgumentNullException.ThrowIfNull(fullFood);
        ArgumentNullException.ThrowIfNull(abridgedFood);

        if (HasCoreMacros(fullFood))
            return fullFood;

        if (!HasCoreMacros(abridgedFood))
            return fullFood;

        var fallbackCanonicalCodes =
            abridgedFood.Nutrients
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.CanonicalCode)
                    && x.Amount.HasValue)
                .Select(x => x.CanonicalCode!)
                .ToHashSet(StringComparer.Ordinal);

        var merged =
            fullFood.Nutrients
                .Where(x =>
                    string.IsNullOrWhiteSpace(x.CanonicalCode)
                    || !fallbackCanonicalCodes.Contains(x.CanonicalCode!))
                .Concat(abridgedFood.Nutrients)
                .ToArray();

        return fullFood with
        {
            Nutrients = merged
        };
    }

    private static FoodCatalogItem ParseSearchFood(JsonElement food)
    {
        var fdcId =
            ReadLong(food, "fdcId")?.ToString(CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException(
                "USDA FDC response did not contain fdcId.");

        var description =
            ReadString(food, "description")
            ?? "(description unavailable)";

        return new FoodCatalogItem(
            NutritionProviderCodes.UsdaFoodDataCentral,
            fdcId,
            description,
            ReadString(food, "dataType"),
            ReadString(food, "brandOwner"),
            ReadString(food, "gtinUpc"),
            ReadDecimal(food, "servingSize"),
            NormalizeUnit(ReadString(food, "servingSizeUnit")),
            ReadString(food, "householdServingFullText"),
            ReadNutrients(food));
    }

    private static IReadOnlyList<NutrientAmount> ReadNutrients(
        JsonElement food)
    {
        if (!TryGetProperty(food, "foodNutrients", out var nutrientsElement)
            || nutrientsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<NutrientAmount>();
        }

        var nutrients = new List<NutrientAmount>();

        foreach (var item in nutrientsElement.EnumerateArray())
        {
            int? nutrientId = ReadInt(item, "nutrientId");
            string? nutrientName = ReadString(item, "nutrientName");
            string? nutrientNumber = ReadString(item, "nutrientNumber");
            string? unitName = ReadString(item, "unitName");

            decimal? amount =
                ReadDecimal(item, "value")
                ?? ReadDecimal(item, "amount");

            if (TryGetProperty(item, "nutrient", out var nested)
                && nested.ValueKind == JsonValueKind.Object)
            {
                nutrientId ??= ReadInt(nested, "id");
                nutrientName ??= ReadString(nested, "name");
                nutrientNumber ??= ReadString(nested, "number");
                unitName ??= ReadString(nested, "unitName");
            }

            var safeName =
                nutrientName
                ?? "(nutrient name unavailable)";

            var safeUnit =
                unitName
                ?? string.Empty;

            nutrients.Add(
                new NutrientAmount(
                    nutrientId,
                    safeName,
                    nutrientNumber,
                    amount,
                    safeUnit,
                    CanonicalNutrientCodes.MapUsdaNutrient(
                        nutrientId,
                        nutrientName,
                        unitName)));
        }

        return nutrients;
    }

    private static IReadOnlyList<NutrientAmount> ReadLabelNutrients(
        JsonElement food)
    {
        if (!TryGetProperty(food, "labelNutrients", out var label)
            || label.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<NutrientAmount>();
        }

        var nutrients = new List<NutrientAmount>();

        AddLabel(
            nutrients,
            label,
            "calories",
            "Label calories",
            "kcal",
            CanonicalNutrientCodes.EnergyKcal);

        AddLabel(
            nutrients,
            label,
            "protein",
            "Label protein",
            "g",
            CanonicalNutrientCodes.ProteinG);

        AddLabel(
            nutrients,
            label,
            "carbohydrates",
            "Label carbohydrates",
            "g",
            CanonicalNutrientCodes.CarbohydrateG);

        AddLabel(
            nutrients,
            label,
            "fat",
            "Label total fat",
            "g",
            CanonicalNutrientCodes.FatG);

        AddLabel(
            nutrients,
            label,
            "fiber",
            "Label fiber",
            "g",
            CanonicalNutrientCodes.FiberG);

        AddLabel(
            nutrients,
            label,
            "sugars",
            "Label sugars",
            "g",
            CanonicalNutrientCodes.SugarG);

        AddLabel(
            nutrients,
            label,
            "sodium",
            "Label sodium",
            "mg",
            CanonicalNutrientCodes.SodiumMg);

        AddLabel(
            nutrients,
            label,
            "saturatedFat",
            "Label saturated fat",
            "g",
            CanonicalNutrientCodes.SaturatedFatG);

        return nutrients;
    }

    private static void AddLabel(
        List<NutrientAmount> nutrients,
        JsonElement label,
        string propertyName,
        string displayName,
        string unitName,
        string canonicalCode)
    {
        if (!TryGetProperty(label, propertyName, out var item)
            || item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var value = ReadDecimal(item, "value");

        if (!value.HasValue)
            return;

        nutrients.Add(
            new NutrientAmount(
                SourceNutrientId: null,
                SourceNutrientName: displayName,
                SourceNutrientNumber: null,
                Amount: value.Value,
                UnitName: unitName,
                CanonicalCode: canonicalCode));
    }

    private static IReadOnlyList<FoodMeasureOption> ReadMeasures(
        JsonElement food,
        decimal basisAmount,
        string basisUnit,
        decimal? servingSize,
        string? servingSizeUnit,
        string? householdServingText)
    {
        var measures = new List<FoodMeasureOption>();
        var normalizedBasisUnit = NormalizeUnit(basisUnit) ?? basisUnit;

        if (servingSize is > 0m
            && !string.IsNullOrWhiteSpace(servingSizeUnit)
            && string.Equals(
                NormalizeUnit(servingSizeUnit),
                normalizedBasisUnit,
                StringComparison.OrdinalIgnoreCase))
        {
            measures.Add(
                new FoodMeasureOption(
                    Code: "serving",
                    Description:
                        BuildServingDescription(
                            servingSize.Value,
                            normalizedBasisUnit,
                            householdServingText),
                    UnitCode: "serving",
                    QuantityPerSelection: 1m,
                    BasisQuantityPerSelection: servingSize.Value,
                    BasisUnit: normalizedBasisUnit));
        }
        else if (normalizedBasisUnit == "serving")
        {
            measures.Add(
                new FoodMeasureOption(
                    Code: "serving",
                    Description:
                        string.IsNullOrWhiteSpace(householdServingText)
                            ? "1 serving"
                            : $"1 serving ({householdServingText.Trim()})",
                    UnitCode: "serving",
                    QuantityPerSelection: 1m,
                    BasisQuantityPerSelection: basisAmount,
                    BasisUnit: normalizedBasisUnit));
        }

        if (normalizedBasisUnit == "g"
            && TryGetProperty(food, "foodPortions", out var portions)
            && portions.ValueKind == JsonValueKind.Array)
        {
            var index = 0;

            foreach (var portion in portions.EnumerateArray())
            {
                index++;

                var gramWeight = ReadDecimal(portion, "gramWeight");
                var amount = ReadDecimal(portion, "amount") ?? 1m;

                if (!gramWeight.HasValue
                    || gramWeight.Value <= 0m
                    || amount <= 0m)
                {
                    continue;
                }

                var id =
                    ReadLong(portion, "id")?.ToString(
                        CultureInfo.InvariantCulture)
                    ?? index.ToString(CultureInfo.InvariantCulture);

                var unitCode = InferPortionUnitCode(portion);

                measures.Add(
                    new FoodMeasureOption(
                        Code: $"portion:{id}",
                        Description:
                            BuildPortionDescription(
                                portion,
                                amount,
                                gramWeight.Value),
                        UnitCode: unitCode,
                        QuantityPerSelection: amount,
                        BasisQuantityPerSelection: gramWeight.Value,
                        BasisUnit: "g"));
            }
        }

        if (normalizedBasisUnit == "g")
        {
            measures.Add(
                new FoodMeasureOption(
                    Code: "g",
                    Description: "gram",
                    UnitCode: "g",
                    QuantityPerSelection: 1m,
                    BasisQuantityPerSelection: 1m,
                    BasisUnit: "g"));

            measures.Add(
                new FoodMeasureOption(
                    Code: "oz",
                    Description: "ounce (28.35 g)",
                    UnitCode: "oz",
                    QuantityPerSelection: 1m,
                    BasisQuantityPerSelection: 28.349523125m,
                    BasisUnit: "g"));
        }
        else if (normalizedBasisUnit == "ml")
        {
            measures.Add(
                new FoodMeasureOption(
                    Code: "ml",
                    Description: "milliliter",
                    UnitCode: "ml",
                    QuantityPerSelection: 1m,
                    BasisQuantityPerSelection: 1m,
                    BasisUnit: "ml"));

            measures.Add(
                new FoodMeasureOption(
                    Code: "tsp",
                    Description: "teaspoon (4.93 ml)",
                    UnitCode: "tsp",
                    QuantityPerSelection: 1m,
                    BasisQuantityPerSelection: 4.92892159375m,
                    BasisUnit: "ml"));

            measures.Add(
                new FoodMeasureOption(
                    Code: "tbsp",
                    Description: "tablespoon (14.79 ml)",
                    UnitCode: "tbsp",
                    QuantityPerSelection: 1m,
                    BasisQuantityPerSelection: 14.78676478125m,
                    BasisUnit: "ml"));

            measures.Add(
                new FoodMeasureOption(
                    Code: "fl_oz",
                    Description: "fluid ounce (29.57 ml)",
                    UnitCode: "fl oz",
                    QuantityPerSelection: 1m,
                    BasisQuantityPerSelection: 29.5735295625m,
                    BasisUnit: "ml"));

            measures.Add(
                new FoodMeasureOption(
                    Code: "cup",
                    Description: "cup (236.59 ml)",
                    UnitCode: "cup",
                    QuantityPerSelection: 1m,
                    BasisQuantityPerSelection: 236.5882365m,
                    BasisUnit: "ml"));
        }
        else if (measures.Count == 0)
        {
            measures.Add(
                new FoodMeasureOption(
                    Code: "basis",
                    Description:
                        $"1 {normalizedBasisUnit}",
                    UnitCode: normalizedBasisUnit,
                    QuantityPerSelection: 1m,
                    BasisQuantityPerSelection: 1m,
                    BasisUnit: normalizedBasisUnit));
        }

        return measures
            .GroupBy(
                x => x.Code,
                StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
    }

    private static bool HasCoreMacros(
        IReadOnlyList<NutrientAmount> nutrients)
    {
        var codes =
            nutrients
                .Where(x => !string.IsNullOrWhiteSpace(x.CanonicalCode))
                .Select(x => x.CanonicalCode!)
                .ToHashSet(StringComparer.Ordinal);

        return
            codes.Contains(CanonicalNutrientCodes.EnergyKcal)
            && codes.Contains(CanonicalNutrientCodes.ProteinG)
            && codes.Contains(CanonicalNutrientCodes.CarbohydrateG)
            && codes.Contains(CanonicalNutrientCodes.FatG);
    }

    private static string BuildServingDescription(
        decimal servingSize,
        string servingUnit,
        string? household)
    {
        var size =
            $"{servingSize:0.##} {servingUnit}";

        return string.IsNullOrWhiteSpace(household)
            ? $"1 serving ({size})"
            : $"1 serving ({household.Trim()} / {size})";
    }

    private static string BuildPortionDescription(
        JsonElement portion,
        decimal amount,
        decimal gramWeight)
    {
        var description =
            ReadString(portion, "portionDescription");

        if (string.IsNullOrWhiteSpace(description))
        {
            var modifier = ReadString(portion, "modifier");
            var measureUnit = ReadMeasureUnit(portion);

            var unit =
                !string.IsNullOrWhiteSpace(modifier)
                    ? modifier
                    : !string.IsNullOrWhiteSpace(measureUnit)
                        ? measureUnit
                        : "portion";

            description =
                $"{amount:0.##} {unit}";
        }

        return $"{description!.Trim()} ({gramWeight:0.##} g)";
    }

    private static string InferPortionUnitCode(
        JsonElement portion)
    {
        var candidates = new[]
        {
            ReadString(portion, "portionDescription"),
            ReadString(portion, "modifier"),
            ReadMeasureUnit(portion)
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var value = candidate.Trim().ToLowerInvariant();

            if (value.Contains("tablespoon", StringComparison.Ordinal)
                || value.Contains("tbsp", StringComparison.Ordinal))
            {
                return "tbsp";
            }

            if (value.Contains("teaspoon", StringComparison.Ordinal)
                || value.Contains("tsp", StringComparison.Ordinal))
            {
                return "tsp";
            }

            if (value.Contains("cup", StringComparison.Ordinal))
                return "cup";

            if (value.Contains("slice", StringComparison.Ordinal))
                return "slice";

            if (value.Contains("piece", StringComparison.Ordinal))
                return "piece";

            if (value.Contains("ounce", StringComparison.Ordinal)
                || value == "oz"
                || value.Contains(" oz", StringComparison.Ordinal))
            {
                return "oz";
            }

            if (value.Contains("container", StringComparison.Ordinal))
                return "container";

            if (value.Contains("bottle", StringComparison.Ordinal))
                return "bottle";

            if (value.Contains("package", StringComparison.Ordinal)
                || value.Contains("packet", StringComparison.Ordinal))
            {
                return "package";
            }

            if (value.Contains("serving", StringComparison.Ordinal))
                return "serving";
        }

        return "portion";
    }

    private static string? ReadMeasureUnit(
        JsonElement portion)
    {
        if (!TryGetProperty(portion, "measureUnit", out var unit)
            || unit.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = ReadString(unit, "name");
        var abbreviation = ReadString(unit, "abbreviation");

        if (!string.IsNullOrWhiteSpace(name)
            && !string.Equals(
                name,
                "undetermined",
                StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        if (!string.IsNullOrWhiteSpace(abbreviation)
            && !string.Equals(
                abbreviation,
                "undetermined",
                StringComparison.OrdinalIgnoreCase))
        {
            return abbreviation;
        }

        return null;
    }

    private static string? NormalizeUnit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var unit = value.Trim().ToLowerInvariant();

        return unit switch
        {
            "g" or "gram" or "grams" or "grm" => "g",
            "ml" or "milliliter" or "milliliters" => "ml",
            "oz" or "ounce" or "ounces" => "oz",
            _ => unit
        };
    }

    private static bool TryGetProperty(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? ReadString(
        JsonElement element,
        string name)
    {
        if (!TryGetProperty(element, name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static int? ReadInt(
        JsonElement element,
        string name)
    {
        if (!TryGetProperty(element, name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number))
        {
            return number;
        }

        return null;
    }

    private static long? ReadLong(
        JsonElement element,
        string name)
    {
        if (!TryGetProperty(element, name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number))
        {
            return number;
        }

        return null;
    }

    private static decimal? ReadDecimal(
        JsonElement element,
        string name)
    {
        if (!TryGetProperty(element, name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(
                value.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out number))
        {
            return number;
        }

        return null;
    }
}
