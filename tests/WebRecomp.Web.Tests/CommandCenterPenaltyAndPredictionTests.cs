using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.Web.Services.Dashboard2;
using WebRecomp.Web.Services.Dashboard3;

namespace WebRecomp.Web.Tests;

public sealed class CommandCenterPenaltyAndPredictionTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);
    [Theory]
    [InlineData("protein", 94.99, 0)] [InlineData("protein", 95, 1)]
    [InlineData("protein", 115, 1)] [InlineData("protein", 115.01, 0)]
    [InlineData("carbs", 84.99, 0)] [InlineData("carbs", 85, .5)]
    [InlineData("carbs", 94.99, .5)] [InlineData("carbs", 95, 1)]
    [InlineData("carbs", 105, 1)] [InlineData("carbs", 105.01, 0)]
    [InlineData("fat", 85, .5)] [InlineData("fat", 95, 1)] [InlineData("fat", 106, 0)]
    public void MacroBoundariesAndScoreUseExactPercentages(string key, double intake, double quality)
    {
        var band = MacroDayPlanner.Bands(100, 100, 100, (decimal)intake, (decimal)intake, (decimal)intake).Single(x => x.Key == key);
        Assert.Equal((decimal)quality, band.Quality);
    }
    [Theory]
    [InlineData(0,0)] [InlineData(2,0)] [InlineData(3,.5)] [InlineData(4,1)] [InlineData(5,2)] [InlineData(6,3)]
    public void TreatPenaltiesAccumulatePerPortion(int count, double expected) =>
        Assert.Equal((decimal)expected, RecompAdherenceScorer.FlexiblePenalty(count));

    [Fact]
    public void FreeTreatsRemainFreeWhenMacrosAreOverAndAllowancesDoNotResetPerDay()
    {
        var days = new List<RecompDayEvidence> {
            new(Today.AddDays(-2),100,100,100,1700,200,200,200,3400,2,"test"),
            new(Today.AddDays(-1),100,100,100,1700,100,100,100,1700,2,"test") };
        var result = RecompAdherenceScorer.Score(Today, days, []);
        Assert.Equal(4m, result.Components.Last().Earned);
        var onlyFree = RecompAdherenceScorer.Score(Today, days.Take(1).ToList(), []);
        Assert.Equal(5m, onlyFree.Components.Last().Earned);
    }
    [Theory]
    [InlineData(0,false,false,0)] [InlineData(9,true,false,0)]
    [InlineData(10,true,false,.5)] [InlineData(99,true,false,.5)]
    [InlineData(100,true,false,1)] [InlineData(0,false,true,1)]
    public void DailyWorkloadPenaltiesRespectRestAndTenPercentThreshold(double burn,bool workout,bool rest,double quality) =>
        Assert.Equal((decimal)quality,new RecompGlycogenDay(Today.AddDays(-1),(decimal)burn,100,rest,workout).Quality);
    [Fact]
    public void ExtraBurnOnOneDayCannotEraseAnotherMissedDay()
    {
        var work = new RecompGlycogenWork(300,200,0,
            [new(Today.AddDays(-2),300,100,false,true),new(Today.AddDays(-1),0,100,false,false)]);
        var result = RecompAdherenceScorer.Score(Today,[],[new("resistance","Resistance",1,2,0,25)],work);
        Assert.Equal(25,result.WorkoutScore); // half average credit, minus half a daily category miss
    }
    private static NutritionDailyLogItemReadModel Item(string slot, decimal kcal=500, string name="Meal",
        decimal protein=30,decimal carbs=50,decimal fat=20,decimal? sugar=null,decimal quantity=1) =>
        new(Guid.NewGuid(),slot,1,name,quantity,"SERVING",null,kcal,protein,carbs,fat,null,sugar,null,null);
    private static NutritionDailyLogReadModel Log(int offset,params NutritionDailyLogItemReadModel[] items) =>
        new(Guid.NewGuid(),Today.AddDays(offset),items,new(items.Sum(x=>x.EnergyKcal??0),0,0,0,null,null,null,null,"COMPLETE"));
    [Fact]
    public void PredictionAddsOnlyUnenteredSlotsAndUsesAvailableSevenDayHistory()
    {
        var logs = new[] {Log(-8,Item("DINNER",9000)),Log(-2,Item("BREAKFAST",400),Item("DINNER",600),Item("SNACK",100)),
            Log(-1,Item("BREAKFAST",500),Item("DINNER",800)),Log(0,Item("BREAKFAST",450))};
        Assert.Equal(1200m,RecompMealPrediction.Predict(Today,logs,450,"FOOD_LOG"));
    }
    [Fact]
    public void PredictionDoesNotDoubleCountManualTotalsWithoutMealDetails()
    {
        Assert.Null(RecompMealPrediction.Predict(Today,[Log(-1,Item("DINNER")),Log(0)],1500,"DAILY_ENTRY"));
        Assert.Null(RecompMealPrediction.Predict(Today,[Log(-1,Item("DINNER")),Log(0)],1500,"MANUAL"));
        Assert.Null(RecompMealPrediction.Predict(Today,[Log(0)],null,null));
    }
    [Fact]
    public void PredictionWorksBeforeFirstMealAndExcludesIncompleteHistory()
    {
        var incomplete=Log(-2,Item("DINNER",9000)) with {Totals=null};
        Assert.Equal(500m,RecompMealPrediction.Predict(Today,[Log(-1,Item("DINNER")),incomplete,Log(0)],null,null));
    }
    [Fact]
    public void DessertDetectionUsesNamesOrSugarAndAvoidsStarchyMixedMeals()
    {
        Assert.True(RecompTreatClassifier.IsTreat(Item("SNACK",300,"Glazed donut",3,35,15)));
        Assert.True(RecompTreatClassifier.IsTreat(Item("SNACK",300,"Sweet treat",3,35,15,25)));
        Assert.False(RecompTreatClassifier.IsTreat(Item("DINNER",300,"Rice with olive oil",3,35,15)));
        Assert.False(RecompTreatClassifier.IsTreat(Item("SNACK",300,"Protein cake",35,20,8)));
        Assert.Equal(2,RecompTreatClassifier.Count(Log(0,Item("SNACK",300,"Donut",3,35,15,quantity:2))));
    }
}
