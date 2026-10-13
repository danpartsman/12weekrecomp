using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Dashboard3;
namespace WebRecomp.Web.Tests;

public sealed class RecompWeeklyTrendTests
{
    private static readonly DateOnly Today = new(2026,10,13);
    private static Measurement Reading(int offset,decimal weight=200,decimal muscle=80) => new()
    { MeasurementId=Guid.NewGuid(),LocalMeasurementDate=Today.AddDays(offset),WeightLb=weight,BodyFatPct=.25m,SkeletalMuscleMassLb=muscle };
    [Theory]
    [InlineData(-1,1,"Favorable")] [InlineData(-1,0,"Favorable")]
    [InlineData(0,1,"Favorable")] [InlineData(1,1,"Mixed")]
    [InlineData(-1,-1,"Mixed")] [InlineData(1,0,"Needs review")]
    [InlineData(0,-1,"Needs review")] [InlineData(.2,-.2,"Stable")]
    public void OutcomeAcknowledgesBothFatAndMuscle(double fat,double muscle,string expected) =>
        Assert.Equal(expected,RecompWeeklyTrend.Outcome((decimal)fat,(decimal)muscle));
    [Fact] public void MissingMetricDoesNotProduceFavorableOrBadOutcome() =>
        Assert.Equal("Partial evidence",RecompWeeklyTrend.Outcome(1,null));
    [Fact] public void BehaviorDaysEndBeforeMorningScaleAndMissingNutritionIsNotZeroFilled()
    {
        var foods = new[] {
            new NutritionDay {NutritionDate=Today.AddDays(-1),ActualCalories=2000,ActualProteinG=200,ActualCarbsG=150,ActualFatG=60},
            new NutritionDay {NutritionDate=Today,ActualCalories=100,ActualProteinG=1,ActualCarbsG=1,ActualFatG=1},
            new NutritionDay {NutritionDate=Today.AddDays(-2),ActualCalories=1000} };
        var result=RecompWeeklyTrend.Build(Today,Today.AddDays(-20),[Reading(-7),Reading(0,196,81)],foods,[],[],new Dictionary<DateOnly,RecompDayEvidence>(),new Dictionary<DateOnly,int?>());
        var point=result.Points.Last();
        Assert.Equal(Today.AddDays(-7),point.WeekStart); Assert.Equal(Today.AddDays(-1),point.WeekEnd);
        Assert.Equal(1,point.NutritionDays); Assert.Equal(2000m,point.Calories); Assert.Equal(200m,point.Protein);
        Assert.Equal(0,point.PrescriptionDays); Assert.Null(point.TargetCalories); Assert.Null(point.TreatItems);
        Assert.Equal(-1m,point.FatChangeLb); Assert.Equal(1m,point.MuscleChangeLb);
    }
    [Fact] public void HistoryHasAtMostFortyTwoDailyPointsAndKeepsWeeklyCardCalculation()
    {
        var result=RecompWeeklyTrend.Build(Today,Today.AddDays(-100),[Reading(-7),Reading(0,196,81)],[],[],[],new Dictionary<DateOnly,RecompDayEvidence>(),new Dictionary<DateOnly,int?>());
        Assert.Equal(42,result.Points.Count); Assert.Equal(Today.AddDays(-41),result.Points.First().Date);
        var card=RecompBodyCompositionComparison.Build(Today,[Reading(-7),Reading(0,196,81)]);
        Assert.Equal(card.Fat.DisplayChangeLb,result.Points.Last().FatChangeLb);
        Assert.Equal(card.Muscle.DisplayChangeLb,result.Points.Last().MuscleChangeLb);
    }
    [Fact] public void NewProgramDoesNotInventACompletedWeek()
    {
        Assert.Empty(RecompWeeklyTrend.Build(Today,Today.AddDays(-6),[],[],[],[],new Dictionary<DateOnly,RecompDayEvidence>(),new Dictionary<DateOnly,int?>()).Points);
    }
    [Fact] public void MultipleSessionsCountAsOneTrainingDayAndRpeUsesKnownSessionMinutes()
    {
        WorkoutSession Session(int offset,decimal minutes,decimal? rpe,bool completed=true) => new()
        {WorkoutTypeCode="MAJOR_PUSH",WorkoutDate=Today.AddDays(offset),DurationMin=minutes,SessionRpe=rpe,CompletedFlag=completed};
        var sessions=new[] {Session(-1,30,8),Session(-1,60,6),Session(-2,45,null),Session(0,90,10),Session(-3,20,10,false)};
        var target=new RecompDayEvidence(Today.AddDays(-1),200,150,60,1940,null,null,null,null,null,"Captured same-day prescription");
        var result=RecompWeeklyTrend.Build(Today,Today.AddDays(-10),[],[],sessions,[],new Dictionary<DateOnly,RecompDayEvidence>{{target.Date,target}},new Dictionary<DateOnly,int?>{{Today.AddDays(-1),2},{Today.AddDays(-2),0}}).Points.Last();
        Assert.Equal(2,result.WorkoutDays); Assert.Equal(135m,result.WorkoutMinutes); Assert.Equal(600m/90m,result.WorkoutRpe);
        Assert.Equal(1,result.PrescriptionDays); Assert.Equal(1940m,result.TargetCalories); Assert.Equal(2,result.TreatItems); Assert.Equal(2,result.TreatKnownDays);
    }
}
