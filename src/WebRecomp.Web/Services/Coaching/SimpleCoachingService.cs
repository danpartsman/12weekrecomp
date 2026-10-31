using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
namespace WebRecomp.Web.Services.Coaching;
public static class SimpleCoaching
{
    public static bool Enabled(IConfiguration? configuration)=>configuration?.GetValue<bool>("SimpleCoaching:Enabled")==true;
}
public sealed class SimpleCoachingService(RecompDbContext db,CoachAccountabilityPreviewService access,AccountabilityLedger ledger,WebRecomp.Web.Services.Notifications.CoachNotificationProcessor? notifications=null,TimeProvider? clock=null)
{
    private readonly TimeProvider time=clock??TimeProvider.System;
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
        workout.Id=id;workout.Simple=true;workout.CreatedAtUtc=time.GetUtcNow();
        var minutes=workout.TimeLimitMinutes??120;
        if(minutes is <1 or >10080)throw new ArgumentException("Choose a completion window between 1 minute and 7 days.");
        workout.TimeLimitMinutes=minutes;
        var start=workout.CreatedAtUtc.Value;
        if(AccountabilityDocument.Date(workout.Date)>today)
        {
            TimeZoneInfo zone;try{zone=TimeZoneInfo.FindSystemTimeZoneById(local.TimeZone);}catch(TimeZoneNotFoundException){zone=TimeZoneInfo.Utc;}catch(InvalidTimeZoneException){zone=TimeZoneInfo.Utc;}
            start=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(AccountabilityDocument.Date(workout.Date).ToDateTime(TimeOnly.MinValue),zone),TimeSpan.Zero);
        }
        workout.DeadlineUtc=start.AddMinutes(minutes);
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
        if(row.Revision!=expected)
        {
            var intervening=await db.CoachAccountabilityEvents.AsNoTracking().Where(x=>x.ClientId==client&&x.Revision>expected).Select(x=>new{x.Kind,x.PayloadJson}).ToListAsync(ct);
            if(expected<1||intervening.Count==0||intervening.Any(x=>x.Kind!="RESULT"||!LedgerOnly(x.PayloadJson)))throw new AccountabilityConflictException();
        }
        var state=AccountabilityDocument.Parse(row.StateJson);var workout=state.SnapWorkouts.SingleOrDefault(w=>w.Id==workoutId&&w.Simple)??throw new ArgumentException("Choose an active Snap Workout.");
        var local=await db.Clients.AsNoTracking().SingleAsync(x=>x.ClientId==client,ct);
        if(AccountabilityDocument.Date(workout.Date)>AppUserContextService.GetClientToday(local))throw new ArgumentException("Results can be entered on or after the workout date.");
        var previous=state.SnapResults.GetValueOrDefault(workoutId);
        result.Finalized=result.Finalized||previous?.Finalized==true;
        result.CompletedAtUtc=result.Finalized?(previous?.Finalized==true?previous.CompletedAtUtc??time.GetUtcNow():time.GetUtcNow()):null;
        if(result.Sets.Keys.Any(id=>workout.Exercises.All(e=>e.Id!=id)))throw new ArgumentException("Choose the assigned exercise rows.");
        if(result.Finalized)
        {
            foreach(var exercise in workout.Exercises)
            {
                var sets=result.Sets.GetValueOrDefault(exercise.Id)??[];
                while(sets.Count<exercise.Sets)sets.Add(new());
                foreach(var set in sets){set.Reps??=0;set.WeightRecorded=set.Weight.HasValue||exercise.MaxWeight==0;set.Weight??=0;}
                result.Sets[exercise.Id]=sets;
            }
        }
        AccountabilityDocument.ValidateResult(state,workoutId,result);
        await SnapWorkoutEngine.SyncAsync(db,local,workout,result,ct);
        state.SnapResults[workoutId]=result;
        row.StateJson=AccountabilityDocument.Serialize(state);Event(row,actor,"RESULT",JsonSerializer.Serialize(new{workoutId,result},AccountabilityDocument.Json));
        var current=Charges(state,AppUserContextService.GetClientToday(local),time.GetUtcNow());
        var changes=AccountabilityLedger.Delta(current,await ledger.ReadAsync(row,ct),row.TermsVersion,actor);
        await ledger.PrepareAutomaticAsync(row,changes,ct);
        ledger.Append(row,actor,changes);await SaveAsync(ct);
    }
    public static IReadOnlyList<AccountabilityCharge> Charges(AccountabilityState saved,DateOnly today,DateTimeOffset? asOf=null)
    {
        var simple=AccountabilityDocument.Parse(AccountabilityDocument.Serialize(saved));
        simple.SnapWorkouts=simple.SnapWorkouts.Where(w=>w.Simple).ToList();
        simple.Agreement=new(){Start="1900-01-01",Mode="open"};simple.Excused=[];
        var charges=new List<AccountabilityCharge>();
        foreach(var w in simple.SnapWorkouts)
        {
            if(AccountabilityDocument.Date(w.Date)>today)continue;
            simple.SnapResults.TryGetValue(w.Id,out var result);
            decimal remaining=w.Cap;
            void Add(string suffix,string kind,string description,decimal raw)
            {
                var amount=AccountabilityAssessment.Cents(Math.Min(raw,remaining));remaining-=amount;
                charges.Add(new($"snap:{w.Id}:{suffix}",AccountabilityDocument.Date(w.Date),kind,$"{w.Name} · {description}",amount,AccountabilityAssessment.Cents(raw)));
            }
            if(w.LatePenalty.HasValue&&w.DeadlineUtc.HasValue)
            {
                var finishedOnTime=result?.Finalized==true&&result.CompletedAtUtc.HasValue&&result.CompletedAtUtc<=w.DeadlineUtc;
                var due=(asOf??DateTimeOffset.UtcNow)>=w.DeadlineUtc&&!finishedOnTime;
                Add("late","late-penalty","Late completion fine",due?w.LatePenalty.Value:0);
            }
            if(result?.Finalized!=true)continue;
            var wholeComplete=SnapWorkoutLibrary.Completed(w,result);
            Add("completion","completion-fee","Whole-workout completion fee",wholeComplete?w.CompletionReward??0:0);
            foreach(var e in w.Exercises)
            {
                var sets=result.Sets.GetValueOrDefault(e.Id)??[];
                bool Qualifies(AccountabilitySet set)=>set.WeightRecorded!=false&&set.Weight>=e.Weight&&(!e.MaxWeight.HasValue||set.Weight<=e.MaxWeight);
                var qualifying=sets.Where(Qualifies).ToList();
                var completedSets=qualifying.Count(s=>s.Reps>=e.Reps);var complete=completedSets>=e.Sets;
                var doneReps=qualifying.Take(e.Sets).Sum(s=>Math.Min(s.Reps??0,e.Reps));
                var oldQuantity=e.Basis switch{"completed-reps"=>doneReps,"completed-sets"=>Math.Min(completedSets,e.Sets),"incomplete-sets"=>Math.Max(0,e.Sets-completedSets),_=>Math.Max(0,e.Sets*e.Reps-doneReps)};
                var penalty=e.Penalty.HasValue?(complete?0:e.Penalty.Value):oldQuantity*e.Rate;
                Add(e.Id+":base",e.Type=="incentive"&&!e.Penalty.HasValue?"completion-fee":"penalty",e.Name+" · "+(e.Penalty.HasValue?"Unfinished exercise penalty":"Exercise charge"),penalty);
                Add(e.Id+":completion","completion-fee",e.Name+" · Exercise completion fee",complete?e.CompletionReward??0:0);
                var extra=complete?Math.Max(0,qualifying.Sum(s=>s.Reps??0)-e.Sets*e.Reps):0;
                Add(e.Id+":reward","incentive-client",$"{e.Name} · {extra} extra reps × ${(e.ExtraRepRate??0):0.00}",extra*(e.ExtraRepRate??0));
            }
        }
        return charges;
    }
    public async Task RefreshAsync(Guid actor,Guid client,bool coach,CancellationToken ct)
    {
        if(!await access.CanViewAsync(actor,client,coach,ct))throw new UnauthorizedAccessException();
        var row=await db.CoachAccountabilityAgreements.SingleOrDefaultAsync(x=>x.ClientId==client,ct);if(row is null)return;
        try{await PostAsync(row,actor,ct);}catch(AccountabilityConflictException){db.ChangeTracker.Clear();}
    }
    private static bool LedgerOnly(string json)
    {
        using var doc=JsonDocument.Parse(json);return doc.RootElement.TryGetProperty("ledgerEntries",out _)&&!doc.RootElement.TryGetProperty("workoutId",out _);
    }
    private async Task PostAsync(CoachAccountabilityAgreement row,Guid actor,CancellationToken ct)
    {
        var state=AccountabilityDocument.Parse(row.StateJson);var local=await db.Clients.AsNoTracking().SingleAsync(x=>x.ClientId==row.ClientId,ct);
        var changes=AccountabilityLedger.Delta(Charges(state,AppUserContextService.GetClientToday(local),time.GetUtcNow()),await ledger.ReadAsync(row,ct),row.TermsVersion,actor);
        if(changes.Count==0)return;
        await ledger.PrepareAutomaticAsync(row,changes,ct);
        ledger.Append(row,actor,changes);await SaveAsync(ct);
    }
    public async Task PostDueAsync(CancellationToken ct)
    {
        var ids=await db.CoachAccountabilityAgreements.AsNoTracking().Select(x=>new{x.ClientId,x.StateJson}).ToListAsync(ct);
        foreach(var candidate in ids)
        {
            var state=AccountabilityDocument.Parse(candidate.StateJson);
            if(!state.SnapWorkouts.Any(w=>w.Simple&&w.LatePenalty>0&&w.DeadlineUtc<=time.GetUtcNow()))continue;
            var actor=await (from link in db.CoachClientRelationships join client in db.Clients on link.ClientId equals client.ClientId
                where link.ClientId==candidate.ClientId&&link.StatusCode=="ACTIVE"&&link.RevokedAtUtc==null&&client.ClientStatusCode=="ACTIVE" select link.CoachUserId).FirstOrDefaultAsync(ct);
            if(actor==Guid.Empty)continue;
            try{var row=await db.CoachAccountabilityAgreements.SingleAsync(x=>x.ClientId==candidate.ClientId,ct);await PostAsync(row,actor,ct);}
            catch(AccountabilityConflictException){db.ChangeTracker.Clear();}
        }
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
