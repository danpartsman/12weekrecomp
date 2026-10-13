using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutIntelligence;

namespace WebRecomp.Web.Services.WorkoutTargeting;

public sealed class WorkoutTargetService(RecompDbContext db, Engine30WorkoutUiCatalogService types,
    Engine30DashboardProjectionService projection)
{
    public async Task<TodayWorkoutViewModel> BuildDashboardAsync(AppClientContext context, CancellationToken ct)
    {
        var today = AppUserContextService.GetClientToday(context.Client!);
        var plan = await db.PlannedWorkouts.AsNoTracking().SingleOrDefaultAsync(x => x.ProgramId == context.Program!.ProgramId && x.PlannedDate == today, ct);
        var activity = plan is null ? "auto" : WorkoutTargetPolicy.ActivityFor(plan.WorkoutTypeCode);
        var definitions = await types.GetActiveMapAsync(ct);
        var definition = plan is not null ? definitions.GetValueOrDefault(plan.WorkoutTypeCode) : null;
        var templateMinutes = plan?.WorkoutTemplateId is Guid templateId
            ? await db.WorkoutTemplates.AsNoTracking().Where(x => x.ClientId == context.Client!.ClientId && x.WorkoutTemplateId == templateId)
                .Select(x => (decimal?)x.DefaultDurationMin).FirstOrDefaultAsync(ct) : null;
        var minutes = plan?.PlannedDurationMin is > 0 ? plan.PlannedDurationMin.Value
            : templateMinutes is > 0 ? templateMinutes.Value : definition?.DefaultMinutes is > 0 ? definition.DefaultMinutes : 45m;
        var plannedRpe = plan?.PlannedRpe ?? (definition?.CountsDedicatedHeavy == true ? 8m : activity is "recovery" or "mobility" ? 4m : 6m);
        var input = new TodayWorkoutViewModel { Activity = activity == "strength" ? "auto" : activity,
            Effort = plannedRpe >= 7 ? "hard" : plannedRpe <= 4 ? "easy" : "moderate",
            Minutes = Math.Clamp((int)minutes, 10, 120) };
        return await BuildAsync(context, input, ct);
    }

    public async Task<TodayWorkoutViewModel> BuildAsync(AppClientContext context, TodayWorkoutViewModel input, CancellationToken ct)
    {
        var client = context.Client!; var program = context.Program!;
        var today = AppUserContextService.GetClientToday(client);
        input.Date = today;
        input.CanStart = program.DetailedWorkoutEntryEnabled;
        input.Notices.Clear(); input.Choices.Clear(); input.Readiness.Clear();
        var checkin = await db.DailyCheckins.AsNoTracking().SingleOrDefaultAsync(x => x.ProgramId == program.ProgramId && x.CheckinDate == today, ct);
        var body = BodyCheckinCodec.Read(checkin?.Notes);
        input.BodyChecked = body.Checked;
        var feelings = body.Regions;
        if (checkin?.Notes?.StartsWith("12WR_BODY_V1:", StringComparison.Ordinal) != true)
            feelings = (await BodyCheckinCodec.PersistentPainAsync(db, program.ProgramId, today, ct)).Regions;
        if (!body.Checked) input.Notices.Add("Body readiness is incomplete. Complete today’s Scale & Feel body check before starting a recommendation.");
        if (feelings.Any(x => x.SharpOrWorsening)) input.Notices.Add("Sharp or worsening pain needs assessment. Recommendations exclude the affected region; stop movements that provoke it.");
        if (checkin?.IllnessFlag == true || body.UnlocatedPain || (checkin?.PainFlag == true && !feelings.Any(x => x.Pain > 0 || x.MovementLimited || x.SharpOrWorsening)))
        {
            input.CanStart = false;
            input.Notices.Add("Illness or unlocated pain is recorded. Clarify your check-in before starting a training recommendation.");
        }
        input.CanStart &= body.Checked;
        var plan = await db.PlannedWorkouts.AsNoTracking().SingleOrDefaultAsync(x => x.ProgramId == program.ProgramId && x.PlannedDate == today, ct);
        input.PlanName = plan?.SourceCode == "WORKOUT_TARGET" ? plan.Notes : plan?.WorkoutTypeCode.Replace('_', ' ');
        var catalog = await db.ExerciseCatalogItems.AsNoTracking().Where(x => x.IsActive)
            .Include(x => x.MuscleTargets).Include(x => x.Equipment).AsSplitQuery().ToListAsync(ct);
        var byId = catalog.ToDictionary(x => x.ExerciseCatalogItemId);
        var start = today.AddDays(-7);
        var recent = await db.WorkoutSessions.AsNoTracking().Where(x => x.ClientId == client.ClientId && x.ProgramId == program.ProgramId
            && x.CompletedFlag && x.WorkoutDate >= start && x.WorkoutDate <= today).ToListAsync(ct);
        var ids = recent.Select(x => x.WorkoutSessionId).ToArray();
        var details = await db.DetailedWorkoutExercises.AsNoTracking().Where(x => ids.Contains(x.WorkoutSessionId)).Include(x=>x.Sets).ToListAsync(ct);
        input.HasCompletedWorkout = recent.Any(x => x.WorkoutDate == today);
        input.ActiveWorkoutSessionId = await db.WorkoutSessions.AsNoTracking().Where(x => x.ClientId == client.ClientId && x.ProgramId == program.ProgramId
            && x.WorkoutDate == today && !x.CompletedFlag && x.SourceRecordId != null && x.SourceRecordId.StartsWith("TARGET:"))
            .OrderByDescending(x => x.UpdatedAtUtc).Select(x => (Guid?)x.WorkoutSessionId).FirstOrDefaultAsync(ct);
        var recentLoad = new Dictionary<string, decimal>();
        var recentExerciseLoad = new Dictionary<Guid, decimal>();
        var recentMuscleLoad = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        static string MuscleKey(ExerciseMuscleTargetRecord muscle) => string.IsNullOrWhiteSpace(muscle.MuscleSlug) ? muscle.MuscleName.Trim() : muscle.MuscleSlug.Trim();
        foreach(var session in recent)
        {
            var rows = details.Where(x => x.WorkoutSessionId == session.WorkoutSessionId).ToList();
            foreach(var row in rows)
            {
                if (!byId.TryGetValue(row.ExerciseCatalogItemId, out var exercise)) continue;
                var sets = row.Sets.Count(s => !s.IsWarmup && (s.Rpe ?? session.SessionRpe ?? 0m) >= 7m);
                // Recent exposures decay for ranking only. Counts remain visible below.
                var factor = 1m / (1 + today.DayNumber - session.WorkoutDate.DayNumber);
                foreach(var muscle in exercise.MuscleTargets)
                {
                    var key=MuscleKey(muscle);
                    recentMuscleLoad[key]=recentMuscleLoad.GetValueOrDefault(key) + sets*factor*(muscle.RoleCode.Equals("primary",StringComparison.OrdinalIgnoreCase)?1m:.5m);
                }
                recentExerciseLoad[exercise.ExerciseCatalogItemId] = recentExerciseLoad.GetValueOrDefault(exercise.ExerciseCatalogItemId) + sets * factor;
                foreach(var region in BodyRegions.FromMuscles(exercise.MuscleTargets.Select(m=>m.MuscleName)))
                    recentLoad[region] = recentLoad.GetValueOrDefault(region) + sets * factor;
            }
            if (rows.Count == 0 && session.SessionRpe >= 7m)
            {
                var rough = CoarseRegions(session.WorkoutTypeCode);
                foreach(var region in rough) recentLoad[region] = recentLoad.GetValueOrDefault(region) + 4m / (1 + today.DayNumber-session.WorkoutDate.DayNumber);
                input.Notices.Add($"{session.WorkoutDate:MMM d}: {session.WorkoutName ?? session.WorkoutTypeCode} has no exercise detail; regional loading is estimated from its category.");
            }
        }
        foreach(var region in BodyRegions.All)
        {
            var feeling = feelings.FirstOrDefault(x=>x.Region == region.Code);
            var status = feeling is not null && (feeling.Mark > 0 || feeling.Pain > 0 || feeling.MovementLimited || feeling.SharpOrWorsening || feeling.Soreness > 0)
                ? "Avoid loading" : recentLoad.GetValueOrDefault(region.Code) >= 1.5m
                ? "Reduce / reassess" : !body.Checked ? "Check needed" : "Available candidate";
            input.Readiness.Add(new(region.Code,region.Name,status,recentLoad.GetValueOrDefault(region.Code),feeling?.Soreness??0,feeling?.Pain??0));
        }
        var lowFuel = false;
        if (await db.Measurements.AnyAsync(x=>x.ClientId==client.ClientId && !x.IsDeleted && x.WeightLb != null,ct))
        {
            var current = await projection.BuildTodayTomorrowAsync(context,today,ct);
            var impact = await projection.BuildYesterdayImpactAsync(context,today,current,ct);
            input.FuelSummary = impact.FourDayCarbohydrateEvidenceDays == 0
                ? "Carb recovery is uncertain: no recent logged carbohydrate evidence."
                : $"Estimated fuel readiness: {impact.IncomingReadinessBandCode.Replace('_',' ').ToLowerInvariant()} · carb evidence on {impact.FourDayCarbohydrateEvidenceDays}/4 days.";
            lowFuel = impact.FourDayCarbohydrateEvidenceDays > 0 && impact.IncomingReadinessBandCode is "LOW" or "VERY_LOW" or "MODERATELY_LOW";
            if (lowFuel) input.Notices.Add("Existing engine estimates indicate reduced fuel readiness. Effort is capped at moderate; review today’s fueling advice.");
        }
        else input.FuelSummary = "Fuel estimates need a usable body-weight measurement.";
        var rpeCap = WorkoutTargetPolicy.EffortCap(input.Effort,lowFuel,checkin);
        var active = (await types.GetActiveMapAsync(ct)).Where(x=>x.Value.IsSelectable).ToDictionary(x=>x.Key,x=>x.Value);
        var templates = await db.WorkoutTemplates.AsNoTracking().Where(x=>x.ClientId==client.ClientId).Include(x=>x.Exercises).ToListAsync(ct);
        if (plan is not null && plan.SourceCode != "WORKOUT_TARGET")
            input.PlanName = plan.WorkoutTemplateId.HasValue ? templates.FirstOrDefault(t=>t.WorkoutTemplateId==plan.WorkoutTemplateId)?.TemplateName ?? input.PlanName
                : plan.SystemWorkoutCode is not null ? SystemWorkoutRecipeCatalog.CoreRecipes.Concat(SystemWorkoutRecipeCatalog.SignatureRecipes).FirstOrDefault(r=>r.SystemCode==plan.SystemWorkoutCode)?.DisplayName ?? input.PlanName
                : active.GetValueOrDefault(plan.WorkoutTypeCode)?.DisplayName ?? input.PlanName;
        var candidates = new List<WorkoutTargetChoice>();
        foreach(var recipe in SystemWorkoutRecipeCatalog.CoreRecipes.Concat(SystemWorkoutRecipeCatalog.SignatureRecipes).Where(x=>active.ContainsKey(x.EngineWorkoutTypeCode)))
        {
            var choice = new WorkoutTargetChoice { Key="system:"+recipe.SystemCode, Name=recipe.DisplayName, Source="System Workouts", TypeCode=recipe.EngineWorkoutTypeCode,
                SystemCode=recipe.SystemCode, Minutes=input.Minutes, Rpe=rpeCap };
            var used = new HashSet<Guid>();
            foreach(var slot in recipe.Slots)
            {
                var exercise = catalog.Where(x=>x.MuscleTargets.Count>0 && !used.Contains(x.ExerciseCatalogItemId) && EquipmentFits(x,input) && !WorkoutTargetPolicy.Excluded(Loading(x),feelings))
                    .Select(x=>new { Item=x, Score=SlotScore(x,slot) }).Where(x=>x.Score>0)
                    .OrderByDescending(x=>x.Score + WorkoutSessionComposer.RankExercise(x.Item, BodyRegions.FromMuscles(slot.MuscleTerms), recentLoad, recentExerciseLoad)).ThenBy(x=>x.Item.Name).Select(x=>x.Item).FirstOrDefault();
                if(exercise is null) { choice.Adapted=true; continue; }
                used.Add(exercise.ExerciseCatalogItemId);
                choice.Exercises.Add(ToExercise(exercise,slot.Sets,slot.Reps,rpeCap,slot.Notes,slot.DurationSeconds));
            }
            if(recipe.Slots.Count>0 && choice.Exercises.Count==0) continue;
            candidates.Add(choice);
        }
        foreach(var template in templates.Where(x=>active.ContainsKey(x.WorkoutTypeCode)))
        {
            var choice=new WorkoutTargetChoice { Key="template:"+template.WorkoutTemplateId,Name=template.TemplateName,Source="My Workouts",TypeCode=template.WorkoutTypeCode,
                TemplateId=template.WorkoutTemplateId,Minutes=input.Minutes,Rpe=rpeCap };
            foreach(var row in template.Exercises.OrderBy(x=>x.ExerciseOrder))
            {
                if(!byId.TryGetValue(row.ExerciseCatalogItemId,out var exercise) || !EquipmentFits(exercise,input)) {choice.Adapted=true;continue;}
                choice.Exercises.Add(ToExercise(exercise,row.Sets,row.Reps,rpeCap,row.Notes));
            }
            if(choice.Exercises.Count>0) candidates.Add(choice);
        }
        if(input.Activity is "targeted" or "fullbody" or "auto")
        {
            var targets=input.Targets.Count>0 ? input.Targets : new List<string>{"chest","upperback","shoulders","quads","hamstrings","glutes"};
            var generated=new WorkoutTargetChoice { Key="build:targeted",Name=input.Activity=="fullbody"?"Your full-body session":input.Activity=="auto"?"Your readiness-based session":"Your targeted session",Source="Built for today",TypeCode=input.Activity=="fullbody"?"FULL_BODY_STRENGTH":TargetType(targets,input.Effort),Minutes=input.Minutes,Rpe=rpeCap };
            var eligible = catalog.Where(e => e.MuscleTargets.Count > 0 && EquipmentFits(e,input) && !WorkoutTargetPolicy.Excluded(Loading(e),feelings)).ToList();
            generated.Exercises = WorkoutSessionComposer.Generate(eligible,input,rpeCap,recentLoad,recentExerciseLoad);
            generated.Reasons.Add("Built from eligible muscle targets, with movement variety and larger compound exercises first.");
            if(generated.Exercises.Count>0 && active.ContainsKey(generated.TypeCode)) candidates.Add(generated);
        }
        foreach(var choice in candidates)
        {
            var activity=WorkoutTargetPolicy.ActivityFor(choice.TypeCode);
            if(input.Activity is "targeted" or "fullbody" ? activity!="strength" : input.Activity!="auto" && activity!=input.Activity) continue;
            if(choice.TypeCode=="REST") {choice.Minutes=0;choice.Rpe=0;}
            if(activity=="mobility") { choice.Rpe=Math.Min(choice.Rpe,4m); choice.Exercises=choice.Exercises.Select(e=>e with {Rpe=Math.Min(e.Rpe,4m)}).ToList(); }
            var originalCount=choice.Exercises.Count;
            choice.Exercises=choice.Exercises.Where(e=>!WorkoutTargetPolicy.Excluded(Loading(byId[e.ExerciseId]),feelings)
                && byId[e.ExerciseId].MuscleTargets.Count>0
                && (input.Activity!="targeted" || e.Regions.Intersect(input.Targets).Any())).ToList();
            if(originalCount>0 && choice.Exercises.Count==0) continue;
            if(choice.Exercises.Count==0 && choice.TypeCode!="REST" && WorkoutTargetPolicy.Excluded(CoarseRegions(choice.TypeCode),feelings)) continue;
            choice.Adapted |= originalCount!=choice.Exercises.Count;
            if(choice.Adapted) choice.Reasons.Add("Exercises unavailable or conflicting with your symptoms/targets were removed. Review the adapted preview.");
            choice.Exercises=choice.Exercises.Select(e=>{
                var reduce=Loading(byId[e.ExerciseId]).Any(r=>recentLoad.GetValueOrDefault(r)>=1.5m)
                    || byId[e.ExerciseId].MuscleTargets.Any(m=>recentMuscleLoad.GetValueOrDefault(MuscleKey(m))>=1.5m);
                if (reduce) choice.Adapted = true;
                return reduce ? e with { Sets=Math.Min(e.Sets,2),Rpe=Math.Min(e.Rpe,6m),Note=(e.Note+" Recent hard training: reduced volume and effort.").Trim() } : e;
            }).ToList();
            if(choice.Exercises.Any(e => Loading(byId[e.ExerciseId]).Any(r => recentLoad.GetValueOrDefault(r)>=1.5m)
                || byId[e.ExerciseId].MuscleTargets.Any(m=>recentMuscleLoad.GetValueOrDefault(MuscleKey(m))>=1.5m)))
                choice.Reasons.Add("Recent hard work in these muscle groups calls for lower volume and effort.");
            if(choice.Exercises.Count>0)
            {
                if(input.Additions.Count>0) AddExtras(choice,input,catalog,feelings,rpeCap);
                WorkoutSessionComposer.FitSession(choice,input.Minutes,byId);
                if(choice.Exercises.Count==0) continue;
            }
            var covered=choice.Exercises.Where(e=>!e.IsAddon).SelectMany(e=>WorkoutSessionComposer.PrimaryRegions(byId[e.ExerciseId])).Distinct().ToList();
            var fullBodyCoverage = covered.Intersect(new[]{"chest","upperback","shoulders","triceps"}).Any()
                && covered.Intersect(new[]{"quads","hamstrings","glutes"}).Any();
            if(input.Activity=="fullbody" && fullBodyCoverage) choice.Score += 100;
            choice.Score -= choice.Reasons.Count(r=>r.StartsWith("Requested ") && r.Contains("unavailable"))*80;
            if(input.Activity=="fullbody" && !fullBodyCoverage)
            { choice.Name+=" — partial body coverage";choice.Adapted=true;choice.Reasons.Add("Full-body coverage is unavailable under today’s constraints; this is a partial alternative."); }
            if(input.Targets.Count>0)
            {
                var missing=input.Targets.Except(covered).ToList();
                if(missing.Count>0) choice.Reasons.Add("Targets not covered: "+string.Join(", ",missing.Select(r=>BodyRegions.All.FirstOrDefault(x=>x.Code==r)?.Name ?? r)));
                choice.Score+=covered.Intersect(input.Targets).Count()*20;
            }
            choice.Score += covered.Count*4 - covered.Sum(r=>recentLoad.GetValueOrDefault(r))*8;
            choice.Score += choice.Source == "My Workouts" ? 30 : choice.Source == "System Workouts" ? 25 : 20;
            choice.Score -= choice.Exercises.Sum(e=>recentExerciseLoad.GetValueOrDefault(e.ExerciseId));
            var planMatch = plan?.WorkoutTemplateId==choice.TemplateId && choice.TemplateId.HasValue || plan?.SystemWorkoutCode==choice.SystemCode && choice.SystemCode is not null
                || plan is not null && plan.SourceCode != "WORKOUT_TARGET" && plan.WorkoutTemplateId is null && plan.SystemWorkoutCode is null && plan.WorkoutTypeCode==choice.TypeCode;
            if(planMatch && !choice.Adapted)
            { choice.Score+=1000;choice.Reasons.Add("Your planned workout fits today’s check-in and available time; keep the plan."); }
            else if(planMatch) {choice.Score+=5;choice.Reasons.Add("An adjusted version of your planned workout.");}
            var priority = covered.OrderBy(r=>recentLoad.GetValueOrDefault(r)).Take(3).Select(r=>BodyRegions.All.First(x=>x.Code==r).Name).ToList();
            if(priority.Count>0) choice.Reasons.Add("Eligible targets with the least recent loading in this session: "+string.Join(", ",priority)+".");
            var exclusions=feelings.Where(f=>f.Mark>0||f.Soreness>0||f.Pain>0).Select(f=>BodyRegions.All.FirstOrDefault(r=>r.Code==f.Region)?.Name ?? f.Region).Distinct().ToList();
            if(exclusions.Count>0) choice.Reasons.Add("Excluded for soreness or pain: "+string.Join(", ",exclusions)+".");
            if(rpeCap < (input.Effort=="hard"?8m:input.Effort=="easy"?4m:6m)) choice.Reasons.Add("Effort reduced for current fuel/recovery information.");
            if(choice.Exercises.Count==0 && choice.TypeCode!="REST" && input.Additions.Count>0)
                choice.Reasons.Add("Add-ons are available with exercise-based strength/mobility matches; this cardio/conditioning match has no add-on exercise block.");
            choice.Reasons.Add("Selection uses your check-in, recent training, equipment and today’s preferences. Readiness is an estimate.");
            input.Choices.Add(choice);
        }
        input.Choices=input.Choices.OrderByDescending(x=>x.Score).ThenBy(x=>x.Name).Take(3).ToList();
        foreach(var choice in input.Choices)
            choice.Revision=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                System.Text.Json.JsonSerializer.Serialize(new { today, choice.Key, choice.TypeCode, choice.Minutes, choice.Rpe, choice.Exercises }))));
        input.Notices=input.Notices.Distinct().ToList();
        if(input.Choices.Count==0) input.Notices.Add("No suitable match for these constraints. Try easier effort, different targets, or review your body check.");
        return input;
    }

    private static void AddExtras(WorkoutTargetChoice choice,TodayWorkoutViewModel input,List<ExerciseCatalogItemRecord> catalog,List<RegionFeeling> feelings,decimal rpe)
    {
        foreach(var addition in input.Additions)
        {
            var terms=addition switch {"posture"=>new[]{"face pull","wall slide","bird dog"},"core"=>new[]{"dead bug","pallof"},"balance"=>new[]{"single leg balance","single-leg stance"},_=>new[]{"hip rotation","thoracic rotation"}};
            var exercise=catalog.Where(x=>terms.Any(t=>x.Name.Contains(t,StringComparison.OrdinalIgnoreCase)) && EquipmentFits(x,input)
                && x.MuscleTargets.Count>0 && !WorkoutTargetPolicy.Excluded(Loading(x),feelings)
                && !choice.Exercises.Any(e=>e.ExerciseId==x.ExerciseCatalogItemId)).OrderBy(x=>x.Name).FirstOrDefault();
            if(exercise is null) {choice.Adapted=true;choice.Reasons.Add("Requested "+addition+" add-on is unavailable with today’s constraints/library metadata.");continue;}
            choice.Exercises.Add(ToExercise(exercise,1,8,Math.Min(rpe,4m),"Controlled, comfortable movement; stop if symptoms worsen.") with {IsAddon=true});
            choice.Reasons.Add("Includes a brief "+addition+" add-on.");
        }
    }
    private static string TargetType(IEnumerable<string> targets,string effort)
    {
        var set=targets.ToHashSet();
        if(effort!="hard") return "ACCESSORY";
        if(set.IsSubsetOf(new[]{"chest","shoulders","triceps"})&&set.Contains("chest")) return "MAJOR_PUSH";
        if(set.IsSubsetOf(new[]{"upperback","lowerback","biceps"})&&set.Contains("upperback")) return "MAJOR_PULL";
        if(set.IsSubsetOf(new[]{"quads","hamstrings","glutes","calves"})&&set.Overlaps(new[]{"quads","hamstrings","glutes"})) return "MAJOR_LEGS";
        return set.Overlaps(new[]{"chest","upperback","shoulders","triceps"}) && set.Overlaps(new[]{"quads","hamstrings","glutes"}) ? "FULL_BODY_STRENGTH" : "ACCESSORY";
    }
    private static TargetExercise ToExercise(ExerciseCatalogItemRecord e,int sets,int reps,decimal rpe,string? note,int? seconds=null)
        =>new(e.ExerciseCatalogItemId,e.Name,e.Mechanics,Math.Clamp(sets,1,6),Math.Clamp(reps,1,30),rpe,
            BodyRegions.FromMuscles(e.MuscleTargets.Select(m=>m.MuscleName)),note,false,seconds);
    private static int SlotScore(ExerciseCatalogItemRecord e,SystemWorkoutExerciseSlot s)
    {
        var name=s.PreferredNameTerms.Select((t,i)=>e.Name.Equals(t,StringComparison.OrdinalIgnoreCase)?300-i
            : e.Name.Contains(t,StringComparison.OrdinalIgnoreCase)?100-i:0).DefaultIfEmpty().Max();
        if(s.RequireNameMatch&&name==0) return 0;
        var muscle=s.MuscleTerms.Any(t=>e.MuscleTargets.Where(m=>m.RoleCode=="primary").Any(m=>m.MuscleName.Contains(t,StringComparison.OrdinalIgnoreCase)));
        if(s.MuscleTerms.Count>0 && !muscle && !s.RequireNameMatch) return 0;
        if(e.Category?.Contains("stretch",StringComparison.OrdinalIgnoreCase)==true&&!s.AllowStretching) return 0;
        var score = name>0?name:muscle?10:0;
        if(s.PreferCompound && WorkoutSessionComposer.IsCompound(e)) score+=6;
        if(s.PreferIsolation && e.Mechanics?.Contains("isolation",StringComparison.OrdinalIgnoreCase)==true) score+=6;
        return score;
    }
    private static bool EquipmentFits(ExerciseCatalogItemRecord e,TodayWorkoutViewModel input)
    {
        var requirements=e.Equipment.Select(x=>x.EquipmentName.ToLowerInvariant()).Where(x=>!x.Contains("body")&&!x.Contains("none")).ToList();
        if(input.Location=="gym") return true;
        if(input.Location=="bodyweight") return requirements.Count==0;
        return requirements.All(x=>input.Equipment.Any(t=>x.Contains(t,StringComparison.OrdinalIgnoreCase)));
    }
    private static HashSet<string> Loading(ExerciseCatalogItemRecord e)
    {
        return BodyRegions.LoadingRegions(e.MuscleTargets.Select(x=>x.MuscleName));
    }
    private static IReadOnlyList<string> CoarseRegions(string type)
        => type.Contains("PUSH")?["chest","shoulders","upperback","triceps"]
            :type.Contains("PULL")?["upperback","shoulders","biceps","lowerback"]
            :type.Contains("LEG")||WorkoutTargetPolicy.ActivityFor(type)=="cardio"?["quads","hamstrings","glutes","calves","hips"]
            :type=="REST"?[]:BodyRegions.All.Select(x=>x.Code).ToArray();
}
