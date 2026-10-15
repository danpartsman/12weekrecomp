using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutIntelligence;
using WebRecomp.Web.ViewModels;
using WebRecomp.WorkoutIntelligence;
namespace WebRecomp.Web.Services.Coaching;
public sealed class CoachWorkoutPlanService(RecompDbContext db, Engine30WorkoutUiCatalogService catalog,
    CoachClientPlanService clientPlans, Engine30DashboardProjectionService engine, ProgramPlanService plans)
{
    public static bool Editable(DateOnly date, DateOnly today, DateOnly programStart, bool sessionExists) => date>=today && date>=programStart && date<=today.AddDays(84) && !sessionExists;
    public static int[] ParseReps(string text,int sets)
    {
        var values=text.Split(',',StringSplitOptions.TrimEntries).Select(x=>int.TryParse(x,NumberStyles.None,CultureInfo.InvariantCulture,out var n)?n:0).ToArray();
        if(sets is <1 or >20 || values.Any(x=>x is <1 or >500) || (values.Length!=1 && values.Length!=sets)) throw new ArgumentException("Enter one rep count or a comma-separated count for each set (1–500 reps).");
        return values.Length==1?Enumerable.Repeat(values[0],sets).ToArray():values;
    }
    public async Task<PlannedWorkout> SourceAsync(AppClientContext context,DateOnly date,string choice,CancellationToken ct)
    {
        var result=new PlannedWorkout {PlannedWorkoutId=Guid.NewGuid(),ClientId=context.Client!.ClientId,ProgramId=context.Program!.ProgramId,PlannedDate=date,WorkoutTypeCode="REST"};
        var types=await catalog.GetActiveMapAsync(ct);
        if(choice.StartsWith("TPL:") && Guid.TryParse(choice[4..],out var id))
        {
            var template=await db.WorkoutTemplates.AsNoTracking().SingleOrDefaultAsync(x=>x.ClientId==result.ClientId && x.WorkoutTemplateId==id,ct)
                ?? throw new ArgumentException("Choose a workout saved for this client.");
            result.WorkoutTemplateId=id;result.WorkoutTypeCode=template.WorkoutTypeCode;result.PlannedDurationMin=template.DefaultDurationMin;
        }
        else if(choice.StartsWith("SYS:") && SystemWorkoutRecipeCatalog.TryGet(choice[4..],out var recipe) && recipe is not null && types.TryGetValue(recipe.EngineWorkoutTypeCode,out var type))
        {
            result.SystemWorkoutCode=recipe.SystemCode;result.WorkoutTypeCode=recipe.EngineWorkoutTypeCode;
            result.PlannedDurationMin=SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(recipe,type.DefaultMinutes);
            result.PlannedRpe=SystemWorkoutRecipeCatalog.ResolveDefaultRpe(recipe,new Engine30WorkoutTypeCatalog().TryGet(recipe.EngineWorkoutTypeCode,out var definition) && definition is not null ? definition.DefaultRpe : 5);
        }
        else if(choice.StartsWith("TYPE:") && types.TryGetValue(choice[5..],out var categoryType))
        {result.WorkoutTypeCode=categoryType.WorkoutTypeCode;result.PlannedDurationMin=categoryType.DefaultMinutes;result.PlannedRpe=new Engine30WorkoutTypeCatalog().TryGet(categoryType.WorkoutTypeCode,out var definition) && definition is not null ? definition.DefaultRpe : 5;}
        else throw new ArgumentException("Choose an available workout.");
        if(!types.ContainsKey(result.WorkoutTypeCode)) throw new ArgumentException("The workout's training category is unavailable.");
        var runtime=(await new Engine30WorkoutRuntimeProfileProvider(db,new Engine30WorkoutProfileService(db)).ResolveAsync(result.WorkoutTypeCode,ct)).Profile;
        if(result.WorkoutTypeCode=="REST") result.PlannedDurationMin=0;
        else if(result.SystemWorkoutCode is string systemCode && SystemWorkoutRecipeCatalog.TryGet(systemCode,out var selectedRecipe) && selectedRecipe is not null)
            result.PlannedDurationMin=SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(selectedRecipe,runtime.DefaultDurationMinutes);
        else if(!result.WorkoutTemplateId.HasValue || result.PlannedDurationMin is null or <=0)
            result.PlannedDurationMin=runtime.DefaultDurationMinutes;
        result.PlannedRpe=result.SystemWorkoutCode is string recipeCode && SystemWorkoutRecipeCatalog.TryGet(recipeCode,out var selected) && selected is not null
            ? SystemWorkoutRecipeCatalog.ResolveDefaultRpe(selected,runtime.DefaultRpe) : runtime.DefaultRpe;
        return result;
    }
    public static bool SupportsExerciseEditing(PlannedWorkout source)
    {
        if(source.WorkoutTemplateId.HasValue) return true;
        var recipe=source.SystemWorkoutCode is string code && SystemWorkoutRecipeCatalog.TryGet(code,out var selected)?selected:SystemWorkoutRecipeCatalog.GetDefaultPresentationForEngine(source.WorkoutTypeCode);
        return recipe is not null && recipe.Slots.Count>0 && recipe.Slots.All(x=>x.DurationSeconds is null && x.DistanceMeters is null && x.BlockType is null);
    }
    public async Task<CoachWorkoutPage> PageAsync(AppClientContext context,DateOnly date,string? choice,CoachWorkoutInput? input,CancellationToken ct)
    {
        var client=context.Client!;var program=context.Program!;
        var existing=await db.PlannedWorkouts.AsNoTracking().SingleOrDefaultAsync(x=>x.ClientId==client.ClientId && x.ProgramId==program.ProgramId && x.PlannedDate==date,ct);
        choice ??=existing?.WorkoutTemplateId is Guid templateId ? "TPL:"+templateId : existing?.SystemWorkoutCode is string system ? "SYS:"+system : "TYPE:"+(existing?.WorkoutTypeCode ?? "REST");
        var source=await SourceAsync(context,date,input?.Choice ?? choice,ct);
        var types=await catalog.GetActiveListAsync(ct);
        var templates=await db.WorkoutTemplates.AsNoTracking().Where(x=>x.ClientId==client.ClientId).OrderBy(x=>x.TemplateName).ToListAsync(ct);
        var options=SystemWorkoutRecipeCatalog.PlanningRecipes.Where(x=>types.Any(t=>t.WorkoutTypeCode==x.EngineWorkoutTypeCode)).Select(x=>new CoachWorkoutOption("SYS:"+x.SystemCode,"System · "+x.DisplayName))
            .Concat(templates.Select(x=>new CoachWorkoutOption("TPL:"+x.WorkoutTemplateId,"Saved · "+x.TemplateName)))
            .Concat(types.Where(x=>x.IsSelectable).Select(x=>new CoachWorkoutOption("TYPE:"+x.WorkoutTypeCode,"Category · "+x.DisplayName))).ToList();
        if(!options.Any(x=>x.Value==choice)) options.Add(new(choice,"Current workout"));
        var resolved=await clientPlans.ResolveAsync(client.ClientId,program.ConfigVersionId,source,types.First(x=>x.WorkoutTypeCode==source.WorkoutTypeCode).DisplayName,ct);
        var canCustomize=SupportsExerciseEditing(source);
        var exercises=await db.ExerciseCatalogItems.AsNoTracking().Where(x=>x.IsActive && x.SourceCode!="12WR_CSV_UNMAPPED").OrderBy(x=>x.Name)
            .Select(x=>new CoachWorkoutOption(x.ExerciseCatalogItemId.ToString(),x.Name)).ToListAsync(ct);
        var existingChoice=existing?.WorkoutTemplateId is Guid currentTemplate ? "TPL:"+currentTemplate : existing?.SystemWorkoutCode is string currentSystem ? "SYS:"+currentSystem : "TYPE:"+(existing?.WorkoutTypeCode ?? "REST");
        var sameSelection=existing is not null && choice==existingChoice;
        input ??=new() {Date=date,Choice=choice,Name=resolved.Name,Minutes=sameSelection && existing?.PlannedDurationMin is >0m?existing.PlannedDurationMin.Value:source.PlannedDurationMin ?? 0,
            Rpe=(sameSelection?existing?.PlannedRpe:null) ?? source.PlannedRpe ?? 5,Exercises=resolved.Exercises.Select(x=>new CoachWorkoutExerciseInput {ExerciseId=x.ExerciseId,Name=x.Name,Sets=x.Sets,Reps=x.Reps,WeightLb=x.WeightLb}).ToList()};
        return new() {ClientId=client.ClientId,ClientName=client.ClientName,Input=input,Options=options,Catalog=exercises,CanCustomize=canCustomize,SourceName=resolved.Name};
    }
    public async Task<string> AvailableRoutineNameAsync(Guid clientId,string requested,CancellationToken ct)
    {
        var name=requested.Trim();
        if(!await db.WorkoutTemplates.AsNoTracking().AnyAsync(x=>x.ClientId==clientId && x.TemplateName.ToUpper()==name.ToUpper(),ct)) return name;
        var baseName=System.Text.RegularExpressions.Regex.Replace(name,@" \(trainer [0-9]+\)$","");
        for(var version=2;version<=10000;version++)
        {
            var suffix=$" (trainer {version})";var candidate=baseName[..Math.Min(baseName.Length,150-suffix.Length)]+suffix;
            if(!await db.WorkoutTemplates.AsNoTracking().AnyAsync(x=>x.ClientId==clientId && x.TemplateName.ToUpper()==candidate.ToUpper(),ct)) return candidate;
        }
        throw new ArgumentException("Choose a new routine name.");
    }
    public async Task<(CoachWorkoutPage Page,PlannedWorkout Candidate,string Fingerprint)> PreviewAsync(AppClientContext context,CoachWorkoutInput input,CancellationToken ct)
    {
        var client=context.Client!;var program=context.Program!;var today=AppUserContextService.GetClientToday(client);
        if(!Editable(input.Date,today,program.StartDate,await db.WorkoutSessions.AnyAsync(x=>x.ClientId==client.ClientId && x.ProgramId==program.ProgramId && x.WorkoutDate==input.Date,ct)))
            throw new ArgumentException("Choose a date in the next 12 weeks with no started or completed workout. Previous workouts remain locked.");
        var page=await PageAsync(context,input.Date,input.Choice,input,ct);
        if(!input.Customize) input.Name=page.SourceName;
        var candidate=await SourceAsync(context,input.Date,input.Choice,ct);
        if(candidate.WorkoutTypeCode=="REST" && (input.Minutes!=0 || input.Customize)) throw new ArgumentException("Rest days have zero workout minutes and no exercise routine.");
        if(candidate.WorkoutTypeCode!="REST" && input.Minutes<=0) throw new ArgumentException("Enter workout minutes greater than zero.");
        if(input.Customize)
        {
            if(!page.CanCustomize) throw new ArgumentException("Choose a rep-based system workout or a saved workout to customize exercises. Timed/class workouts retain their original format.");
            if(input.Exercises.Count is <1 or >40) throw new ArgumentException("Use between 1 and 40 exercises.");
            var ids=input.Exercises.Select(x=>x.ExerciseId).Distinct().ToArray();
            var valid=await db.ExerciseCatalogItems.AsNoTracking().Where(x=>ids.Contains(x.ExerciseCatalogItemId) && x.IsActive && (x.SourceCode!="12WR_CSV_UNMAPPED" || x.SourceExternalId.StartsWith(client.ClientId.ToString("N")+":"))).Select(x=>x.ExerciseCatalogItemId).ToListAsync(ct);
            if(ids.Length!=valid.Count) throw new ArgumentException("An exercise is unavailable or belongs to another client.");
            foreach(var exercise in input.Exercises) ParseReps(exercise.Reps,exercise.Sets);
            input.Name=await AvailableRoutineNameAsync(client.ClientId,input.Name,ct);
        }
        candidate.PlannedDurationMin=input.Minutes;candidate.PlannedRpe=input.Rpe;candidate.SourceCode="COACH";
        var week=Engine30DashboardProjectionService.WeekStart(input.Date);
        var rows=await WeekAsync(client.ClientId,program.ProgramId,week,ct);
        var fingerprint=Fingerprint(rows);
        var proposed=rows.Where(x=>x.PlannedDate!=input.Date).Append(candidate).ToArray();
        var planCode=await plans.GetPlanCodeForDateAsync(program,week<program.StartDate?program.StartDate:week,ct);
        page.Before=await engine.BuildPlanningWeekAsync(context,week,planCode,ct);
        page.After=await engine.BuildPlanningWeekAsync(context,week,planCode,ct,plannedWorkoutOverrides:proposed);
        if(page.After.Values.Any(x=>x.CoachPrescriptionNotice?.Contains("need review")==true)) throw new ArgumentException("This schedule conflicts with the trainer's macro/calorie rules. Review the prescription rules before saving.");
        var copy=JsonSerializer.Deserialize<CoachWorkoutInput>(JsonSerializer.Serialize(input))!;copy.PreviewToken=null;
        page.PreviewToken=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {client.ClientId,program.ProgramId,Input=copy,Fingerprint=fingerprint,Before=page.Before,After=page.After}))));
        return(page,candidate,fingerprint);
    }
    public Task<List<PlannedWorkout>> WeekAsync(Guid clientId,Guid programId,DateOnly week,CancellationToken ct) => db.PlannedWorkouts.AsNoTracking().Where(x=>x.ClientId==clientId && x.ProgramId==programId && x.PlannedDate>=week && x.PlannedDate<=week.AddDays(6)).OrderBy(x=>x.PlannedDate).ToListAsync(ct);
    public static string Fingerprint(IEnumerable<PlannedWorkout> rows) => JsonSerializer.Serialize(rows.OrderBy(x=>x.PlannedDate));
    public async Task<bool> SaveAsync(AppClientContext context,CoachWorkoutInput input,PlannedWorkout candidate,string fingerprint,CoachWorkoutPage preview,CancellationToken ct)
    {
        var client=context.Client!;var program=context.Program!;var revisionId=Guid.NewGuid();var templateId=Guid.NewGuid();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct):null;
            if(!await new CoachCenterService(db).CanAccessAsync(context.User.UserId,client.ClientId,ct)) return false;
            if(await db.CoachWorkoutPlanRevisions.AnyAsync(x=>x.CoachWorkoutPlanRevisionId==revisionId,ct)) return true;
            if(!await db.Clients.AnyAsync(x=>x.ClientId==client.ClientId && x.ClientStatusCode=="ACTIVE",ct)) return false;
            if(!await db.Programs.AnyAsync(x=>x.ProgramId==program.ProgramId && x.ClientId==client.ClientId && x.ProgramStatusCode=="ACTIVE",ct)) return false;
            if(!Editable(input.Date,AppUserContextService.GetClientToday(client),program.StartDate,await db.WorkoutSessions.AnyAsync(x=>x.ClientId==client.ClientId && x.ProgramId==program.ProgramId && x.WorkoutDate==input.Date,ct))) return false;
            var week=Engine30DashboardProjectionService.WeekStart(input.Date);var rows=await WeekAsync(client.ClientId,program.ProgramId,week,ct);
            if(Fingerprint(rows)!=fingerprint) return false;
            var previous=rows.SingleOrDefault(x=>x.PlannedDate==input.Date);var now=DateTime.UtcNow;
            if(input.Customize)
            {
                if(await db.WorkoutTemplates.AnyAsync(x=>x.ClientId==client.ClientId && x.TemplateName.ToUpper()==input.Name.Trim().ToUpper(),ct)) return false;
                var ids=input.Exercises.Select(x=>x.ExerciseId).Distinct().ToArray();
                var exercises=await db.ExerciseCatalogItems.AsNoTracking().Where(x=>ids.Contains(x.ExerciseCatalogItemId) && x.IsActive && (x.SourceCode!="12WR_CSV_UNMAPPED" || x.SourceExternalId.StartsWith(client.ClientId.ToString("N")+":"))).ToDictionaryAsync(x=>x.ExerciseCatalogItemId,ct);
                if(exercises.Count!=ids.Length) return false;
                db.WorkoutTemplates.Add(new() {WorkoutTemplateId=templateId,ClientId=client.ClientId,TemplateName=input.Name.Trim(),WorkoutTypeCode=candidate.WorkoutTypeCode,DefaultDurationMin=input.Minutes,
                    CreatedAtUtc=now,UpdatedAtUtc=now,Exercises=input.Exercises.Select((x,i)=>new WorkoutTemplateExerciseRecord {WorkoutTemplateExerciseId=Guid.NewGuid(),WorkoutTemplateId=templateId,
                        ExerciseCatalogItemId=x.ExerciseId,ExerciseOrder=(short)(i+1),ExerciseNameSnapshot=exercises[x.ExerciseId].Name,MechanicsSnapshot=exercises[x.ExerciseId].Mechanics,
                        Sets=(short)x.Sets,Reps=(short)ParseReps(x.Reps,x.Sets)[0],SetRepsJson=JsonSerializer.Serialize(ParseReps(x.Reps,x.Sets)),MaxWeight=x.WeightLb,LoadUnitCode="LB",CreatedAtUtc=now,UpdatedAtUtc=now}).ToList()});
                candidate.WorkoutTemplateId=templateId;candidate.SystemWorkoutCode=null;
            }
            var stored=previous is null?candidate:await db.PlannedWorkouts.SingleAsync(x=>x.PlannedWorkoutId==previous.PlannedWorkoutId && x.ClientId==client.ClientId && x.ProgramId==program.ProgramId,ct);
            var original=await clientPlans.ResolveAsync(client.ClientId,program.ConfigVersionId,previous,preview.Before?[input.Date].WorkoutDisplayName ?? "Not scheduled",ct);
            var before=JsonSerializer.Serialize(new {Name=original.Name,Plan=previous,Targets=preview.Before});
            stored.WorkoutTypeCode=candidate.WorkoutTypeCode;stored.WorkoutTemplateId=candidate.WorkoutTemplateId;stored.SystemWorkoutCode=candidate.SystemWorkoutCode;
            stored.PlannedDurationMin=input.Minutes;stored.PlannedRpe=input.Rpe;stored.SourceCode="COACH";stored.Notes="Adjusted by your trainer";stored.UpdatedAtUtc=now;
            if(previous is null) {stored.CreatedAtUtc=now;db.PlannedWorkouts.Add(stored);}
            db.CoachWorkoutPlanRevisions.Add(new() {CoachWorkoutPlanRevisionId=revisionId,ClientId=client.ClientId,ProgramId=program.ProgramId,CoachUserId=context.User.UserId,WorkoutDate=input.Date,
                Reason=input.Reason.Trim(),BeforeJson=before,AfterJson=JsonSerializer.Serialize(new {Name=input.Customize?input.Name:preview.SourceName,Plan=stored,Targets=preview.After,Exercises=input.Customize?input.Exercises:null}),CreatedAtUtc=now});
            await db.SaveChangesAsync(ct);if(transaction is not null) await transaction.CommitAsync(ct);return true;
        });
    }
}
