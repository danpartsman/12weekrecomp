using WebRecomp.Web.Services.Coaching;
namespace WebRecomp.Web.Tests;

public sealed class SnapMinimumChargeTests
{
    private static readonly DateOnly Day=new(2026,11,13);
    private static readonly DateTimeOffset Deadline=new(2026,11,13,12,0,0,TimeSpan.Zero);
    private static AccountabilityState State(int reps=10,bool finalized=true,int finishMinutes=0,decimal? minimum=10)
        =>new(){Rules=AccountabilityDocument.RuleIds.Select(id=>new AccountabilityRule{Id=id}).ToList(),Agreement=new(){Start="2026-11-13"},
            SnapWorkouts=[new(){Id="snap-test",Simple=true,Name="Minimum example",Date="2026-11-13",CreatedAtUtc=Deadline.AddHours(-2),DeadlineUtc=Deadline,LatePenalty=20,Cap=30,MinimumCharge=minimum,
                Exercises=[new(){Id="exercise-test",Name="Push ups",Sets=1,Reps=10,MaxWeight=0,Penalty=5,ExtraRepRate=1}]}],
            SnapResults=new(){["snap-test"]=new(){Finalized=finalized,CompletedAtUtc=finalized?Deadline.AddMinutes(finishMinutes):null,
                Sets=new(){["exercise-test"]=[new(){Reps=reps,Weight=0}]}}}};

    [Theory]
    [InlineData(10,10)]
    [InlineData(15,10)]
    [InlineData(20,10)]
    [InlineData(25,15)]
    [InlineData(50,30)]
    public void OnTimeMinimumIncludesExtraRepIncentivesAndRespectsMaximum(int reps,decimal expected)
    {
        var charges=SimpleCoachingService.Charges(State(reps),Day,Deadline.AddHours(1));
        Assert.Equal(expected,charges.Sum(c=>c.Amount));
        Assert.All(charges,c=>Assert.True(c.Amount>=0));
        Assert.Contains(charges,c=>c.Key.EndsWith(":minimum")&&c.Description.Contains("minimum total $10.00"));
    }

    [Fact]
    public void MinimumRequiresCompletePrescribedWorkAndTimelyFinalSubmission()
    {
        Assert.Equal(0,SimpleCoachingService.Charges(State(finalized:false),Day,Deadline.AddMinutes(-1)).Sum(c=>c.Amount));
        Assert.Equal(5,SimpleCoachingService.Charges(State(reps:9),Day,Deadline.AddMinutes(1)).Sum(c=>c.Amount));
        Assert.Equal(20,SimpleCoachingService.Charges(State(finishMinutes:1),Day,Deadline.AddMinutes(2)).Sum(c=>c.Amount));
        Assert.Equal(10,SimpleCoachingService.Charges(State(),Day,Deadline).Sum(c=>c.Amount));
        var invalidWeight=State();invalidWeight.SnapWorkouts[0].Exercises[0].MaxWeight=35;
        invalidWeight.SnapResults["snap-test"].Sets["exercise-test"][0].Weight=40;
        Assert.Equal(5,SimpleCoachingService.Charges(invalidWeight,Day,Deadline).Sum(c=>c.Amount));
    }

    [Fact]
    public void CoachCanChangeOrDisableMinimumAndLegacyAssignmentsKeepTheirAmounts()
    {
        Assert.Equal(22,SimpleCoachingService.Charges(State(15,minimum:22),Day,Deadline).Sum(c=>c.Amount));
        Assert.Equal(0,SimpleCoachingService.Charges(State(minimum:0),Day,Deadline).Sum(c=>c.Amount));
        var old=State(minimum:null);var json=AccountabilityDocument.Serialize(old);
        Assert.DoesNotContain("minimumCharge",json);
        Assert.Null(AccountabilityDocument.Parse(json).SnapWorkouts[0].MinimumCharge);
        Assert.Equal(0,SimpleCoachingService.Charges(old,Day,Deadline).Sum(c=>c.Amount));
        Assert.Equal(5,SimpleCoachingService.Charges(State(15,minimum:null),Day,Deadline).Sum(c=>c.Amount));
    }

    [Fact]
    public void MinimumPostsOnceAndCorrectionsReverseTheTopUp()
    {
        var actor=Guid.NewGuid();var state=State();
        var charges=SimpleCoachingService.Charges(state,Day,Deadline);
        var posted=AccountabilityLedger.Delta(charges,[],1,actor);
        Assert.Equal(10,posted.Sum(e=>e.Amount));
        Assert.Empty(AccountabilityLedger.Delta(charges,posted,1,actor));
        state.SnapResults["snap-test"].Sets["exercise-test"][0].Reps=25;
        var more=AccountabilityLedger.Delta(SimpleCoachingService.Charges(state,Day,Deadline),posted,1,actor);
        var combined=posted.Concat(more).ToList();Assert.Equal(15,combined.Sum(e=>e.Amount));
        Assert.Contains(more,e=>e.SourceKey.EndsWith(":minimum")&&e.Amount==-10);
        state.SnapResults["snap-test"].CompletedAtUtc=Deadline.AddMinutes(1);
        var late=AccountabilityLedger.Delta(SimpleCoachingService.Charges(state,Day,Deadline.AddMinutes(2)),combined,1,actor);
        Assert.Equal(30,combined.Concat(late).Sum(e=>e.Amount));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(31)]
    [InlineData(0.001)]
    public void InvalidMinimumCannotBeSaved(decimal minimum)
    {
        var state=State(minimum:minimum);
        Assert.Contains("Minimum coach charge",Assert.Throws<ArgumentException>(()=>AccountabilityDocument.Validate(state)).Message);
    }
}
