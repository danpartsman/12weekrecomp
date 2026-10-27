using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
namespace WebRecomp.Web.Services.Coaching;

public sealed class SnapWorkoutLibrary(RecompDbContext db, CoachCenterService coaches)
{
    public async Task<IReadOnlyList<AccountabilityWorkout>> ReadAsync(Guid coach, Guid client, CancellationToken ct)
    {
        if (!await coaches.CanAccessAsync(coach, client, ct)
            || !await db.Clients.AnyAsync(x => x.ClientId == client && x.ClientStatusCode == "ACTIVE", ct)) throw new UnauthorizedAccessException();
        // Only assignments authored by this coach. Removed assignments remain reusable in the audit history.
        var history = await db.CoachAccountabilityEvents.AsNoTracking().Where(x => x.ActorUserId == coach && x.Kind == "CONFIG")
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Revision).Take(500).Select(x => x.PayloadJson).ToListAsync(ct);
        var candidates = new List<AccountabilityWorkout>(); var seen = new HashSet<string>();
        foreach (var json in history)
        {
            AccountabilityState state;
            try { state = AccountabilityDocument.Parse(json); } catch (ArgumentException) { continue; }
            foreach (var workout in state.SnapWorkouts.AsEnumerable().Reverse())
            {
                var template = new AccountabilityWorkout { Name = workout.Name, Cap = workout.Cap,
                    Exercises = workout.Exercises.Select((e, i) => new AccountabilityExercise { Id = "exercise-" + i, CatalogId = e.CatalogId,
                        Name = e.Name, Sets = e.Sets, Reps = e.Reps, Weight = e.Weight, Type = e.Type, Basis = e.Basis, Rate = e.Rate,
                        CompletionReward = e.CompletionReward, ExtraRepRate = e.ExtraRepRate }).ToList() };
                var signature = AccountabilityDocument.Canonical(JsonSerializer.Serialize(template, AccountabilityDocument.Json));
                if (seen.Add(signature)) candidates.Add(template);
            }
        }
        var ids = candidates.SelectMany(x => x.Exercises).Select(x => Guid.Parse(x.CatalogId)).Distinct().ToArray();
        var prefix = client.ToString("N") + ":";
        var allowed = (await db.ExerciseCatalogItems.AsNoTracking().Where(x => ids.Contains(x.ExerciseCatalogItemId) && x.IsActive
            && (x.SourceCode != "12WR_CSV_UNMAPPED" || x.SourceExternalId.StartsWith(prefix))).Select(x => x.ExerciseCatalogItemId).ToListAsync(ct)).ToHashSet();
        return candidates.Where(w => w.Exercises.All(e => allowed.Contains(Guid.Parse(e.CatalogId)))).Take(100).ToList();
    }
    public static bool Completed(AccountabilityWorkout workout, AccountabilityResult? result) => result is { Finalized: true }
        && workout.Exercises.All(e => result.Sets.TryGetValue(e.Id, out var sets)
            && sets.Count(s => s.Reps >= e.Reps && s.Weight >= e.Weight) >= e.Sets);
}
