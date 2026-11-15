using WebRecomp.Web.Services.Engine30;
namespace WebRecomp.Web.Tests;
public sealed class Engine30RollingGlycogenTests
{
    private static readonly DateOnly Today=new(2026,11,15);
    private static RollingGlycogenDay[] Days(decimal? carbs, decimal demand=0) => Enumerable.Range(0,4)
        .Select(i=>new RollingGlycogenDay(Today.AddDays(-4+i),demand,100,carbs)).ToArray();
    private static RollingGlycogenEstimate Model(RollingGlycogenDay[] days,decimal next=0) =>
        Engine30RollingGlycogen.Calculate(150,days,next,160,100,2000,208,55);
    [Fact] public void RestDaysCanRefillStoresAndReduceLaterRecommendation()
    {
        var a=Model(Days(100)); var b=Model(Days(300));
        Assert.True(b.LowG>a.LowG); Assert.True(b.CarbAdjustmentG<0);
        Assert.InRange(b.CarbAdjustmentG,-40,0); Assert.InRange(b.HighG,0,b.CapacityG);
    }
    [Fact] public void DepletionCanRaiseCarbsButCannotCreateUnlimitedDebt()
    {
        var a=Model(Days(50,80),100);
        Assert.True(a.CarbAdjustmentG>0); Assert.InRange(a.CarbAdjustmentG,0,40);
        Assert.InRange(a.LowG,0,a.CapacityG); Assert.True(a.LowG<=a.HighG);
    }
    [Fact] public void MissingFoodPausesAdjustmentInsteadOfAssumingStarvation()
    {
        var days=Days(50,80); days[2]=days[2] with {CarbsG=null};
        var a=Model(days); Assert.Equal(3,a.EvidenceDays); Assert.Equal(0,a.CarbAdjustmentG);
        Assert.Contains("Incomplete",a.Confidence);
    }
    [Fact] public void UnrecordedTrainingDoesNotBecomeConfirmedRest()
    {
        var days=Days(50); days[0]=days[0] with {HasTrainingEvidence=false};
        var a=Model(days); Assert.Equal(0,a.CarbAdjustmentG); Assert.Equal(3,a.EvidenceDays);
    }
    [Fact] public void MoreUpcomingDemandDoesNotLowerTheFuelReserve()
    {
        var a=Model(Days(140)); var b=Model(Days(140),100);
        Assert.True(b.TargetLowG>=a.TargetLowG); Assert.True(b.CarbAdjustmentG>=a.CarbAdjustmentG);
    }
    [Fact] public void SavedEvidenceKeepsAgreementBaselineSeparateFromFuelReference()
    {
        var day = new WebRecomp.Web.Services.Dashboard3.RecompDayEvidence(Today,208,180,55,2000,180,170,60,1940,null,"test")
            {AgreementCarbsTarget=160};
        var copy=System.Text.Json.JsonSerializer.Deserialize<WebRecomp.Web.Services.Dashboard3.RecompDayEvidence>(System.Text.Json.JsonSerializer.Serialize(day))!;
        Assert.Equal(180,copy.CarbsTarget); Assert.Equal(160,copy.AgreementCarbsTarget);
    }
    [Fact] public void DuplicateOrGappedDatesAreRejected()
    {
        var days=Days(140);days[1]=days[0]; Assert.Throws<ArgumentException>(()=>Model(days));
    }
    [Fact] public void ModelExposesAssumptionsRatherThanClaimingMeasuredStores()
    {
        var a=Model(Days(140)); Assert.Contains("not a glycogen measurement",a.Method);
        Assert.Contains("Fat is not credited",a.Method); Assert.Contains("Low",a.Confidence);
    }
    [Fact] public void RecommendationDoesNotExceedEnergyHeadroomOrCarbFloor()
    {
        var a=Engine30RollingGlycogen.Calculate(150,Days(0,100),100,160,150,2000,208,55);
        Assert.InRange(160+a.CarbAdjustmentG,150,(2000-208m*.9m*4-55*9)/4);
    }
}
