using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Notifications;

namespace WebRecomp.Web.Controllers;

public sealed record CoachConversationItem(Guid Id, DateTime CreatedUtc, Guid AuthorId, string AuthorName, string Kind, string? WorkoutId, string Body);
public sealed record CoachActivityItem(DateTime CreatedUtc, string Description);
public sealed record CoachMacroOffer(Guid VersionId, DateOnly EffectiveWeek, string Reason, string? EngineJson, string? AdjustedJson, string RulesJson, string Fingerprint, bool Accepted, DateTime? AcceptedUtc, bool FinancialOptIn);
public sealed record CoachMacroAcceptance(Guid VersionId, Guid ClientId, Guid CoachId, Guid OwnerId, string Fingerprint, DateTime AcceptedUtc);
public sealed record CoachWorkspacePage(Guid ClientId, Guid CoachId, string CoachName, bool IsCoach, IReadOnlyList<CoachConversationItem> Messages, IReadOnlyList<CoachActivityItem> Activity, IReadOnlyList<(string Id,string Name)> Workouts, IReadOnlyList<CoachMacroOffer> MacroOffers);

[Authorize, Route("coach-workspace"), ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CoachWorkspaceController(RecompDbContext db, AppUserContextService users, NotificationFiles files, IEmailService email) : Controller
{
    private string Key(Guid client,Guid coach) => $"conversations/{client:N}-{coach:N}";
    private async Task<(Guid Actor,bool IsCoach,Guid Owner)?> Allowed(Guid client,Guid coach,CancellationToken ct)
    {
        var user=(await users.GetAsync(HttpContext,ct)).User;
        var active=await db.CoachClientRelationships.AsNoTracking().AnyAsync(x=>x.ClientId==client&&x.CoachUserId==coach&&x.StatusCode=="ACTIVE"&&x.RevokedAtUtc==null,ct);
        if(!active)return null;
        var owner=await db.UserClientAccess.AsNoTracking().Where(x=>x.ClientId==client&&x.AccessRoleCode=="OWNER"&&x.RevokedAtUtc==null).Select(x=>x.UserId).Distinct().Take(2).ToListAsync(ct);
        if(owner.Count!=1)return null;
        if(user.UserId!=coach&&user.UserId!=owner[0])return null;
        return (user.UserId,user.UserId==coach,owner[0]);
    }
    private async Task<CoachWorkspacePage> Build(Guid client,Guid coach,bool isCoach,CancellationToken ct)
    {
        var coachName=await db.UserAccounts.AsNoTracking().Where(x=>x.UserId==coach).Select(x=>x.PreferredDisplayName??x.DisplayName??"Coach").SingleAsync(ct);
        var messages=files.Read<List<CoachConversationItem>>(Key(client,coach))??[];
        var events=await db.CoachAccountabilityEvents.AsNoTracking().Where(x=>x.ClientId==client&&x.ActorUserId==coach).OrderByDescending(x=>x.CreatedAtUtc).Take(40).Select(x=>new{x.CreatedAtUtc,x.Kind}).ToListAsync(ct);
        var activity=events.Select(x=>new CoachActivityItem(x.CreatedAtUtc,x.Kind=="CONFIG"?"Coaching plan or Snap Workout updated":x.Kind=="RESULT"?"Workout results or ledger updated":"Coaching activity: "+x.Kind)).ToList();
        var ownerId=await db.UserClientAccess.AsNoTracking().Where(x=>x.ClientId==client&&x.AccessRoleCode=="OWNER"&&x.RevokedAtUtc==null).Select(x=>x.UserId).SingleAsync(ct);
        var versions=await db.CoachPrescriptionRuleVersions.AsNoTracking().Where(x=>x.ClientId==client&&x.CoachUserId==coach).OrderByDescending(x=>x.CreatedAtUtc).Take(20).ToListAsync(ct);
        var offers=new List<CoachMacroOffer>();
        foreach(var version in versions)
        {
            var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version.CoachPrescriptionRuleVersionId+"|"+version.RulesJson+"|"+version.PreviewJson)));
            var receipt=files.Read<CoachMacroAcceptance>($"macro-acceptance/{client:N}-{coach:N}-{version.CoachPrescriptionRuleVersionId:N}");
            string? engineJson=null,adjustedJson=null;bool financialOptIn=false;
            try {using var preview=JsonDocument.Parse(version.PreviewJson);if(preview.RootElement.TryGetProperty("Engine",out var engine))engineJson=engine.GetRawText();if(preview.RootElement.TryGetProperty("Adjusted",out var adjusted))adjustedJson=adjusted.GetRawText();if(preview.RootElement.TryGetProperty("ApplyPenaltiesAndIncentives",out var financial))financialOptIn=financial.ValueKind==JsonValueKind.True;}catch(JsonException){}
            offers.Add(new(version.CoachPrescriptionRuleVersionId,version.EffectiveWeek,version.Reason,engineJson,adjustedJson,version.RulesJson,fingerprint,receipt?.Fingerprint==fingerprint&&receipt.OwnerId==ownerId,receipt?.Fingerprint==fingerprint?receipt.AcceptedUtc:null,financialOptIn));
        }
        var agreement=await db.CoachAccountabilityAgreements.AsNoTracking().Where(x=>x.ClientId==client).Select(x=>x.StateJson).FirstOrDefaultAsync(ct);
        var workouts=new List<(string Id,string Name)>();
        if(agreement is not null)try {
            var state=WebRecomp.Web.Services.Coaching.AccountabilityDocument.Parse(agreement);
            workouts=state.SnapWorkouts.Where(w=>w.Simple&&messages.Any(m=>m.WorkoutId==w.Id)||w.Simple&&events.Count>0).Select(w=>(w.Id,w.Name)).ToList();
        }catch(ArgumentException){}
        return new(client,coach,coachName,isCoach,messages.OrderByDescending(x=>x.CreatedUtc).Take(100).Reverse().ToList(),activity,workouts,offers);
    }
    [HttpGet("{clientId:guid}/{coachId:guid}")]
    public async Task<IActionResult> Index(Guid clientId,Guid coachId,CancellationToken ct)
    {
        var auth=await Allowed(clientId,coachId,ct);
        return auth is null?NotFound():View(await Build(clientId,coachId,auth.Value.IsCoach,ct));
    }
    [HttpPost("{clientId:guid}/{coachId:guid}/accept-macros"),ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptMacros(Guid clientId,Guid coachId,Guid versionId,string fingerprint,CancellationToken ct)
    {
        var auth=await Allowed(clientId,coachId,ct);
        if(auth is null)return NotFound();
        if(auth.Value.IsCoach)return Forbid();
        var version=await db.CoachPrescriptionRuleVersions.AsNoTracking().SingleOrDefaultAsync(x=>x.CoachPrescriptionRuleVersionId==versionId&&x.ClientId==clientId&&x.CoachUserId==coachId,ct);
        if(version is null)return NotFound();
        var current=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version.CoachPrescriptionRuleVersionId+"|"+version.RulesJson+"|"+version.PreviewJson)));
        if(!string.Equals(current,fingerprint,StringComparison.Ordinal))return Conflict("The prescription changed. Review the latest proposal.");
        var key=$"macro-acceptance/{clientId:N}-{coachId:N}-{versionId:N}";
        using(files.Lease(key))files.Write(key,new CoachMacroAcceptance(versionId,clientId,coachId,auth.Value.Owner,current,DateTime.UtcNow));
        TempData["CoachWorkspaceStatus"]="Coach macro prescription accepted and recorded. Fee rules remain unchanged unless separately agreed.";
        return RedirectToAction(nameof(Index),new{clientId,coachId});
    }
    public sealed class MessageInput
    {
        [Required,StringLength(2000,MinimumLength=1)]public string Body{get;set;}="";
        [Required]public string Kind{get;set;}="message";
        [StringLength(64)]public string? WorkoutId{get;set;}
    }
    [HttpPost("{clientId:guid}/{coachId:guid}/send"),ValidateAntiForgeryToken,EnableRateLimiting("access-code"),RequestSizeLimit(8192)]
    public async Task<IActionResult> Send(Guid clientId,Guid coachId,MessageInput input,CancellationToken ct)
    {
        var auth=await Allowed(clientId,coachId,ct);
        if(auth is null)return NotFound();
        if(!ModelState.IsValid||string.IsNullOrWhiteSpace(input.Body)||input.Kind is not ("message" or "snap-note"))return BadRequest("Enter a message of up to 2,000 characters.");
        if(input.Kind=="snap-note"&&string.IsNullOrWhiteSpace(input.WorkoutId))return BadRequest("Choose a Snap Workout.");
        if(input.Kind=="message"&&input.WorkoutId is not null)return BadRequest("Workout references are for Snap notes only.");
        if(input.Kind=="snap-note")
        {
            var json=await db.CoachAccountabilityAgreements.AsNoTracking().Where(x=>x.ClientId==clientId).Select(x=>x.StateJson).FirstOrDefaultAsync(ct);
            if(json is null||!WebRecomp.Web.Services.Coaching.AccountabilityDocument.Parse(json).SnapWorkouts.Any(w=>w.Simple&&w.Id==input.WorkoutId))return BadRequest("Unknown Snap Workout.");
        }
        var sender=await db.UserAccounts.AsNoTracking().Where(x=>x.UserId==auth.Value.Actor).Select(x=>x.PreferredDisplayName??x.DisplayName??"Member").SingleAsync(ct);
        var key=Key(clientId,coachId);
        using(files.Lease(key))
        {
            var messages=files.Read<List<CoachConversationItem>>(key)??[];
            messages.Add(new(Guid.NewGuid(),DateTime.UtcNow,auth.Value.Actor,sender,input.Kind,input.WorkoutId,input.Body.Trim()));
            if(messages.Count>1000)messages=messages.TakeLast(1000).ToList();
            files.Write(key,messages);
        }
        var recipient=auth.Value.IsCoach?auth.Value.Owner:coachId;
        var target=await db.UserAccounts.AsNoTracking().Where(x=>x.UserId==recipient).Select(x=>x.ContactEmail??x.EmailNormalized).SingleOrDefaultAsync(ct);
        if(!string.IsNullOrWhiteSpace(target))
        {
            try {
                var address=new MailAddress(target.Trim()).Address;
                var subject=input.Kind=="snap-note"?"12WR: New Snap Workout note":"12WR: New coach message";
                var body=$"{sender} sent you a message in 12 Week Recomp.\n\n{input.Body.Trim()}\n\nOpen: {files.BaseUrl}/coach-workspace/{clientId:D}/{coachId:D}";
                var sent=await email.SendAsync(new(address,subject,body),ct);
                TempData["CoachWorkspaceStatus"]=sent.Success?"Message saved and email sent.":"Message saved. Email delivery was unsuccessful.";
            }catch(Exception){TempData["CoachWorkspaceStatus"]="Message saved. Email could not be delivered.";}
        }
        else TempData["CoachWorkspaceStatus"]="Message saved. Recipient has no email address.";
        return RedirectToAction(nameof(Index),new{clientId,coachId});
    }
}
