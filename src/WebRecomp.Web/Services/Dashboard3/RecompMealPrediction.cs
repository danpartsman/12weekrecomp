using WebRecomp.NutritionIntelligence.Persistence;

namespace WebRecomp.Web.Services.Dashboard3;

public static class RecompMealPrediction
{
    public static decimal? Predict(DateOnly date, IEnumerable<NutritionDailyLogReadModel> logs,
        decimal? actualEnergy, string? source)
    {
        var rows = logs.ToArray();
        var today = rows.SingleOrDefault(x => x.LogDate == date);
        if (source is not (null or "FOOD_LOG" or "MANUAL") || today is null) return null;
        if (today.Items.Count > 0 && !RecompTreatClassifier.Complete(today)) return null;
        if (today.Items.Count == 0 && actualEnergy > 0) return null;
        var history = rows.Where(x => x.LogDate >= date.AddDays(-7) && x.LogDate < date
            && x.Items.Count > 0 && RecompTreatClassifier.Complete(x)).ToArray();
        if (history.Length == 0) return null;
        var entered = today.Items.Select(x => x.MealSlotCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remaining = history.SelectMany(x => x.Items).Where(x => !entered.Contains(x.MealSlotCode))
            .Sum(x => x.EnergyKcal!.Value) / history.Length;
        return (actualEnergy ?? today.Items.Sum(x => x.EnergyKcal ?? 0m)) + remaining;
    }
}

public static class RecompTreatClassifier
{
    public static bool Complete(NutritionDailyLogReadModel log) => log.Totals?.CompletenessCode == NutritionCompletenessCodes.Complete
        && log.Items.All(x => x.EnergyKcal.HasValue && x.ProteinG.HasValue && x.CarbohydrateG.HasValue && x.FatG.HasValue);

    public static int Count(NutritionDailyLogReadModel log) => log.Items.Sum(x => IsTreat(x)
        ? Math.Max(1, x.QuantityUnitCode is "SERVING" or "PORTION" or "PIECE" or "ITEM"
            ? (int)Math.Ceiling(x.Quantity) : 1) : 0);

    public static bool IsTreat(NutritionDailyLogItemReadModel item)
    {
        if (item.ProteinG is not decimal protein || item.CarbohydrateG is not decimal carbs || item.FatG is not decimal fat) return false;
        var energy = protein * 4m + carbs * 4m + fat * 9m;
        if (energy <= 0 || protein * 4m / energy > .15m || fat * 9m / energy < .20m) return false;
        // Names identify dessert types; sugar strengthens classification when supplied by the provider.
        // Carbs alone cannot distinguish sugar from starch and are never enough to label a food.
        var name = item.DescriptionSnapshot.ToLowerInvariant();
        var dessert = System.Text.RegularExpressions.Regex.IsMatch(name,
            @"\b(doughnuts?|donuts?|cakes?|cupcakes?|pies?|ice cream|cookies?|brownies?|pastr(y|ies)|candy|chocolate)\b");
        return (dessert && carbs * 4m / energy >= .25m)
            || (item.SugarG is decimal sugar && sugar * 4m / energy >= .20m && fat * 9m / energy >= .25m);
    }
}
