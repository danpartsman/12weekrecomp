using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.Web.Services.Nutrition;

namespace WebRecomp.Web.Tests;

public sealed class NutritionDayCopyPlannerTests
{
    private static DailyLogItemWriteModel Food(string slot = "BREAKFAST") => new(
        slot, 1, new(CustomFoodId: Guid.Parse("8dad1837-a8c1-4f26-9271-14e8c5407352")),
        "Yogurt", 2m, "SERVING", 240m, "USER_ENTERED", 180m, 30m, 12m, 2m,
        null, 8m, 40m, .5m, SourceServingId: "one-cup");

    [Fact] public void EmptyTodayKeepsMealSlotsPortionsReferencesAndNutrientSnapshots()
    {
        var source = new[] { Food(), Food("LUNCH"), Food("DINNER"), Food("SNACK") };
        var copied = NutritionDayCopyPlanner.MissingItems(source, []);
        Assert.Equal(source, copied);
        Assert.Null(copied[0].FiberG);
        Assert.Equal(2m, copied[0].Quantity);
        Assert.Equal("one-cup", copied[0].SourceServingId);
    }

    [Fact] public void MatchingEntriesAreSkippedEvenAfterTheirDisplayOrderChanges()
    {
        Assert.Empty(NutritionDayCopyPlanner.MissingItems([Food()], [Food() with { ItemOrder = 8 }]));
    }

    [Fact] public void RepeatedPortionsKeepTheirOriginalMultiplicity()
    {
        var source = new[] { Food(), Food() with { ItemOrder = 2 } };
        var missing = NutritionDayCopyPlanner.MissingItems(source, [Food()]);
        Assert.Single(missing);
        Assert.Empty(NutritionDayCopyPlanner.MissingItems(source, new[] { Food() }.Concat(missing).ToArray()));
    }

    [Fact] public void ExistingFoodInAnotherMealOrDifferentPortionDoesNotHideYesterdayEntry()
    {
        var today = new[] { Food("LUNCH"), Food() with { Quantity = 1m, EnergyKcal = 90m } };
        Assert.Single(NutritionDayCopyPlanner.MissingItems([Food()], today));
        Assert.Equal(2, today.Length);
        Assert.Equal(1m, today[1].Quantity);
    }

    [Fact] public void DifferentFoodReferenceOrNutrientSnapshotIsNotAnExactMatch()
    {
        var today = new[] { Food() with { Food = new(CustomFoodId: Guid.NewGuid()) },
            Food() with { ProteinG = 35m } };
        Assert.Single(NutritionDayCopyPlanner.MissingItems([Food()], today));
    }

    [Fact] public void MissingNutrientsRemainUnknownWhenCopied()
    {
        var partial = Food() with { ProteinG = null, NutrientSnapshotPolicyCode = "REFERENCE_ONLY" };
        var copied = Assert.Single(NutritionDayCopyPlanner.MissingItems([partial], []));
        Assert.Null(copied.ProteinG);
        Assert.Equal("REFERENCE_ONLY", copied.NutrientSnapshotPolicyCode);
    }

    [Fact] public void EmptyYesterdayDoesNotInventFoodFromTodaysEntries()
    {
        Assert.Empty(NutritionDayCopyPlanner.MissingItems([], [Food()]));
    }
}
