using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
namespace WebRecomp.Web.Controllers;
public sealed record SimpleSnapPage(Guid ClientId,string Name,bool Coach,int Revision,DateOnly Today,AccountabilityState State,SimpleSnapController.AssignmentInput Draft);
[Authorize,Route("clients/{clientId:guid}/snap"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class SimpleSnapController(AppUserContextService users,SimpleCoachingService simple,CoachAccountabilityPreviewService access,AccountabilityLedger ledger,IConfiguration configuration):Controller
{
    private bool Coach=>User.IsInRole("Coach")||User.IsInRole("Administrator");
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid clientId,CancellationToken ct)=>await Page(clientId,null,ct);
    private async Task<IActionResult> Page(Guid clientId,AssignmentInput? draft,CancellationToken ct,ResultInput? retry=null)
    {
        if(!SimpleCoaching.Enabled(configuration))return NotFound();
        var user=(await users.GetAsync(HttpContext,ct)).User;
        try
        {
            var row=await simple.ReadAsync(user.UserId,clientId,Coach,ct);var clock=await ledger.ClockAsync(clientId,ct);
            var state=row is null?new AccountabilityState():AccountabilityDocument.Parse(row.StateJson);
            var retryWorkout=state.SnapWorkouts.SingleOrDefault(w=>w.Simple&&w.Id==retry?.WorkoutId);
            if(retryWorkout is not null&&retry is not null)
            {
                var result=new AccountabilityResult();
                foreach(var exercise in retryWorkout.Exercises)
                {
                    var entered=retry.Exercises.FirstOrDefault(e=>e.Id==exercise.Id)?.Sets;
                    result.Sets[exercise.Id]=Enumerable.Range(0,Math.Max(exercise.Sets,Math.Min(20,entered?.Count??0))).Select(i=>new AccountabilitySet{Reps=entered is not null&&i<entered.Count?entered[i].Reps:null,Weight=entered is not null&&i<entered.Count?entered[i].Weight:null}).ToList();
                }
                state.SnapResults[retryWorkout.Id]=result;
            }
            return View("Index",new SimpleSnapPage(clientId,await access.ClientNameAsync(clientId,ct)??"Client",Coach&&await access.CanAssignSnapAsync(user.UserId,clientId,ct),row?.Revision??0,clock.Today,state,draft??new(){Date=clock.Today}));
        }
        catch(UnauthorizedAccessException){return NotFound();}
    }
    [HttpPost("assign"),ValidateAntiForgeryToken,EnableRateLimiting("access-code"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Assign(Guid clientId,AssignmentInput input,CancellationToken ct)
    {
        if(!SimpleCoaching.Enabled(configuration)||!Coach)return NotFound();
        if(!ModelState.IsValid)return await Page(clientId,input,ct);
        try
        {
            var user=(await users.GetAsync(HttpContext,ct)).User;
            await simple.AssignAsync(user.UserId,clientId,input.Revision,input.RequestId,new(){Name=input.Name.Trim(),Date=input.Date.ToString("yyyy-MM-dd"),Cap=input.Cap,CompletionReward=input.CompletionReward,
                DeadlineUtc=input.DeadlineUtc,Exercises=input.Exercises.Select(e=>new AccountabilityExercise{Name=e.Name.Trim(),Sets=e.Sets,Reps=e.Reps,MaxWeight=e.MaxWeight,Type=e.Type,Basis=e.Basis,Rate=e.Rate,CompletionReward=e.CompletionReward,ExtraRepRate=e.ExtraRepRate}).ToList()},ct);
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
            var user=(await users.GetAsync(HttpContext,ct)).User;
            await simple.ResultAsync(user.UserId,clientId,Coach,input.Revision,input.WorkoutId,new(){Finalized=input.Action=="finish",Sets=input.Exercises.ToDictionary(e=>e.Id,e=>e.Sets.Select(s=>new AccountabilitySet{Reps=s.Reps,Weight=s.Weight}).ToList())},ct);
            TempData["SimpleStatus"]=input.Action=="finish"?"Workout finished. Your coach can see the results and calculated account entries.":"Progress saved.";
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
        public DateTimeOffset? DeadlineUtc{get;set;}
        public decimal Cap{get;set;}=100;
        public decimal CompletionReward{get;set;}
        public List<ExerciseInput> Exercises{get;set;}=[new()];
    }
    public sealed class ExerciseInput
    {
        [Required,StringLength(250)]public string Name{get;set;}="";
        public int Sets{get;set;}=3;public int Reps{get;set;}=10;public decimal MaxWeight{get;set;}
        public string Type{get;set;}="fine";public string Basis{get;set;}="incomplete-reps";public decimal Rate{get;set;}
        public decimal CompletionReward{get;set;}public decimal ExtraRepRate{get;set;}
    }
    public sealed class ResultInput
    {
        public int Revision{get;set;}public string WorkoutId{get;set;}="";public string Action{get;set;}="progress";
        public List<ExerciseResults> Exercises{get;set;}=[];
    }
    public sealed class ExerciseResults{public string Id{get;set;}="";public List<SetInput> Sets{get;set;}=[];}
    public sealed class SetInput{public int? Reps{get;set;}public decimal? Weight{get;set;}}
}
