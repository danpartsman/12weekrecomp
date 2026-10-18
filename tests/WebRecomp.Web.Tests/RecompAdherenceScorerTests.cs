using WebRecomp.Web.Services.Dashboard2;
using WebRecomp.Web.Services.Dashboard3;

namespace WebRecomp.Web.Tests;
public sealed class RecompAdherenceScorerTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static List<RecompDayEvidence> Week() => Enumerable.Range(0, 7).Select(i =>
        new RecompDayEvidence(Monday.AddDays(i), 200, 250, 60, 2340, null, null, null, null, null, "test")).ToList();
    private static List<RecompTrainingGauge> Training(int completed = 0, int remaining = 3) =>
        [new("resistance", "Resistance", completed, 3, remaining, 25),
         new("cardio", "Cardio / mobility", 0, 2, 2, 8), new("recovery", "Recovery", 0, 2, 2, 7)];

    [Fact] public void EmptyAndFutureCategoriesDoNotCreatePerfectScores()
    {
        var result = RecompAdherenceScorer.Score(Monday, Week(), Training());
        Assert.False(result.HasEvidence); Assert.Null(result.DisplayScore);
        Assert.Null(result.NutritionScore); Assert.Null(result.WorkoutScore);
        Assert.All(result.Components, x => Assert.False(x.Included));
    }
    [Fact] public void UnknownPastNutritionIsExcludedFromDenominator()
    {
        var result = RecompAdherenceScorer.Score(Monday.AddDays(2), Week(), Training(1, 2));
        Assert.Equal(100, result.Score); Assert.Null(result.NutritionScore);
        Assert.Equal(0, result.KnownNutritionDays);
    }
    [Fact] public void TodayNutritionIsUnfinishedUntilDayEnds()
    {
        var days = Week(); days[0] = days[0] with { Protein = 0, Carbs = 500, Fat = 0 };
        Assert.Null(RecompAdherenceScorer.Score(Monday, days, Training()).DisplayScore);
        Assert.Equal(33, RecompAdherenceScorer.Score(Monday.AddDays(1), days, Training()).NutritionScore);
    }
    [Fact] public void SmallFatOverageIsAWholeDayCategoryMiss()
    {
        var days = Week(); days[0] = days[0] with { Protein = 210, Carbs = 250, Fat = 67 };
        var result = RecompAdherenceScorer.Score(Monday.AddDays(1), days, Training());
        Assert.Equal(82, result.NutritionScore);
        Assert.Equal(0, result.Components.Single(x => x.Label == "Fat balance").Earned);
        Assert.Equal(25, result.Components.Single(x => x.Label == "Protein consistency").Earned);
    }
    [Fact] public void OneMissAcrossTwoKnownDaysLosesHalfTheCategory()
    {
        var days = Week(); days[0] = days[0] with { Protein = 100 };
        days[1] = days[1] with { Protein = 200 };
        var result = RecompAdherenceScorer.Score(Monday.AddDays(2), days, Training());
        Assert.Equal(75, result.NutritionScore); Assert.Equal(75, result.Score);
    }
    [Fact] public void ProteinAllowsMoreOverageThanFat()
    {
        var bands = MacroDayPlanner.Bands(200, 250, 60, 220, 250, 72);
        Assert.Equal(1m, RecompAdherenceScorer.Quality(bands[0].Eaten, bands[0].Low, bands[0].High, false));
        Assert.Equal(0m, RecompAdherenceScorer.Quality(bands[2].Eaten, bands[2].Low, bands[2].High, false));
    }
    [Fact] public void ScheduledFutureTrainingDoesNotEarnCreditForPastMisses()
    {
        var result = RecompAdherenceScorer.Score(Monday.AddDays(3), Week(), Training(0, 2));
        Assert.Equal(50, result.WorkoutScore);
        Assert.False(result.Components.Single(x => x.Label == "Recovery").Included);
    }
    [Fact] public void CatchingUpWorkRestoresCreditWithoutErasingNutritionMiss()
    {
        var days = Week(); days[0] = days[0] with { Protein = 100, Carbs = 250, Fat = 60 };
        var behind = RecompAdherenceScorer.Score(Monday.AddDays(3), days, Training(0, 1));
        var caught = RecompAdherenceScorer.Score(Monday.AddDays(3), days, Training(2, 1));
        Assert.True(caught.Score > behind.Score); Assert.True(caught.Score < 100);
    }
    [Fact] public void ModeledWorkUsesDueWorkAndExtraBurnCannotReplaceResistance()
    {
        var result = RecompAdherenceScorer.Score(Monday.AddDays(4), Week(), Training(0, 0), new(1000, 400, 0));
        Assert.Equal(50, result.WorkoutScore);
        var partial = RecompAdherenceScorer.Score(Monday.AddDays(4), Week(), Training(3, 0), new(100, 400, 0));
        Assert.Equal(50, partial.WorkoutScore);
    }
    [Fact] public void UnconfirmedFlexibilityIsExcluded()
    {
        Assert.Equal("gray", RecompAdherenceScorer.Score(Monday, Week(), Training()).FlexibleBand);
    }
    [Theory] [InlineData(0,"green")] [InlineData(2,"green")] [InlineData(3,"yellow")] [InlineData(5,"red")]
    public void ConfirmedFinishedDayFlexibilityKeepsThresholds(int count,string expected)
    {
        var days = Week(); days[0] = days[0] with { FlexibleItems = count };
        Assert.Equal(expected, RecompAdherenceScorer.Score(Monday.AddDays(1), days, Training()).FlexibleBand);
    }
    [Theory] [InlineData("RECOMP_SCORE_V1")] [InlineData("RECOMP_SCORE_V2")] [InlineData("RECOMP_SCORE_V3")] [InlineData("RECOMP_SCORE_V4")]
    public void HistoricalCaptureKeepsOriginalScoreComponents(string version)
    {
        var parts = new List<RecompScoreComponent> { new("Protein",25,12.5m,"saved"),new("Carbs",20,10,"saved"),new("Fat",10,5,"saved"),new("Training",25,12.5m,"saved"),new("Flex",5,2.5m,"saved") };
        var saved = new RecompScoreSnapshot(version, Monday, 42, 2, 3, Week(), Training(), parts);
        var restored = RecompCockpitService.RestoreCapturedScore(Monday,saved);
        Assert.Equal(42,restored.Score); Assert.Equal(50,restored.NutritionScore); Assert.Equal(parts,restored.Components);
    }
    [Fact] public void RollingMondayRetainsPreviousSevenCompletedDays()
    {
        var start = RecompCockpitService.RollingStart(Monday, Monday.AddDays(-30));
        var days = Enumerable.Range(0, 7).Select(i => new RecompDayEvidence(start.AddDays(i),
            200, 250, 60, 2340, i == 0 ? 100 : 200, 250, 60, 2340, null, "test")).ToList();
        var result = RecompAdherenceScorer.Score(Monday, days, []);
        Assert.Equal(Monday.AddDays(-7), start);
        Assert.Equal(7, result.CompletedNutritionDays); Assert.Equal(7, result.KnownNutritionDays);
        Assert.Equal(93, result.NutritionScore);
        Assert.Equal(6.5m / 7m * 25m, result.Components[0].Earned);
    }
    [Fact] public void NewProgramUsesAvailableCompletedDaysWithoutInventingEarlierEvidence()
    {
        var start = RecompCockpitService.RollingStart(Monday, Monday.AddDays(-2));
        var days = Enumerable.Range(0, 2).Select(i => new RecompDayEvidence(start.AddDays(i),
            200, 250, 60, 2340, 200, 250, 60, 2340, null, "test")).ToList();
        var result = RecompAdherenceScorer.Score(Monday, days, []);
        Assert.Equal(2, result.CompletedNutritionDays); Assert.Equal(100, result.NutritionScore);
    }

}
