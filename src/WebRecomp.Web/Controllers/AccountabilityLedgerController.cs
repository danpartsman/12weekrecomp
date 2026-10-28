using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
namespace WebRecomp.Web.Controllers;
public sealed record AccountabilityLedgerPage(Guid ClientId,string ClientName,bool CanRecord,int Revision,bool Approved,IReadOnlyList<AccountabilityLedgerEntry> Entries,DateOnly Today,string TimeZone);
[Authorize,Route("clients/{clientId:guid}/coach-ledger")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class AccountabilityLedgerController(AppUserContextService users,CoachAccountabilityStore store,AccountabilityLedger ledger,CoachAccountabilityPreviewService access,IConfiguration configuration):Controller
{
    private bool CoachRole=>User.IsInRole("Coach")||User.IsInRole("Administrator");
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid clientId,CancellationToken ct)
    {
        if(!WebRecomp.Web.ViewModels.AccountabilityPreview.Enabled(configuration))return NotFound();
        var user=(await users.GetAsync(HttpContext,ct)).User;
        try
        {
            var row=await store.ReadAsync(user.UserId,clientId,CoachRole,ct);
            var name=await access.ClientNameAsync(clientId,ct)??"Client";
            var clock=await ledger.ClockAsync(clientId,ct);
            if(row is null)return View(new AccountabilityLedgerPage(clientId,name,false,0,false,[],clock.Today,clock.Zone));
            try{await ledger.RefreshAsync(user.UserId,row,user,ct);}
            catch(AccountabilityConflictException){TempData["LedgerStatus"]="Another window updated the account. Reload to see the latest automatic assessments.";return RedirectToAction(nameof(Index),new{clientId});}
            return View(new AccountabilityLedgerPage(clientId,name,CoachRole&&user.UserId==row.CoachUserId,row.Revision,await ledger.ApprovedAsync(row,ct),await ledger.ReadAsync(row,ct),clock.Today,clock.Zone));
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
            var row=await store.ReadAsync(user.UserId,clientId,true,ct);if(row is null)return NotFound();
            await ledger.RecordAsync(user.UserId,row,input.ExpectedRevision,input.RequestId,input.Kind,input.Date,input.Amount,input.Description,ct);
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
            var row=await store.ReadAsync(user.UserId,clientId,true,ct);if(row is null)return NotFound();
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
