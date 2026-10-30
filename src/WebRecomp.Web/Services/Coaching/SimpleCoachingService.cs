using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
namespace WebRecomp.Web.Services.Coaching;
public static class SimpleCoaching
{
    public static bool Enabled(IConfiguration? configuration)=>configuration?.GetValue<bool>("SimpleCoaching:Enabled")==true;
}
public sealed class SimpleCoachingService(RecompDbContext db,CoachAccountabilityPreviewService access,AccountabilityLedger ledger,WebRecomp.Web.Services.Notifications.CoachNotificationProcessor? notifications=null)
{
    public async Task<CoachAccountabilityAgreement?> ReadAsync(Guid actor,Guid client,bool coach,CancellationToken ct)
    {
        if(!await access.CanViewAsync(actor,client,coach,ct))throw new UnauthorizedAccessException();
        return await db.CoachAccountabilityAgreements.AsNoTracking().SingleOrDefaultAsync(x=>x.ClientId==client,ct);
    }
    public async Task<CoachAccountabilityAgreement> EnsureAsync(Guid actor,Guid client,CancellationToken ct)
    {
        if(!await access.CanAssignSnapAsync(actor,client,ct))throw new UnauthorizedAccessException();
        var row=await db.CoachAccountabilityAgreements.SingleOrDefaultAsync(x=>x.ClientId==client,ct);
        if(row is not null)return row;
        var local=await db.Clients.AsNoTracking().SingleAsync(x=>x.ClientId==client,ct);
        var state=new AccountabilityState{Rules=AccountabilityDocument.RuleIds.Select(id=>new AccountabilityRule{Id=id}).ToList(),Agreement=new(){Start=AppUserContextService.GetClientToday(local).ToString("yyyy-MM-dd")}};
        var json=AccountabilityDocument.Serialize(state);var terms=AccountabilityDocument.ExpectedTerms(state);
        row=new(){ClientId=client,CoachUserId=actor,StateJson=json,TermsJson=terms,TermsHash=AccountabilityDocument.HashTerms(terms),TermsVersion=1,Revision=0,CreatedAtUtc=DateTime.UtcNow};
        db.CoachAccountabilityAgreements.Add(row);Event(row,actor,"CONFIG",json);return row;
    }
    public async Task AssignAsync(Guid actor,Guid client,int expected,Guid request,AccountabilityWorkout workout,CancellationToken ct)
    {
        if(request==Guid.Empty)throw new ArgumentException("Reload before assigning.");
        var row=await EnsureAsync(actor,client,ct);var state=AccountabilityDocument.Parse(row.StateJson);
        var id="simple-"+request.ToString("N");if(state.SnapWorkouts.Any(w=>w.Id==id))return;
        if(expected!=(row.Revision==1&&db.Entry(row).State==EntityState.Added?0:row.Revision))throw new AccountabilityConflictException();
        var local=await db.Clients.AsNoTracking().SingleAsync(x=>x.ClientId==client,ct);var today=AppUserContextService.GetClientToday(local);
        if(AccountabilityDocument.Date(workout.Date)<today||AccountabilityDocument.Date(workout.Date)>today.AddDays(84))throw new ArgumentException("Choose today or a date within the next 12 weeks.");
        workout.Id=id;workout.Simple=true;workout.CreatedAtUtc=DateTimeOffset.UtcNow;
        if(workout.DeadlineUtc.HasValue&&workout.DeadlineUtc<=workout.CreatedAtUtc)throw new ArgumentException("Choose a future deadline.");
        foreach(var e in workout.Exercises){e.Id="exercise-"+Guid.NewGuid().ToString("N");e.CatalogId="";e.Weight=0;}
        if(state.Agreement.Start=="")state.Agreement.Start=today.ToString("yyyy-MM-dd");
        state.SnapWorkouts.Add(workout);AccountabilityDocument.Validate(state);
        row.StateJson=AccountabilityDocument.Serialize(state);Event(row,actor,"CONFIG",row.StateJson);await SaveAsync(ct);
        if(notifications is not null)try{await notifications.QueueConfigAsync(client,row.Revision,ct);}catch(Exception){/* Notification worker recovers the CONFIG event. */}
    }
    public async Task ResultAsync(Guid actor,Guid client,bool coach,int expected,string workoutId,AccountabilityResult result,CancellationToken ct)
    {
        if(!await access.CanViewAsync(actor,client,coach,ct))throw new UnauthorizedAccessException();
        var row=await db.CoachAccountabilityAgreements.SingleOrDefaultAsync(x=>x.ClientId==client,ct)??throw new ArgumentException("No workout is assigned.");
        if(row.Revision!=expected)throw new AccountabilityConflictException();
        var state=AccountabilityDocument.Parse(row.StateJson);var workout=state.SnapWorkouts.SingleOrDefault(w=>w.Id==workoutId&&w.Simple)??throw new ArgumentException("Choose an active Snap Workout.");
        var local=await db.Clients.AsNoTracking().SingleAsync(x=>x.ClientId==client,ct);
        if(AccountabilityDocument.Date(workout.Date)>AppUserContextService.GetClientToday(local))throw new ArgumentException("Results can be entered on or after the workout date.");
        result.CompletedAtUtc=result.Finalized?DateTimeOffset.UtcNow:null;
        AccountabilityDocument.ValidateResult(state,workoutId,result);state.SnapResults[workoutId]=result;
        row.StateJson=AccountabilityDocument.Serialize(state);Event(row,actor,"RESULT",JsonSerializer.Serialize(new{workoutId,result},AccountabilityDocument.Json));
        var current=Charges(state,AppUserContextService.GetClientToday(local));
        var changes=AccountabilityLedger.Delta(current,await ledger.ReadAsync(row,ct),row.TermsVersion,actor);
        ledger.Append(row,actor,changes);await SaveAsync(ct);
    }
    public static IReadOnlyList<AccountabilityCharge> Charges(AccountabilityState saved,DateOnly today)
    {
        var simple=AccountabilityDocument.Parse(AccountabilityDocument.Serialize(saved));
        simple.SnapWorkouts=simple.SnapWorkouts.Where(w=>w.Simple).ToList();
        simple.Agreement=new(){Start="1900-01-01",Mode="open"};simple.Excused=[];
        return AccountabilityAssessment.Snap(simple,today);
    }
    private void Event(CoachAccountabilityAgreement row,Guid actor,string kind,string payload)
    {
        row.Revision++;row.UpdatedAtUtc=DateTime.UtcNow;
        db.CoachAccountabilityEvents.Add(new(){EventId=Guid.NewGuid(),ClientId=row.ClientId,ActorUserId=actor,Kind=kind,Revision=row.Revision,TermsVersion=row.TermsVersion,PayloadJson=payload,CreatedAtUtc=row.UpdatedAtUtc});
    }
    private async Task SaveAsync(CancellationToken ct)
    {
        try{await db.SaveChangesAsync(ct);}
        catch(DbUpdateConcurrencyException){db.ChangeTracker.Clear();throw new AccountabilityConflictException();}
        catch(DbUpdateException e)when(e.InnerException is Microsoft.Data.SqlClient.SqlException sql&&sql.Number is 2601 or 2627){db.ChangeTracker.Clear();throw new AccountabilityConflictException();}
    }
}
