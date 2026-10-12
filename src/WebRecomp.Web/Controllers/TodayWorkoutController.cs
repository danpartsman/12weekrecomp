using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.WorkoutTargeting;

namespace WebRecomp.Web.Controllers;

public sealed class TodayWorkoutController(RecompDbContext db, AppUserContextService users, WorkoutTargetService targets) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var context=await users.GetAsync(HttpContext,cancellationToken);
        if(context.Client is null || context.Program is null) return RedirectToAction("Index","Intake");
        Response.Headers["Cache-Control"]="no-store";
        return View(await targets.BuildAsync(context,new TodayWorkoutViewModel(),cancellationToken));
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Recommend(TodayWorkoutViewModel model,CancellationToken cancellationToken)
    {
        var context=await users.GetAsync(HttpContext,cancellationToken);
        if(context.Client is null || context.Program is null) return RedirectToAction("Index","Intake");
        model.Date=AppUserContextService.GetClientToday(context.Client);
        ValidateSelections(model);
        if(!ModelState.IsValid) return View("Index",model);
        return View("Index",await targets.BuildAsync(context,model,cancellationToken));
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(TodayWorkoutViewModel model,string choiceKey,string choiceRevision,CancellationToken cancellationToken)
    {
        var context=await users.GetAsync(HttpContext,cancellationToken);
        if(context.Client is null || context.Program is null) return RedirectToAction("Index","Intake");
        model.Date=AppUserContextService.GetClientToday(context.Client);
        ValidateSelections(model);
        if(!ModelState.IsValid) return View("Index",model);
        // Recompute from owned records; posted exercise IDs, effort caps and eligibility are never trusted.
        var result=await targets.BuildAsync(context,model,cancellationToken);
        var choice=result.Choices.SingleOrDefault(x=>x.Key==choiceKey);
        if(!result.CanStart || choice is null) { ModelState.AddModelError("","Update Scale & Feel and review a current recommendation first."); return View("Index",result); }
        if(choice.Revision!=choiceRevision) {ModelState.AddModelError("","Your recommendation changed since the preview. Review the updated exercises before using it.");return View("Index",result);}
        var today=AppUserContextService.GetClientToday(context.Client);
        var now=DateTime.UtcNow;
        var source="TARGET:"+choice.Key+":"+choice.Revision[..16];
        await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,cancellationToken);
        var existing=await db.WorkoutSessions.SingleOrDefaultAsync(x=>x.ClientId==context.Client.ClientId
            && x.ProgramId==context.Program.ProgramId && x.WorkoutDate==today && !x.CompletedFlag && x.SourceRecordId==source,cancellationToken);
        if(existing is not null) return RedirectToAction("Index","DetailedWorkout",new{workoutSessionId=existing.WorkoutSessionId,date=today.ToString("yyyy-MM-dd")});
        var session=new WorkoutSession {
            WorkoutSessionId=Guid.NewGuid(),ClientId=context.Client.ClientId,ProgramId=context.Program.ProgramId,WorkoutDate=today,
            WorkoutName=((choice.Adapted?"Adapted: ":"")+choice.Name)[..Math.Min(120,((choice.Adapted?"Adapted: ":"")+choice.Name).Length)],WorkoutTypeCode=choice.TypeCode,
            // The customized draft owns its exercises; reusable templates/system recipes remain unchanged.
            SystemWorkoutCode=choice.SystemCode,DurationMin=choice.Minutes,SessionRpe=choice.TypeCode=="REST"?null:choice.Rpe,
            TypicalRestSec=choice.TypeCode=="REST"?null:(short)60,CompletedFlag=false,EntrySourceCode=RecompInputSourceCodes.DetailedWorkout,
            SourceRecordId=source,CalculationEngineVersion=WorkoutActualService.ActiveEngineVersionCode,CreatedAtUtc=now,UpdatedAtUtc=now,
            Notes="Today’s Workout recommendation. "+string.Join(" ",choice.Reasons)
        };
        db.WorkoutSessions.Add(session);
        short order=0;
        foreach(var exercise in choice.Exercises)
        {
            var row=new DetailedWorkoutExerciseRecord { DetailedWorkoutExerciseId=Guid.NewGuid(),WorkoutSessionId=session.WorkoutSessionId,
                ExerciseCatalogItemId=exercise.ExerciseId,ExerciseOrder=++order,ExerciseNameSnapshot=exercise.Name,MechanicsSnapshot=exercise.Mechanics,
                Notes=exercise.Note,CreatedAtUtc=now,UpdatedAtUtc=now };
            for(short set=1;set<=exercise.Sets;set++) row.Sets.Add(new DetailedWorkoutSetRecord { DetailedWorkoutSetId=Guid.NewGuid(),
                DetailedWorkoutExerciseId=row.DetailedWorkoutExerciseId,SetOrder=set,Reps=exercise.DurationSeconds.HasValue?null:(short)exercise.Reps,
                Rpe=exercise.Rpe,RestSeconds=60,DurationSeconds=exercise.DurationSeconds,CreatedAtUtc=now,UpdatedAtUtc=now });
            db.DetailedWorkoutExercises.Add(row);
        }
        // Accepted draft replaces the estimate only while there are no completed actuals today.
        if(!await db.WorkoutSessions.AnyAsync(x=>x.ClientId==context.Client.ClientId && x.ProgramId==context.Program.ProgramId
            && x.WorkoutDate==today && x.CompletedFlag,cancellationToken))
        {
            var plan=await db.PlannedWorkouts.SingleOrDefaultAsync(x=>x.ProgramId==context.Program.ProgramId&&x.PlannedDate==today,cancellationToken);
            if(plan is null) {plan=new PlannedWorkout {PlannedWorkoutId=Guid.NewGuid(),ClientId=context.Client.ClientId,ProgramId=context.Program.ProgramId,
                PlannedDate=today,WorkoutTypeCode=choice.TypeCode,CreatedAtUtc=now};db.PlannedWorkouts.Add(plan);}
            plan.WorkoutTypeCode=choice.TypeCode;plan.WorkoutTemplateId=null;plan.SystemWorkoutCode=choice.SystemCode;
            plan.PlannedDurationMin=choice.Minutes;plan.PlannedRpe=session.SessionRpe;plan.SourceCode="WORKOUT_TARGET";plan.Notes=session.WorkoutName;plan.UpdatedAtUtc=now;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        TempData["StatusMessage"]="Recommended workout is ready to review. Complete it to update actuals and nutrition.";
        return RedirectToAction("Index","DetailedWorkout",new{workoutSessionId=session.WorkoutSessionId,date=today.ToString("yyyy-MM-dd")});
    }
    private void ValidateSelections(TodayWorkoutViewModel model)
    {
        if(model.Minutes < 10 + model.Additions.Count * 4) ModelState.AddModelError("Minutes","Allow at least 10 minutes for the main session plus 4 minutes for each add-on.");
        if(model.Targets.Count>BodyRegions.All.Length || model.Targets.Any(x=>!BodyRegions.All.Any(r=>r.Code==x))) ModelState.AddModelError("Targets","Choose valid target regions.");
        if(model.Activity=="targeted"&&model.Targets.Count==0) ModelState.AddModelError("Targets","Choose at least one muscle target.");
        if(model.Additions.Any(x=>x is not ("posture" or "core" or "mobility" or "balance")) || model.Additions.Count>4) ModelState.AddModelError("Additions","Choose valid add-ons.");
        if(model.Equipment.Any(x=>x is not ("dumbbell" or "barbell" or "cable" or "machine" or "band" or "bench")) || model.Equipment.Count>6) ModelState.AddModelError("Equipment","Choose equipment from the list.");
        model.Targets=model.Targets.Distinct().ToList();model.Additions=model.Additions.Distinct().ToList();
    }
}
