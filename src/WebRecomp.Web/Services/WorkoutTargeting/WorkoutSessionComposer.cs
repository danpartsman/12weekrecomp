using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.WorkoutTargeting;

/// <summary>Transparent selection/time-budget heuristics, not recovery-time diagnoses.</summary>
public static class WorkoutSessionComposer
{
    public static IReadOnlyList<string> PrimaryRegions(ExerciseCatalogItemRecord e)
        => BodyRegions.FromMuscles(e.MuscleTargets.Where(m => m.RoleCode.Equals("primary", StringComparison.OrdinalIgnoreCase)).Select(m => m.MuscleName));

    public static string Pattern(ExerciseCatalogItemRecord e)
    {
        var regions = PrimaryRegions(e);
        if (e.Category?.Contains("stretch", StringComparison.OrdinalIgnoreCase) == true) return "mobility";
        if (regions.Contains("quads")) return "knee dominant";
        if (regions.Intersect(new[] { "glutes", "hamstrings" }).Any()) return "hip dominant";
        if (regions.Contains("upperback") && !regions.Contains("shoulders")) return "pull";
        if (regions.Intersect(new[] { "chest", "shoulders", "triceps" }).Any()) return IsCompound(e) ? "push" : "upper isolation";
        return regions.FirstOrDefault() ?? "unknown";
    }
    public static bool IsCompound(ExerciseCatalogItemRecord e)
        => e.Mechanics?.Contains("compound", StringComparison.OrdinalIgnoreCase) == true;

    public static decimal RankExercise(ExerciseCatalogItemRecord e, IEnumerable<string> targets,
        IReadOnlyDictionary<string, decimal> recentRegions, IReadOnlyDictionary<Guid, decimal> recentExercises)
    {
        var primary = PrimaryRegions(e);
        var wanted = targets.ToHashSet();
        return primary.Count(wanted.Contains) * 20m
            + (IsCompound(e) ? 6m : 0m)
            - BodyRegions.LoadingRegions(e.MuscleTargets.Select(m => m.MuscleName)).Sum(r => recentRegions.GetValueOrDefault(r)) * 4m
            - recentExercises.GetValueOrDefault(e.ExerciseCatalogItemId) * 3m
            - (e.DifficultyLevel?.Contains("advanced", StringComparison.OrdinalIgnoreCase) == true ? 4m : 0m);
    }

    public static List<TargetExercise> Generate(List<ExerciseCatalogItemRecord> eligible, TodayWorkoutViewModel input,
        decimal rpe, IReadOnlyDictionary<string, decimal> recentRegions, IReadOnlyDictionary<Guid, decimal> recentExercises)
    {
        var targets = input.Targets.Count > 0 ? input.Targets : new List<string> { "chest", "upperback", "quads", "hamstrings", "glutes", "shoulders" };
        var pool = eligible.Where(e => PrimaryRegions(e).Intersect(targets).Any()
            && e.Category?.Contains("stretch", StringComparison.OrdinalIgnoreCase) != true
            && e.Category?.Contains("mobility", StringComparison.OrdinalIgnoreCase) != true
            && !new[]{"wall slide","bird dog","thoracic rotation","hip rotation","single leg balance","single-leg stance"}.Any(t=>e.Name.Contains(t,StringComparison.OrdinalIgnoreCase))).ToList();
        var maxExercises = Math.Clamp((input.Minutes - input.Additions.Count * 4 - 5) / 6, 1, input.Targets.Count == 1 ? 4 : 7);
        var chosen = new List<ExerciseCatalogItemRecord>();
        var covered = new HashSet<string>();
        // Cover requested areas before adding a second variation of the same movement.
        foreach (var target in targets.OrderBy(t => recentRegions.GetValueOrDefault(t)))
        {
            if (chosen.Count >= maxExercises) break;
            if (covered.Contains(target)) continue;
            var next = pool.Where(e => !chosen.Contains(e) && PrimaryRegions(e).Contains(target))
                .OrderByDescending(e => RankExercise(e, targets.Except(covered), recentRegions, recentExercises))
                .ThenBy(e => e.Name).FirstOrDefault();
            if (next is null) continue;
            chosen.Add(next); covered.UnionWith(PrimaryRegions(next));
        }
        while (chosen.Count < maxExercises)
        {
            var next = pool.Where(e => !chosen.Contains(e)
                    && chosen.Count(c => Pattern(c) == Pattern(e)) < (input.Targets.Count == 1 ? 2 : 1))
                .OrderByDescending(e => RankExercise(e, targets, recentRegions, recentExercises)
                    + (chosen.Any(c => Pattern(c) == Pattern(e)) ? 0m : 12m))
                .ThenBy(e => e.Name).FirstOrDefault();
            if (next is null) break;
            chosen.Add(next);
        }
        return chosen.OrderByDescending(IsCompound).ThenBy(e => recentRegions.GetValueOrDefault(PrimaryRegions(e).FirstOrDefault() ?? ""))
            .Select(e => new TargetExercise(e.ExerciseCatalogItemId, e.Name, e.Mechanics, 3,
                IsCompound(e) && rpe >= 7 ? 8 : 10, rpe,
                BodyRegions.FromMuscles(e.MuscleTargets.Select(m => m.MuscleName)),
                "Warm up first; choose a load matching the prescribed effort.", false,
                e.MeasurementType.Contains("time", StringComparison.OrdinalIgnoreCase) ? 30 : null)).ToList();
    }

    public static int EstimateMinutes(IEnumerable<TargetExercise> exercises, int warmupMinutes)
        => (int)Math.Ceiling(warmupMinutes + exercises.Sum(e => 1m
            + (e.Sets * (e.DurationSeconds ?? 40) + Math.Max(0, e.Sets - 1) * e.RestSeconds) / 60m));

    public static void FitSession(WorkoutTargetChoice choice, int availableMinutes, IReadOnlyDictionary<Guid, ExerciseCatalogItemRecord> catalog)
    {
        if (choice.Exercises.Count == 0) return;
        choice.WarmupMinutes = WorkoutTargetPolicy.ActivityFor(choice.TypeCode) == "strength" ? 5 : 2;
        var originals = choice.Exercises.Select(e => e with {
            RestSeconds = e.IsAddon ? 30 : IsCompound(catalog[e.ExerciseId]) ? e.Rpe >= 7 ? 120 : 90 : 60
        }).ToList();
        // Preserve saved/curated ordering; generated sessions already put compounds first.
        var requested = originals.ToList();
        var fitted = originals.Select(e => e with { Sets = 1 }).ToList();
        while (fitted.Count > 0 && EstimateMinutes(fitted, choice.WarmupMinutes) > availableMinutes)
        {
            var index = fitted.Count(e => !e.IsAddon) > 1 ? fitted.FindLastIndex(e => !e.IsAddon) : fitted.Count - 1;
            fitted.RemoveAt(index); originals.RemoveAt(index);
        }
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < fitted.Count; i++)
            {
                if (fitted[i].Sets >= originals[i].Sets) continue;
                var proposed = fitted[i] with { Sets = fitted[i].Sets + 1 };
                var previous = fitted[i]; fitted[i] = proposed;
                if (EstimateMinutes(fitted, choice.WarmupMinutes) <= availableMinutes) changed = true;
                else fitted[i] = previous;
            }
        }
        if (fitted.Count != requested.Count || fitted.Where((e, i) => e.Sets != originals[i].Sets).Any())
        {
            choice.Adapted = true;
            choice.Reasons.Add("Exercise count and sets adjusted to your available time.");
        }
        if (requested.Where(e=>e.IsAddon).Any(e=>!fitted.Any(f=>f.ExerciseId==e.ExerciseId)))
        {
            choice.Reasons.RemoveAll(r=>r.StartsWith("Includes a brief"));
            choice.Reasons.Add("Some requested add-ons could not fit the available time.");
        }
        choice.Exercises = fitted;
        if (fitted.Count > 0) choice.Rpe = fitted.Max(e => e.Rpe);
        choice.Minutes = fitted.Count == 0 ? 0 : EstimateMinutes(fitted, choice.WarmupMinutes);
        if (fitted.Count > 0) choice.Reasons.Add($"About {choice.Minutes} minutes including {choice.WarmupMinutes} minutes of warm-up, transitions and prescribed rests.");
    }
}
