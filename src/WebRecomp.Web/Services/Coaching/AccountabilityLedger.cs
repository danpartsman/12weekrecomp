using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
namespace WebRecomp.Web.Services.Coaching;

public sealed class AccountabilityLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceKey { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal RawAmount { get; set; }
    public int TermsVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid ActorUserId { get; set; }
    public Guid? CoachUserId { get; set; }
    public bool Automatic { get; set; }
    public string TermsKey { get; set; } = "";
}
public sealed class AccountabilityLedger(RecompDbContext db, CoachAccountabilityPreviewService access,IConfiguration? configuration=null)
{
    // The audit stream stays append-only; financial views are scoped to a coach/client pair.
    public async Task<CoachAccountabilityAgreement?> AccountAsync(Guid actor,Guid clientId,bool coachRole,CancellationToken ct)
    {
        if(!await access.IsOwnerAsync(actor,clientId,ct) && !(coachRole && await HasRelationshipAsync(actor,clientId,ct)))throw new UnauthorizedAccessException();
        return await db.CoachAccountabilityAgreements.AsNoTracking().SingleOrDefaultAsync(x=>x.ClientId==clientId,ct);
    }
    // Ledger batches share the existing append-only RESULT audit stream, without changing signed terms.
    public async Task<List<AccountabilityLedgerEntry>> ReadAsync(CoachAccountabilityAgreement row, CancellationToken ct)
    {
        var payloads=await db.CoachAccountabilityEvents.AsNoTracking().Where(x=>x.ClientId==row.ClientId&&x.Kind=="RESULT")
            .OrderBy(x=>x.Revision).Select(x=>x.PayloadJson).ToListAsync(ct);
        var entries=new List<AccountabilityLedgerEntry>();
        foreach(var payload in payloads)
        {
            using var doc=JsonDocument.Parse(payload);
            if(doc.RootElement.ValueKind==JsonValueKind.Object&&doc.RootElement.TryGetProperty("ledgerEntries",out var array))
                entries.AddRange(JsonSerializer.Deserialize<List<AccountabilityLedgerEntry>>(array.GetRawText(),AccountabilityDocument.Json)??[]);
        }
        var legacy=entries.Where(x=>x.Automatic&&x.SourceKey.StartsWith("rule:")&&x.TermsKey=="").ToArray();
        if(legacy.Length>0)
        {
            var versions=legacy.Select(x=>x.TermsVersion).Distinct().ToArray();
            var history=await db.CoachAccountabilityEvents.AsNoTracking().Where(x=>x.ClientId==row.ClientId&&x.Kind=="CONFIG"&&versions.Contains(x.TermsVersion))
                .OrderBy(x=>x.Revision).Select(x=>new{x.TermsVersion,x.PayloadJson}).ToListAsync(ct);
            foreach(var entry in legacy)
            {
                var config=history.FirstOrDefault(x=>x.TermsVersion==entry.TermsVersion);if(config is null)continue;
                var state=AccountabilityDocument.Parse(config.PayloadJson);var parts=entry.SourceKey.Split(':');
                var rule=state.Rules.Single(x=>x.Id==parts[1]);var period=AccountabilityAssessment.Period(AccountabilityDocument.Date(parts[2]),rule.Frequency);
                entry.SourceKey=$"rule:{rule.Id}:{period.Start:yyyy-MM-dd}:{period.End:yyyy-MM-dd}";
                entry.TermsKey=AccountabilityAssessment.RuleTermsKey(state,rule);
            }
        }
        if(SimpleCoaching.Enabled(configuration))
        {
            // Preserve raw audit records while presenting the corrected client-pays convention.
            // Flip adjustments and reversals with their original kinds as well as original entries.
            foreach(var entry in entries)
            {
                if(entry.Kind=="incentive"){entry.Amount=-entry.Amount;entry.Kind="incentive-client";}
                else if(entry.Kind=="payment-coach"){entry.Amount=-entry.Amount;entry.Kind="payment-client";}
            }
        }
        await AttributeAsync(row,entries,ct);
        return entries;
    }
    public async Task<bool> ApprovedAsync(CoachAccountabilityAgreement row,CancellationToken ct)
    {
        var owners=await db.UserClientAccess.AsNoTracking().Where(x=>x.ClientId==row.ClientId&&x.AccessRoleCode=="OWNER"&&x.RevokedAtUtc==null).Select(x=>x.UserId).ToListAsync(ct);
        return await db.CoachAccountabilityApprovals.AsNoTracking().AnyAsync(x=>x.ClientId==row.ClientId&&x.CoachUserId==row.CoachUserId&&x.TermsHash==row.TermsHash&&x.TermsVersion==row.TermsVersion&&owners.Contains(x.OwnerUserId),ct)
            || db.CoachAccountabilityApprovals.Local.Any(x=>x.ClientId==row.ClientId&&x.CoachUserId==row.CoachUserId&&x.TermsHash==row.TermsHash&&x.TermsVersion==row.TermsVersion&&owners.Contains(x.OwnerUserId));
    }
    public async Task<List<AccountabilityLedgerEntry>> SnapDeltaAsync(CoachAccountabilityAgreement row,AccountabilityState state,Guid actor,CancellationToken ct)
    {
        if(SimpleCoaching.Enabled(configuration))return [];
        if(!await ApprovedAsync(row,ct))return [];
        var client=await db.Clients.AsNoTracking().SingleAsync(x=>x.ClientId==row.ClientId,ct);
        var current=AccountabilityAssessment.Snap(state,AppUserContextService.GetClientToday(client)).ToList();
        if(state.Enabled && state.Rules.Any(x=>x.Assigned && Array.IndexOf(AccountabilityDocument.RuleIds,x.Id)>=4))
        {
            var today=AppUserContextService.GetClientToday(client);var days=new Dictionary<DateOnly,JsonElement>();
            for(var d=today.AddDays(-84);d<=today;d=d.AddDays(1))days[d]=JsonSerializer.SerializeToElement(new{date=d.ToString("yyyy-MM-dd"),completed=d<today});
            current.AddRange(AccountabilityAssessment.Rules(state,days,today));
        }
        var posted=await ReadAsync(row,ct);
        // Removing an assignment or excusing its date records a reversal rather than deleting old lines.
        foreach(var key in posted.Where(x=>x.Automatic&&x.SourceKey.StartsWith("snap:")).Select(x=>x.SourceKey).Distinct())
            if(current.All(x=>x.Key!=key))current.Add(new(key,AppUserContextService.GetClientToday(client),posted.First(x=>x.SourceKey==key).Kind,"Removed Snap assignment",0,0));
        var delta=Delta(current,posted,row.TermsVersion,actor);
        await PrepareAutomaticAsync(row,delta,ct);
        return delta;
    }
    public static List<AccountabilityLedgerEntry> Delta(IEnumerable<AccountabilityCharge> charges,IReadOnlyList<AccountabilityLedgerEntry> posted,int version,Guid actor)
    {
        var output=new List<AccountabilityLedgerEntry>();
        foreach(var charge in charges.Where(x=>x.Ready))
        {
            var old=posted.Where(x=>x.Automatic&&x.SourceKey==charge.Key).ToArray();
            if(charge.Key.StartsWith("rule:")&&old.Any(x=>x.TermsKey!=""&&charge.TermsKey!=""?x.TermsKey!=charge.TermsKey:x.TermsVersion!=version))continue;
            var signed=charge.Kind=="incentive"?-charge.Amount:charge.Amount;var delta=AccountabilityAssessment.Cents(signed-old.Sum(x=>x.Amount));
            if(delta==0)continue;
            var detail=charge.RawAmount>charge.Amount?$" · ${charge.RawAmount:0.00} before cap; ${charge.Amount:0.00} applied":"";
            if(!charge.Settle)detail+=" · Below this period's settlement threshold";
            output.Add(new(){SourceKey=charge.Key,Date=charge.Date,Kind=charge.Kind is "completion-fee" or "penalty" or "late-penalty" or "incentive-client"?charge.Kind:charge.Kind=="incentive"?"incentive":"fee",Amount=delta,RawAmount=charge.RawAmount,
                TermsVersion=version,TermsKey=charge.TermsKey,ActorUserId=actor,Automatic=true,Description=(old.Length>0?"Adjustment · ":"")+charge.Description+detail});
        }
        return output;
    }
    public void Append(CoachAccountabilityAgreement row,Guid actor,IReadOnlyList<AccountabilityLedgerEntry> entries)
    {
        if(entries.Count==0)return;
        if(entries.Any(x=>!x.CoachUserId.HasValue))throw new InvalidOperationException("Assign financial entries to a coach before posting.");
        if(db.Entry(row).State==EntityState.Detached)db.Attach(row);
        row.Revision++;row.UpdatedAtUtc=DateTime.UtcNow;
        db.CoachAccountabilityEvents.Add(new(){EventId=Guid.NewGuid(),ClientId=row.ClientId,ActorUserId=actor,Kind="RESULT",Revision=row.Revision,TermsVersion=row.TermsVersion,
            CreatedAtUtc=row.UpdatedAtUtc,PayloadJson=JsonSerializer.Serialize(new{ledgerEntries=entries},AccountabilityDocument.Json)});
    }
    public async Task RefreshAsync(Guid actor,CoachAccountabilityAgreement row,UserAccount user,CancellationToken ct)
    {
        if(SimpleCoaching.Enabled(configuration))return;
        if(!await access.CanViewAsync(actor,row.ClientId,await access.CanAssignSnapAsync(actor,row.ClientId,ct),ct))throw new UnauthorizedAccessException();
        if(!await access.CanAssignSnapAsync(row.CoachUserId,row.ClientId,ct))return;
        if(!await ApprovedAsync(row,ct))return;
        var state=AccountabilityDocument.Parse(row.StateJson);var changes=await SnapDeltaAsync(row,state,actor,ct);
        if(state.Enabled&&state.Rules.Any(x=>x.Assigned))
        {
            var client=await db.Clients.AsNoTracking().SingleAsync(x=>x.ClientId==row.ClientId,ct);var today=AppUserContextService.GetClientToday(client);
            var start=AccountabilityDocument.Date(state.Agreement.Start);var first=start>today.AddDays(-84)?start:today.AddDays(-84);
            var days=new Dictionary<DateOnly,JsonElement>();
            for(var date=first;date<=today;)
            {
                var data=await access.EvidenceAsync(user,row.ClientId,date,ct,true);
                if(data is not null)
                {
                    var json=JsonSerializer.SerializeToElement(data,AccountabilityDocument.Json);
                    foreach(var day in json.GetProperty("periodDays").EnumerateArray())days[DateOnly.ParseExact(day.GetProperty("date").GetString()!,"yyyy-MM-dd")]=day.Clone();
                }
                var next=new DateOnly(date.Year,date.Month,1).AddMonths(1);date=next>date?next:date.AddDays(1);
            }
            changes.AddRange(Delta(AccountabilityAssessment.Rules(state,days,today).Where(x=>x.Date>=first && changes.All(c=>c.SourceKey!=x.Key)),await ReadAsync(row,ct),row.TermsVersion,actor));
        }
        await PrepareAutomaticAsync(row,changes,ct);
        Append(row,actor,changes);await SaveAsync(ct);
    }
    public async Task RecordAsync(Guid coach,CoachAccountabilityAgreement row,int expected,Guid requestId,string kind,DateOnly date,decimal amount,string description,CancellationToken ct)
    {
        var active=await access.CanAssignSnapAsync(coach,row.ClientId,ct);
        if(!active && (!await HasRelationshipAsync(coach,row.ClientId,ct) || kind is not ("payment-client" or "payment-coach")))throw new UnauthorizedAccessException();
        var allowed=SimpleCoaching.Enabled(configuration)?new[]{"fee","completion-fee","penalty","incentive-client","payment-client"}:new[]{"fee","incentive","payment-client","payment-coach"};
        if(requestId==Guid.Empty||!allowed.Contains(kind)||!AccountabilityDocument.Money(amount)||amount<=0||string.IsNullOrWhiteSpace(description)||description.Length>300)
            throw new ArgumentException("Enter a positive dollar amount and a short description.");
        var source="manual:"+requestId.ToString("N");var signed=kind is "incentive" or "payment-client"?-amount:amount;
        var existing=(await ReadAsync(row,ct)).SingleOrDefault(x=>x.SourceKey==source&&x.CoachUserId==coach);
        if(existing is not null){if(existing.Amount!=signed||existing.Kind!=kind||existing.Date!=date||existing.Description!=description.Trim())throw new AccountabilityConflictException();return;}
        if(expected!=row.Revision)throw new AccountabilityConflictException();
        var client=await db.Clients.AsNoTracking().SingleAsync(x=>x.ClientId==row.ClientId,ct);var today=AppUserContextService.GetClientToday(client);
        if(date>today||date<new DateOnly(1900,1,1))throw new ArgumentException("Choose today or an earlier payment date.");
        Append(row,coach,[new(){SourceKey=source,Date=date,Kind=kind,Description=description.Trim(),Amount=signed,RawAmount=amount,TermsVersion=row.TermsVersion,ActorUserId=coach,CoachUserId=coach}]);
        await SaveAsync(ct);
    }
    public async Task ReverseAsync(Guid coach,CoachAccountabilityAgreement row,int expected,Guid entryId,string reason,CancellationToken ct)
    {
        if(!await HasRelationshipAsync(coach,row.ClientId,ct))throw new UnauthorizedAccessException();
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>300)throw new ArgumentException("Enter a reason for the correction.");
        var entries=await ReadAsync(row,ct);var original=entries.SingleOrDefault(x=>x.CoachUserId==coach&&x.Id==entryId&&!x.Automatic&&!x.SourceKey.StartsWith("reverse:"))??throw new ArgumentException("Choose a manual entry to reverse.");
        if(!await access.CanAssignSnapAsync(coach,row.ClientId,ct) && original.Kind is not ("payment-client" or "payment-coach"))throw new UnauthorizedAccessException();
        var key="reverse:"+entryId.ToString("N");if(entries.Any(x=>x.SourceKey==key))return;
        if(row.Revision!=expected)throw new AccountabilityConflictException();
        Append(row,coach,[new(){SourceKey=key,Date=original.Date,Kind=original.Kind,Amount=-original.Amount,RawAmount=original.RawAmount,ActorUserId=coach,CoachUserId=coach,TermsVersion=row.TermsVersion,Description="Reversal · "+original.Description+" · "+reason.Trim()}]);
        await SaveAsync(ct);
    }
    public async Task PrepareAutomaticAsync(CoachAccountabilityAgreement row,List<AccountabilityLedgerEntry> entries,CancellationToken ct)
    {
        await AttributeAsync(row,entries,ct);
        var active=await db.CoachClientRelationships.AsNoTracking().Where(x=>x.ClientId==row.ClientId&&x.StatusCode=="ACTIVE"&&x.RevokedAtUtc==null).ToListAsync(ct);
        var history=await db.CoachAccountabilityEvents.AsNoTracking().Where(x=>x.ClientId==row.ClientId&&x.Kind=="CONFIG").OrderBy(x=>x.Revision).ToListAsync(ct);
        var assignments=new Dictionary<string,DateTime>();
        foreach(var config in history)
        {
            using var doc=JsonDocument.Parse(config.PayloadJson);
            if(doc.RootElement.TryGetProperty("snapWorkouts",out var workouts))
                foreach(var workout in workouts.EnumerateArray())
                    if(workout.TryGetProperty("id",out var id)&&id.GetString() is string key)assignments.TryAdd(key,config.CreatedAtUtc);
        }
        entries.RemoveAll(entry=>entry.Automatic&&!active.Any(link=>link.CoachUserId==entry.CoachUserId&&
            (!link.AcceptedAtUtc.HasValue||!entry.SourceKey.StartsWith("snap:")||assignments.GetValueOrDefault(entry.SourceKey.Split(':')[1])>=link.AcceptedAtUtc.Value)));
    }

    private Task<bool> HasRelationshipAsync(Guid coach,Guid client,CancellationToken ct) =>
        db.CoachClientRelationships.AnyAsync(x=>x.CoachUserId==coach&&x.ClientId==client,ct);

    public async Task<IReadOnlyList<CoachLedgerAccount>> AccountsAsync(Guid actor,Guid client,bool coachRole,CancellationToken ct)
    {
        var owner=await access.IsOwnerAsync(actor,client,ct);
        var row=await AccountAsync(actor,client,coachRole,ct);
        var entries=row is null?[]:await ReadAsync(row,ct);
        var links=await db.CoachClientRelationships.AsNoTracking().Where(x=>x.ClientId==client).ToListAsync(ct);
        var ids=links.Select(x=>x.CoachUserId).Concat(entries.Where(x=>x.CoachUserId.HasValue).Select(x=>x.CoachUserId!.Value)).Distinct().ToList();
        if(row is not null)ids.Add(row.CoachUserId);
        ids=ids.Distinct().Where(id=>id!=Guid.Empty&&(owner||id==actor)).ToList();
        var users=await db.UserAccounts.AsNoTracking().Where(x=>ids.Contains(x.UserId)).ToListAsync(ct);
        var profiles=await db.CoachProfiles.AsNoTracking().Where(x=>ids.Contains(x.UserId)).ToListAsync(ct);
        var accounts=ids.Select(id=>new CoachLedgerAccount(id,
            profiles.FirstOrDefault(x=>x.UserId==id)?.DisplayName??users.FirstOrDefault(x=>x.UserId==id)?.PreferredDisplayName??users.FirstOrDefault(x=>x.UserId==id)?.DisplayName??"Coach",
            links.Any(x=>x.CoachUserId==id&&x.StatusCode=="ACTIVE"&&x.RevokedAtUtc==null),
            entries.Where(x=>x.CoachUserId==id).Sum(x=>x.Amount))).OrderByDescending(x=>x.Active).ThenBy(x=>x.Name).ToList();
        if(owner&&entries.Any(x=>x.CoachUserId==Guid.Empty))accounts.Add(new(Guid.Empty,"Earlier shared account · coach unconfirmed",false,entries.Where(x=>x.CoachUserId==Guid.Empty).Sum(x=>x.Amount)));
        return accounts;
    }

    // Legacy ownership comes from immutable assignment/configuration events, never from the
    // current viewer or the client's newest coach. Unresolved history remains a separate account.
    public async Task AttributeAsync(CoachAccountabilityAgreement row,IReadOnlyList<AccountabilityLedgerEntry> entries,CancellationToken ct)
    {
        if(entries.All(x=>x.CoachUserId.HasValue))return;
        var history=await db.CoachAccountabilityEvents.AsNoTracking().Where(x=>x.ClientId==row.ClientId&&x.Kind=="CONFIG").OrderBy(x=>x.Revision).ToListAsync(ct);
        history.AddRange(db.CoachAccountabilityEvents.Local.Where(x=>x.ClientId==row.ClientId&&x.Kind=="CONFIG"&&history.All(y=>y.EventId!=x.EventId)));
        var coaches=(await db.CoachClientRelationships.AsNoTracking().Where(x=>x.ClientId==row.ClientId).Select(x=>x.CoachUserId).Distinct().ToListAsync(ct)).ToHashSet();
        coaches.Add(row.CoachUserId);
        var workouts=new Dictionary<string,Guid>();var rules=new Dictionary<int,Guid>();
        foreach(var config in history.OrderBy(x=>x.Revision))
        {
            // Read only assignment IDs; adding ownership must not rewrite signed terms.
            using var doc=JsonDocument.Parse(config.PayloadJson);
            if(doc.RootElement.TryGetProperty("snapWorkouts",out var array))
                foreach(var workout in array.EnumerateArray())
                    if(workout.TryGetProperty("id",out var id)&&id.GetString() is string key)
                        workouts.TryAdd(key,coaches.Contains(config.ActorUserId)?config.ActorUserId:Guid.Empty);
            rules.TryAdd(config.TermsVersion,coaches.Contains(config.ActorUserId)?config.ActorUserId:Guid.Empty);
        }
        foreach(var entry in entries.Where(x=>!x.CoachUserId.HasValue&&!x.SourceKey.StartsWith("reverse:")))
        {
            Guid coach;
            if(!entry.Automatic)coach=coaches.Contains(entry.ActorUserId)?entry.ActorUserId:Guid.Empty;
            else if(entry.SourceKey.StartsWith("snap:"))coach=workouts.GetValueOrDefault(entry.SourceKey.Split(':')[1]);
            else coach=rules.GetValueOrDefault(entry.TermsVersion);
            entry.CoachUserId=coach;
        }
        // Reversals always stay with their original entry, even if another person posted them.
        foreach(var entry in entries.Where(x=>!x.CoachUserId.HasValue&&x.SourceKey.StartsWith("reverse:")))
            entry.CoachUserId=Guid.TryParseExact(entry.SourceKey[8..],"N",out var id)?entries.FirstOrDefault(x=>x.Id==id)?.CoachUserId??Guid.Empty:Guid.Empty;
    }
    public async Task<(DateOnly Today,string Zone)> ClockAsync(Guid clientId,CancellationToken ct)
    {
        var client=await db.Clients.AsNoTracking().SingleAsync(x=>x.ClientId==clientId,ct);
        return(AppUserContextService.GetClientToday(client),client.TimeZone);
    }
    private async Task SaveAsync(CancellationToken ct)
    {
        try{await db.SaveChangesAsync(ct);}
        catch(DbUpdateConcurrencyException){db.ChangeTracker.Clear();throw new AccountabilityConflictException();}
        catch(DbUpdateException e)when(e.InnerException is Microsoft.Data.SqlClient.SqlException sql&&sql.Number is 2601 or 2627){db.ChangeTracker.Clear();throw new AccountabilityConflictException();}
    }
}

public sealed record CoachLedgerAccount(Guid CoachUserId,string Name,bool Active,decimal Balance);
