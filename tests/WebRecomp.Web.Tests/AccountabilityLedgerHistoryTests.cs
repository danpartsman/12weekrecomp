using WebRecomp.Web.Controllers;
using WebRecomp.Web.Services.Coaching;
using Xunit;
namespace WebRecomp.Web.Tests;
public sealed class AccountabilityLedgerHistoryTests
{
    [Fact]
    public void FiveEntryPreviewPreservesFullHistoryBalancesIncludingPayments()
    {
        var today=new DateOnly(2026,10,30);
        var entries=Enumerable.Range(0,8).Select(i=>new AccountabilityLedgerEntry{Description=$"Entry {i}",Amount=i==7?-20:10,CreatedAtUtc=new DateTime(2026,10,30,12,0,i,DateTimeKind.Utc)}).ToArray();
        var page=new AccountabilityLedgerPage(Guid.NewGuid(),"Client",true,1,true,entries,today,"America/Los_Angeles");
        Assert.Equal(5,page.History.Count);Assert.Equal("Entry 3",page.History[0].Entry.Description);Assert.Equal(40,page.History[0].Balance);Assert.Equal(50,page.History[^1].Balance);Assert.Equal(50,page.Entries.Sum(e=>e.Amount));
    }
    [Fact]
    public void ThirtyDaysUsesLocalPostingDateAndIncludesOpeningBalance()
    {
        var today=new DateOnly(2026,10,30);
        var entries=new[]{
            new AccountabilityLedgerEntry{Amount=100,CreatedAtUtc=new DateTime(2026,9,1,12,0,0,DateTimeKind.Utc)},
            new AccountabilityLedgerEntry{Amount=10,CreatedAtUtc=new DateTime(2026,10,1,6,59,0,DateTimeKind.Utc)},
            new AccountabilityLedgerEntry{Amount=20,Date=new DateOnly(2026,9,1),CreatedAtUtc=new DateTime(2026,10,1,7,0,0,DateTimeKind.Utc)},
            new AccountabilityLedgerEntry{Amount=-30,CreatedAtUtc=new DateTime(2026,10,31,6,59,0,DateTimeKind.Utc)},
            new AccountabilityLedgerEntry{Amount=40,CreatedAtUtc=new DateTime(2026,10,31,7,0,0,DateTimeKind.Utc)}};
        var page=new AccountabilityLedgerPage(Guid.NewGuid(),"Client",false,1,true,entries,today,"America/Los_Angeles"){ThirtyDays=true};
        Assert.Equal(2,page.History.Count);Assert.Equal(130,page.History[0].Balance);Assert.Equal(100,page.History[1].Balance);Assert.Equal(140,page.Entries.Sum(e=>e.Amount));
    }
}
