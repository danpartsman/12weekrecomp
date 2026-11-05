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
public sealed record SimpleSnapPage(Guid ClientId,string Name,bool Coach,int Revision,DateOnly Today,AccountabilityState State,SimpleSnapController.AssignmentInput Draft,string ClientTimeZone){public Dictionary<string,SnapWorkoutEstimate?> Estimates{get;init;}=[];public Dictionary<string,decimal> AssessedCharges{get;init;}=[];public Dictionary<string,decimal> PostedCharges{get;init;}=[];public List<(string Id,string Name,string Date)> RecentTemplates{get;init;}=[];}
[Authorize,Route("clients/{clientId:guid}/snap"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class SimpleSnapController(AppUserContextService users,SimpleCoachingService simple,CoachAccountabilityPreviewService access,AccountabilityLedger ledger,IConfiguration configuration,SnapWorkoutEstimateService? estimates=null):Controller
{
    // Snap Workout permissions are scoped to the active coach-client relationship, not role-claim spelling.
    private Task<bool> IsAssignedCoachAsync(Guid actor,Guid clientId,CancellationToken ct)=>access.CanAssignSnapAsync(actor,clientId,ct);
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid clientId,CancellationToken ct)=>await Page(clientId,null,ct);
    private async Task<IActionResult> Page(Guid clientId,AssignmentInput? draft,CancellationToken ct,ResultInput? retry=null)
    {
        if(!SimpleCoaching.Enabled(configuration))return NotFound();
        var user=(await users.GetAsync(HttpContext,ct)).User;
        try
        {
            var assignedCoach=await IsAssignedCoachAsync(user.UserId,clientId,ct);
            await simple.RefreshAsync(user.UserId,clientId,assignedCoach,ct);
            var row=await simple.ReadAsync(user.UserId,clientId,assignedCoach,ct);var clock=await ledger.ClockAsync(clientId,ct);
            var state=row is null?new AccountabilityState():AccountabilityDocument.Parse(row.StateJson);
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
            return View("Index",new SimpleSnapPage(clientId,await access.ClientNameAsync(clientId,ct)??"Client",assignedCoach,row?.Revision??0,clock.Today,state,draft??new(){Date=clock.Today,Name="Snap Workout "+TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(clock.Zone)).ToString("M/d/yy h:mmtt",System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant()},clock.Zone){Estimates=readings,AssessedCharges=assessed,PostedCharges=posted,RecentTemplates=recent});
        }
        catch(UnauthorizedAccessException){return NotFound();}
    }
    private static AssignmentInput FromWorkout(AccountabilityWorkout w,int revision,DateOnly today)=>new(){
        Revision=revision,RequestId=Guid.NewGuid(),Name=w.Name,Date=today,TimeAllowed=(w.TimeLimitMinutes??120)/60m,
        LatePenalty=w.LatePenalty??0,Cap=w.Cap,CompletionReward=w.CompletionReward??0,
        IncompleteExerciseFee=w.Exercises.FirstOrDefault()?.Penalty??0,CoachIncentivePerRep=w.Exercises.FirstOrDefault()?.ExtraRepRate??0,
        Exercises=w.Exercises.Select(e=>new ExerciseInput{Name=e.Name,Sets=e.Sets,Reps=e.Reps,MaxWeight=e.MaxWeight??0,Rpe=e.Rpe,MuscleFocus=e.MuscleFocus,Type=e.Type,Basis=e.Basis,Rate=e.Rate}).ToList()
    };
    [HttpGet("template")]
    public async Task<IActionResult> Template(Guid clientId,string workoutId,CancellationToken ct)
    {
        if(!SimpleCoaching.Enabled(configuration))return NotFound();
        var actor=(await users.GetAsync(HttpContext,ct)).User.UserId;
        if(!await IsAssignedCoachAsync(actor,clientId,ct))return NotFound();
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
    [HttpGet("reuse/{workoutId}")]
    public async Task<IActionResult> Reuse(Guid clientId,string workoutId,CancellationToken ct)
    {
        var actor=(await users.GetAsync(HttpContext,ct)).User.UserId;
        if(!await IsAssignedCoachAsync(actor,clientId,ct))return NotFound();
        var row=await simple.ReadAsync(actor,clientId,true,ct);
        if(row is null)return NotFound();
        var workout=AccountabilityDocument.Parse(row.StateJson).SnapWorkouts.SingleOrDefault(w=>w.Simple&&w.Id==workoutId);
        if(workout is null)return NotFound();
        var clock=await ledger.ClockAsync(clientId,ct);
        ViewData["LoadedTemplateId"]=workout.Id;
        return await Page(clientId,FromWorkout(workout,row.Revision,clock.Today),ct);
    }
    [HttpPost("repeat/{workoutId}"),ValidateAntiForgeryToken,EnableRateLimiting("access-code")]
    public async Task<IActionResult> Repeat(Guid clientId,string workoutId,CancellationToken ct)
    {
        var actor=(await users.GetAsync(HttpContext,ct)).User.UserId;
        if(!await IsAssignedCoachAsync(actor,clientId,ct))return NotFound();
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
    public async Task<IActionResult> Status(Guid clientId,CancellationToken ct)
    {
        if(!SimpleCoaching.Enabled(configuration))return NotFound();
        var user=(await users.GetAsync(HttpContext,ct)).User;
        try
        {
            var assignedCoach=await IsAssignedCoachAsync(user.UserId,clientId,ct);
            await simple.RefreshAsync(user.UserId,clientId,assignedCoach,ct);var row=await simple.ReadAsync(user.UserId,clientId,assignedCoach,ct);
            var state=row is null?new AccountabilityState():AccountabilityDocument.Parse(row.StateJson);
            return Json(new{nowUtc=DateTimeOffset.UtcNow,workouts=state.SnapWorkouts.Where(w=>w.Simple).Select(w=>new{w.Id,w.DeadlineUtc,finished=state.SnapResults.GetValueOrDefault(w.Id)?.Finalized==true,finishedAtUtc=state.SnapResults.GetValueOrDefault(w.Id)?.CompletedAtUtc})});
        }
        catch(UnauthorizedAccessException){return NotFound();}
    }
    [HttpPost("estimate"),ValidateAntiForgeryToken,EnableRateLimiting("access-code"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Estimate(Guid clientId,AssignmentInput input,CancellationToken ct)
    {
        if(!SimpleCoaching.Enabled(configuration)||estimates is null)return NotFound();
        var user=(await users.GetAsync(HttpContext,ct)).User;
        if(!await access.CanAssignSnapAsync(user.UserId,clientId,ct))return NotFound();
        if(!ModelState.IsValid||input.Exercises.Count is <1 or >15||input.Exercises.Any(e=>e.Sets is <1 or >10||e.Reps is <1 or >100))return BadRequest(new{message="Enter exercise names, sets and reps to estimate."});
        try
        {
            foreach(var e in input.Exercises)SnapWorkoutEngine.ValidateFocus(e.MuscleFocus);
            var workout=new AccountabilityWorkout{Exercises=input.Exercises.Select(e=>new AccountabilityExercise{Name=e.Name,Sets=e.Sets,Reps=e.Reps,MaxWeight=e.MaxWeight,Rpe=e.Rpe,MuscleFocus=e.MuscleFocus}).ToList()};
            var reading=await estimates.EstimateAsync(clientId,workout,null,(await ledger.ClockAsync(clientId,ct)).Today,ct);
            return Json(new{estimate=reading,message=reading is null?"Add the client's weight, height, date of birth and sex to calculate estimates.":null});
        }
        catch(ArgumentException e){return BadRequest(new{message=e.Message});}
    }
    [HttpPost("assign"),ValidateAntiForgeryToken,EnableRateLimiting("access-code"),RequestSizeLimit(65536)]
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
            var uniqueName=UniqueName(input.Name,prior,now);
            await simple.AssignAsync(user.UserId,clientId,input.Revision,input.RequestId,new(){Name=uniqueName,Date=input.Date.ToString("yyyy-MM-dd"),Cap=input.Cap,CompletionReward=input.CompletionReward,
                TimeLimitMinutes=(int)Math.Ceiling(input.TimeAllowed*60),LatePenalty=input.LatePenalty,Exercises=input.Exercises.Select(e=>new AccountabilityExercise{Name=e.Name.Trim(),Sets=e.Sets,Reps=e.Reps,MaxWeight=e.MaxWeight,Type=e.Type,Basis=e.Basis,Rate=e.Rate,Penalty=input.IncompleteExerciseFee,CompletionReward=0,Rpe=e.Rpe,MuscleFocus=e.MuscleFocus,ExtraRepRate=input.CoachIncentivePerRep}).ToList()},ct);
            TempData["SimpleStatus"]="Workout assigned. Your client can open it in their portal.";return RedirectToAction(nameof(Index),new{clientId});
        }
        catch(UnauthorizedAccessException){return NotFound();}
        catch(Exception e)when(e is ArgumentException or AccountabilityConflictException){ModelState.AddModelError("",e.Message);return await Page(clientId,input,ct);}
    }
    [HttpPost("result"),ValidateAntiForgeryToken,EnableRateLimiting("access-code"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Result(Guid clientId,ResultInput input,CancellationToken ct)
    {
        if(!SimpleCoaching.Enabled(configuration))return NotFound();
        try
        {
            if(!ModelState.IsValid)throw new ArgumentException("Check the completed reps and weight.");
            var finish=input.SubmitMode=="finish"||(Request.HasFormContentType&&Request.Form["Action"]=="finish");
            var user=(await users.GetAsync(HttpContext,ct)).User;
            await simple.ResultAsync(user.UserId,clientId,await IsAssignedCoachAsync(user.UserId,clientId,ct),input.Revision,input.WorkoutId,new(){Finalized=finish,DurationMinutes=null,Sets=input.Exercises.ToDictionary(e=>e.Id,e=>e.Sets.Select(s=>new AccountabilitySet{Reps=s.Reps,Weight=s.Weight,Rpe=e.Rpe}).ToList())},ct);
            TempData["SimpleStatus"]=finish?"Workout finished. Your coach can see the results and calculated account entries.":"Progress saved.";
        }
        catch(UnauthorizedAccessException){return NotFound();}
        catch(Exception e)when(e is ArgumentException or AccountabilityConflictException){ModelState.AddModelError("",e.Message);return await Page(clientId,null,ct,input);}
        return RedirectToAction(nameof(Index),new{clientId,assignment=input.WorkoutId});
    }
    public sealed class AssignmentInput
    {
        public int Revision{get;set;}
        public Guid RequestId{get;set;}=Guid.NewGuid();
        [Required,StringLength(100)]public string Name{get;set;}="Snap Workout";
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
        [Required,StringLength(250)]public string Name{get;set;}="";
        [Range(typeof(decimal),"1","10")]public decimal? Rpe{get;set;}
        [StringLength(250)]public string? MuscleFocus{get;set;}
        public int Sets{get;set;}=3;public int Reps{get;set;}=10;public decimal MaxWeight{get;set;}
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
