using WebRecomp.Web.Services.Dashboard3;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Tests;

public sealed class RecompAdherenceScorerTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static List<RecompDayEvidence> Week() => Enumerable.Range(0, 7).Select(i =>
        new RecompDayEvidence(Monday.AddDays(i), 200, 250, 60, 2340, null, null, null, null, null, "test")).ToList();
    private static List<RecompTrainingGauge> Training(int completed = 0, int remaining = 3) =>
        [new("resistance", "Resistance", completed, 3, remaining, 25),
         new("cardio", "Cardio / mobility", 0, 2, 2, 8),
         new("recovery", "Recovery / Zone 2", 0, 2, 2, 7)];

    [Fact] public void MondayStartsWithFullPotentialNotCompletedAchievement()
    {
        var result = RecompAdherenceScorer.Score(Monday, Week(), Training());
        Assert.Equal(100, result.Score); Assert.Equal(0, result.CompletedNutritionDays);
        Assert.Null(result.FlexibleItems);
    }
    [Fact] public void MissingIntakeDoesNotMeanZeroOrPenalizeLogging()
    {
        var result = RecompAdherenceScorer.Score(Monday.AddDays(2), Week(), Training());
        Assert.Equal(100, result.Score); Assert.Equal(2, result.CompletedNutritionDays);
        Assert.Equal(0, result.KnownNutritionDays);
    }
    [Fact] public void TodayShortfallKeepsPotentialButYesterdayShortfallLosesPoints()
    {
        var days = Week(); days[0] = days[0] with { Protein = 0, Carbs = 0, Fat = 0 };
        Assert.Equal(100, RecompAdherenceScorer.Score(Monday, days, Training()).Score);
        Assert.True(RecompAdherenceScorer.Score(Monday.AddDays(1), days, Training()).Score < 100);
    }
    [Fact] public void KnownOverageAppearsEvenBeforeDayEnds()
    {
        var days = Week(); days[0] = days[0] with { Carbs = 500 };
        Assert.True(RecompAdherenceScorer.Score(Monday, days, Training()).Score < 100);
    }
    [Fact] public void CatchingUpTrainingRaisesScoreWithoutErasingEarlierNutritionMiss()
    {
        var days = Week(); days[0] = days[0] with { Protein = 100, Carbs = 250, Fat = 60 };
        var behind = RecompAdherenceScorer.Score(Monday.AddDays(3), days, Training(0, 1));
        var caughtUp = RecompAdherenceScorer.Score(Monday.AddDays(3), days, Training(2, 1));
        Assert.True(caughtUp.Score > behind.Score); Assert.True(caughtUp.Score < 100);
    }
    [Fact] public void ExtraWorkoutsDoNotRewardExceedingThePlan()
    {
        Assert.Equal(RecompAdherenceScorer.Score(Monday.AddDays(4), Week(), Training(3, 0)).Score,
            RecompAdherenceScorer.Score(Monday.AddDays(4), Week(), Training(9, 0)).Score);
    }
    [Theory] [InlineData(0, "green")] [InlineData(1, "green")] [InlineData(2, "green")]
    [InlineData(3, "yellow")] [InlineData(4, "yellow")] [InlineData(5, "red")]
    public void FlexibleItemThresholdsUseReportedWeeklyItems(int count, string expected)
    {
        var days = Week(); days[0] = days[0] with { FlexibleItems = count, Protein = 200, Carbs = 250, Fat = 60 };
        Assert.Equal(expected, RecompAdherenceScorer.Score(Monday, days, Training()).FlexibleBand);
    }
    [Fact] public void FlexibleItemDayOverMacrosTurnsRedEvenWithOnlyOneItem()
    {
        var days = Week(); days[0] = days[0] with { FlexibleItems = 1, Carbs = 300 };
        Assert.Equal("red", RecompAdherenceScorer.Score(Monday, days, Training()).FlexibleBand);
    }
    [Fact] public void OverMacrosOnDifferentDayDoesNotBlameFlexibleItems()
    {
        var days = Week(); days[0] = days[0] with { Carbs = 300, FlexibleItems = 0 };
        days[1] = days[1] with { Carbs = 250, FlexibleItems = 1 };
        Assert.Equal("green", RecompAdherenceScorer.Score(Monday.AddDays(1), days, Training()).FlexibleBand);
    }
    [Theory] [InlineData(59, "red")] [InlineData(60, "yellow")]
    [InlineData(79, "yellow")] [InlineData(80, "green")]
    public void ScoreBandEdgesAreUnambiguous(int score, string expected) =>
        Assert.Equal(expected, new RecompScoreResult { Score = score }.Band);
    [Fact] public void SessionsUseSingleCategoryAndZoneTwoIsRecovery()
    {
        var types = new Dictionary<string, WorkoutTypeDefinition>();
        Assert.Equal("recovery", RecompCockpitService.Group("ZONE_2", types));
        Assert.Equal("cardio", RecompCockpitService.Group("MOBILITY_RECOVERY", types));
        Assert.Equal("resistance", RecompCockpitService.Group("MIXED_GYM_SESSION", types));
        Assert.Equal("unclassified", RecompCockpitService.Group("UNKNOWN", types));
    }
    [Fact] public void WorkoutGaugeIncludesModeledWorkAndCanRecoverWithRemainingPlan()
    {
        var training = Training(3, 0);
        var low = RecompAdherenceScorer.Score(Monday.AddDays(4), Week(), training,
            new RecompGlycogenWork(100, 400, 0));
        var recovered = RecompAdherenceScorer.Score(Monday.AddDays(4), Week(), training,
            new RecompGlycogenWork(100, 400, 300));
        Assert.True(low.WorkoutScore < recovered.WorkoutScore);
        Assert.Equal(100, recovered.WorkoutScore);
        Assert.Equal(100, low.NutritionScore);
    }
    [Fact] public void ExtraGlycogenBurnCannotReplaceMissedResistanceOrEarnBonus()
    {
        var result = RecompAdherenceScorer.Score(Monday.AddDays(4), Week(), Training(0, 0),
            new RecompGlycogenWork(1000, 400, 0));
        Assert.Equal(0m, result.Components.Single(x => x.Label == "Glycogen work + resistance").Earned);
        Assert.True(result.WorkoutScore < 100);
        Assert.Equal(1m, new RecompGlycogenWork(1000, 400, 0).ProjectedFraction);
    }
    [Fact] public void NutritionAndWorkoutSummariesExcludeFlexibilityPenalty()
    {
        var days = Week(); days[0] = days[0] with { FlexibleItems = 5 };
        var result = RecompAdherenceScorer.Score(Monday, days, Training(),
            new RecompGlycogenWork(0, 400, 400));
        Assert.Equal(100, result.NutritionScore);
        Assert.Equal(100, result.WorkoutScore);
        Assert.Equal(95, result.Score);
    }
    [Fact] public void NoModeledBurnGoalIsNeutralInsteadOfDividingByZero()
    {
        Assert.Equal(1m, new RecompGlycogenWork(0, 0, 0).ProjectedFraction);
    }

}
