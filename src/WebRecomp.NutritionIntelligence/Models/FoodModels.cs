namespace WebRecomp.NutritionIntelligence.Models;

public static class NutritionProviderCodes
{
    public const string UsdaFoodDataCentral = "USDA_FDC";
    public const string FatSecret = "FATSECRET";
}

public static class CanonicalNutrientCodes
{
    public const string EnergyKcal = "ENERGY_KCAL";
    public const string ProteinG = "PROTEIN_G";
    public const string CarbohydrateG = "CARBOHYDRATE_G";
    public const string FatG = "FAT_G";
    public const string FiberG = "FIBER_G";
    public const string SugarG = "SUGAR_G";
    public const string SodiumMg = "SODIUM_MG";
    public const string SaturatedFatG = "SATURATED_FAT_G";

    public static string? MapUsdaNutrient(
        int? sourceNutrientId,
        string? sourceName,
        string? unitName)
    {
        var byId = sourceNutrientId switch
        {
            1003 => ProteinG,
            1004 => FatG,
            1005 => CarbohydrateG,
            1008 => EnergyKcal,
            2047 => EnergyKcal,
            2048 => EnergyKcal,
            1079 => FiberG,
            1093 => SodiumMg,
            1258 => SaturatedFatG,
            2000 => SugarG,
            _ => null
        };

        if (byId is not null)
            return byId;

        var name = (sourceName ?? string.Empty).Trim().ToLowerInvariant();
        var unit = (unitName ?? string.Empty).Trim().ToLowerInvariant();

        if (name == "protein")
            return ProteinG;

        if (name is "total lipid (fat)" or "total fat")
            return FatG;

        if (name.Contains("carbohydrate", StringComparison.Ordinal))
            return CarbohydrateG;

        if (name.Contains("fiber", StringComparison.Ordinal))
            return FiberG;

        if (name == "sodium, na" || name == "sodium")
            return SodiumMg;

        if (name.Contains("sugars, total", StringComparison.Ordinal)
            || name == "total sugars")
        {
            return SugarG;
        }

        if (name.Contains("fatty acids, total saturated", StringComparison.Ordinal)
            || name == "saturated fat")
        {
            return SaturatedFatG;
        }

        if (name.StartsWith("energy", StringComparison.Ordinal)
            && (unit == "kcal" || unit == "calories"))
        {
            return EnergyKcal;
        }

        return null;
    }
}

public sealed record FoodSearchRequest(
    string Query,
    int PageNumber = 1,
    int PageSize = 25,
    IReadOnlyList<string>? DataTypes = null);

public sealed record NutrientAmount(
    int? SourceNutrientId,
    string SourceNutrientName,
    string? SourceNutrientNumber,
    decimal? Amount,
    string UnitName,
    string? CanonicalCode);

public sealed record FoodMeasureOption(
    string Code,
    string Description,
    string UnitCode,
    decimal QuantityPerSelection,
    decimal BasisQuantityPerSelection,
    string BasisUnit);

public sealed record FoodCatalogItem(
    string ProviderCode,
    string SourceFoodId,
    string Description,
    string? DataType,
    string? BrandOwner,
    string? GtinUpc,
    decimal? ServingSize,
    string? ServingSizeUnit,
    string? HouseholdServingText,
    IReadOnlyList<NutrientAmount> Nutrients,
    decimal NutrientBasisAmount = 100m,
    string NutrientBasisUnit = "g",
    IReadOnlyList<FoodMeasureOption>? Measures = null);

public sealed record FoodSearchPage(
    int PageNumber,
    int PageSize,
    int TotalHits,
    int TotalPages,
    IReadOnlyList<FoodCatalogItem> Foods);

public sealed record FoodComposition(
    FoodCatalogItem Food,
    string RawSourceJson);
