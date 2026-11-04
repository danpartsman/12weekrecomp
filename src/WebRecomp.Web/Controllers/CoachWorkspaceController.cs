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

public sealed record CoachConversationItem(Guid Id, DateTime CreatedUtc, Guid AuthorId, string AuthorName, string Kind, string? WorkoutId, string Body, Guid? ThreadId=null, string? Subject=null);
public sealed record CoachActivityItem(DateTime CreatedUtc, string Description);
public sealed record CoachMacroOffer(Guid VersionId, DateOnly EffectiveWeek, string Reason, string? EngineJson, string? AdjustedJson, string RulesJson, string Fingerprint, bool Accepted, DateTime? AcceptedUtc, bool FinancialOptIn);
public sealed record CoachMacroAcceptance(Guid VersionId, Guid ClientId, Guid CoachId, Guid OwnerId, string Fingerprint, DateTime AcceptedUtc);
public sealed record CoachFinancialRule(string Name, bool Enabled, string Frequency, decimal Amount, decimal Minimum, decimal Maximum, string Criterion);
public sealed record CoachFinancialAgreement(int Revision, string Status, DateTime UpdatedUtc, Guid UpdatedBy, string? ClientNote, IReadOnlyList<CoachFinancialRule> Rules);
public sealed record CoachWorkspacePage(Guid ClientId, Guid CoachId, string CoachName, bool IsCoach, IReadOnlyList<CoachConversationItem> Messages, IReadOnlyList<CoachActivityItem> Activity, IReadOnlyList<(string Id,string Name)> Workouts, IReadOnlyList<CoachMacroOffer> MacroOffers)
{
    public CoachFinancialAgreement? FinancialAgreement { get; init; }
}

[Authorize, Route("coach-workspace"), ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CoachWorkspaceController(RecompDbContext db, AppUserContextService users, NotificationFiles files, IEmailService email) : Controller
{
    private string FinancialKey(Guid client, Guid coach) => $"financial-agreements/{client:N}-{coach:N}";
    private static readonly (string Name, string Criterion, string Frequency)[] FinancialCategories =
    [
        ("Protein outside target", "Outside -5% to +15% of prescribed protein", "daily"),
        ("Carbohydrates outside target", "Outside ±5% of prescribed carbohydrates", "daily"),
        ("Fat outside target", "Outside ±5% of prescribed fat", "daily"),
        ("Calories outside prescribed range", "Outside coach-assigned calorie limits", "daily"),
        ("Missed planned workout", "Scheduled workout not completed without excuse", "weekly"),
        ("Missing nutrition tracking", "Required daily nutrition log incomplete", "daily"),
        ("Missing scale check-in", "Required scale or recovery entry incomplete", "daily"),
        ("Cheat-food occurrence", "Each reviewed cheat-food occurrence", "daily"),
        ("Nutrition compliance bonus", "All agreed nutrition criteria met", "weekly"),
        ("Workout completion bonus", "All planned workouts completed", "weekly")
    ];
    private static CoachFinancialAgreement DefaultFinancial(Guid coach) => new(0, "draft", DateTime.UtcNow, coach, null,
        FinancialCategories.Select(c => new CoachFinancialRule(c.Name, false, c.Frequency, 0m, 0m, 0m, c.Criterion)).ToList());
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
        return new(client,coach,coachName,isCoach,messages.OrderByDescending(x=>x.CreatedUtc).Take(100).Reverse().ToList(),activity,workouts,offers)
        { FinancialAgreement = files.Read<CoachFinancialAgreement>(FinancialKey(client,coach)) ?? DefaultFinancial(coach) };
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
    public sealed class FinancialInput
    {
        public int ExpectedRevision { get; set; }
        public List<bool> Enabled { get; set; } = [];
        public List<decimal> Amount { get; set; } = [];
        public List<decimal> Minimum { get; set; } = [];
        public List<decimal> Maximum { get; set; } = [];
        public List<string> Frequency { get; set; } = [];
    }
    [HttpPost("{clientId:guid}/{coachId:guid}/financial-proposal"), ValidateAntiForgeryToken]
    public async Task<IActionResult> FinancialProposal(Guid clientId, Guid coachId, FinancialInput input, CancellationToken ct)
    {
        var auth = await Allowed(clientId, coachId, ct);
        if(auth is null) return NotFound();
        if(!auth.Value.IsCoach) return Forbid();
        var n=FinancialCategories.Length;
        if(input.Enabled.Count!=n || input.Amount.Count!=n || input.Minimum.Count!=n || input.Maximum.Count!=n || input.Frequency.Count!=n)
            return BadRequest("Review all accountability criteria.");
        var rules=new List<CoachFinancialRule>();
        for(var i=0;i<n;i++)
        {
            var amount=input.Amount[i];var min=input.Minimum[i];var max=input.Maximum[i];
            if(amount<0 || amount>10000 || min<0 || max<0 || max>10000 || min>max || (input.Enabled[i] && (amount<=0 || max<=0)))
                return BadRequest("Amounts must be nonnegative, enabled rules need an amount and cap, and minimum cannot exceed maximum.");
            if(input.Frequency[i] is not ("daily" or "weekly" or "monthly"))return BadRequest("Choose daily, weekly, or monthly.");
            var c=FinancialCategories[i];
            rules.Add(new(c.Name,input.Enabled[i],input.Frequency[i],amount,min,max,c.Criterion));
        }
        var key=FinancialKey(clientId,coachId);
        using(files.Lease(key))
        {
            var current=files.Read<CoachFinancialAgreement>(key)??DefaultFinancial(coachId);
            if(current.Revision!=input.ExpectedRevision)return Conflict("The agreement changed. Reload and review before saving.");
            files.Write(key,new CoachFinancialAgreement(current.Revision+1,"pending",DateTime.UtcNow,auth.Value.Actor,null,rules));
        }
        TempData["CoachWorkspaceStatus"]="Financial proposal sent for client review. No new automatic fees are authorized until acceptance.";
        return RedirectToAction(nameof(Index),new{clientId,coachId});
    }
    [HttpPost("{clientId:guid}/{coachId:guid}/financial-response"), ValidateAntiForgeryToken]
    public async Task<IActionResult> FinancialResponse(Guid clientId, Guid coachId, int expectedRevision, string decision, [StringLength(2000)] string? note, CancellationToken ct)
    {
        var auth=await Allowed(clientId,coachId,ct);
        if(auth is null)return NotFound();
        if(auth.Value.IsCoach)return Forbid();
        if(decision is not ("accepted" or "changes-requested" or "declined"))return BadRequest("Choose a valid response.");
        if(decision!="accepted" && string.IsNullOrWhiteSpace(note))return BadRequest("Please explain the changes you request or why you decline.");
        var key=FinancialKey(clientId,coachId);
        using(files.Lease(key))
        {
            var current=files.Read<CoachFinancialAgreement>(key);
            if(current is null || current.Revision!=expectedRevision || current.Status!="pending")return Conflict("This proposal has changed. Reload to review it.");
            files.Write(key,current with {Revision=current.Revision+1,Status=decision,UpdatedUtc=DateTime.UtcNow,UpdatedBy=auth.Value.Actor,ClientNote=note?.Trim()});
        }
        TempData["CoachWorkspaceStatus"]=decision=="accepted"?"Financial agreement accepted.":"Your response was saved for the coach.";
        return RedirectToAction(nameof(Index),new{clientId,coachId});
    }
    public sealed class MessageInput
    {
        [Required,StringLength(2000,MinimumLength=1)]public string Body{get;set;}="";
        [Required]public string Kind{get;set;}="message";
        [StringLength(64)]public string? WorkoutId{get;set;}
        public Guid? ThreadId{get;set;}
        [StringLength(120)]public string? Subject{get;set;}
    }
    [HttpPost("{clientId:guid}/{coachId:guid}/send"),ValidateAntiForgeryToken,EnableRateLimiting("access-code"),RequestSizeLimit(8192)]
    public async Task<IActionResult> Send(Guid clientId,Guid coachId,MessageInput input,CancellationToken ct)
    {
        var auth=await Allowed(clientId,coachId,ct);
        if(auth is null)return NotFound();
        if(!ModelState.IsValid||string.IsNullOrWhiteSpace(input.Body)||input.Kind is not ("message" or "snap-note"))return BadRequest("Enter a message of up to 2,000 characters.");
        if(input.Kind=="message"&&input.ThreadId is null&&string.IsNullOrWhiteSpace(input.Subject))return BadRequest("Enter a thread subject.");
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
            if(input.Kind=="message"&&input.ThreadId is Guid thread&&!messages.Any(m=>m.Kind=="message"&&(m.ThreadId??m.Id)==thread))return BadRequest("Thread not found.");
            var threadId=input.Kind=="message"?(input.ThreadId??Guid.NewGuid()):(Guid?)null;
            messages.Add(new(Guid.NewGuid(),DateTime.UtcNow,auth.Value.Actor,sender,input.Kind,input.WorkoutId,input.Body.Trim(),threadId,input.Kind=="message"&&input.ThreadId is null?input.Subject?.Trim():null));
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
