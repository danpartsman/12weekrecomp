namespace WebRecomp.NutritionIntelligence.Models;

public sealed record NutritionFacts(
    decimal? EnergyKcal,
    decimal? ProteinG,
    decimal? CarbohydrateG,
    decimal? FatG,
    decimal? FiberG,
    decimal? SugarG,
    decimal? SodiumMg,
    decimal? SaturatedFatG);

public static class NutritionFactsCalculator
{
    public static NutritionFacts ForMeasure(
        FoodCatalogItem food,
        FoodMeasureOption measure,
        decimal selections)
    {
        ArgumentNullException.ThrowIfNull(food);
        ArgumentNullException.ThrowIfNull(measure);

        if (selections <= 0)
            throw new ArgumentOutOfRangeException(nameof(selections));

        if (measure.BasisQuantityPerSelection <= 0)
        {
            throw new InvalidOperationException(
                "Food measure basis quantity must be greater than zero.");
        }

        return ForQuantity(
            food,
            selections * measure.BasisQuantityPerSelection,
            measure.BasisUnit);
    }

    public static NutritionFacts ForQuantity(
        FoodCatalogItem food,
        decimal quantity,
        string quantityUnit)
    {
        ArgumentNullException.ThrowIfNull(food);

        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        if (food.NutrientBasisAmount <= 0)
        {
            throw new InvalidOperationException(
                "Food nutrient basis amount must be greater than zero.");
        }

        if (!string.Equals(
                food.NutrientBasisUnit,
                quantityUnit,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Cannot scale a {food.NutrientBasisUnit} nutrient basis " +
                $"using {quantityUnit} quantity.");
        }

        var scale = quantity / food.NutrientBasisAmount;

        return new NutritionFacts(
            Scale(food, CanonicalNutrientCodes.EnergyKcal, scale),
            Scale(food, CanonicalNutrientCodes.ProteinG, scale),
            Scale(food, CanonicalNutrientCodes.CarbohydrateG, scale),
            Scale(food, CanonicalNutrientCodes.FatG, scale),
            Scale(food, CanonicalNutrientCodes.FiberG, scale),
            Scale(food, CanonicalNutrientCodes.SugarG, scale),
            Scale(food, CanonicalNutrientCodes.SodiumMg, scale),
            Scale(food, CanonicalNutrientCodes.SaturatedFatG, scale));
    }

    private static decimal? Scale(
        FoodCatalogItem food,
        string canonicalCode,
        decimal scale)
    {
        var value =
            food.Nutrients
                .Where(x =>
                    string.Equals(
                        x.CanonicalCode,
                        canonicalCode,
                        StringComparison.Ordinal)
                    && x.Amount.HasValue)
                .Select(x => x.Amount)
                .FirstOrDefault();

        return value.HasValue
            ? decimal.Round(
                value.Value * scale,
                4,
                MidpointRounding.AwayFromZero)
            : null;
    }
}
