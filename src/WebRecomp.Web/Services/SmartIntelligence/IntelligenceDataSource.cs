using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services.Dashboard3;
using WebRecomp.Web.Services.WorkoutTargeting;
namespace WebRecomp.Web.Services.SmartIntelligence;

/// <summary>Read-only existing records; no engine runs, recommendations, or SQL writes.</summary>
public sealed class IntelligenceDataSource(RecompDbContext db)
{
    public async Task<IntelligenceReport> LoadAsync(Guid clientId, DateOnly today, int days, string? sensitivity, CancellationToken ct)
    {
        days = days is 84 or 168 ? days : 42;
        var start=today.AddDays(1-days);var warmup=start.AddDays(-28);
        var scales=await db.Measurements.AsNoTracking().Where(x=>x.ClientId==clientId&&!x.IsDeleted&&x.LocalMeasurementDate>=warmup&&x.LocalMeasurementDate<=today).ToListAsync(ct);
        var dailyScale=scales.GroupBy(x=>x.LocalMeasurementDate).ToDictionary(g=>g.Key,g=>g.OrderByDescending(x=>x.MeasurementAtUtc).ThenByDescending(x=>x.CreatedAtUtc).First());
        var skeletal=scales.Count(x=>x.SkeletalMuscleMassLb.HasValue)>=2;
        var nutrition=await db.NutritionDays.AsNoTracking().Where(x=>x.ClientId==clientId&&x.NutritionDate>=warmup&&x.NutritionDate<=today).ToListAsync(ct);
        var food=nutrition.GroupBy(x=>x.NutritionDate).ToDictionary(g=>g.Key,g=>g.OrderByDescending(x=>x.UpdatedAtUtc).ThenByDescending(x=>x.CreatedAtUtc).First());
        var sessions=await db.WorkoutSessions.AsNoTracking().Where(x=>x.ClientId==clientId&&x.CompletedFlag&&x.WorkoutDate>=warmup&&x.WorkoutDate<=today).OrderBy(x=>x.CreatedAtUtc).ToListAsync(ct);
        var ids=sessions.Select(x=>x.WorkoutSessionId).ToArray();
        var exercises=await db.DetailedWorkoutExercises.AsNoTracking().Include(x=>x.Sets).Where(x=>ids.Contains(x.WorkoutSessionId)).ToListAsync(ct);
        var checkins=await db.DailyCheckins.AsNoTracking().Where(x=>x.ClientId==clientId&&x.CheckinDate>=warmup&&x.CheckinDate<=today).ToListAsync(ct);
        var checks=checkins.GroupBy(x=>x.CheckinDate).ToDictionary(g=>g.Key,g=>g.OrderByDescending(x=>x.UpdatedAtUtc).First());
        var recommendations=await db.Recommendations.AsNoTracking().Where(x=>x.ClientId==clientId&&x.ActionDate>=warmup&&x.ActionDate<=today).ToListAsync(ct);
        var recs=recommendations.GroupBy(x=>x.ActionDate).ToDictionary(g=>g.Key,g=>g.OrderByDescending(x=>x.CreatedAtUtc).First());
        var captures=await db.AnalysisRuns.AsNoTracking().Where(x=>x.ClientId==clientId&&x.ActionDate>=warmup&&x.ActionDate<=today&&x.EngineVersion.StartsWith("RECOMP_SCORE_V")).OrderBy(x=>x.StartedAtUtc).ToListAsync(ct);
        var saved=new Dictionary<DateOnly,RecompDayEvidence>();
        foreach(var run in captures)
        {
            try { var snap=JsonSerializer.Deserialize<RecompScoreSnapshot>(run.DiagnosticJson??"null");
                var target=snap?.TargetCapture is {} capture&&capture.Date==run.ActionDate?capture:snap?.Days?.FirstOrDefault(x=>x.Date==run.ActionDate);
                if(target is not null)saved.TryAdd(run.ActionDate,target);
            }catch(JsonException){ /* Legacy records cannot provide usable targets. */ }
        }
        static double? N(decimal? v)=>v.HasValue?(double)v.Value:null;
        static string? Note(string? s)=>string.IsNullOrWhiteSpace(s)?null:s[..Math.Min(600,s.Length)];
        var history=new List<IntelligenceDay>();
        for(var date=warmup;date<=today;date=date.AddDays(1))
        {
            dailyScale.TryGetValue(date,out var m);food.TryGetValue(date,out var f);checks.TryGetValue(date,out var c);recs.TryGetValue(date,out var r);saved.TryGetValue(date,out var target);
            var workouts=sessions.Where(x=>x.WorkoutDate==date).ToArray();
            var partial=f?.ActualCalories is >0 and <1000;
            var w=workouts.Select(x=>new IntelligenceWorkout(x.WorkoutName??x.WorkoutTypeCode,x.WorkoutTypeCode,(double)x.DurationMin,N(x.SessionRpe),x.EntrySourceCode,
                exercises.Where(e=>e.WorkoutSessionId==x.WorkoutSessionId).OrderBy(e=>e.ExerciseOrder).Select(e=>new IntelligenceExercise(e.ExerciseNameSnapshot,N(e.MaxWeight),e.LoadUnitCode,
                    e.Sets.Count(s=>!s.IsWarmup),e.Sets.Where(s=>!s.IsWarmup).OrderBy(s=>s.SetOrder).Select(s=>(int?)s.Reps).ToArray(),Note(e.Notes),
                    e.Notes?.Contains("estimate",StringComparison.OrdinalIgnoreCase)==true||e.Notes?.Contains("not specified",StringComparison.OrdinalIgnoreCase)==true)).ToArray())).ToArray();
            history.Add(new(){Date=date,CompletedDay=date<today,Weight=N(m?.WeightLb),Fat=m?.WeightLb is {} weight&&m.BodyFatPct is {} bf?N(weight*bf):null,
                Muscle=N(skeletal?m?.SkeletalMuscleMassLb:m?.MuscleMassLb),Water=N(m?.BodyWaterLb),Waist=N(m?.WaistIn),SubcutaneousFat=N(m?.SubcutaneousFatPct*100),VisceralFat=N(m?.VisceralFatRating),
                Calories=N(f?.ActualCalories),Protein=N(f?.ActualProteinG),Carbs=N(f?.ActualCarbsG),DietaryFat=N(f?.ActualFatG),PossiblePartialFoodDay=partial,
                TargetCalories=target is not null?N(target.EnergyTarget):N(r?.RecommendedCalories),TargetProtein=target is not null?N(target.ProteinTarget):N(r?.RecommendedProteinG),
                TargetCarbs=target is not null?N(target.CarbsTarget):N(r?.RecommendedCarbsG),TargetFat=target is not null?N(target.FatTarget):N(r?.RecommendedFatG),
                TargetSource=target is not null?"Captured same-day target":r is not null?"Latest stored recommendation (may differ from original)":null,
                TrainingMinutes=workouts.Length>0?workouts.Sum(x=>(double)x.DurationMin):null,
                TrainingLoad=workouts.Length>0&&workouts.All(x=>x.SessionRpe.HasValue||x.DurationMin==0)?workouts.Sum(x=>(double)(x.DurationMin*(x.SessionRpe??0))):null,
                EstimatedBurn=workouts.Length>0&&workouts.All(x=>x.CalculatedWorkoutCalories.HasValue)?workouts.Sum(x=>N(x.CalculatedWorkoutCalories)??0):null,
                EstimatedGlycogen=workouts.Length>0&&workouts.All(x=>x.CalculatedScaledGlycogenBurnG.HasValue)?workouts.Sum(x=>N(x.CalculatedScaledGlycogenBurnG)??0):null,
                Sleep=N(c?.SleepHours),Energy=c?.EnergyScore,RecoveryNote=Note(BodyCheckinCodec.Read(c?.Notes).Note),FoodNote=Note(f?.Notes),Workouts=w});
        }
        return IntelligenceStatistics.Build(history,start,today,sensitivity??"balanced",skeletal?"Skeletal muscle estimate":"Generic muscle estimate");
    }
}
