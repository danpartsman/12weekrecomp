using System.Text.Json;
using WebRecomp.Web.Services.Coaching;
namespace WebRecomp.Web.Tests;
public sealed class AccountabilityAssessmentTests
{
    [Fact]
    public void RulesRequireCompleteReviewedEvidenceAndApplyPerPeriodCaps()
    {
        var s=new AccountabilityState{Enabled=true,Agreement=new(){Start="2026-10-26"},Rules=AccountabilityDocument.RuleIds.Select(id=>new AccountabilityRule{Id=id,Amount=2}).ToList()};
        var rule=s.Rules.Single(x=>x.Id=="cheat-foods");rule.Assigned=true;rule.Max=5;
        s.Observations["2026-10-26"]=new(){{"cheat-foods",2}};s.Observations["2026-10-27"]=new(){{"cheat-foods",3}};
        var days=Enumerable.Range(0,7).ToDictionary(i=>new DateOnly(2026,10,26).AddDays(i),i=>JsonSerializer.SerializeToElement(new{date=new DateOnly(2026,10,26).AddDays(i).ToString("yyyy-MM-dd"),completed=true}));
        var rows=AccountabilityAssessment.Rules(s,days,new DateOnly(2026,11,2));
        Assert.Equal(new decimal[]{4,5},rows.Select(x=>x.Amount));
        s.Excused["2026-10-26"]=true;Assert.Equal(0,AccountabilityAssessment.Rules(s,days,new DateOnly(2026,11,2))[0].Amount);
        rule.Frequency="monthly";Assert.Empty(AccountabilityAssessment.Rules(s,days,new DateOnly(2026,11,2)));
    }
    [Fact]
    public void DeltaDoesNotRepricePostedRulePeriodsUnderNewTerms()
    {
        var charge=new AccountabilityCharge("rule:protein:2026-10-26:2026-10-26",new(2026,10,26),"fine","Protein",5,5);
        var first=AccountabilityLedger.Delta([charge],[],1,Guid.NewGuid());Assert.Equal(5,Assert.Single(first).Amount);
        Assert.Empty(AccountabilityLedger.Delta([charge],first,1,Guid.NewGuid()));
        Assert.Empty(AccountabilityLedger.Delta([charge with{Amount=10,RawAmount=10}],first,2,Guid.NewGuid()));
        Assert.Equal(-5,Assert.Single(AccountabilityLedger.Delta([charge with{Amount=0,RawAmount=0}],first,1,Guid.NewGuid())).Amount);
    }
}
