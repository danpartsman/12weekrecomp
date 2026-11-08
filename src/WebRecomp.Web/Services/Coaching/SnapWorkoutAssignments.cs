using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.Services.Coaching;

internal static class SnapWorkoutAssignments
{
    public static async Task<Dictionary<string, Guid>> OwnersAsync(RecompDbContext db, Guid clientId, CancellationToken ct)
    {
        var events = await db.CoachAccountabilityEvents.AsNoTracking()
            .Where(entry => entry.ClientId == clientId && entry.Kind == "CONFIG")
            .OrderBy(entry => entry.Revision)
            .Select(entry => new { entry.ActorUserId, entry.PayloadJson }).ToListAsync(ct);
        var owners = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var entry in events)
        {
            using var document = JsonDocument.Parse(entry.PayloadJson);
            if (!document.RootElement.TryGetProperty("snapWorkouts", out var workouts)) continue;
            foreach (var workout in workouts.EnumerateArray())
                if (workout.TryGetProperty("id", out var id) && id.GetString() is { } key)
                    owners.TryAdd(key, entry.ActorUserId);
        }
        return owners;
    }

    public static void Filter(AccountabilityState state, IReadOnlyDictionary<string, Guid> owners, Guid coach)
    {
        state.SnapWorkouts.RemoveAll(workout => owners.GetValueOrDefault(workout.Id) != coach);
        var visible = state.SnapWorkouts.Select(workout => workout.Id).ToHashSet();
        foreach (var id in state.SnapResults.Keys.Where(id => !visible.Contains(id)).ToArray())
            state.SnapResults.Remove(id);
    }

    public static async Task<Dictionary<Guid, string>> NamesAsync(RecompDbContext db, IEnumerable<Guid> coachIds, CancellationToken ct)
    {
        var ids = coachIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var accounts = await db.UserAccounts.AsNoTracking().Where(user => ids.Contains(user.UserId))
            .ToDictionaryAsync(user => user.UserId, user => user.PreferredDisplayName ?? user.DisplayName ?? "Coach", ct);
        var profiles = await db.CoachProfiles.AsNoTracking().Where(profile => ids.Contains(profile.UserId))
            .ToListAsync(ct);
        foreach (var profile in profiles.Where(profile => !string.IsNullOrWhiteSpace(profile.DisplayName)))
            accounts[profile.UserId] = profile.DisplayName;
        return accounts;
    }
}
