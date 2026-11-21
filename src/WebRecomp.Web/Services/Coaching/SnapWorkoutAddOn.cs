using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.Services.Coaching;

public sealed record SnapAddOnPanel(Guid ClientId, string WorkoutId, Guid? CoachUserId, string Title,
    SnapWorkoutAddOn Offer, string? Response, bool CanRespond);

public sealed record SnapWorkoutAddOn(Guid Id, string Focus, string Exercise, string Instructions)
{
    private sealed record Prescription(string Exercise, string Instructions);
    private sealed record MuscleGroup(string Name, string[] Muscles, Prescription Warmup, Prescription Finisher);

    private static readonly MuscleGroup[] Groups =
    [
        new("Chest",
            ["chest", "pecs", "pectorals", "pectoralis major"],
            new("Wall slides", "1 set of 8 slow reps. Keep the motion comfortable."),
            new("Wall or incline push-up", "1 set of 8–15 controlled reps. Use a stable surface and stop with 2 reps in reserve.")),
        new("Shoulders",
            ["shoulders", "delts", "deltoids", "front delts", "side delts", "front deltoids", "side deltoids", "rear delts", "rear deltoids", "lateral deltoid", "anterior deltoid", "posterior deltoid"],
            new("Easy shoulder circles", "1 set of 8 slow circles in each direction. Keep the motion comfortable."),
            new("Shoulder scaption", "1 set of 12–15 slow reps. Raise your arms slightly forward of your sides to a comfortable height, then lower slowly.")),
        new("Triceps",
            ["triceps", "triceps brachii"],
            new("Unloaded elbow extensions", "1 set of 8 easy reps per side. Straighten and bend the elbow slowly."),
            new("Close-grip wall push-up", "1 set of 8–15 controlled reps. Keep your elbows close and stop with 2 reps in reserve.")),
        new("Biceps and forearms",
            ["biceps", "biceps brachii", "brachialis", "brachioradialis", "forearms", "arms"],
            new("Unloaded curls with palm turns", "1 set of 8 easy reps per side. Turn the palm up as you bend the elbow."),
            new("Self-resisted curl", "1 set of 8–12 controlled reps per side. Use the opposite hand to provide light, steady resistance.")),
        new("Back",
            ["back", "upper back", "middle back", "lower back", "lats", "latissimus dorsi", "traps", "trapezius", "rhomboids", "neck"],
            new("Standing shoulder blade glide", "1 set of 8 slow reps. Gently draw the shoulder blades together and release."),
            new("Prone W raises", "1 set of 8–12 controlled reps. Pause briefly at the top and stop with 2 reps in reserve.")),
        new("Quads and inner thighs",
            ["quads", "quadriceps", "adductors", "inner thighs", "legs", "lower body"],
            new("Supported bodyweight squat", "1 set of 8 easy reps. Use a stable support and a comfortable depth."),
            new("Chair sit-to-stand", "1 set of 10–15 controlled reps from a stable chair. Stop with 2 reps in reserve.")),
        new("Glutes and hamstrings",
            ["glutes", "gluteus maximus", "gluteus medius", "hamstrings", "hips", "hip flexors", "abductors"],
            new("Bodyweight hip hinge", "1 set of 8 slow reps. Keep a soft bend in the knees."),
            new("Glute bridge", "1 set of 10–15 controlled reps. Pause briefly at the top and stop with 2 reps in reserve.")),
        new("Calves",
            ["calves", "gastrocnemius", "soleus"],
            new("Supported ankle rocks", "1 set of 8 easy reps per side. Keep the working heel down."),
            new("Supported calf raise", "1 set of 12–20 controlled reps. Hold a stable support and stop with 2 reps in reserve.")),
        new("Core",
            ["core", "abs", "abdominals", "obliques", "rectus abdominis"],
            new("Standing trunk rotation", "1 set of 6 slow reps per side in a comfortable range."),
            new("Supine heel taps", "1 set of 6–10 controlled reps per side. Stop if you lose trunk position."))
    ];

    public static async Task<string> FocusAsync(IReadOnlyList<AccountabilityExercise> exercises,
        RecompDbContext? db, CancellationToken ct)
    {
        var names = exercises.Where(e => string.IsNullOrWhiteSpace(e.MuscleFocus)).Select(e => e.Name).Distinct().ToArray();
        var targets = db is null || names.Length == 0
            ? []
            : await (from target in db.ExerciseMuscleTargets.AsNoTracking()
                     join item in db.ExerciseCatalogItems.AsNoTracking() on target.ExerciseCatalogItemId equals item.ExerciseCatalogItemId
                     where target.RoleCode == "primary" && item.IsActive && item.SourceCode != "12WR_CSV_UNMAPPED" && names.Contains(item.Name)
                     select new { item.Name, target.MuscleName, target.MuscleSlug }).ToArrayAsync(ct);
        var scores = new Dictionary<MuscleGroup, decimal>();
        foreach (var exercise in exercises)
        {
            var muscles = SnapWorkoutEngine.Focus(exercise.MuscleFocus);
            if (muscles.Length == 0)
                muscles = targets.Where(t => t.Name.Equals(exercise.Name, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(t => new[] { t.MuscleName, t.MuscleSlug.Replace('-', ' ') }).ToArray();
            var matching = Groups.Where(group => muscles.Intersect(group.Muscles, StringComparer.OrdinalIgnoreCase).Any()).ToArray();
            foreach (var group in matching)
                scores[group] = scores.GetValueOrDefault(group) + (decimal)exercise.Sets / matching.Length;
        }

        return scores.OrderByDescending(x => x.Value).Select(x => x.Key.Name).FirstOrDefault()
            ?? throw new ArgumentException("Add a muscle focus to a Snap exercise or pick a catalog exercise with a muscle target to assign optional work.");
    }

    public static SnapWorkoutAddOn Create(string focus, bool finisher)
    {
        var group = Groups.Single(group => group.Name == focus);
        var prescription = finisher ? group.Finisher : group.Warmup;
        return new(Guid.NewGuid(), focus, prescription.Exercise, prescription.Instructions);
    }

    public static SnapWorkoutAddOn? KeepIfUnchanged(SnapWorkoutAddOn? previous, SnapWorkoutAddOn? updated) =>
        previous is not null && updated is not null && previous == (updated with { Id = previous.Id }) ? previous : updated;

    public static void Validate(SnapWorkoutAddOn? offer)
    {
        if (offer is not null && (offer.Id == Guid.Empty || string.IsNullOrWhiteSpace(offer.Focus) ||
            offer.Focus.Length > 80 || string.IsNullOrWhiteSpace(offer.Exercise) || offer.Exercise.Length > 120 ||
            string.IsNullOrWhiteSpace(offer.Instructions) || offer.Instructions.Length > 300))
            throw new ArgumentException("Check the optional Snap exercise.");
    }
}
