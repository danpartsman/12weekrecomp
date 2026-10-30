using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
namespace WebRecomp.Web.Services.Coaching;
public sealed class AccountabilityConflictException() : Exception("This agreement changed in another window. Reload before saving.");
public sealed class CoachAccountabilityStore(RecompDbContext db, CoachAccountabilityPreviewService access, TimeProvider? clock=null, WebRecomp.Web.Services.Notifications.CoachNotificationProcessor? notifications=null, AccountabilityLedger? ledger=null)
{
    private readonly TimeProvider time=clock??TimeProvider.System;
    public Task<CoachAccountabilityAgreement?> ReadAsync(Guid actor,Guid client,bool coachRole,CancellationToken ct)=>ReadCoreAsync(actor,client,coachRole,ct,false);
    public Task<CoachAccountabilityAgreement?> ReadSnapAsync(Guid actor,Guid client,bool coachRole,CancellationToken ct)=>ReadCoreAsync(actor,client,coachRole,ct,true);
    private async Task<CoachAccountabilityAgreement?> ReadCoreAsync(Guid actor,Guid client,bool coachRole,CancellationToken ct,bool snapAccess)
    {
        if(!await access.CanViewAsync(actor,client,coachRole,ct))throw new UnauthorizedAccessException();
        var row=await db.CoachAccountabilityAgreements.AsNoTracking().SingleOrDefaultAsync(x=>x.ClientId==client,ct);
        if(row is not null)await AuthorizeRow(row,actor,coachRole,ct,snapAccess);
        return row;
    }
    private async Task AuthorizeRow(CoachAccountabilityAgreement row,Guid actor,bool coachRole,CancellationToken ct,bool snapAccess=false)
    {
        if(!await access.CanAssignSnapAsync(row.CoachUserId,row.ClientId,ct)
            || !((coachRole&&(actor==row.CoachUserId||(snapAccess&&await access.CanAssignSnapAsync(actor,row.ClientId,ct))))||await access.CanApproveAsync(actor,row.ClientId,ct)))throw new UnauthorizedAccessException();
    }
    public async Task<object> EnvelopeAsync(CoachAccountabilityAgreement? row,CancellationToken ct)
    {
        if(row is null)return new {state=(object?)null,revision=0,termsVersion=0,terms=(string?)null,approval=(object?)null};
        using var document=JsonDocument.Parse(row.StateJson);
        var approved=await db.CoachAccountabilityApprovals.AsNoTracking().Where(x=>x.ClientId==row.ClientId&&x.CoachUserId==row.CoachUserId&&x.TermsVersion==row.TermsVersion&&x.TermsHash==row.TermsHash).OrderByDescending(x=>x.ApprovedAtUtc).FirstOrDefaultAsync(ct);
        if(approved is not null&&!await access.IsOwnerAsync(approved.OwnerUserId,row.ClientId,ct))approved=null;
        return new {state=document.RootElement.Clone(),revision=row.Revision,termsVersion=row.TermsVersion,terms=row.TermsJson,
            approval=approved is null?null:new{approvedAt=DateTime.SpecifyKind(approved.ApprovedAtUtc,DateTimeKind.Utc),signedBy=approved.SignedBy,termsVersion=approved.TermsVersion}};
    }
    public async Task<CoachAccountabilityAgreement> SaveCoachAsync(Guid actor,Guid client,bool coachRole,int expected,string stateJson,string terms,CancellationToken ct,bool snapOnly=false)
    {
        if(!coachRole||!await access.CanAssignSnapAsync(actor,client,ct))throw new UnauthorizedAccessException();
        var state=AccountabilityDocument.Parse(stateJson);AccountabilityDocument.ValidateTerms(state,terms);
        var ids=state.SnapWorkouts.SelectMany(x=>x.Exercises).Where(x=>x.CatalogId!="").Select(x=>Guid.Parse(x.CatalogId)).Distinct().ToArray();
        var prefix=client.ToString("N")+":";
        var valid=await db.ExerciseCatalogItems.AsNoTracking().Where(x=>ids.Contains(x.ExerciseCatalogItemId)&&x.IsActive&&(x.SourceCode!="12WR_CSV_UNMAPPED"||x.SourceExternalId.StartsWith(prefix))).Select(x=>x.ExerciseCatalogItemId).ToListAsync(ct);
        if(valid.Count!=ids.Length)throw new ArgumentException("One or more exercises are not available to this client.");
        var row=await db.CoachAccountabilityAgreements.SingleOrDefaultAsync(x=>x.ClientId==client,ct);
        if(row is null)
        {
            if(expected!=0)throw new AccountabilityConflictException();
            row=new(){ClientId=client,CoachUserId=actor,CreatedAtUtc=time.GetUtcNow().UtcDateTime};db.CoachAccountabilityAgreements.Add(row);
        }
        else
        {
            await AuthorizeRow(row,actor,coachRole,ct,snapOnly);
            if(!snapOnly&&row.CoachUserId!=actor)throw new UnauthorizedAccessException();
            if(row.Revision!=expected)throw new AccountabilityConflictException();
            // A configuration save cannot silently overwrite completion records saved by the client.
            var before=AccountabilityDocument.Parse(row.StateJson);
            if(snapOnly&&WithoutSnap(before)!=WithoutSnap(state))throw new ArgumentException("Snap Workout changes cannot change agreement dates, fees or payment settings. Reload the saved agreement.");
            foreach(var pair in before.SnapResults)
            {
                var replacement=state.SnapWorkouts.SingleOrDefault(w=>w.Id==pair.Key);
                if(replacement is null)continue;
                var original=before.SnapWorkouts.Single(w=>w.Id==pair.Key);
                if(AccountabilityDocument.Canonical(JsonSerializer.Serialize(original,AccountabilityDocument.Json))!=AccountabilityDocument.Canonical(JsonSerializer.Serialize(replacement,AccountabilityDocument.Json)))
                    throw new ArgumentException("A recorded Snap Workout keeps its original targets. Create a new assignment to change them.");
                state.SnapResults[pair.Key]=pair.Value;
            }
            AccountabilityDocument.Validate(state);
            var node=JsonNode.Parse(stateJson)!.AsObject();node["snapResults"]=JsonSerializer.SerializeToNode(state.SnapResults,AccountabilityDocument.Json);stateJson=node.ToJsonString();
        }
        var hash=AccountabilityDocument.HashTerms(terms);
        if(row.TermsVersion==0||row.TermsHash!=hash)row.TermsVersion++;
        row.StateJson=stateJson;row.TermsJson=terms;row.TermsHash=hash;
        Update(row,actor,"CONFIG",stateJson);var configRevision=row.Revision;
        if(ledger is not null)ledger.Append(row,actor,await ledger.SnapDeltaAsync(row,state,actor,ct));
        await Commit(ct);
        if (notifications is not null)
        {
            try { await notifications.QueueConfigAsync(client,configRevision,ct); }
            catch (Exception) { /* The immutable CONFIG event is recovered by the notification worker. */ }
        }
        return row;
    }
    public async Task<CoachAccountabilityAgreement> SaveResultAsync(Guid actor,Guid client,bool coachRole,int expected,string workoutId,string resultJson,CancellationToken ct)
    {
        var row=await db.CoachAccountabilityAgreements.SingleOrDefaultAsync(x=>x.ClientId==client,ct)??throw new ArgumentException("Your coach has not saved an agreement yet.");
        await AuthorizeRow(row,actor,coachRole,ct,true);if(row.Revision!=expected)throw new AccountabilityConflictException();
        var state=AccountabilityDocument.Parse(row.StateJson);AccountabilityResult result;
        try{result=JsonSerializer.Deserialize<AccountabilityResult>(resultJson,AccountabilityDocument.Json)??throw new JsonException();}
        catch(JsonException){throw new ArgumentException("Check completed sets, reps and weight.");}
        AccountabilityDocument.ValidateResult(state,workoutId,result);
        var w=state.SnapWorkouts.Single(x=>x.Id==workoutId);
        var localClient=await db.Clients.AsNoTracking().SingleAsync(x=>x.ClientId==client,ct);
        if(AccountabilityDocument.Date(w.Date)>AppUserContextService.GetClientToday(localClient))throw new ArgumentException("Future workouts cannot be completed yet.");
        if(result.Finalized&&result.CompletedAtUtc.HasValue&&(result.CompletedAtUtc>time.GetUtcNow().AddMinutes(1)||(w.CreatedAtUtc.HasValue&&result.CompletedAtUtc<w.CreatedAtUtc.Value.AddMinutes(-1))))throw new ArgumentException("Use a real completion time after assignment and no later than now.");
        state.SnapResults[workoutId]=result;
        var node=JsonNode.Parse(row.StateJson)!.AsObject();node["snapResults"]=JsonSerializer.SerializeToNode(state.SnapResults,AccountabilityDocument.Json);row.StateJson=node.ToJsonString();
        var ledgerEntries=ledger is null?new List<AccountabilityLedgerEntry>():await ledger.SnapDeltaAsync(row,state,actor,ct);
        Update(row,actor,"RESULT",JsonSerializer.Serialize(new{workoutId,result,ledgerEntries},AccountabilityDocument.Json));await Commit(ct);return row;
    }
    public async Task<CoachAccountabilityAgreement> ApproveAsync(Guid actor,Guid client,int expected,string terms,string signedBy,CancellationToken ct)
    {
        var row=await db.CoachAccountabilityAgreements.SingleOrDefaultAsync(x=>x.ClientId==client,ct)??throw new ArgumentException("Your coach has not saved an agreement yet.");
        await AuthorizeRow(row,actor,false,ct);
        if(!await access.CanApproveAsync(actor,client,ct))throw new UnauthorizedAccessException();
        if(row.Revision!=expected||row.TermsHash!=AccountabilityDocument.HashTerms(terms))throw new AccountabilityConflictException();
        var state=AccountabilityDocument.Parse(row.StateJson);AccountabilityDocument.ValidateTerms(state,terms);
        if(string.IsNullOrWhiteSpace(state.Agreement.Start))throw new ArgumentException("Your coach must save agreement dates first.");
        var existing=await db.CoachAccountabilityApprovals.AnyAsync(x=>x.ClientId==client&&x.OwnerUserId==actor&&x.TermsVersion==row.TermsVersion&&x.TermsHash==row.TermsHash,ct);
        if(!existing)
        {
            db.CoachAccountabilityApprovals.Add(new(){ApprovalId=Guid.NewGuid(),ClientId=client,CoachUserId=row.CoachUserId,OwnerUserId=actor,TermsVersion=row.TermsVersion,TermsHash=row.TermsHash,TermsJson=row.TermsJson,SignedBy=signedBy,ApprovedAtUtc=time.GetUtcNow().UtcDateTime});
            Update(row,actor,"APPROVE",row.TermsJson);
            if(ledger is not null)ledger.Append(row,actor,await ledger.SnapDeltaAsync(row,state,actor,ct));
            await Commit(ct);
        }
        return row;
    }
    private static string WithoutSnap(AccountabilityState state)
    {
        var node=JsonNode.Parse(AccountabilityDocument.Serialize(state))!.AsObject();
        node.Remove("snapWorkouts");node.Remove("snapResults");
        return AccountabilityDocument.Canonical(node.ToJsonString());
    }
    private void Update(CoachAccountabilityAgreement row,Guid actor,string kind,string payload)
    {
        row.Revision++;row.UpdatedAtUtc=time.GetUtcNow().UtcDateTime;
        db.CoachAccountabilityEvents.Add(new(){EventId=Guid.NewGuid(),ClientId=row.ClientId,ActorUserId=actor,Kind=kind,Revision=row.Revision,TermsVersion=row.TermsVersion,PayloadJson=payload,CreatedAtUtc=row.UpdatedAtUtc});
    }
    private async Task Commit(CancellationToken ct)
    {
        try{await db.SaveChangesAsync(ct);}
        catch(DbUpdateConcurrencyException){db.ChangeTracker.Clear();throw new AccountabilityConflictException();}
        catch(DbUpdateException e) when(e.InnerException is Microsoft.Data.SqlClient.SqlException sql&&sql.Number is 2601 or 2627){db.ChangeTracker.Clear();throw new AccountabilityConflictException();}
    }
}
