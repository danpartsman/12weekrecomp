using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services.SmartIntelligence;
namespace WebRecomp.Web.Tests;

public sealed class SmartIntelligenceTests
{
    private static DateOnly Start=>new(2026,1,1);
    private static (DateOnly Date,double? Value)[] Values(int n,Func<int,double?> f)=>Enumerable.Range(0,n).Select(i=>(Start.AddDays(i),f(i))).ToArray();
    [Fact] public void IsolatedSpikeIsDownweightedAndDoesNotBecomeTissueGain()
    {
        var fit=RobustStateEstimator.Fit("fat","Fat",Values(70,i=>i==55?65:55),.8,"balanced");
        Assert.True(fit.Points[55].Downweighted);
        Assert.InRange(fit.Points[55].Estimate!.Value,54.5,55.6);
        Assert.InRange(fit.Points[69].Estimate!.Value,54.5,55.6);
    }
    [Fact] public void MissingWeekPredictsWithoutZeroFillingAndWidensUncertainty()
    {
        var fit=RobustStateEstimator.Fit("muscle","Muscle",Values(70,i=>i is >=50 and <57?null:89),.5,"balanced");
        Assert.Null(fit.Points[56].Raw);Assert.Equal(7,fit.Points[56].DaysSinceReading);
        Assert.InRange(fit.Points[56].Estimate!.Value,88.9,89.1);
        Assert.True(fit.Points[56].Upper-fit.Points[56].Lower>fit.Points[49].Upper-fit.Points[49].Lower);
    }
    [Fact] public void SustainedDirectionAndReversalAreAllowed()
    {
        var fit=RobustStateEstimator.Fit("fat","Fat",Values(140,i=>i<70?55+i*.03:57.1-(i-70)*.05),.8,"balanced");
        Assert.True(fit.Points[60].WeeklySlope>0);
        Assert.True(fit.Points[139].WeeklySlope<0);
        Assert.True(fit.Points[139].Estimate<fit.Points[90].Estimate);
    }
    [Fact] public void FutureReadingsCannotRewriteEarlierCausalEstimate()
    {
        var shortFit=RobustStateEstimator.Fit("fat","Fat",Values(50,i=>55+Math.Sin(i)*.6),.8,"balanced");
        var longFit=RobustStateEstimator.Fit("fat","Fat",Values(70,i=>i<50?55+Math.Sin(i)*.6:90),.8,"balanced");
        Assert.Equal(shortFit.Points[49].Estimate,longFit.Points[49].Estimate);
    }
    [Fact] public void MissingIsNotZeroInSevenDayComparisonAndNoVariationIsNotZeroCorrelation()
    {
        var fit=RobustStateEstimator.Fit("fat","Fat",Values(30,i=>i%7==0?null:55),.8,"balanced");
        Assert.Equal(55d,fit.Points[29].Average7);Assert.Equal(0d,fit.Points[29].Change7);
        Assert.Null(IntelligenceStatistics.Pearson(Enumerable.Range(0,20).Select(i=>(1d,(double)i)).ToArray()));
        Assert.Null(IntelligenceStatistics.Pearson(Enumerable.Range(0,7).Select(i=>((double)i,(double)i)).ToArray()));
    }
    [Fact] public void CorrelationsRespectDateLagsNotRowOffsetsAcrossGaps()
    {
        var days=Enumerable.Range(0,30).Where(i=>i!=12).Select(i=>new IntelligenceDay{Date=Start.AddDays(i),CompletedDay=true,Carbs=i%4*30+100,Weight=210+i%3}).ToArray();
        var results=IntelligenceStatistics.Correlations(days);
        var test=results.Single(x=>x.Input=="Carbs"&&x.Outcome=="Weight change"&&x.LagDays==1);
        Assert.Equal(27,test.Pairs);
    }
    [Fact] public async Task DataSourceIsClientScopedLatestDailyReadOnlyAndKeepsUnknowns()
    {
        using var db=new RecompDbContext(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var client=Guid.NewGuid();var program=Guid.NewGuid();var today=Start.AddDays(40);
        db.Measurements.AddRange(new(){MeasurementId=Guid.NewGuid(),ClientId=client,LocalMeasurementDate=today.AddDays(-1),MeasurementAtUtc=new(2026,2,9,7,0,0),WeightLb=210,BodyFatPct=.25m,SkeletalMuscleMassLb=89},
            new(){MeasurementId=Guid.NewGuid(),ClientId=client,LocalMeasurementDate=today.AddDays(-1),MeasurementAtUtc=new(2026,2,9,8,0,0),WeightLb=211,BodyFatPct=.25m,SkeletalMuscleMassLb=90},
            new(){MeasurementId=Guid.NewGuid(),ClientId=Guid.NewGuid(),LocalMeasurementDate=today,WeightLb=999},
            new(){MeasurementId=Guid.NewGuid(),ClientId=client,LocalMeasurementDate=today,WeightLb=888,IsDeleted=true});
        db.NutritionDays.Add(new(){NutritionDayId=Guid.NewGuid(),ClientId=client,ProgramId=program,NutritionDate=today.AddDays(-1),ActualCalories=772,ActualFatG=25});
        db.WorkoutSessions.AddRange(new(){WorkoutSessionId=Guid.NewGuid(),ClientId=client,ProgramId=program,WorkoutDate=today.AddDays(-1),WorkoutTypeCode="PUSH",DurationMin=30,SessionRpe=8,CompletedFlag=true},
            new(){WorkoutSessionId=Guid.NewGuid(),ClientId=client,ProgramId=program,WorkoutDate=today.AddDays(-1),WorkoutTypeCode="PULL",DurationMin=60,SessionRpe=8,CompletedFlag=false});
        await db.SaveChangesAsync();
        var report=await new IntelligenceDataSource(db).LoadAsync(client,today,42,"balanced",default);
        Assert.Equal(42,report.Days.Count);Assert.Null(report.Days[^1].Weight);Assert.Null(report.Days[^1].Calories);
        Assert.Equal(211d,report.Days[^2].Weight);Assert.Equal(52.75d,report.Days[^2].Fat);
        Assert.True(report.Days[^2].PossiblePartialFoodDay);Assert.Equal(30d,report.Days[^2].TrainingMinutes);
        Assert.Null(report.Days[^2].TargetCalories);Assert.All(db.ChangeTracker.Entries(),x=>Assert.Equal(EntityState.Unchanged,x.State));
    }
}
