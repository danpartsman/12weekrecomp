using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
namespace WebRecomp.Web.Services.Coaching;

/// <summary>Snap-to-canonical workout boundary. Deadline hours are never exercise duration.</summary>
public static class SnapWorkoutEngine
{
    public const string Source="SNAP_WORKOUT";
    public const string WorkoutCode="FULL_BODY_STRENGTH";
    private static readonly Dictionary<string,string> Muscles=new(StringComparer.OrdinalIgnoreCase)
    {
        ["chest"]="UPPER_BODY",["pecs"]="UPPER_BODY",["shoulders"]="UPPER_BODY",["delts"]="UPPER_BODY",["front delts"]="UPPER_BODY",["rear delts"]="UPPER_BODY",["side delts"]="UPPER_BODY",
        ["back"]="UPPER_BODY",["upper back"]="UPPER_BODY",["lower back"]="UPPER_BODY",["lats"]="UPPER_BODY",["traps"]="UPPER_BODY",["rhomboids"]="UPPER_BODY",["biceps"]="UPPER_BODY",["triceps"]="UPPER_BODY",["forearms"]="UPPER_BODY",["arms"]="UPPER_BODY",["abs"]="UPPER_BODY",["core"]="UPPER_BODY",["abdominals"]="UPPER_BODY",
        ["quads"]="LOWER_BODY",["quadriceps"]="LOWER_BODY",["hamstrings"]="LOWER_BODY",["glutes"]="LOWER_BODY",["calves"]="LOWER_BODY",["hips"]="LOWER_BODY",["hip flexors"]="LOWER_BODY",["adductors"]="LOWER_BODY",["abductors"]="LOWER_BODY",["legs"]="LOWER_BODY",["upper body"]="UPPER_BODY",["lower body"]="LOWER_BODY"
    };
    public static string[] Focus(string? value)=> (value??"").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static void ValidateFocus(string? value)
    {
        if((value?.Length??0)>250||Focus(value).Any(x=>!Muscles.ContainsKey(x)))throw new ArgumentException("Use muscle names such as chest, shoulders, triceps, back, biceps, abs, quads, hamstrings, glutes or calves, separated by commas.");
    }
    private static List<(AccountabilityExercise Exercise,AccountabilitySet Set)> Work(AccountabilityWorkout workout,AccountabilityResult? result)
        =>workout.Exercises.SelectMany(e=>(result is null?Enumerable.Range(0,e.Sets).Select(_=>new AccountabilitySet{Reps=e.Reps,Weight=e.MaxWeight,Rpe=e.Rpe}):result.Sets.GetValueOrDefault(e.Id)??[]).Where(s=>s.Reps>0).Select(s=>(e,s))).ToList();
    // Explicit estimate for uncaptured time: 3 seconds per rep and 90 seconds between working sets.
    public static decimal Duration(AccountabilityWorkout workout,AccountabilityResult? result)
    {
        if(result?.DurationMinutes is >0)return result.DurationMinutes.Value;
        var work=Work(workout,result);return Math.Min(360,Math.Max(1,Math.Round(work.Sum(x=>(x.Set.Reps??0)*.05m)+Math.Max(0,work.Count-1)*1.5m,1)));
    }
    public static decimal? Rpe(AccountabilityWorkout workout,AccountabilityResult? result)
    {
        var values=Work(workout,result).Select(x=>x.Set.Rpe??x.Exercise.Rpe).Where(x=>x.HasValue).Select(x=>x!.Value).ToArray();
        return values.Length==0?null:Math.Round(values.Average(),1);
    }
    public static Engine30WorkoutSessionInput Input(AccountabilityWorkout workout,AccountabilityResult? result,Engine30WorkoutAdminProfile profile,decimal? duration=null,decimal? actualRpe=null)
    {
        var work=Work(workout,result);var minutes=duration??Duration(workout,result);var rpe=actualRpe??Rpe(workout,result);
        var regionSets=new Dictionary<string,decimal>{{"UPPER_BODY",0},{"LOWER_BODY",0}};
        var unspecified=0m;
        foreach(var (exercise,_) in work)
        {
            var regions=Focus(exercise.MuscleFocus).Where(Muscles.ContainsKey).Select(x=>Muscles[x]).Distinct().ToArray();
            if(regions.Length==0){unspecified++;continue;}
            foreach(var region in regions)regionSets[region]+=1m/regions.Length;
        }
        foreach(var region in profile.MuscleRegions)if(regionSets.ContainsKey(region.MuscleRegionCode))regionSets[region.MuscleRegionCode]+=unspecified/Math.Max(1,profile.MuscleRegions.Count);
        var groups=profile.MuscleRegions.Select(x=>new Engine30WorkoutMuscleWork(x.MuscleRegionCode,regionSets.GetValueOrDefault(x.MuscleRegionCode))).ToArray();
        var details=new Engine30ResistanceSegmentDetails(TotalHardSets:work.Count,AverageRepsPerSet:work.Count==0?null:work.Average(x=>(decimal)(x.Set.Reps??0)),MuscleGroups:groups);
        return new(WorkoutCode,minutes,PlannedRpeOverride:result is null?rpe:null,ActualDurationMinutes:result is null?null:minutes,ActualRpe:result is null?null:rpe,Segments:[new("SNAP",profile.DefaultModalityCode,minutes,profile.DefaultRpe,PlannedRpeOverride:result is null?rpe:null,ActualDurationMinutes:result is null?null:minutes,ActualRpe:result is null?null:rpe,Resistance:details)]);
    }
    public static async Task SyncAsync(RecompDbContext db,Client client,AccountabilityWorkout workout,AccountabilityResult result,CancellationToken ct)
    {
        if(!result.Finalized)return;
        var session=await db.WorkoutSessions.SingleOrDefaultAsync(x=>x.ClientId==client.ClientId&&x.EntrySourceCode==Source&&x.SourceRecordId==workout.Id,ct);
        if(session is null)
        {
            var program=await db.Programs.Where(x=>x.ClientId==client.ClientId&&x.ProgramStatusCode=="ACTIVE").OrderByDescending(x=>x.StartDate).FirstOrDefaultAsync(ct)??throw new ArgumentException("An active client program is needed to finish and log this workout.");
            session=new(){WorkoutSessionId=Guid.NewGuid(),ClientId=client.ClientId,ProgramId=program.ProgramId,WorkoutTypeCode=WorkoutCode,EntrySourceCode=Source,SourceRecordId=workout.Id,CreatedAtUtc=DateTime.UtcNow};db.WorkoutSessions.Add(session);
        }
        var zone=TimeZoneInfo.FindSystemTimeZoneById(client.TimeZone);
        session.WorkoutDate=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(result.CompletedAtUtc??DateTimeOffset.UtcNow,zone).DateTime);
        session.WorkoutName=workout.Name;session.DurationMin=Duration(workout,result);session.SessionRpe=Rpe(workout,result);session.TypicalRestSec=90;
        session.Notes="Snap Workout. "+(result.DurationMinutes.HasValue?"Reported duration.":"Estimated duration: 3 seconds per rep + 90 seconds between working sets.");
        new WorkoutActualService(db).ApplyCurrentEngineState(session,true);
    }
}

public sealed record SnapWorkoutEstimate(decimal Calories,decimal Glycogen,decimal Minutes,bool Actual);
public sealed class SnapWorkoutEstimateService(RecompDbContext db,IEngine30WorkoutRuntimeProfileProvider profiles,Engine30DailyRuntimeService runtime)
{
    public async Task<SnapWorkoutEstimate?> EstimateAsync(Guid clientId,AccountabilityWorkout workout,AccountabilityResult? result,DateOnly today,CancellationToken ct)
    {
        var client=await db.Clients.AsNoTracking().SingleAsync(x=>x.ClientId==clientId,ct);
        var measurement=await db.Measurements.AsNoTracking().Where(x=>x.ClientId==clientId&&!x.IsDeleted&&x.WeightLb>0&&x.LocalMeasurementDate<=today).OrderByDescending(x=>x.LocalMeasurementDate).ThenByDescending(x=>x.MeasurementAtUtc).FirstOrDefaultAsync(ct);
        if(client.DateOfBirth is null||string.IsNullOrEmpty(client.SexCode)||client.HeightIn<=0||measurement is null)return null;
        var profile=(await profiles.ResolveAsync(SnapWorkoutEngine.WorkoutCode,ct)).Profile;
        var input=SnapWorkoutEngine.Input(workout,result,profile);
        var calculated=await runtime.CalculateAsync(new(today,client.DateOfBirth.Value,client.SexCode,client.HeightIn,measurement.WeightLb!.Value,measurement.BodyFatPct is >1?measurement.BodyFatPct/100:measurement.BodyFatPct,[],"MINIMAL",input),ct);
        return new(calculated.Energy.NetWorkoutActivityEnergyKcal,calculated.Glycogen.EstimatedGlycogenDepletedG,SnapWorkoutEngine.Duration(workout,result),result is not null);
    }
}
