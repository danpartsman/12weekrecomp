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
    public bool Automatic { get; set; }
}
public sealed class AccountabilityLedger(RecompDbContext db, CoachAccountabilityPreviewService access)
{
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
        if(!await ApprovedAsync(row,ct))return [];
        var client=await db.Clients.AsNoTracking().SingleAsync(x=>x.ClientId==row.ClientId,ct);
        var current=AccountabilityAssessment.Snap(state,AppUserContextService.GetClientToday(client)).ToList();
        var posted=await ReadAsync(row,ct);
        // Removing an assignment or excusing its date records a reversal rather than deleting old lines.
        foreach(var key in posted.Where(x=>x.Automatic&&x.SourceKey.StartsWith("snap:")).Select(x=>x.SourceKey).Distinct())
            if(current.All(x=>x.Key!=key))current.Add(new(key,AppUserContextService.GetClientToday(client),posted.First(x=>x.SourceKey==key).Kind,"Removed Snap assignment",0,0));
        return Delta(current,posted,row.TermsVersion,actor);
    }
    public static List<AccountabilityLedgerEntry> Delta(IEnumerable<AccountabilityCharge> charges,IReadOnlyList<AccountabilityLedgerEntry> posted,int version,Guid actor)
    {
        var output=new List<AccountabilityLedgerEntry>();
        foreach(var charge in charges.Where(x=>x.Ready))
        {
            var old=posted.Where(x=>x.Automatic&&x.SourceKey==charge.Key).ToArray();
            if(charge.Key.StartsWith("rule:")&&old.Any(x=>x.TermsVersion!=version))continue;
            var signed=charge.Kind=="incentive"?-charge.Amount:charge.Amount;var delta=AccountabilityAssessment.Cents(signed-old.Sum(x=>x.Amount));
            if(delta==0)continue;
            var detail=charge.RawAmount>charge.Amount?$" · ${charge.RawAmount:0.00} before cap; ${charge.Amount:0.00} applied":"";
            if(!charge.Settle)detail+=" · Below this period's settlement threshold";
            output.Add(new(){SourceKey=charge.Key,Date=charge.Date,Kind=charge.Kind=="incentive"?"incentive":"fee",Amount=delta,RawAmount=charge.RawAmount,
                TermsVersion=version,ActorUserId=actor,Automatic=true,Description=(old.Length>0?"Adjustment · ":"")+charge.Description+detail});
        }
        return output;
    }
    public void Append(CoachAccountabilityAgreement row,Guid actor,IReadOnlyList<AccountabilityLedgerEntry> entries)
    {
        if(entries.Count==0)return;
        if(db.Entry(row).State==EntityState.Detached)db.Attach(row);
        row.Revision++;row.UpdatedAtUtc=DateTime.UtcNow;
        db.CoachAccountabilityEvents.Add(new(){EventId=Guid.NewGuid(),ClientId=row.ClientId,ActorUserId=actor,Kind="RESULT",Revision=row.Revision,TermsVersion=row.TermsVersion,
            CreatedAtUtc=row.UpdatedAtUtc,PayloadJson=JsonSerializer.Serialize(new{ledgerEntries=entries},AccountabilityDocument.Json)});
    }
    public async Task RefreshAsync(Guid actor,CoachAccountabilityAgreement row,UserAccount user,CancellationToken ct)
    {
        if(!await access.CanViewAsync(actor,row.ClientId,actor==row.CoachUserId,ct)||!await access.CanAssignSnapAsync(row.CoachUserId,row.ClientId,ct))throw new UnauthorizedAccessException();
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
            changes.AddRange(Delta(AccountabilityAssessment.Rules(state,days,today).Where(x=>x.Date>=first),await ReadAsync(row,ct),row.TermsVersion,actor));
        }
        Append(row,actor,changes);await SaveAsync(ct);
    }
    public async Task RecordAsync(Guid coach,CoachAccountabilityAgreement row,int expected,Guid requestId,string kind,DateOnly date,decimal amount,string description,CancellationToken ct)
    {
        if(coach!=row.CoachUserId||!await access.CanAssignSnapAsync(coach,row.ClientId,ct))throw new UnauthorizedAccessException();
        if(requestId==Guid.Empty||!new[]{"fee","incentive","payment-client","payment-coach"}.Contains(kind)||!AccountabilityDocument.Money(amount)||amount<=0||string.IsNullOrWhiteSpace(description)||description.Length>300)
            throw new ArgumentException("Enter a positive dollar amount and a short description.");
        var source="manual:"+requestId.ToString("N");var signed=kind is "incentive" or "payment-client"?-amount:amount;
        var existing=(await ReadAsync(row,ct)).SingleOrDefault(x=>x.SourceKey==source);
        if(existing is not null){if(existing.Amount!=signed||existing.Kind!=kind||existing.Date!=date||existing.Description!=description.Trim())throw new AccountabilityConflictException();return;}
        if(expected!=row.Revision)throw new AccountabilityConflictException();
        var client=await db.Clients.AsNoTracking().SingleAsync(x=>x.ClientId==row.ClientId,ct);var today=AppUserContextService.GetClientToday(client);
        if(date>today||date<new DateOnly(1900,1,1))throw new ArgumentException("Choose today or an earlier payment date.");
        Append(row,coach,[new(){SourceKey=source,Date=date,Kind=kind,Description=description.Trim(),Amount=signed,RawAmount=amount,TermsVersion=row.TermsVersion,ActorUserId=coach}]);
        await SaveAsync(ct);
    }
    public async Task ReverseAsync(Guid coach,CoachAccountabilityAgreement row,int expected,Guid entryId,string reason,CancellationToken ct)
    {
        if(coach!=row.CoachUserId||!await access.CanAssignSnapAsync(coach,row.ClientId,ct))throw new UnauthorizedAccessException();
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>300)throw new ArgumentException("Enter a reason for the correction.");
        var entries=await ReadAsync(row,ct);var original=entries.SingleOrDefault(x=>x.Id==entryId&&!x.Automatic&&!x.SourceKey.StartsWith("reverse:"))??throw new ArgumentException("Choose a manual entry to reverse.");
        var key="reverse:"+entryId.ToString("N");if(entries.Any(x=>x.SourceKey==key))return;
        if(row.Revision!=expected)throw new AccountabilityConflictException();
        Append(row,coach,[new(){SourceKey=key,Date=original.Date,Kind=original.Kind,Amount=-original.Amount,RawAmount=original.RawAmount,ActorUserId=coach,TermsVersion=row.TermsVersion,Description="Reversal · "+original.Description+" · "+reason.Trim()}]);
        await SaveAsync(ct);
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
