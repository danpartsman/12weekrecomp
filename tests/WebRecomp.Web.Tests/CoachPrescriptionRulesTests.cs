using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Tests;
public sealed class CoachPrescriptionRulesTests
{
    private static readonly DateOnly Week=new(2026,10,19);
    private static Engine30DashboardDayProjection Day(DateOnly date) => new(date,false,"REST","Rest",Engine30PrescriptionCategoryCodes.RestRecoveryOther,
        0,null,1800,0,0,0,2000,1820,0,1820,180,140,60,100,100,0,"ENGINE",0,0,"3000.0","CFG",null,null);
    private static Engine30DashboardDayProjection[] Days() => Enumerable.Range(0,7).Select(i=>Day(Week.AddDays(i))).ToArray();
    private static CoachPrescriptionRules Adjusting() => new() {CarbsMin=100,CarbsMax=250,FatMin=50,FatMax=70,AdjustCarbsThenFat=true,Reason="Reviewed"};
    [Fact]
    public void CalorieMinimumDoesNotAutomaticallyFillMacros()
    {
        var result=CoachPrescriptionAllocator.Evaluate(Days(),new() {DailyCaloriesMin=2000,Reason="Test"});
        Assert.NotEmpty(result.Errors);
        Assert.All(result.Days,d=>{Assert.Equal(140m,d.RecommendedCarbsG);Assert.Equal(60m,d.FatG);Assert.Equal(1820m,d.PrescribedEnergyKcal);});
    }
    [Fact]
    public void ExplicitDailyAdjustmentPreservesProteinAndUsesCarbsFirst()
    {
        var rules=Adjusting();rules.DailyCaloriesMin=2000;rules.DailyCaloriesMax=2000;
        var result=CoachPrescriptionAllocator.Evaluate(Days(),rules);
        Assert.Empty(result.Errors);
        Assert.All(result.Days,d=>{Assert.Equal(180m,d.ProteinG);Assert.Equal(185m,d.RecommendedCarbsG);Assert.Equal(60m,d.FatG);Assert.Equal(2000m,d.PrescribedEnergyKcal);});
    }
    [Fact]
    public void WeeklyAllocationRespectsDailyAndMacroLimitsAndExactMacroCalories()
    {
        var rules=Adjusting();rules.WeeklyCaloriesMax=12800;rules.WeeklyCaloriesMin=12800;rules.DailyCaloriesMin=1700;rules.DailyCaloriesMax=2200;
        var days=Days().Select((d,i)=>d with {RecommendedCarbsG=140+i*10,PrescribedEnergyKcal=1820+i*40}).ToArray();
        var result=CoachPrescriptionAllocator.Evaluate(days,rules);
        Assert.Empty(result.Errors);Assert.InRange(Math.Abs(result.WeeklyCalories-12800),0,0.01m);
        Assert.All(result.Days,d=>{Assert.InRange(d.PrescribedEnergyKcal,1700,2200);Assert.InRange(d.RecommendedCarbsG,100,250);Assert.InRange(d.FatG,50,70);
            Assert.Equal(180m,d.ProteinG);Assert.Equal(d.ProteinG*4+d.RecommendedCarbsG*4+d.FatG*9,d.PrescribedEnergyKcal);});
        Assert.True(result.Days.Last().RecommendedCarbsG>result.Days.First().RecommendedCarbsG);
    }
    [Fact]
    public void InfeasibleWeeklyRangeIsReportedInsteadOfInventingCalories()
    {
        var rules=Adjusting();rules.CarbsMax=140;rules.FatMax=60;rules.WeeklyCaloriesMin=20000;
        var result=CoachPrescriptionAllocator.Evaluate(Days(),rules);
        Assert.NotEmpty(result.Errors);Assert.Equal(12740m,result.WeeklyCalories);
    }
    [Fact]
    public void ProteinAndFatBoundsAreAppliedAndDisabledRulesPreserveEngine()
    {
        var rules=new CoachPrescriptionRules {ProteinMin=190,ProteinMax=200,FatMax=55,Reason="Test"};
        var result=CoachPrescriptionAllocator.Evaluate(Days(),rules);
        Assert.Empty(result.Errors);Assert.All(result.Days,d=>{Assert.Equal(190m,d.ProteinG);Assert.Equal(55m,d.FatG);});
        rules.Enabled=false;Assert.Equal(Days(),CoachPrescriptionAllocator.Evaluate(Days(),rules).Days);
    }
    [Fact]
    public async Task ScheduledRulesRemainScopedAndRevocationRemovesThem()
    {
        using var db=new RecompDbContext(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var client=Guid.NewGuid();var program=Guid.NewGuid();var coach=Guid.NewGuid();
        var link=new CoachClientRelationship {CoachClientRelationshipId=Guid.NewGuid(),CoachUserId=coach,ClientId=client,StatusCode="ACTIVE"};
        var rules=new CoachPrescriptionRules {ProteinMin=190,Reason="Test"};
        db.AddRange(link,new CoachPrescriptionRuleVersion {CoachPrescriptionRuleVersionId=Guid.NewGuid(),ClientId=client,ProgramId=program,CoachUserId=coach,
            EffectiveWeek=Week,IsEnabled=true,RulesJson=JsonSerializer.Serialize(rules),Reason="Test",PreviewJson="{}",CreatedAtUtc=DateTime.UtcNow});await db.SaveChangesAsync();
        var store=new CoachPrescriptionRuleStore(db);var baseline=Days().ToDictionary(x=>x.Date);
        Assert.Same(baseline,await store.ApplyAsync(Guid.NewGuid(),program,Week,baseline,default));
        Assert.Same(baseline,await store.ApplyAsync(client,Guid.NewGuid(),Week,baseline,default));
        Assert.Same(baseline,await store.ApplyAsync(client,program,Week.AddDays(-7),baseline,default));
        Assert.All((await store.ApplyAsync(client,program,Week,baseline,default)).Values,d=>Assert.Equal(190m,d.ProteinG));
        link.StatusCode="REVOKED";link.RevokedAtUtc=DateTime.UtcNow;await db.SaveChangesAsync();
        Assert.Same(baseline,await store.ApplyAsync(client,program,Week,baseline,default));
    }
    [Fact]
    public async Task RuntimeConflictIsVisibleAndKeepsTheEnginePrescription()
    {
        using var db=new RecompDbContext(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var client=Guid.NewGuid();var program=Guid.NewGuid();var coach=Guid.NewGuid();
        db.AddRange(new CoachClientRelationship {CoachClientRelationshipId=Guid.NewGuid(),CoachUserId=coach,ClientId=client,StatusCode="ACTIVE"},
            new CoachPrescriptionRuleVersion {CoachPrescriptionRuleVersionId=Guid.NewGuid(),ClientId=client,ProgramId=program,CoachUserId=coach,EffectiveWeek=Week,IsEnabled=true,
                RulesJson=JsonSerializer.Serialize(new CoachPrescriptionRules {DailyCaloriesMin=2000,Reason="Test"}),Reason="Test",PreviewJson="{}",CreatedAtUtc=DateTime.UtcNow});await db.SaveChangesAsync();
        var result=await new CoachPrescriptionRuleStore(db).ApplyAsync(client,program,Week,Days().ToDictionary(x=>x.Date),default);
        Assert.All(result.Values,d=>{Assert.Equal(1820m,d.PrescribedEnergyKcal);Assert.Contains("need review",d.CoachPrescriptionNotice);});
    }
}
