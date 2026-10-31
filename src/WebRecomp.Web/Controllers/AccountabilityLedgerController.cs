using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
namespace WebRecomp.Web.Controllers;
public sealed record AccountabilityLedgerPage(Guid ClientId,string ClientName,bool CanRecord,int Revision,bool Approved,IReadOnlyList<AccountabilityLedgerEntry> Entries,DateOnly Today,string TimeZone,bool CanReviewAgreement=true)
{
    public bool ThirtyDays{get;init;}
    public Guid? CoachUserId{get;init;}
    public string CoachName{get;init;}="Coach";
    public bool ActiveCoach{get;init;}=true;
    public IReadOnlyList<CoachLedgerAccount> Accounts{get;init;}=[];
    public IReadOnlyList<AccountabilityLedgerHistoryRow> History
    {
        get
        {
            decimal balance=0;
            var rows=Entries.Select(entry=>{balance+=entry.Amount;return new AccountabilityLedgerHistoryRow(entry,balance);}).ToList();
            if(!ThirtyDays)return rows.TakeLast(5).ToList();
            TimeZoneInfo zone;try{zone=TimeZoneInfo.FindSystemTimeZoneById(TimeZone);}catch(TimeZoneNotFoundException){zone=TimeZoneInfo.Utc;}catch(InvalidTimeZoneException){zone=TimeZoneInfo.Utc;}
            return rows.Where(row=>{var date=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(row.Entry.CreatedAtUtc,DateTimeKind.Utc),zone));return date>=Today.AddDays(-29)&&date<=Today;}).ToList();
        }
    }
}
public sealed record AccountabilityLedgerHistoryRow(AccountabilityLedgerEntry Entry,decimal Balance);
[Authorize,Route("clients/{clientId:guid}/coach-ledger")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class AccountabilityLedgerController(AppUserContextService users,AccountabilityLedger ledger,CoachAccountabilityPreviewService access,IConfiguration configuration,SimpleCoachingService? simple=null):Controller
{
    private bool CoachRole=>User.IsInRole("Coach")||User.IsInRole("Administrator");
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid clientId,CancellationToken ct,bool thirtyDays=false,Guid? coachUserId=null)
    {
        if(!WebRecomp.Web.ViewModels.AccountabilityPreview.Enabled(configuration))return NotFound();
        var user=(await users.GetAsync(HttpContext,ct)).User;
        try
        {
            var row=await ledger.AccountAsync(user.UserId,clientId,CoachRole,ct);
            var owner=await access.IsOwnerAsync(user.UserId,clientId,ct);
            var active=await access.CanAssignSnapAsync(user.UserId,clientId,ct);
            // Former coaches have settlement access only; opening their history cannot create assessments.
            if(await access.CanViewAsync(user.UserId,clientId,CoachRole,ct))
            {
                if(SimpleCoaching.Enabled(configuration))await simple!.RefreshAsync(user.UserId,clientId,CoachRole,ct);
                if(row is not null)try{await ledger.RefreshAsync(user.UserId,row,user,ct);}
                catch(AccountabilityConflictException){TempData["LedgerStatus"]="Another window updated the account. Reload to see the latest automatic assessments.";return RedirectToAction(nameof(Index),new{clientId,coachUserId,thirtyDays});}
            }
            row=await ledger.AccountAsync(user.UserId,clientId,CoachRole,ct);
            var accounts=await ledger.AccountsAsync(user.UserId,clientId,CoachRole,ct);
            var selected=coachUserId.HasValue?accounts.FirstOrDefault(x=>x.CoachUserId==coachUserId):accounts.FirstOrDefault(x=>x.CoachUserId==user.UserId)??accounts.FirstOrDefault();
            if(coachUserId.HasValue&&selected is null)return NotFound();
            var entries=row is null||selected is null?[]:(await ledger.ReadAsync(row,ct)).Where(x=>x.CoachUserId==selected.CoachUserId).ToList();
            var clock=await ledger.ClockAsync(clientId,ct);
            var canRecord=CoachRole&&selected?.CoachUserId==user.UserId;
            return View(SimpleCoaching.Enabled(configuration)?"Simple":"Index",new AccountabilityLedgerPage(clientId,await access.ClientNameAsync(clientId,ct)??"Client",canRecord,row?.Revision??0,row is not null&&await ledger.ApprovedAsync(row,ct),entries,clock.Today,clock.Zone,
                (owner||active)&&row is not null&&selected?.CoachUserId==row.CoachUserId&&(owner||user.UserId==row.CoachUserId))
                {ThirtyDays=thirtyDays,CoachUserId=selected?.CoachUserId,CoachName=selected?.Name??"Coach",ActiveCoach=selected?.Active??false,Accounts=accounts});
        }
        catch(UnauthorizedAccessException){return NotFound();}
    }
    [HttpPost("entry"),ValidateAntiForgeryToken,EnableRateLimiting("access-code"),RequestSizeLimit(16384)]
    public async Task<IActionResult> Entry(Guid clientId,LedgerInput input,CancellationToken ct)
    {
        if(!WebRecomp.Web.ViewModels.AccountabilityPreview.Enabled(configuration)||!CoachRole)return NotFound();
        if(!ModelState.IsValid){TempData["LedgerStatus"]="Check the amount, date and description.";return RedirectToAction(nameof(Index),new{clientId});}
        var user=(await users.GetAsync(HttpContext,ct)).User;
        try
        {
            var expected=input.ExpectedRevision;
            var row=await ledger.AccountAsync(user.UserId,clientId,true,ct);
            if(row is null&&SimpleCoaching.Enabled(configuration)){row=await simple!.EnsureAsync(user.UserId,clientId,ct);if(expected==0)expected=row.Revision;}
            if(row is null)return NotFound();
            await ledger.RecordAsync(user.UserId,row,expected,input.RequestId,input.Kind,input.Date,input.Amount,input.Description,ct);
            TempData["LedgerStatus"]="Entry recorded. Both coach and client can see the updated balance.";
        }
        catch(UnauthorizedAccessException){return NotFound();}
        catch(Exception e)when(e is ArgumentException or AccountabilityConflictException){TempData["LedgerStatus"]=e.Message;}
        return RedirectToAction(nameof(Index),new{clientId});
    }
    [HttpPost("reverse"),ValidateAntiForgeryToken,EnableRateLimiting("access-code"),RequestSizeLimit(16384)]
    public async Task<IActionResult> Reverse(Guid clientId,int expectedRevision,Guid entryId,string reason,CancellationToken ct)
    {
        if(!WebRecomp.Web.ViewModels.AccountabilityPreview.Enabled(configuration)||!CoachRole)return NotFound();
        var user=(await users.GetAsync(HttpContext,ct)).User;
        try
        {
            var row=await ledger.AccountAsync(user.UserId,clientId,true,ct);
            if(row is null&&SimpleCoaching.Enabled(configuration))row=await simple!.EnsureAsync(user.UserId,clientId,ct);
            if(row is null)return NotFound();
            await ledger.ReverseAsync(user.UserId,row,expectedRevision,entryId,reason,ct);TempData["LedgerStatus"]="Correction recorded as a reversal; the original line is retained.";
        }
        catch(UnauthorizedAccessException){return NotFound();}
        catch(Exception e)when(e is ArgumentException or AccountabilityConflictException){TempData["LedgerStatus"]=e.Message;}
        return RedirectToAction(nameof(Index),new{clientId});
    }
    public sealed class LedgerInput
    {
        public Guid RequestId{get;set;}
        public int ExpectedRevision{get;set;}
        [Required]public string Kind{get;set;}="payment-client";
        public DateOnly Date{get;set;}
        public decimal Amount{get;set;}
        [Required,StringLength(300)]public string Description{get;set;}="";
    }
}
