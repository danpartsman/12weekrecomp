using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Dashboard3;
namespace WebRecomp.Web.Tests;

public sealed class RecompWeeklyTrendTests
{
    private static readonly DateOnly Today = new(2026,10,13);
    private static Measurement Reading(int offset,decimal weight=200,decimal muscle=80) => new()
    { MeasurementId=Guid.NewGuid(),LocalMeasurementDate=Today.AddDays(offset),WeightLb=weight,BodyFatPct=.25m,SkeletalMuscleMassLb=muscle };
    [Theory]
    [InlineData(196,81,"Favorable","green")]
    [InlineData(196,80,"Favorable","green")]
    [InlineData(200,81,"Favorable","green")]
    [InlineData(204,81,"Mixed","yellow")]
    [InlineData(196,79,"Mixed","yellow")]
    [InlineData(204,80,"Needs review","red")]
    [InlineData(200,79,"Needs review","red")]
    [InlineData(200,80,"Stable","gray")]
    public void DotsCombineTheConfirmedFatAndMuscleDirections(int weight, int muscle, string status, string band)
    {
        var readings = Enumerable.Range(3, 20).Select(i => Reading(-i))
            .Concat([Reading(-2, weight, muscle), Reading(-1, weight, muscle)]);
        var point = RecompWeeklyTrend.Build(Today, Today.AddDays(-30), readings, [], [], [],
            new Dictionary<DateOnly, RecompDayEvidence>(), new Dictionary<DateOnly, int?>()).Points.Last();
        Assert.Equal(status, point.Status);
        Assert.Equal(band, point.Band);
    }

    [Fact]
    public void DotsStartYellowAndCanConfirmAcrossAMissedWeighIn()
    {
        var readings = Enumerable.Range(4, 20).Select(i => Reading(-i))
            .Concat([Reading(-3, 196, 81), Reading(-1, 196, 81)]);
        var points = RecompWeeklyTrend.Build(Today, Today.AddDays(-30), readings, [], [], [],
            new Dictionary<DateOnly, RecompDayEvidence>(), new Dictionary<DateOnly, int?>()).Points;
        Assert.Equal("yellow", points.Single(p => p.Date == Today.AddDays(-2)).Band);
        Assert.Equal("yellow", points.Single(p => p.Date == Today.AddDays(-1)).Band);
        Assert.Equal("green", points.Last().Band);
    }
    [Fact] public void BehaviorDaysEndBeforeMorningScaleAndMissingNutritionIsNotZeroFilled()
    {
        var foods = new[] {
            new NutritionDay {NutritionDate=Today.AddDays(-1),ActualCalories=2000,ActualProteinG=200,ActualCarbsG=150,ActualFatG=60},
            new NutritionDay {NutritionDate=Today,ActualCalories=100,ActualProteinG=1,ActualCarbsG=1,ActualFatG=1},
            new NutritionDay {NutritionDate=Today.AddDays(-2),ActualCalories=1000} };
        var result=RecompWeeklyTrend.Build(Today,Today.AddDays(-20),[Reading(-8),Reading(-1,196,81)],foods,[],[],new Dictionary<DateOnly,RecompDayEvidence>(),new Dictionary<DateOnly,int?>());
        var point=result.Points.Last();
        Assert.Equal(Today.AddDays(-7),point.WeekStart); Assert.Equal(Today.AddDays(-1),point.WeekEnd);
        Assert.Equal(1,point.NutritionDays); Assert.Equal(2000m,point.Calories); Assert.Equal(200m,point.Protein);
        Assert.Equal(0,point.PrescriptionDays); Assert.Null(point.TargetCalories); Assert.Null(point.TreatItems);
        Assert.Equal(-1m,point.FatChangeLb); Assert.Equal(1m,point.MuscleChangeLb);
        Assert.Equal("gray", point.Band);
    }
    [Fact] public void HistoryHasAtMostFortyTwoDailyPointsAndKeepsWeeklyCardCalculation()
    {
        var result=RecompWeeklyTrend.Build(Today,Today.AddDays(-100),[Reading(-8),Reading(-1,196,81)],[],[],[],new Dictionary<DateOnly,RecompDayEvidence>(),new Dictionary<DateOnly,int?>());
        Assert.Equal(42,result.Points.Count); Assert.Equal(Today.AddDays(-41),result.Points.First().Date);
        var card=RecompBodyCompositionComparison.Build(Today,[Reading(-8),Reading(-1,196,81)]);
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
