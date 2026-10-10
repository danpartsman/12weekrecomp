using WebRecomp.NutritionIntelligence.Persistence;

namespace WebRecomp.Web.Services.Nutrition;

public static class NutritionDayCopyPlanner
{
    public static List<DailyLogItemWriteModel> MissingItems(
        IReadOnlyList<DailyLogItemWriteModel> source, IReadOnlyList<DailyLogItemWriteModel> existing)
    {
        // Compare full snapshots, preserving repeated portions in the source day.
        // Row order is presentation only; it must not defeat retry protection.
        var counts = existing.GroupBy(x => x with { ItemOrder = 0 })
            .ToDictionary(x => x.Key, x => x.Count());
        var missing = new List<DailyLogItemWriteModel>();
        foreach (var item in source)
        {
            var key = item with { ItemOrder = 0 };
            if (counts.TryGetValue(key, out var count) && count > 0)
                counts[key] = count - 1;
            else
                missing.Add(item);
        }
        return missing;
    }
}
