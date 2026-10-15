using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Dashboard2;
using WebRecomp.Web.Services.Dashboard3;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutIntelligence;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Services.Coaching;
public sealed class CoachClientPlanService(RecompDbContext db, Engine30DashboardProjectionService engine,
    ProgramPlanService plans, SystemWorkoutPreviewService workouts, DashboardService dashboard, RecompCockpitService cockpit)
{
    public static decimal? Pounds(decimal? value, string unit) => unit == "KG" && value.HasValue ? value * 2.2046226218m : value;
    public static string Reps(WorkoutTemplateExerciseRecord x)
    {
        try { var values=JsonSerializer.Deserialize<int[]>(x.SetRepsJson ?? "null"); if(values?.Length==x.Sets) return string.Join(", ",values); }
        catch(JsonException) { }
        return x.RepsPrescription ?? x.Reps.ToString();
    }
    public async Task<(string Name,IReadOnlyList<CoachPlanExercise> Exercises)> ResolveAsync(Guid clientId, Guid configId, PlannedWorkout? plan, string fallback, CancellationToken ct)
    {
        if(plan is null) return(fallback,Array.Empty<CoachPlanExercise>());
        if(plan.WorkoutTemplateId is Guid id)
        {
            var template=await db.WorkoutTemplates.AsNoTracking().Include(x=>x.Exercises).SingleOrDefaultAsync(x=>x.WorkoutTemplateId==id && x.ClientId==clientId,ct);
            if(template is not null) return (template.TemplateName,template.Exercises.OrderBy(x=>x.ExerciseOrder).Select(x=>new CoachPlanExercise(x.ExerciseCatalogItemId,x.ExerciseNameSnapshot,x.Sets,Reps(x),Pounds(x.MaxWeight,x.LoadUnitCode))).ToArray());
        }
        var code=plan?.SystemWorkoutCode ?? SystemWorkoutRecipeCatalog.GetDefaultPresentationForEngine(plan?.WorkoutTypeCode ?? "REST")?.SystemCode;
        if(code is not null)
        {
            var preview=await workouts.BuildAsync(clientId,configId,code,"LB",ct,allowCatalogWrites:false);
            if(preview is not null) return(preview.DisplayName,preview.Exercises.Select(x=>new CoachPlanExercise(x.ExerciseId,x.ExerciseName,x.Sets,string.Join(", ",x.SetReps),Pounds(x.MaxWeight,x.LoadUnitCode))).ToArray());
        }
        return(fallback,Array.Empty<CoachPlanExercise>());
    }
    public async Task<CoachClientPlan> BuildAsync(AppClientContext context, DateOnly? requested, CancellationToken ct)
    {
        var client=context.Client!;var program=context.Program!;var today=AppUserContextService.GetClientToday(client);
        var week=Engine30DashboardProjectionService.WeekStart(requested ?? today);var end=week.AddDays(6);
        var planCode=await plans.GetPlanCodeForDateAsync(program,week<program.StartDate?program.StartDate:week,ct);
        var baseline=await engine.BuildPlanningWeekAsync(context,week,planCode,ct,includeCoachRules:false);
        var assigned=await new CoachPrescriptionRuleStore(db).ApplyAsync(client.ClientId,program.ProgramId,week,baseline,ct);
        var scheduled=await db.PlannedWorkouts.AsNoTracking().Where(x=>x.ClientId==client.ClientId && x.ProgramId==program.ProgramId && x.PlannedDate>=week && x.PlannedDate<=end).ToDictionaryAsync(x=>x.PlannedDate,ct);
        var intake=await db.NutritionDays.AsNoTracking().Where(x=>x.ClientId==client.ClientId && x.ProgramId==program.ProgramId && x.NutritionDate>=week && x.NutritionDate<=end).ToDictionaryAsync(x=>x.NutritionDate,ct);
        var sessions=await db.WorkoutSessions.AsNoTracking().Where(x=>x.ClientId==client.ClientId && x.ProgramId==program.ProgramId && x.WorkoutDate>=week && x.WorkoutDate<=end).ToListAsync(ct);
        var canonical=await DetailedWorkoutEngine30Adapter.CanonicalCompletedSessionsAsync(db,sessions.Where(x=>x.CompletedFlag).ToArray(),ct);
        var sessionIds=canonical.Select(x=>x.WorkoutSessionId).ToArray();
        var details=await db.DetailedWorkoutExercises.AsNoTracking().Include(x=>x.Sets).Where(x=>sessionIds.Contains(x.WorkoutSessionId)).ToListAsync(ct);
        var days=new List<CoachPlanDay>();
        foreach(var date in baseline.Keys.OrderBy(x=>x))
        {
            scheduled.TryGetValue(date,out var plan);intake.TryGetValue(date,out var actual);
            var resolved=await ResolveAsync(client.ClientId,program.ConfigVersionId,plan,baseline[date].WorkoutDisplayName,ct);
            var completed=canonical.Where(x=>x.WorkoutDate==date).Select(x=>new CoachActualWorkout(x,details.Where(e=>e.WorkoutSessionId==x.WorkoutSessionId).OrderBy(e=>e.ExerciseOrder).ToArray())).ToArray();
            days.Add(new(date,baseline[date],assigned[date],actual,plan,resolved.Name,resolved.Exercises,completed,
                date>=today && date<=today.AddDays(84) && !sessions.Any(x=>x.WorkoutDate==date)));
        }
        var day=await dashboard.BuildAsync(context,today,ct);
        var progress=await cockpit.BuildAsync(context,day,MacroDayPlanner.Build(day,null,false,23,null),ct,captureHistory:false);
        var history=await db.CoachWorkoutPlanRevisions.AsNoTracking().Where(x=>x.ClientId==client.ClientId && x.ProgramId==program.ProgramId)
            .OrderByDescending(x=>x.CreatedAtUtc).Take(20).ToListAsync(ct);
        var ids=history.Select(x=>x.CoachUserId).Distinct().ToArray();
        var authors=await db.UserAccounts.Where(x=>ids.Contains(x.UserId)).ToDictionaryAsync(x=>x.UserId,x=>x.PreferredDisplayName ?? x.DisplayName ?? "Trainer",ct);
        return new() {Today=today,WeekStart=week,Days=days,Progress=progress,History=history,Authors=authors};
    }
}
