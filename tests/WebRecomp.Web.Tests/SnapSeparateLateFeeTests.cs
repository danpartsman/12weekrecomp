using WebRecomp.Web.Services.Coaching;
namespace WebRecomp.Web.Tests;

public sealed class SnapSeparateLateFeeTests
{
    private static readonly DateOnly Day=new(2026,11,16);
    private static readonly DateTimeOffset Deadline=new(2026,11,16,12,0,0,TimeSpan.Zero);
    private static AccountabilityState State(decimal cap,int reps,bool late=true,bool finalized=true,bool separate=true)
        =>new(){Rules=AccountabilityDocument.RuleIds.Select(id=>new AccountabilityRule{Id=id}).ToList(),Agreement=new(){Start="2026-11-16"},
            SnapWorkouts=[new(){Id="separate-late",Simple=true,Name="Separate late fee",Date="2026-11-16",CreatedAtUtc=Deadline.AddHours(-2),DeadlineUtc=Deadline,LatePenalty=20,Cap=cap,MinimumCharge=0,
                Exercises=[new(){Id="e1",Name="Push ups",Sets=1,Reps=10,MaxWeight=0,Penalty=50,ExtraRepRate=1}]}],
            SnapResults=new(){["separate-late"]=new(){Finalized=finalized,LateFeeOutsideCap=separate,CompletedAtUtc=finalized?Deadline.AddMinutes(late?1:-1):null,
                Sets=new(){["e1"]=[new(){Reps=reps,Weight=0}]}}}};

    [Theory]
    [InlineData(30,50,50)] // Capped extra reps plus the full late fee.
    [InlineData(30,0,50)] // Capped incomplete-exercise fee plus the full late fee.
    [InlineData(0,50,20)] // A zero workout cap does not waive the late fee.
    [InlineData(5,50,25)] // The late fee may exceed the workout cap.
    public void LateFeeIsAddedOnTopOfCappedWorkoutCharges(decimal cap,int reps,decimal total)
    {
        var charges=SimpleCoachingService.Charges(State(cap,reps),Day,Deadline.AddHours(1));
        Assert.Equal(20,Assert.Single(charges,c=>c.Kind=="late-penalty").Amount);
        Assert.True(charges.Where(c=>c.Kind!="late-penalty").Sum(c=>c.Amount)<=cap);
        Assert.Equal(total,charges.Sum(c=>c.Amount));
    }
    [Fact] public void OnTimeMinimumAndMaximumStillApplyOnlyToWorkoutCharges()
    {
        var state=State(30,50,late:false);state.SnapWorkouts[0].MinimumCharge=10;
        Assert.Equal(30,SimpleCoachingService.Charges(state,Day,Deadline.AddHours(1)).Sum(c=>c.Amount));
        state.SnapResults["separate-late"].Sets["e1"][0].Reps=10;
        Assert.Equal(10,SimpleCoachingService.Charges(state,Day,Deadline.AddHours(1)).Sum(c=>c.Amount));
    }
    [Fact] public void UnfinishedLegacyWorkoutUsesFullLateFeeButCompletedHistoryKeepsOriginalCap()
    {
        Assert.Equal(20,SimpleCoachingService.Charges(State(5,0,finalized:false,separate:false),Day,Deadline.AddHours(1)).Sum(c=>c.Amount));
        var historical=State(30,50,separate:false);
        var charges=SimpleCoachingService.Charges(historical,Day,Deadline.AddHours(1));
        Assert.Equal(30,charges.Sum(c=>c.Amount));
        var posted=AccountabilityLedger.Delta(charges,[],1,Guid.NewGuid());
        Assert.Empty(AccountabilityLedger.Delta(SimpleCoachingService.Charges(historical,Day,Deadline.AddHours(2)),posted,1,Guid.NewGuid()));
    }
    [Fact] public void NewTotalsPostOnceAndCorrectionsKeepLateFeeSeparate()
    {
        var state=State(30,50);var actor=Guid.NewGuid();
        var charges=SimpleCoachingService.Charges(state,Day,Deadline.AddHours(1));
        var posted=AccountabilityLedger.Delta(charges,[],1,actor);
        Assert.Equal(50,posted.Sum(c=>c.Amount));Assert.Empty(AccountabilityLedger.Delta(charges,posted,1,actor));
        state.SnapResults["separate-late"].Sets["e1"][0].Reps=15;
        var corrected=SimpleCoachingService.Charges(state,Day,Deadline.AddHours(1));
        var delta=AccountabilityLedger.Delta(corrected,posted,1,actor);
        Assert.Equal(25,posted.Concat(delta).Sum(c=>c.Amount));
        Assert.DoesNotContain(delta,c=>c.SourceKey.EndsWith(":late"));
        Assert.True(AccountabilityDocument.Parse(AccountabilityDocument.Serialize(state)).SnapResults["separate-late"].LateFeeOutsideCap);
    }
}
