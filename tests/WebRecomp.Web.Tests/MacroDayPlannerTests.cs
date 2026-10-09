using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.Web.Services.Dashboard2;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Tests;

public class MacroDayPlannerTests
{
    private static DashboardViewModel Day(decimal p = 100, decimal c = 100, decimal f = 30) => new()
    {
        Calories = 2340, ProteinG = 200, CarbsG = 250, FatG = 60,
        ActualProteinG = p, ActualCarbsG = c, ActualFatG = f,
        ActualCalories = p * 4 + c * 4 + f * 9
    };
    private static NutritionDailyLogItemReadModel Item(string slot, decimal? p, decimal c, decimal f) =>
        new(Guid.NewGuid(), slot, 1, "Food", 1, "SERVING", null, (p ?? 0) * 4 + c * 4 + f * 9,
            p, c, f, null, null, null, null);
    private static NutritionDailyLogReadModel Log(decimal? p = 100) => new(Guid.NewGuid(), new(2026, 10, 9),
        [Item("BREAKFAST", p / 2, 50, 15), Item("LUNCH", p / 2, 50, 15)],
        new(1070, p ?? 0, 100, 30, null, null, null, null, p.HasValue ? "COMPLETE" : "PARTIAL"));

    [Fact] public void LunchLoggedSplitsRemainingHundredProteinAcrossDinnerAndSnack()
    {
        var result = MacroDayPlanner.Build(Day(), Log(), true, 14);
        Assert.Equal(2, result.Suggestions.Count);
        Assert.All(result.Suggestions, x => Assert.Equal(50m, x.Protein));
        Assert.Equal(100m, result.Bands[0].Eaten);
        Assert.Equal(2, result.Meals.Count);
        Assert.True(result.Suggestions.Sum(x => x.Energy) <= result.EnergyRemaining);
    }
    [Fact] public void ManualOverrideDoesNotDoubleCountFoodLog()
    {
        var result = MacroDayPlanner.Build(Day(150), Log(), false, 14, 2);
        Assert.Empty(result.Meals);
        Assert.Equal(150m, result.Bands[0].Eaten);
        Assert.Equal(50m, result.Suggestions.Sum(x => x.Protein));
    }
    [Fact] public void MissingIntakeDoesNotBecomeZeroOrGenerateMeals()
    {
        var day = Day(); day.ActualProteinG = null;
        var result = MacroDayPlanner.Build(day, null, false, 14);
        Assert.False(result.CanPlan); Assert.Empty(result.Suggestions);
    }
    [Fact] public void IncompleteFoodsDoNotGenerateFalsePrecision()
    {
        var result = MacroDayPlanner.Build(Day(), Log(null), true, 14);
        Assert.False(result.CanPlan); Assert.Empty(result.Suggestions);
        Assert.Null(result.Bands[0].Eaten);
    }
    [Fact] public void EnergyOverageStopsAutomaticAdditionsEvenWithProteinShort()
    {
        var result = MacroDayPlanner.Build(Day(100, 400, 100), null, false, 14);
        Assert.True(result.OverEnergy); Assert.Empty(result.Suggestions);
        Assert.Equal(100m, result.UnallocatedProtein);
    }
    [Fact] public void InBudgetBandsDoNotOverrideCombinedEnergyCheck()
    {
        var result = MacroDayPlanner.Build(Day(220, 275, 66), null, false, 14);
        Assert.All(result.Bands, x => Assert.Equal("Within range", x.Status));
        Assert.True(result.OverEnergy); Assert.Empty(result.Suggestions);
    }
    [Fact] public void ConflictingMacroAndCalorieRecordsPauseAllocator()
    {
        var day = Day(); day.ActualCalories = 100;
        var result = MacroDayPlanner.Build(day, null, false, 14);
        Assert.True(result.MacroEnergyMismatch); Assert.Empty(result.Suggestions);
    }
    [Theory] [InlineData(-2, 1)] [InlineData(20, 4)]
    public void InvalidOccasionCountIsClamped(int input, int expected)
    {
        var result = MacroDayPlanner.Build(Day(), null, false, 14, input);
        Assert.Equal(expected, result.Occasions);
        Assert.Equal(100m, result.Suggestions.Sum(x => x.Protein));
    }
    [Fact] public void CoveredCentralTargetsDoNotEncourageEatingToUpperBands()
    {
        var result = MacroDayPlanner.Build(Day(200, 250, 60), null, false, 14);
        Assert.Empty(result.Suggestions);
        Assert.Contains("no need to eat more", result.Advice);
    }
    [Fact] public void DeletingFoodChangesProgressAndRemainingAllocation()
    {
        var before = MacroDayPlanner.Build(Day(), Log(), true, 14, 2);
        var afterLog = new NutritionDailyLogReadModel(Guid.NewGuid(), new(2026, 10, 9),
            [Item("BREAKFAST", 50, 50, 15)], new(535, 50, 50, 15, null, null, null, null, "COMPLETE"));
        var after = MacroDayPlanner.Build(Day(), afterLog, true, 14, 2);
        Assert.Equal(50m, after.Bands[0].Eaten);
        Assert.True(after.Suggestions.Sum(x => x.Protein) > before.Suggestions.Sum(x => x.Protein));
    }
}
