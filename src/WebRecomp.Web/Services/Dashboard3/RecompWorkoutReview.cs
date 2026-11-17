using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutIntelligence;

namespace WebRecomp.Web.Services.Dashboard3;

public sealed record RecompWorkoutRequirement(string Key, string Label, bool Met, string Evidence, string NextAction)
{
    public int Deduction => Met ? 0 : 5;
}
public sealed record RecompWorkoutReview(DateOnly Start, DateOnly End, bool FullWindow, int MinimumReps,
    decimal MinimumSessionMinutes, IReadOnlyList<RecompWorkoutRequirement> Requirements, int UnmappedExercises)
{
    public int Deductions => FullWindow ? Requirements.Sum(x => x.Deduction) : 0;
    public int? Score => FullWindow ? 100 - Deductions : null;
    public int MetCount => Requirements.Count(x => x.Met);
    public string NextAction => !FullWindow ? "Building your first seven-day training baseline; no workout deductions yet."
        : Requirements.FirstOrDefault(x => !x.Met)?.NextAction ?? "All six workout requirements are covered. Continue your plan and protect recovery.";
}
public sealed record RecompWorkingReps(Guid SessionId, string Name, IReadOnlyList<string> Groups, int Reps);

/// <summary>Product checklist, not a physiological or exercise-prescription model.</summary>
public sealed class RecompWorkoutReviewService(RecompDbContext db, IConfiguration? configuration = null)
{
    public async Task<RecompWorkoutReview> BuildAsync(AppClientContext context, DateOnly today,
        IReadOnlyList<WorkoutSession> sessions, IReadOnlyList<PlannedWorkout> plans, IReadOnlyList<DailyCheckin> checkins, CancellationToken ct)
    {
        var from = today.AddDays(-6);
        var eligible = sessions.Where(x => x.CompletedFlag && x.WorkoutDate >= from && x.WorkoutDate <= today).ToArray();
        var ids = eligible.Select(x => x.WorkoutSessionId).ToArray();
        var details = await db.DetailedWorkoutExercises.AsNoTracking().Include(x => x.Sets)
            .Where(x => ids.Contains(x.WorkoutSessionId)).ToListAsync(ct);
        var exerciseIds = details.Select(x => x.ExerciseCatalogItemId).Distinct().ToArray();
        var muscles = await db.ExerciseMuscleTargets.AsNoTracking().Where(x => exerciseIds.Contains(x.ExerciseCatalogItemId)
            && x.RoleCode == "primary").ToListAsync(ct);
        var reps = new List<RecompWorkingReps>();
        foreach (var detail in details)
        {
            var targets = muscles.Where(x => x.ExerciseCatalogItemId == detail.ExerciseCatalogItemId).Select(x => x.MuscleSlug + " " + x.MuscleName).ToArray();
            var groups = ExerciseGroups(detail.ExerciseNameSnapshot);
            if (groups.Length == 0) groups = MuscleGroups(targets);
            reps.Add(new(detail.WorkoutSessionId, detail.ExerciseNameSnapshot, groups,
                detail.Sets.Where(x => !x.IsWarmup && x.Reps > 0).Sum(x => (int)x.Reps!.Value)));
        }
        // Snap sessions use a generic engine type: inspect actual exercises rather than
        // treating one push-up assignment as a full-body strength session.
        if (eligible.Any(x => x.EntrySourceCode == SnapWorkoutEngine.Source))
        {
            var row = await db.CoachAccountabilityAgreements.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == context.Client!.ClientId, ct);
            var state = row is null ? null : AccountabilityDocument.Parse(row.StateJson);
            foreach (var session in eligible.Where(x => x.EntrySourceCode == SnapWorkoutEngine.Source))
            {
                if (details.Any(x => x.WorkoutSessionId == session.WorkoutSessionId)) continue;
                var workout = state?.SnapWorkouts.SingleOrDefault(x => x.Id == session.SourceRecordId);
                var result = workout is null ? null : state!.SnapResults.GetValueOrDefault(workout.Id);
                if (workout is null || result?.Finalized != true) continue;
                foreach (var exercise in workout.Exercises)
                {
                    var groups = ExerciseGroups(exercise.Name);
                    if (groups.Length == 0) groups = MuscleGroups([exercise.MuscleFocus ?? ""]);
                    reps.Add(new(session.WorkoutSessionId, exercise.Name, groups,
                        result.Sets.GetValueOrDefault(exercise.Id)?.Sum(x => Math.Max(0, x.Reps ?? 0)) ?? 0));
                }
            }
        }
        var minimumReps = Math.Clamp(configuration?.GetValue<int?>("CommandCenterTraining:MinimumGroupReps") ?? 60, 1, 1000);
        var minimumMinutes = Math.Clamp(configuration?.GetValue<decimal?>("CommandCenterTraining:MinimumSessionMinutes") ?? 20m, 1m, 120m);
        return Build(today, context.Program!.StartDate, eligible, plans, checkins, reps,
            details.Select(x => x.WorkoutSessionId).ToHashSet(), minimumReps, minimumMinutes);
    }

    public static RecompWorkoutReview Build(DateOnly today, DateOnly programStart, IReadOnlyList<WorkoutSession> sessions,
        IReadOnlyList<PlannedWorkout> plans, IReadOnlyList<DailyCheckin> checkins, IReadOnlyList<RecompWorkingReps> work,
        IReadOnlySet<Guid>? detailedSessions = null, int minimumReps = 60, decimal minimumMinutes = 20m)
    {
        var start = today.AddDays(-6); if (programStart > start) start = programStart;
        var active = sessions.Where(x => x.CompletedFlag && x.WorkoutDate >= start && x.WorkoutDate <= today)
            .DistinctBy(x => x.WorkoutSessionId).ToArray();
        var ids = active.Select(x => x.WorkoutSessionId).ToHashSet();
        var actual = work.Where(x => ids.Contains(x.SessionId) && x.Reps > 0).ToArray();
        var requirements = new List<RecompWorkoutRequirement>();
        foreach (var (key, label, sample) in new[] { ("push", "Upper push", "chest, shoulders or triceps"), ("pull", "Upper pull", "back or biceps"), ("legs", "Legs", "quads, hamstrings, glutes or calves") })
        {
            var total = actual.Where(x => x.Groups.Contains(key)).Sum(x => x.Reps);
            var typed = active.Where(x => x.DurationMin >= minimumMinutes && x.EntrySourceCode != SnapWorkoutEngine.Source
                && (detailedSessions?.Contains(x.WorkoutSessionId) != true || IsStrengthClass(x))
                && SessionGroups(x).Contains(key)).ToArray();
            var met = total >= minimumReps || typed.Length > 0;
            var evidence = $"{total} / {minimumReps} working reps" + (typed.Length > 0 ? $" · {typed.Length} qualifying session/class log(s)" : "");
            requirements.Add(new(key, label, met, evidence, met ? $"{label} is covered in this seven-day window."
                : $"Record {Math.Max(0, minimumReps - total)} more {label.ToLowerInvariant()} working reps ({sample}), or complete and log a matching {minimumMinutes:0}+ minute strength session."));
        }
        var cardio = active.Count(x => x.DurationMin >= minimumMinutes && IsCardio(x));
        for (var i = 1; i <= 2; i++)
            requirements.Add(new("cardio-" + i, "Cardio session " + i, cardio >= i, $"{Math.Min(cardio, 2)} / 2 qualifying sessions · {minimumMinutes:0}+ minutes each",
                cardio >= i ? "This cardio requirement is covered." : $"Complete and record {2 - Math.Min(cardio, 2)} more {minimumMinutes:0}+ minute cardio session(s), such as cardio, Orangetheory 2G/3G or HIIT."));
        bool NoTraining(DateOnly date) => !active.Any(x => x.WorkoutDate == date && !IsRecovery(x)
            && (actual.Any(r => r.SessionId == x.WorkoutSessionId) || (x.EntrySourceCode != SnapWorkoutEngine.Source && x.DurationMin > 0)));
        var rest = active.Where(x => (Raw(x.WorkoutTypeCode) is "REST" or "RECOVERY_0_RPE2") && NoTraining(x.WorkoutDate)).Select(x => x.WorkoutDate)
            .Concat(plans.Where(x => x.PlannedDate >= start && x.PlannedDate < today && (Raw(x.WorkoutTypeCode) is "REST" or "RECOVERY_0_RPE2")
                && checkins.Any(c => c.CheckinDate == x.PlannedDate) && NoTraining(x.PlannedDate)).Select(x => x.PlannedDate)).Distinct().Count();
        requirements.Add(new("rest", "Rest / recovery day", rest > 0, $"{Math.Min(rest, 1)} / 1 confirmed rest day",
            rest > 0 ? "A rest day is recorded in this window." : "Keep a rest day in your weekly rotation and record it. A missing workout entry alone is not proof of rest."));
        return new(start, today, today.DayNumber - programStart.DayNumber >= 6, minimumReps, minimumMinutes,
            requirements, actual.Count(x => x.Groups.Count == 0));
    }
    private static string Raw(string? text) => (text ?? "").Trim().ToUpperInvariant().Replace('-', '_').Replace(' ', '_');
    private static string Words(string? text) => (text ?? "").ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
    public static string[] MuscleGroups(IEnumerable<string> muscles)
    {
        var words = " " + string.Join(" ", muscles.Select(Words)) + " "; var result = new List<string>();
        if (new[] { "chest", "pectoral", "pecs", "triceps", "anterior delt", "front delt", "lateral delt", "side delt", "shoulder" }.Any(words.Contains)) result.Add("push");
        if (new[] { "lats", "latissimus", "back", "biceps", "rhomboid", "trapezius", "traps", "rear delt", "posterior delt", "forearm" }.Any(words.Contains)) result.Add("pull");
        if (new[] { "quad", "hamstring", "glute", "calf", "calves", "soleus", "gastrocnemius", "adductor", "abductor", "hip flexor" }.Any(words.Contains)) result.Add("legs");
        return result.ToArray();
    }
    public static string[] ExerciseGroups(string name)
    {
        var n = Words(name);
        if (new[] { "push up", "pushup", "bench press", "chest press", "chest fly", "shoulder press", "overhead press", "tricep", "lateral raise", "dip" }.Any(n.Contains)) return ["push"];
        if (new[] { "squat", "lunge", "deadlift", "leg press", "leg curl", "leg extension", "calf raise", "hip thrust", "glute" }.Any(n.Contains)) return ["legs"];
        if (new[] { "pull up", "pullup", "chin up", "pulldown", "row", "bicep", "hammer curl", "dumbbell curl", "barbell curl", "preacher curl", "cable curl", "reverse fly", "rear delt" }.Any(n.Contains)) return ["pull"];
        return [];
    }
    private static bool IsStrengthClass(WorkoutSession s) => Raw(s.SystemWorkoutCode).Contains("STRENGTH50") || Raw(s.SystemWorkoutCode).Contains("STRENGTH_50")
        || Raw(s.SystemWorkoutCode).Contains("STRENGTH60") || Raw(s.WorkoutTypeCode).StartsWith("OTF_STRENGTH");
    public static string[] SessionGroups(WorkoutSession s)
    {
        var raw = Raw(s.WorkoutTypeCode); var system = Raw(s.SystemWorkoutCode);
        if (raw.Contains("STRENGTH_UPPER")) return ["push", "pull"];
        if (raw.Contains("STRENGTH_LOWER")) return ["legs"];
        if (IsStrengthClass(s) && SystemWorkoutRecipeCatalog.TryGet(s.SystemWorkoutCode, out var classRecipe))
        {
            var name = Words(classRecipe.DisplayName);
            if (name.Contains("upper")) return ["push", "pull"];
            if (name.Contains("lower")) return ["legs"];
        }
        if (system == SystemWorkoutRecipeCatalog.ChestTricepsGlutesHamstringsCode) return ["push", "legs"];
        if (system == SystemWorkoutRecipeCatalog.BackBicepsQuadsCode) return ["pull", "legs"];
        if (system == SystemWorkoutRecipeCatalog.ShouldersAbsCalvesCode) return ["push", "legs"];
        if (IsStrengthClass(s) || raw == "FULL_BODY_STRENGTH" || raw is "MAJOR_MIXED" or "MAJOR_60_RPE8") return ["push", "pull", "legs"];
        if (raw.Contains("PUSH")) return ["push"];
        if (raw.Contains("PULL")) return ["pull"];
        if (raw.Contains("LEGS") || raw.Contains("LOWER")) return ["legs"];
        return [];
    }
    public static bool IsCardio(WorkoutSession s)
    {
        if (IsStrengthClass(s)) return false;
        var code = Engine30WorkoutUiCatalogService.NormalizeCode(s.WorkoutTypeCode);
        var system = Raw(s.SystemWorkoutCode);
        return code is "ORANGETHEORY" or "TRUE_HIIT" or "GENERAL_CARDIO" or "ZONE2_CARDIO" or "MIXED_CONDITIONING" or "EASY_CARDIO" or "WALKING" or "RUNNING" or "TREADMILL" or "CYCLING" or "ROWING" or "ELLIPTICAL" or "STAIR_CLIMBER" or "SWIMMING" or "RUNNING_INTERVALS" or "ROWING_INTERVALS" or "HIIT" or "OTF_2G" or "OTF_3G"
            || system.StartsWith("OTF_2G_") || system.StartsWith("OTF_3G_");
    }
    private static bool IsRecovery(WorkoutSession s) => Engine30WorkoutUiCatalogService.NormalizeCode(s.WorkoutTypeCode) is "REST" or "MOBILITY_RECOVERY";
}
