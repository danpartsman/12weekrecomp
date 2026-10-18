using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Services.Dashboard2;

public sealed record MacroBand(string Key, string Name, string Color, decimal Target, decimal Low, decimal High, decimal? Eaten)
{
    public decimal YellowLow => Target * (Key == "protein" ? .90m : .75m);
    public decimal YellowHigh => Target * (Key == "protein" ? 1.20m : 1.10m);
    private decimal BaseQuality => !Eaten.HasValue || Target <= 0 ? 0m : Eaten >= Low && Eaten <= High ? 1m
        : Eaten >= YellowLow && Eaten <= YellowHigh ? .5m : 0m;
    // Intake below prescription gets half the penalty; above-prescription scoring stays unchanged.
    public decimal Quality => Eaten.HasValue && Eaten.Value < Target
        ? 1m - (1m - BaseQuality) / 2m
        : BaseQuality;
    public decimal Remaining => Math.Max(0, Low - (Eaten ?? 0));
    public string Status => !Eaten.HasValue ? "Not logged" : Eaten > High ? "Above planned range" : Eaten >= Low ? "Within range" : "Still to plan";
}
public sealed record MealProgress(string Label, decimal Protein, decimal Carbs, decimal Fat);
public sealed record MealAllocation(string Label, decimal Protein, decimal Carbs, decimal Fat)
{
    public decimal Energy => Protein * 4 + Carbs * 4 + Fat * 9;
}
public sealed class MacroDayPlan
{
    public List<MacroBand> Bands { get; init; } = [];
    public List<MealProgress> Meals { get; init; } = [];
    public List<MealAllocation> Suggestions { get; init; } = [];
    public decimal? EnergyEaten { get; init; }
    public decimal EnergyTarget { get; init; }
    public decimal? PredictedEnergy { get; set; }
    public string PredictionNotice { get; set; } = "";
    public decimal EnergyRemaining => Math.Max(0, EnergyTarget - (EnergyEaten ?? 0));
    public decimal UnallocatedProtein { get; set; }
    public decimal UnallocatedCarbs { get; set; }
    public decimal UnallocatedFat { get; set; }
    public string SourceNote { get; init; } = "";
    public string Advice { get; set; } = "";
    public bool CanPlan { get; init; }
    public bool OverEnergy => EnergyEaten > EnergyTarget;
    public bool HasLog { get; init; }
    public bool MacroEnergyMismatch { get; init; }
    public int Occasions { get; init; }
}

/// <summary>
/// Read-only meal allocation around authoritative engine targets. Bands are UI planning
/// tolerances, NOT measured physiological limits. No writeback, calorie debt, or absorption cap.
/// </summary>
public static class MacroDayPlanner
{
    private static readonly string[] Slots = ["BREAKFAST", "LUNCH", "DINNER", "SNACK"];
    public static List<MacroBand> Bands(decimal protein, decimal carbs, decimal fat,
        decimal? actualProtein = null, decimal? actualCarbs = null, decimal? actualFat = null) =>
    [
        new("protein", "Protein", "#168CFF", protein, protein * .95m, protein * 1.15m, actualProtein),
        new("carbs", "Carbs", "#FFD400", carbs, carbs * .95m, carbs * 1.05m, actualCarbs),
        new("fat", "Fat", "#24D96B", fat, fat * .95m, fat * 1.05m, actualFat)
    ];

    public static MacroDayPlan Build(DashboardViewModel day, NutritionDailyLogReadModel? log,
        bool foodLogAuthoritative, int localHour, int? occasions = null)
    {
        var hasItems = log?.Items.Count > 0;
        var useFood = foodLogAuthoritative && hasItems;
        var incomplete = useFood && (log!.Totals is null ||
            log.Totals.CompletenessCode != NutritionCompletenessCodes.Complete ||
            log.Items.Any(x => !x.ProteinG.HasValue || !x.CarbohydrateG.HasValue || !x.FatG.HasValue || !x.EnergyKcal.HasValue));
        decimal? p = useFood ? (incomplete ? null : log!.Totals!.ProteinG) : day.ActualProteinG;
        decimal? c = useFood ? (incomplete ? null : log!.Totals!.CarbohydrateG) : day.ActualCarbsG;
        decimal? f = useFood ? (incomplete ? null : log!.Totals!.FatG) : day.ActualFatG;
        decimal? energy = useFood ? (incomplete ? null : log!.Totals!.EnergyKcal) : day.ActualCalories;
        var known = p.HasValue && c.HasValue && f.HasValue && energy.HasValue;
        var macroEnergy = (p ?? 0) * 4 + (c ?? 0) * 4 + (f ?? 0) * 9;
        var mismatch = known && Math.Abs(macroEnergy - energy!.Value) > Math.Max(100, energy.Value * .10m);
        var meals = new List<MealProgress>();
        if (useFood && !incomplete)
        {
            foreach (var slot in Slots.Concat(log!.Items.Select(x => x.MealSlotCode).Except(Slots)))
            {
                var items = log.Items.Where(x => x.MealSlotCode == slot).ToArray();
                if (items.Length > 0) meals.Add(new(Label(slot), items.Sum(x => x.ProteinG ?? 0),
                    items.Sum(x => x.CarbohydrateG ?? 0), items.Sum(x => x.FatG ?? 0)));
            }
        }
        var dinnerLogged = useFood && log!.Items.Any(x => x.MealSlotCode == "DINNER");
        var lunchLogged = useFood && log!.Items.Any(x => x.MealSlotCode == "LUNCH");
        var count = Math.Clamp(occasions ?? (dinnerLogged || localHour >= 21 ? 1 : lunchLogged || localHour >= 14 ? 2 : localHour >= 10 ? 3 : 4), 1, 4);
        var plan = new MacroDayPlan
        {
            Bands = Bands(day.ProteinG, day.CarbsG, day.FatG, p, c, f), Meals = meals,
            EnergyEaten = energy, EnergyTarget = day.Calories, HasLog = known,
            CanPlan = known && !mismatch && day.Calories > 0 && day.ProteinG > 0,
            MacroEnergyMismatch = mismatch, Occasions = count,
            SourceNote = incomplete ? "Some logged foods lack complete nutrients. Complete those entries before planning the remainder."
                : useFood ? "Food Log totals. Meals are grouped by meal label, not measured eating time. Entries do not imply that a meal is finished."
                : known ? "Daily Entry totals are authoritative. Meal timing is unavailable; food-log meals have not been added again."
                : "Enter food or daily totals first. Missing entries are not treated as zero food eaten."
        };
        if (!plan.CanPlan)
        {
            plan.Advice = mismatch ? "Calories and macro-derived energy differ materially. Review the entries before relying on a remaining-food estimate."
                : plan.SourceNote;
            return plan;
        }
        var dp = Math.Max(0, day.ProteinG - p!.Value);
        var dc = Math.Max(0, day.CarbsG - c!.Value);
        var df = Math.Max(0, day.FatG - f!.Value);
        var budget = plan.EnergyRemaining;
        var ap = Math.Min(dp, Math.Floor(budget / 4)); budget -= ap * 4;
        // Reserve the lower planned fat allocation, then prioritize training carbohydrate.
        var fatFloor = Math.Max(0, plan.Bands[2].Low - f.Value);
        var af = Math.Min(fatFloor, Math.Floor(budget / 9)); budget -= af * 9;
        var ac = Math.Min(dc, Math.Floor(budget / 4)); budget -= ac * 4;
        var extraFat = Math.Min(df - af, Math.Floor(budget / 9)); af += extraFat;
        plan.UnallocatedProtein = dp - ap; plan.UnallocatedCarbs = dc - ac; plan.UnallocatedFat = df - af;
        var labels = count switch
        {
            4 => new[] { "Next meal", "Lunch / next meal", "Dinner", "Later snack" },
            3 => new[] { "Next meal", "Dinner", "Later snack" },
            2 => new[] { dinnerLogged ? "Next eating occasion" : "Dinner / next meal", "Later snack or shake" },
            _ => new[] { "Next eating occasion" }
        };
        for (var i = 0; i < count && ap + ac + af > 0; i++)
        {
            var left = count - i;
            var pp = left == 1 ? ap : Math.Floor(ap / left);
            // Put most carbs/fat in meals, leaving a smaller later snack.
            var weight = left == 1 ? 1m : left == 2 ? .70m : 1m / left;
            var cc = left == 1 ? ac : Math.Floor(ac * weight);
            var ff = left == 1 ? af : Math.Floor(af * weight);
            plan.Suggestions.Add(new(labels[i], pp, cc, ff));
            ap -= pp; ac -= cc; af -= ff;
        }
        plan.Advice = plan.OverEnergy
            ? "Logged intake is above today's energy plan. Do not chase the tops of the macro bands. Review any remaining protein need and avoid automatically adding a shake."
            : plan.UnallocatedProtein + plan.UnallocatedCarbs + plan.UnallocatedFat > 0
            ? "The remaining energy allocation cannot cover every macro target. This is a tradeoff to review, not an instruction to skip needed food. Start with lean protein and check the full meal's nutrients."
            : plan.Suggestions.Count == 0
            ? "Today's central macro targets are covered. There is no need to eat more just to reach the upper edges of the bands."
            : $"Spread the remaining {dp:0} g protein across {count} eating occasion(s). The portions below also reserve carbohydrate and fat within today's remaining energy allocation.";
        return plan;
    }
    private static string Label(string code) => code.Length == 0 ? "Meal" : code[..1].ToUpperInvariant() + code[1..].ToLowerInvariant();
}
