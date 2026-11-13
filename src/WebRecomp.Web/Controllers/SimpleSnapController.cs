using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.WorkoutIntelligence.Persistence;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
namespace WebRecomp.Web.Controllers;
public sealed record SimpleSnapPage(Guid ClientId,string Name,bool Coach,int Revision,DateOnly Today,AccountabilityState State,SimpleSnapController.AssignmentInput Draft,string ClientTimeZone){public Guid? CoachUserId{get;init;}public string? CoachName{get;init;}public Dictionary<string,string> AssignedBy{get;init;}=[];public Dictionary<string,Guid> AssignmentOwners{get;init;}=[];public Dictionary<string,SnapWorkoutEstimate?> Estimates{get;init;}=[];public Dictionary<string,decimal> AssessedCharges{get;init;}=[];public Dictionary<string,decimal> PostedCharges{get;init;}=[];public List<(string Id,string Name,string Date)> RecentTemplates{get;init;}=[];}
public sealed record SimpleSnapCompletionPage(Guid ClientId, string ClientName, Guid CoachId, string CoachName,
    bool IsCoach, int Revision, AccountabilityWorkout Workout, AccountabilityResult Result,
    IReadOnlyList<AccountabilityCharge> Charges, decimal PostedTotal, string TimeZone);
[Authorize,Route("clients/{clientId:guid}/snap"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class SimpleSnapController(AppUserContextService users,SimpleCoachingService simple,CoachAccountabilityPreviewService access,AccountabilityLedger ledger,IConfiguration configuration,SnapWorkoutEstimateService? estimates=null,SnapAvailabilityService? schedule=null,RecompDbContext? scheduleDb=null):Controller
{
    // Snap Workout permissions are scoped to the active coach-client relationship, not role-claim spelling.
    private Task<bool> IsAssignedCoachAsync(Guid actor,Guid clientId,CancellationToken ct)=>access.CanAssignSnapAsync(actor,clientId,ct);
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid clientId,CancellationToken ct,Guid? coachUserId=null)=>await Page(clientId,null,ct,coachUserId:coachUserId);
    private async Task<IActionResult> Page(Guid clientId,AssignmentInput? draft,CancellationToken ct,ResultInput? retry=null,Guid? coachUserId=null)
    {
        if(!SimpleCoaching.Enabled(configuration))return NotFound();
        var user=(await users.GetAsync(HttpContext,ct)).User;
        try
        {
            var assignedCoach=await IsAssignedCoachAsync(user.UserId,clientId,ct);
            await simple.RefreshAsync(user.UserId,clientId,assignedCoach,ct);
            var row=await simple.ReadAsync(user.UserId,clientId,assignedCoach,ct);var clock=await ledger.ClockAsync(clientId,ct);
            var state=row is null?new AccountabilityState():AccountabilityDocument.Parse(row.StateJson);
            await simple.ApplyAvailabilityAsync(clientId,state,ct);
            var scope=await ResolveScopeAsync(user.UserId,clientId,assignedCoach,coachUserId,ct);
            var owners=await simple.AssignmentOwnersAsync(clientId,ct);
            if(scope.HasValue)SnapWorkoutAssignments.Filter(state,owners,scope.Value);
            ViewData["SnapCoachScope"]=scope;
            var selected=retry?.WorkoutId??Request.Query["assignment"].FirstOrDefault();
            if(scope.HasValue&&!string.IsNullOrWhiteSpace(selected)&&!state.SnapWorkouts.Any(w=>w.Simple&&w.Id==selected))
                return NotFound();
            var names=await simple.AssignmentNamesAsync(state.SnapWorkouts.Select(w=>owners.GetValueOrDefault(w.Id))
                .Concat(scope.HasValue?[scope.Value]:Array.Empty<Guid>()),ct);
            var assignedBy=state.SnapWorkouts.ToDictionary(w=>w.Id,w=>names.GetValueOrDefault(owners.GetValueOrDefault(w.Id))??"Coach unavailable");
            var retryWorkout=state.SnapWorkouts.SingleOrDefault(w=>w.Simple&&w.Id==retry?.WorkoutId);
            if(retryWorkout is not null&&retry is not null)
            {
                var result=new AccountabilityResult{DurationMinutes=null};
                foreach(var exercise in retryWorkout.Exercises)
                {
                    var entered=retry.Exercises.FirstOrDefault(e=>e.Id==exercise.Id)?.Sets;
                    result.Sets[exercise.Id]=Enumerable.Range(0,Math.Max(exercise.Sets,Math.Min(20,entered?.Count??0))).Select(i=>new AccountabilitySet{Reps=entered is not null&&i<entered.Count?entered[i].Reps:null,Rpe=retry.Exercises.FirstOrDefault(e=>e.Id==exercise.Id)?.Rpe,Weight=entered is not null&&i<entered.Count?entered[i].Weight:null}).ToList();
                }
                state.SnapResults[retryWorkout.Id]=result;
            }
            var readings=new Dictionary<string,SnapWorkoutEstimate?>();
            if(estimates is not null)foreach(var workout in state.SnapWorkouts.Where(w=>w.Simple)){var actual=state.SnapResults.GetValueOrDefault(workout.Id);readings[workout.Id]=await estimates.EstimateAsync(clientId,workout,actual?.Finalized==true?actual:null,clock.Today,ct);}
            var assessed=state.Rules.Count==AccountabilityDocument.RuleIds.Length?SimpleCoachingService.Charges(state,clock.Today).Where(x=>x.Ready).GroupBy(x=>x.Key.Split(':')[1]).ToDictionary(x=>x.Key,x=>x.Sum(y=>y.Amount)):new Dictionary<string,decimal>();
            var posted=new Dictionary<string,decimal>();
            if(row is not null){var entries=await ledger.ReadAsync(row,ct);posted=entries.Where(x=>x.SourceKey.StartsWith("snap:")&&(!assignedCoach||x.CoachUserId==user.UserId)).GroupBy(x=>x.SourceKey.Split(':')[1]).ToDictionary(x=>x.Key,x=>x.Sum(y=>y.Amount));}
            var recent=assignedCoach?state.SnapWorkouts.Where(w=>w.Simple).Reverse().OrderByDescending(w=>w.CreatedAtUtc??DateTimeOffset.MinValue).Select(w=>(w.Id,w.Name,w.Date)).ToList():new List<(string Id,string Name,string Date)>();
            return View("Index",new SimpleSnapPage(clientId,await access.ClientNameAsync(clientId,ct)??"Client",assignedCoach,row?.Revision??0,clock.Today,state,draft??new(){Date=clock.Today,Name="Snap Workout "+TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(clock.Zone)).ToString("M/d/yy h:mmtt",System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant()},clock.Zone){Estimates=readings,AssessedCharges=assessed,PostedCharges=posted,RecentTemplates=recent,CoachUserId=scope,CoachName=scope.HasValue?names.GetValueOrDefault(scope.Value)??"Coach":null,AssignedBy=assignedBy,AssignmentOwners=owners});
        }
        catch(UnauthorizedAccessException){return NotFound();}
    }
    private async Task<Guid?> ResolveScopeAsync(Guid actor, Guid clientId, bool assignedCoach, Guid? selected, CancellationToken ct)
    {
        if (assignedCoach)
        {
            if (selected.HasValue && selected.Value != actor) throw new UnauthorizedAccessException();
            return actor;
        }
        if (selected.HasValue && !await access.CanAssignSnapAsync(selected.Value, clientId, ct))
            throw new UnauthorizedAccessException();
        return selected;
    }

    private static AssignmentInput FromWorkout(AccountabilityWorkout w,int revision,DateOnly today)=>new(){
        Revision=revision,RequestId=Guid.NewGuid(),Name=w.Name,Date=today,TimeAllowed=(w.TimeLimitMinutes??120)/60m,
        LatePenalty=w.LatePenalty??0,Cap=w.Cap,CompletionReward=w.CompletionReward??0,CoachNote=w.CoachNote,
        IncompleteExerciseFee=w.Exercises.FirstOrDefault()?.Penalty??0,CoachIncentivePerRep=w.Exercises.FirstOrDefault()?.ExtraRepRate??0,
        Exercises=w.Exercises.Select(e=>new ExerciseInput{Id=e.Id,Name=e.Name,Sets=e.Sets,Reps=e.Reps,MaxWeight=e.MaxWeight>0?e.MaxWeight:null,Rpe=e.Rpe,MuscleFocus=e.MuscleFocus,Type=e.Type,Basis=e.Basis,Rate=e.Rate}).ToList()
    };
    [HttpGet("template")]
    public async Task<IActionResult> Template(Guid clientId,string workoutId,CancellationToken ct)
    {
        if(!SimpleCoaching.Enabled(configuration))return NotFound();
        var actor=(await users.GetAsync(HttpContext,ct)).User.UserId;
        if(!await IsAssignedCoachAsync(actor,clientId,ct))return NotFound();
        if ((await simple.AssignmentOwnersAsync(clientId,ct)).GetValueOrDefault(workoutId)!=actor)return NotFound();
        var row=await simple.ReadAsync(actor,clientId,true,ct);
        if(row is null)return NotFound();
        var clock=await ledger.ClockAsync(clientId,ct);
        var workout=AccountabilityDocument.Parse(row.StateJson).SnapWorkouts.SingleOrDefault(w=>w.Simple&&w.Id==workoutId);
        if(workout is null)return NotFound();
        ViewData["LoadedTemplateId"]=workout.Id;
        return await Page(clientId,FromWorkout(workout,row.Revision,clock.Today),ct);
    }
    private static string BaseName(string name)=>System.Text.RegularExpressions.Regex.Replace(name,@"\s+\d{4}-\d{2}-\d{2}\s+\d{4}(?:/\d+)?$","").Trim();
    private static string UniqueName(string name,IReadOnlyList<AccountabilityWorkout> workouts,DateTimeOffset now)
    {
        var root=name.Trim();
        if(string.IsNullOrWhiteSpace(root))root="Snap Workout "+now.ToString("M/d/yy h:mmtt",System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant();
        var candidate=root;
        for(var number=2;workouts.Any(w=>w.Simple&&string.Equals(w.Name,candidate,StringComparison.OrdinalIgnoreCase));number++)
        {
            var suffix="/"+number;
            candidate=root[..Math.Min(root.Length,100-suffix.Length)]+suffix;
        }
        return candidate;
    }
    [HttpGet("edit/{workoutId}")]
    public async Task<IActionResult> Edit(Guid clientId,string workoutId,CancellationToken ct)
    {
        if(!SimpleCoaching.Enabled(configuration))return NotFound();
        var actor=(await users.GetAsync(HttpContext,ct)).User.UserId;
        if(!await IsAssignedCoachAsync(actor,clientId,ct)||(await simple.AssignmentOwnersAsync(clientId,ct)).GetValueOrDefault(workoutId)!=actor)return NotFound();
        var row=await simple.ReadAsync(actor,clientId,true,ct);if(row is null)return NotFound();
        var state=AccountabilityDocument.Parse(row.StateJson);
        var workout=state.SnapWorkouts.SingleOrDefault(w=>w.Simple&&w.Id==workoutId);
        if(workout is null)return NotFound();
        if(state.SnapResults.GetValueOrDefault(workoutId)?.Finalized==true){TempData["SimpleStatus"]="Completed Snap Workouts cannot be edited. Cancel the assignment to withdraw its charges.";return RedirectToAction(nameof(Index),new{clientId});}
        var draft=FromWorkout(workout,row.Revision,(await ledger.ClockAsync(clientId,ct)).Today);
        draft.EditWorkoutId=workout.Id;draft.Date=AccountabilityDocument.Date(workout.Date);
        return await Page(clientId,draft,ct);
    }
    [HttpPost("cancel/{workoutId}"),ValidateAntiForgeryToken,EnableRateLimiting(SnapRequestRateLimits.Submissions)]
    public async Task<IActionResult> Cancel(Guid clientId,string workoutId,int revision,CancellationToken ct)
    {
        if(!SimpleCoaching.Enabled(configuration))return NotFound();
        var actor=(await users.GetAsync(HttpContext,ct)).User.UserId;
        try{await simple.CancelAsync(actor,clientId,revision,workoutId,ct);TempData["SimpleStatus"]="Snap Workout cancelled. Its automatic charges were reversed; completed training stays in history.";}
        catch(UnauthorizedAccessException){return NotFound();}
        catch(Exception e)when(e is ArgumentException or AccountabilityConflictException){TempData["SimpleStatus"]=e.Message;}
        return RedirectToAction(nameof(Index),new{clientId});
    }
    [HttpGet("reuse/{workoutId}")]
    public async Task<IActionResult> Reuse(Guid clientId,string workoutId,CancellationToken ct)
    {
        var actor=(await users.GetAsync(HttpContext,ct)).User.UserId;
        if(!await IsAssignedCoachAsync(actor,clientId,ct))return NotFound();
        if ((await simple.AssignmentOwnersAsync(clientId,ct)).GetValueOrDefault(workoutId)!=actor)return NotFound();
        var row=await simple.ReadAsync(actor,clientId,true,ct);
        if(row is null)return NotFound();
        var workout=AccountabilityDocument.Parse(row.StateJson).SnapWorkouts.SingleOrDefault(w=>w.Simple&&w.Id==workoutId);
        if(workout is null)return NotFound();
        var clock=await ledger.ClockAsync(clientId,ct);
        ViewData["LoadedTemplateId"]=workout.Id;
        return await Page(clientId,FromWorkout(workout,row.Revision,clock.Today),ct);
    }
    [HttpPost("repeat/{workoutId}"),ValidateAntiForgeryToken,EnableRateLimiting(SnapRequestRateLimits.Submissions)]
    public async Task<IActionResult> Repeat(Guid clientId,string workoutId,CancellationToken ct)
    {
        var actor=(await users.GetAsync(HttpContext,ct)).User.UserId;
        if(!await IsAssignedCoachAsync(actor,clientId,ct))return NotFound();
        if ((await simple.AssignmentOwnersAsync(clientId,ct)).GetValueOrDefault(workoutId)!=actor)return NotFound();
        var row=await simple.ReadAsync(actor,clientId,true,ct);
        if(row is null)return NotFound();
        var original=AccountabilityDocument.Parse(row.StateJson).SnapWorkouts.SingleOrDefault(w=>w.Simple&&w.Id==workoutId);
        if(original is null)return NotFound();
        var today=(await ledger.ClockAsync(clientId,ct)).Today;
        var input=FromWorkout(original,row.Revision,today);
        return await Assign(clientId,input,ct);
    }
    [HttpGet("exercises")]
    public async Task<IActionResult> Exercises(Guid clientId,string? q,[FromServices] IExerciseCatalogStore catalog,[FromServices] RecompDbContext db,CancellationToken ct)
    {
        if(!SimpleCoaching.Enabled(configuration))return NotFound();
        var actor=(await users.GetAsync(HttpContext,ct)).User.UserId;
        if(!await IsAssignedCoachAsync(actor,clientId,ct))return NotFound();
        var term=q?.Trim();
        if(string.IsNullOrEmpty(term)||term.Length<2)return Json(Array.Empty<object>());
        if(term.Length>250)return BadRequest();
        var rows=await catalog.SearchAsync(term,12,ct);
        var ids=rows.Select(x=>x.ExerciseId).ToArray();
        var muscles=await db.ExerciseMuscleTargets.AsNoTracking().Where(x=>ids.Contains(x.ExerciseCatalogItemId)).OrderBy(x=>x.DisplayOrder).ToListAsync(ct);
        return Json(rows.Select(x=>new{name=x.Name,muscles=string.Join(", ",muscles.Where(m=>m.ExerciseCatalogItemId==x.ExerciseId).Select(m=>m.MuscleName).Distinct())}));
    }
    [HttpGet("status")]
    public async Task<IActionResult> Status(Guid clientId,CancellationToken ct,Guid? coachUserId=null)
    {
        if(!SimpleCoaching.Enabled(configuration))return NotFound();
        var user=(await users.GetAsync(HttpContext,ct)).User;
        try
        {
            var assignedCoach=await IsAssignedCoachAsync(user.UserId,clientId,ct);
            await simple.RefreshAsync(user.UserId,clientId,assignedCoach,ct);var row=await simple.ReadAsync(user.UserId,clientId,assignedCoach,ct);
            var state=row is null?new AccountabilityState():AccountabilityDocument.Parse(row.StateJson);
            await simple.ApplyAvailabilityAsync(clientId,state,ct);
            var scope=await ResolveScopeAsync(user.UserId,clientId,assignedCoach,coachUserId,ct);
            if(scope.HasValue)SnapWorkoutAssignments.Filter(state,await simple.AssignmentOwnersAsync(clientId,ct),scope.Value);
            return Json(new{nowUtc=DateTimeOffset.UtcNow,workouts=state.SnapWorkouts.Where(w=>w.Simple).Select(w=>new{w.Id,w.DeadlineUtc,timing=w.Timing,finished=state.SnapResults.GetValueOrDefault(w.Id)?.Finalized==true,finishedAtUtc=state.SnapResults.GetValueOrDefault(w.Id)?.CompletedAtUtc})});
        }
        catch(UnauthorizedAccessException){return NotFound();}
    }
    [HttpGet("timing")]
    public async Task<IActionResult> Timing(Guid clientId, DateOnly date, int minutes,
        [FromServices] SnapAvailabilityService availability, [FromServices] RecompDbContext db, CancellationToken ct, bool overrideSchedule = false)
    {
        if(!SimpleCoaching.Enabled(configuration))return NotFound();
        var actor=(await users.GetAsync(HttpContext,ct)).User.UserId;
        if(!await IsAssignedCoachAsync(actor,clientId,ct))return NotFound();
        if(!ModelState.IsValid||minutes is <1 or >10080)return BadRequest();
        var client=await db.Clients.AsNoTracking().SingleAsync(c=>c.ClientId==clientId,ct);
        var today=AppUserContextService.GetClientToday(client);
        if(date<today||date>today.AddDays(84))return BadRequest();
        var now=DateTimeOffset.UtcNow;
        var start=date>today?SnapAvailabilityClock.Utc(date,TimeOnly.MinValue,SnapAvailabilityClock.Zone(client.TimeZone)):now;
        var workout=new AccountabilityWorkout{Simple=true,OverrideSchedule=overrideSchedule,OverrideHealthConflicts=overrideSchedule,CreatedAtUtc=now,TimingStartUtc=start,TimeLimitMinutes=minutes,DeadlineUtc=start.AddMinutes(minutes)};
        var state=new AccountabilityState{SnapWorkouts=[workout]};
        await availability.ApplyAsync(client,state,ct);
        var through=date.AddDays(6);
        var zone=SnapAvailabilityClock.Zone(client.TimeZone);
        var windows=await availability.WindowsAsync(client,date,through,ct);
        var programId=await db.Programs.AsNoTracking().Where(p=>p.ClientId==clientId&&p.ProgramStatusCode=="ACTIVE")
            .OrderByDescending(p=>p.StartDate).Select(p=>(Guid?)p.ProgramId).FirstOrDefaultAsync(ct);
        var plans=await db.PlannedWorkouts.AsNoTracking().Where(p=>p.ClientId==clientId&&p.ProgramId==programId
            &&p.PlannedDate>=date&&p.PlannedDate<=through).ToListAsync(ct);
        var names=await PlannedWorkoutNames.LoadAsync(db,clientId,plans,ct);
        var conflict=await availability.ConflictAsync(client,start,minutes,ct);
        var days=Enumerable.Range(0,7).Select(i=>
        {
            var day=date.AddDays(i);
            var left=SnapAvailabilityClock.Utc(day,TimeOnly.MinValue,zone);
            var right=SnapAvailabilityClock.Utc(day.AddDays(1),TimeOnly.MinValue,zone,true);
            return new { date=day, label=day.ToString("ddd, MMM d"), workout=names.GetValueOrDefault(day)??"No weekly workout planned",
                reservedMinutes=SnapAvailabilityClock.Merge(windows).Sum(w=>Math.Max(0,((w.End<right?w.End:right)-(w.Start>left?w.Start:left)).TotalMinutes)),
                reserved=windows.Where(w=>w.Start<right&&w.End>left).OrderBy(w=>w.Start).Select(w=>new {
                    label=w.Reason, start=w.Start<left?left:w.Start, end=w.End>right?right:w.End,
                    allDay=w.Start<=left&&w.End>=right }) };
        });
        return Json(new { workout.Timing!.DeadlineUtc,workout.Timing.AvailableFromUtc,workout.Timing.PausedUntilUtc,
            workout.Timing.RemainingSeconds,workout.Timing.Reason,timeZone=client.TimeZone,overrideSchedule,conflict,days });
    }
    [HttpPost("estimate"),ValidateAntiForgeryToken,EnableRateLimiting(SnapRequestRateLimits.Estimates),RequestSizeLimit(65536)]
    public async Task<IActionResult> Estimate(Guid clientId,AssignmentInput input,CancellationToken ct)
    {
        if(!SimpleCoaching.Enabled(configuration)||estimates is null)return NotFound();
        var user=(await users.GetAsync(HttpContext,ct)).User;
        if(!await access.CanAssignSnapAsync(user.UserId,clientId,ct))return NotFound();
        if(!ModelState.IsValid||input.Exercises.Count is <1 or >15||input.Exercises.Any(e=>e.Sets is <1 or >10||e.Reps is <1 or >100))return BadRequest(new{message="Enter exercise names, sets and reps to estimate."});
        try
        {
            foreach(var e in input.Exercises)SnapWorkoutEngine.ValidateFocus(e.MuscleFocus);
            var workout=new AccountabilityWorkout{Exercises=input.Exercises.Select(e=>new AccountabilityExercise{Name=e.Name,Sets=e.Sets,Reps=e.Reps,MaxWeight=e.MaxWeight??0,Rpe=e.Rpe,MuscleFocus=e.MuscleFocus}).ToList()};
            var reading=await estimates.EstimateAsync(clientId,workout,null,(await ledger.ClockAsync(clientId,ct)).Today,ct);
            return Json(new{estimate=reading,message=reading is null?"Add the client's weight, height, date of birth and sex to calculate estimates.":null});
        }
        catch(ArgumentException e){return BadRequest(new{message=e.Message});}
    }
    [HttpPost("assign"),ValidateAntiForgeryToken,EnableRateLimiting(SnapRequestRateLimits.Submissions),RequestSizeLimit(65536)]
    public async Task<IActionResult> Assign(Guid clientId,AssignmentInput input,CancellationToken ct)
    {
        if(!SimpleCoaching.Enabled(configuration))return NotFound();
        if(!ModelState.IsValid)return await Page(clientId,input,ct);
        try
        {
            var user=(await users.GetAsync(HttpContext,ct)).User;
            var current=await simple.ReadAsync(user.UserId,clientId,true,ct);
            var prior=current is null?new List<AccountabilityWorkout>():AccountabilityDocument.Parse(current.StateJson).SnapWorkouts;
            var zone=(await ledger.ClockAsync(clientId,ct)).Zone;
            var now=TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(zone));
            if(schedule is not null && scheduleDb is not null)
            {
                var client=await scheduleDb.Clients.AsNoTracking().SingleAsync(c=>c.ClientId==clientId,ct);
                var today=AppUserContextService.GetClientToday(client);
                if(input.Date<today||input.Date>today.AddDays(84))throw new ArgumentException("Choose today or a date within the next 12 weeks.");
                var start=input.Date>today?SnapAvailabilityClock.Utc(input.Date,TimeOnly.MinValue,SnapAvailabilityClock.Zone(client.TimeZone)):DateTimeOffset.UtcNow;
                var conflict=await schedule.ConflictAsync(client,start,(int)Math.Ceiling(input.TimeAllowed*60),ct);
                if(conflict.HasConflict)
                {
                    var overrideRequested=input.ConflictChoice=="override"||input.OverrideSchedule;
                    if((overrideRequested&&!conflict.CanOverride)||(!overrideRequested&&input.ConflictChoice!="extend")
                        ||(!overrideRequested&&!conflict.ExtendedDeadlineUtc.HasValue))
                    {
                        ViewData["SnapConflictRequired"]=true;
                        ModelState.AddModelError("", "This completion window conflicts with reserved time. Choose an extension or an available conflict override.");
                        return await Page(clientId,input,ct);
                    }
                }
            }
            var uniqueName=UniqueName(input.Name,prior.Where(w=>w.Id!=input.EditWorkoutId).ToArray(),now);
            var workout=new AccountabilityWorkout{Name=uniqueName,CoachNote=string.IsNullOrWhiteSpace(input.CoachNote)?null:input.CoachNote.Trim(),Date=input.Date.ToString("yyyy-MM-dd"),Cap=input.Cap,CompletionReward=input.CompletionReward,
                OverrideSchedule=input.ConflictChoice=="override"||input.OverrideSchedule,OverrideHealthConflicts=input.ConflictChoice=="override",TimeLimitMinutes=(int)Math.Ceiling(input.TimeAllowed*60),LatePenalty=input.LatePenalty,Exercises=input.Exercises.Select(e=>new AccountabilityExercise{Id=input.EditWorkoutId is null?"":e.Id,Name=e.Name.Trim(),Sets=e.Sets,Reps=e.Reps,MaxWeight=e.MaxWeight??0,Type=e.Type,Basis=e.Basis,Rate=e.Rate,Penalty=input.IncompleteExerciseFee,CompletionReward=0,Rpe=e.Rpe,MuscleFocus=e.MuscleFocus,ExtraRepRate=input.CoachIncentivePerRep}).ToList()};
            if(input.EditWorkoutId is not null)await simple.UpdateAsync(user.UserId,clientId,input.Revision,input.EditWorkoutId,workout,ct);
            else await simple.AssignAsync(user.UserId,clientId,input.Revision,input.RequestId,workout,ct);
            TempData["SimpleStatus"]=input.EditWorkoutId is null?"Workout assigned. Your client can open it in their portal.":"Snap Workout updated. The completion window has restarted.";return RedirectToAction(nameof(Index),new{clientId});
        }
        catch(UnauthorizedAccessException){return NotFound();}
        catch(Exception e)when(e is ArgumentException or AccountabilityConflictException){ModelState.AddModelError("",e.Message);return await Page(clientId,input,ct);}
    }
    [HttpPost("result"),ValidateAntiForgeryToken,EnableRateLimiting(SnapRequestRateLimits.Submissions),RequestSizeLimit(65536)]
    public async Task<IActionResult> Result(Guid clientId,ResultInput input,CancellationToken ct,Guid? coachUserId=null)
    {
        if(!SimpleCoaching.Enabled(configuration))return NotFound();
        try
        {
            if(!ModelState.IsValid)throw new ArgumentException("Check the completed reps and weight.");
            var finish=input.SubmitMode=="finish"||(Request.HasFormContentType&&Request.Form["Action"]=="finish");
            var user=(await users.GetAsync(HttpContext,ct)).User;
            var isCoach=await IsAssignedCoachAsync(user.UserId,clientId,ct);
            var scope=await ResolveScopeAsync(user.UserId,clientId,isCoach,coachUserId,ct);
            await simple.ResultAsync(user.UserId,clientId,isCoach,input.Revision,input.WorkoutId,new(){Finalized=finish,DurationMinutes=null,Sets=input.Exercises.ToDictionary(e=>e.Id,e=>e.Sets.Select(s=>new AccountabilitySet{Reps=s.Reps,Weight=s.Weight,Rpe=e.Rpe}).ToList())},ct,scope);
            TempData["SimpleStatus"]=finish?"Workout finished. Your coach can see the results and calculated account entries.":"Progress saved.";
        }
        catch(UnauthorizedAccessException){return NotFound();}
        catch(Exception e)when(e is ArgumentException or AccountabilityConflictException){ModelState.AddModelError("",e.Message);return await Page(clientId,null,ct,input,coachUserId);}
        return input.SubmitMode=="finish"||(Request.HasFormContentType&&Request.Form["Action"]=="finish")
            ? RedirectToAction(nameof(Completion),new{clientId,workoutId=input.WorkoutId})
            : RedirectToAction(nameof(Index),new{clientId,assignment=input.WorkoutId,coachUserId});
    }
    [HttpGet("completion/{workoutId}")]
    public async Task<IActionResult> Completion(Guid clientId, string workoutId, CancellationToken ct)
    {
        if (!SimpleCoaching.Enabled(configuration)) return NotFound();
        var actor = (await users.GetAsync(HttpContext, ct)).User.UserId;
        try
        {
            var isCoach = await IsAssignedCoachAsync(actor, clientId, ct);
            var row = await simple.ReadAsync(actor, clientId, isCoach, ct);
            if (row is null) return NotFound();
            var owners = await simple.AssignmentOwnersAsync(clientId, ct);
            if (!owners.TryGetValue(workoutId, out var coach) || (isCoach && coach != actor)) return NotFound();
            await simple.RefreshAsync(actor, clientId, isCoach, ct);
            row = await simple.ReadAsync(actor, clientId, isCoach, ct);
            var state = AccountabilityDocument.Parse(row!.StateJson);
            await simple.ApplyAvailabilityAsync(clientId,state,ct);
            var workout = state.SnapWorkouts.SingleOrDefault(w => w.Simple && w.Id == workoutId);
            if (workout is null || state.SnapResults.GetValueOrDefault(workoutId) is not { Finalized: true } result)
                return NotFound();
            var names = await simple.AssignmentNamesAsync([coach], ct);
            var clock = await ledger.ClockAsync(clientId, ct);
            var prefix = $"snap:{workoutId}:";
            var charges = SimpleCoachingService.Charges(state, clock.Today)
                .Where(charge => charge.Key.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            var posted = (await ledger.ReadAsync(row, ct))
                .Where(entry => entry.CoachUserId == coach && entry.SourceKey.StartsWith(prefix, StringComparison.Ordinal))
                .Sum(entry => entry.Amount);
            ViewData["SnapCoachScope"] = coach;
            return View(new SimpleSnapCompletionPage(clientId, await access.ClientNameAsync(clientId, ct) ?? "Client",
                coach, names.GetValueOrDefault(coach) ?? "Coach", isCoach, row.Revision, workout, result,
                charges, posted, clock.Zone));
        }
        catch (UnauthorizedAccessException) { return NotFound(); }
    }

    [HttpPost("completion/{workoutId}/note"), ValidateAntiForgeryToken, EnableRateLimiting(SnapRequestRateLimits.Submissions)]
    public async Task<IActionResult> CompletionNote(Guid clientId, string workoutId, CompletionNoteInput input, CancellationToken ct)
    {
        if (!SimpleCoaching.Enabled(configuration)) return NotFound();
        if (!ModelState.IsValid)
        {
            TempData["SnapCompletionStatus"] = "Keep the note within 1,000 characters.";
            return RedirectToAction(nameof(Completion), new { clientId, workoutId });
        }
        try
        {
            var actor = (await users.GetAsync(HttpContext, ct)).User.UserId;
            var isCoach = await IsAssignedCoachAsync(actor, clientId, ct);
            await simple.SaveNoteAsync(actor, clientId, isCoach, input.Revision, workoutId, input.Note, ct);
            TempData["SnapCompletionStatus"] = "Note saved.";
        }
        catch (UnauthorizedAccessException) { return NotFound(); }
        catch (Exception error) when (error is ArgumentException or AccountabilityConflictException)
        {
            TempData["SnapCompletionStatus"] = error.Message;
        }
        return RedirectToAction(nameof(Completion), new { clientId, workoutId });
    }

    public sealed class CompletionNoteInput
    {
        public int Revision { get; set; }
        [StringLength(1000)] public string? Note { get; set; }
    }

    public sealed class AssignmentInput
    {
        [StringLength(64)]public string? EditWorkoutId{get;set;}
        public int Revision{get;set;}
        public Guid RequestId{get;set;}=Guid.NewGuid();
        [StringLength(1000)]public string? CoachNote{get;set;}
        [Required,StringLength(100)]public string Name{get;set;}="Snap Workout";
        [RegularExpression("^(extend|override)?$")]public string? ConflictChoice{get;set;}
        public bool OverrideSchedule{get;set;}
        public DateOnly Date{get;set;}
        [Range(typeof(decimal),"0.0166666667","168")]public decimal TimeAllowed{get;set;}=2;
        [Range(typeof(decimal),"0","10000")]public decimal IncompleteExerciseFee{get;set;}
        [Range(typeof(decimal),"0","100")]public decimal CoachIncentivePerRep{get;set;}
        public decimal LatePenalty{get;set;}
        public decimal Cap{get;set;}=100;
        public decimal CompletionReward{get;set;}
        public List<ExerciseInput> Exercises{get;set;}=[new()];
    }
    public sealed class ExerciseInput
    {
        [StringLength(64)]public string Id{get;set;}="";
        [Required,StringLength(250)]public string Name{get;set;}="";
        [Range(typeof(decimal),"1","10")]public decimal? Rpe{get;set;}
        [StringLength(250)]public string? MuscleFocus{get;set;}
        public int Sets{get;set;}=3;public int Reps{get;set;}=10;
        [Range(typeof(decimal),"0","2000")]public decimal? MaxWeight{get;set;}
        public string Type{get;set;}="fine";public string Basis{get;set;}="incomplete-reps";public decimal Rate{get;set;}
        public decimal? Penalty{get;set;}=0;public decimal CompletionReward{get;set;}public decimal ExtraRepRate{get;set;}
    }
    public sealed class ResultInput
    {
        public int Revision{get;set;}public string WorkoutId{get;set;}="";public string SubmitMode{get;set;}="progress";
        public List<ExerciseResults> Exercises{get;set;}=[];
    }
    public sealed class ExerciseResults{[Range(typeof(decimal),"1","10")]public decimal? Rpe{get;set;}public string Id{get;set;}="";public List<SetInput> Sets{get;set;}=[];}
    public sealed class SetInput{public int? Reps{get;set;}public decimal? Weight{get;set;}}
}
