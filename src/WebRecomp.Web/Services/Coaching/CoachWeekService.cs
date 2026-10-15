using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Services.Coaching;
public sealed class CoachWeekService(RecompDbContext db, Engine30DashboardProjectionService engine, ProgramPlanService plans,
    Engine30WorkoutUiCatalogService catalog, CoachWorkoutPlanService workouts)
{
    public static string Group(string? code,string category) => code is null?"Unscheduled":code=="REST"?"Rest":code=="MOBILITY_RECOVERY"?"Recovery":code is "MAJOR_LEGS" or "MAJOR_PUSH" or "MAJOR_PULL" or "FULL_BODY_STRENGTH"?"Hard strength":code=="ACCESSORY"?"Accessory":category switch {
        Engine30PrescriptionCategoryCodes.MajorStrength=>"Hard strength", Engine30PrescriptionCategoryCodes.AccessoryStrength=>"Accessory",
        _=>code is "TRUE_HIIT" or "ORANGETHEORY" or "MIXED_CONDITIONING" or "RESISTANCE_CIRCUIT" or "RUNNING_INTERVALS" or "CYCLING_INTERVALS" or "ROWING_INTERVALS"?"Conditioning":"Cardio"};
    public static decimal? MaintenanceCarbs(Engine30DashboardDayProjection d) => d.MaintenanceEnergyKcal>=4*d.ProteinG+9*d.FatG?(d.MaintenanceEnergyKcal-4*d.ProteinG-9*d.FatG)/4:null;
    public static bool ValidWeek(DateOnly week,DateOnly today,DateOnly programStart) => week>=today && week>=programStart && week<=today.AddDays(84) && Engine30DashboardProjectionService.WeekStart(week)==week;
    public async Task<CoachWeekPage> BuildAsync(AppClientContext context,DateOnly week,CoachWeekInput? input,CancellationToken ct)
    {
        var client=context.Client!;var program=context.Program!;
        if(!ValidWeek(week,AppUserContextService.GetClientToday(client),program.StartDate)) throw new ArgumentException("Choose an upcoming Monday within 12 weeks and after program start. Previous prescriptions are preserved.");
        var rows=await workouts.WeekAsync(client.ClientId,program.ProgramId,week,ct);
        var types=await catalog.GetActiveListAsync(ct);
        var options=types.Where(x=>x.IsSelectable || rows.Any(r=>r.WorkoutTypeCode==x.WorkoutTypeCode))
            .Select(x=>new CoachWeekWorkoutOption(x.WorkoutTypeCode,x.DisplayName,Group(x.WorkoutTypeCode,x.WorkoutCategoryCode))).ToArray();
        var planCode=await plans.GetPlanCodeForDateAsync(program,week,ct);
        var baseline=await engine.BuildPlanningWeekAsync(context,week,planCode,ct,includeCoachRules:false);
        var assigned=await new CoachPrescriptionRuleStore(db).ApplyAsync(client.ClientId,program.ProgramId,week,baseline,ct);
        IReadOnlyList<string>? suggested=null;
        if(rows.Count==0)
        {
            suggested=WeeklyPlanService.BuildRecommendedTemplate(program.SelectedPlanCode,program.AvailableTrainingDaysWeek,client.TrainingExperienceCode);
            var proposed=suggested.Select((code,i)=>new PlannedWorkout {PlannedWorkoutId=Guid.NewGuid(),ClientId=client.ClientId,ProgramId=program.ProgramId,PlannedDate=week.AddDays(i),WorkoutTypeCode=code}).ToArray();
            baseline=await engine.BuildPlanningWeekAsync(context,week,planCode,ct,includeCoachRules:false,plannedWorkoutOverrides:proposed);
        }
        var defaultGoals=suggested is not null?await new CoachPrescriptionRuleStore(db).ApplyAsync(client.ClientId,program.ProgramId,week,baseline,ct):assigned;
        var latest=await db.CoachPrescriptionRuleVersions.AsNoTracking().Where(x=>x.ClientId==client.ClientId && x.ProgramId==program.ProgramId).OrderByDescending(x=>x.CreatedAtUtc).ThenByDescending(x=>x.CoachPrescriptionRuleVersionId).FirstOrDefaultAsync(ct);
        if(input is null)
        {
            var activeCoaches=db.CoachClientRelationships.Where(x=>x.ClientId==client.ClientId && x.StatusCode=="ACTIVE" && x.RevokedAtUtc==null).Select(x=>x.CoachUserId);
            var effective=await db.CoachPrescriptionRuleVersions.AsNoTracking().Where(x=>x.ClientId==client.ClientId && x.ProgramId==program.ProgramId && x.EffectiveWeek<=week && activeCoaches.Contains(x.CoachUserId))
                .OrderByDescending(x=>x.EffectiveWeek).ThenByDescending(x=>x.CreatedAtUtc).ThenByDescending(x=>x.CoachPrescriptionRuleVersionId).FirstOrDefaultAsync(ct);
            var rules=effective is {IsEnabled:true}?JsonSerializer.Deserialize<CoachPrescriptionRules>(effective.RulesJson)!:new();
            rules.EffectiveWeek=week;rules.ExpectedVersionId=latest?.CoachPrescriptionRuleVersionId;rules.Reason="";rules.PreviewToken=null;
            // Defaults are explicit coach-editable goals, never a calorie top-up.
            rules.DailyProteinTarget ??=defaultGoals.Values.First().ProteinG;
            rules.WeeklyCarbsTarget ??=defaultGoals.Values.Sum(x=>x.RecommendedCarbsG);
            rules.WeeklyFatTarget ??=defaultGoals.Values.Sum(x=>x.FatG);
            rules.AdjustCarbsThenFat=false;rules.Enabled=true;
            input=new() {Rules=rules,Workouts=Enumerable.Range(0,7).Select(i=>rows.SingleOrDefault(x=>x.PlannedDate==week.AddDays(i))?.WorkoutTypeCode ?? suggested?[i] ?? "REST").ToList()};
        }
        var page=new CoachWeekPage {SuggestedSchedule=suggested is not null,ClientId=client.ClientId,ClientName=client.ClientName,Input=input,Options=options,
            Engine=baseline.Values.OrderBy(x=>x.Date).ToArray(),Assigned=assigned.Values.OrderBy(x=>x.Date).ToArray(),Fingerprint=CoachWorkoutPlanService.Fingerprint(rows)};
        return page;
    }
    public async Task PreviewAsync(AppClientContext context,CoachWeekPage page,CancellationToken ct)
    {
        var input=page.Input;var week=input.Rules.EffectiveWeek;var client=context.Client!;var program=context.Program!;
        var errors=CoachPrescriptionAllocator.Validate(input.Rules).ToList();
        if(input.Workouts.Count!=7 || input.Workouts.Any(code=>!page.Options.Any(x=>x.Code==code))) errors.Add("Choose seven valid workout days.");
        if(!input.Rules.Enabled) errors.Add("Enable the coach assignment to save this worksheet.");
        if(await db.WorkoutSessions.AnyAsync(x=>x.ClientId==client.ClientId && x.ProgramId==program.ProgramId && x.WorkoutDate>=week && x.WorkoutDate<=week.AddDays(6),ct)) errors.Add("This week contains a started or completed workout. Use a later week.");
        if(errors.Count>0) {page.Errors=errors;return;}
        var rows=await workouts.WeekAsync(client.ClientId,program.ProgramId,week,ct);
        var proposed=new List<PlannedWorkout>();
        for(var i=0;i<7;i++)
        {
            var date=week.AddDays(i);var previous=rows.SingleOrDefault(x=>x.PlannedDate==date);
            if(previous?.WorkoutTypeCode==input.Workouts[i]) {proposed.Add(previous);continue;}
            var runtime=await new Engine30WorkoutRuntimeProfileProvider(db,new Engine30WorkoutProfileService(db)).ResolveAsync(input.Workouts[i],ct);
            proposed.Add(new() {PlannedWorkoutId=previous?.PlannedWorkoutId ?? Guid.NewGuid(),RowVersion=previous?.RowVersion ?? [],ClientId=client.ClientId,ProgramId=program.ProgramId,PlannedDate=date,
                WorkoutTypeCode=input.Workouts[i],PlannedDurationMin=input.Workouts[i]=="REST"?0:runtime.Profile.DefaultDurationMinutes,
                PlannedRpe=runtime.Profile.DefaultRpe,SourceCode="COACH",Notes="Adjusted by your trainer",CreatedAtUtc=previous?.CreatedAtUtc ?? DateTime.UtcNow});
        }
        var planCode=await plans.GetPlanCodeForDateAsync(program,week,ct);
        var engineDays=await engine.BuildPlanningWeekAsync(context,week,planCode,ct,includeCoachRules:false,plannedWorkoutOverrides:proposed);
        var evaluation=CoachPrescriptionAllocator.Evaluate(engineDays.Values.ToArray(),input.Rules);
        page.Errors=evaluation.Errors;page.Preview=evaluation.Days;page.Proposed=proposed;
        // Candidate IDs/timestamps are deliberately excluded: preview and save reconstruct independently.
        var inputCopy=JsonSerializer.Deserialize<CoachWeekInput>(JsonSerializer.Serialize(input))!;inputCopy.PreviewToken=null;inputCopy.Rules.PreviewToken=null;
        page.PreviewToken=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {client.ClientId,program.ProgramId,Input=inputCopy,page.Fingerprint,Engine=engineDays.Values.OrderBy(x=>x.Date),Assigned=page.Assigned,Targets=page.Preview}))));
    }
    public async Task<bool> SaveAsync(AppClientContext context,CoachWeekPage page,CancellationToken ct)
    {
        if(page.Errors.Count>0 || page.Preview is null || page.PreviewToken!=page.Input.PreviewToken) return false;
        var client=context.Client!;var program=context.Program!;var week=page.Input.Rules.EffectiveWeek;var versionId=Guid.NewGuid();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct):null;
            if(await db.CoachPrescriptionRuleVersions.AnyAsync(x=>x.CoachPrescriptionRuleVersionId==versionId,ct)) return true;
            if(!await new CoachCenterService(db).CanAccessAsync(context.User.UserId,client.ClientId,ct)) return false;
            if(!await db.Clients.AnyAsync(x=>x.ClientId==client.ClientId && x.ClientStatusCode=="ACTIVE",ct) || !await db.Programs.AnyAsync(x=>x.ProgramId==program.ProgramId && x.ClientId==client.ClientId && x.ProgramStatusCode=="ACTIVE",ct)) return false;
            if(!ValidWeek(week,AppUserContextService.GetClientToday(client),program.StartDate)) return false;
            if(await db.WorkoutSessions.AnyAsync(x=>x.ClientId==client.ClientId && x.ProgramId==program.ProgramId && x.WorkoutDate>=week && x.WorkoutDate<=week.AddDays(6),ct)) return false;
            var latest=await db.CoachPrescriptionRuleVersions.Where(x=>x.ClientId==client.ClientId && x.ProgramId==program.ProgramId).OrderByDescending(x=>x.CreatedAtUtc).ThenByDescending(x=>x.CoachPrescriptionRuleVersionId).Select(x=>(Guid?)x.CoachPrescriptionRuleVersionId).FirstOrDefaultAsync(ct);
            if(latest!=page.Input.Rules.ExpectedVersionId) return false;
            var rows=await workouts.WeekAsync(client.ClientId,program.ProgramId,week,ct);
            if(CoachWorkoutPlanService.Fingerprint(rows)!=page.Fingerprint) return false;
            var now=DateTime.UtcNow;
            foreach(var candidate in page.Proposed)
            {
                var previous=rows.SingleOrDefault(x=>x.PlannedDate==candidate.PlannedDate);
                if(previous?.WorkoutTypeCode==candidate.WorkoutTypeCode) continue;
                var before=JsonSerializer.Serialize(new {Name=previous?.WorkoutTypeCode ?? "Not scheduled",Plan=previous,Targets=page.Assigned});
                candidate.UpdatedAtUtc=now;
                if(previous is not null) db.PlannedWorkouts.Update(candidate); else {candidate.CreatedAtUtc=now;db.PlannedWorkouts.Add(candidate);}
                db.CoachWorkoutPlanRevisions.Add(new() {CoachWorkoutPlanRevisionId=Guid.NewGuid(),ClientId=client.ClientId,ProgramId=program.ProgramId,CoachUserId=context.User.UserId,WorkoutDate=candidate.PlannedDate,
                    Reason=page.Input.Rules.Reason.Trim(),BeforeJson=before,AfterJson=JsonSerializer.Serialize(new {Name=page.Options.Single(x=>x.Code==candidate.WorkoutTypeCode).Name,Plan=candidate,Targets=page.Preview}),CreatedAtUtc=now});
            }
            db.CoachPrescriptionRuleVersions.Add(new() {CoachPrescriptionRuleVersionId=versionId,ClientId=client.ClientId,ProgramId=program.ProgramId,CoachUserId=context.User.UserId,
                EffectiveWeek=week,IsEnabled=true,RulesJson=JsonSerializer.Serialize(page.Input.Rules),Reason=page.Input.Rules.Reason.Trim(),CreatedAtUtc=now,
                PreviewJson=JsonSerializer.Serialize(new {Engine=page.Engine,Adjusted=page.Preview,Workouts=page.Proposed})});
            await db.SaveChangesAsync(ct);if(transaction is not null) await transaction.CommitAsync(ct);return true;
        });
    }
}
