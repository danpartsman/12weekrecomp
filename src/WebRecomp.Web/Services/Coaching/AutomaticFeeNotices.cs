using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services.Notifications;

namespace WebRecomp.Web.Services.Coaching;

public sealed record AutomaticFeeNotice(string Id, string Title, string Url);

public static class AutomaticFeeNotices
{
    public static string ReceiptKey(Guid user) => $"users/automatic-fees-{user:N}";

    public static async Task<IReadOnlyList<AutomaticFeeNotice>> ReadAsync(
        RecompDbContext db, NotificationFiles files, Guid user, Guid? selectedClient, bool isCoach, CancellationToken ct)
    {
        Guid? ownedClient = null;
        if (selectedClient is { } clientId)
        {
            var owners = await db.UserClientAccess.AsNoTracking()
                .Where(x => x.ClientId == clientId && x.AccessRoleCode == "OWNER" && x.RevokedAtUtc == null)
                .Select(x => x.UserId).Distinct().Take(2).ToListAsync(ct);
            if (owners.Count == 1 && owners[0] == user) ownedClient = clientId;
        }
        if (!ownedClient.HasValue && !isCoach) return [];

        var pairs = await (from link in db.CoachClientRelationships.AsNoTracking()
            join client in db.Clients.AsNoTracking() on link.ClientId equals (Guid?)client.ClientId
            join coach in db.UserAccounts.AsNoTracking() on link.CoachUserId equals coach.UserId
            where link.StatusCode == "ACTIVE" && link.RevokedAtUtc == null
                && client.ClientStatusCode == "ACTIVE" && coach.AccountStatusCode == "ACTIVE"
                && (link.ClientId == ownedClient || (isCoach && link.CoachUserId == user))
            select new { client.ClientId, CoachId = coach.UserId, client.ClientName,
                CoachName = coach.PreferredDisplayName ?? coach.DisplayName ?? "Coach" }).Distinct().ToListAsync(ct);
        if (pairs.Count == 0) return [];
        var byPair = pairs.ToDictionary(x => (x.ClientId, x.CoachId));
        var clients = pairs.Select(x => x.ClientId).Distinct().ToArray();
        var seen = (files.Read<List<string>>(ReceiptKey(user)) ?? []).ToHashSet();
        var since = DateTime.UtcNow.AddDays(-30);
        var events = await db.CoachAccountabilityEvents.AsNoTracking()
            .Where(x => clients.Contains(x.ClientId) && x.Kind == "RESULT" && x.CreatedAtUtc >= since
                && x.PayloadJson.Contains("\"ledgerEntries\"") && x.PayloadJson.Contains("agreement:"))
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Revision)
            .Select(x => new { x.ClientId, x.PayloadJson }).ToListAsync(ct);
        var notices = new List<AutomaticFeeNotice>();
        foreach (var batch in events)
        {
            using var payload = JsonDocument.Parse(batch.PayloadJson);
            if (!payload.RootElement.TryGetProperty("ledgerEntries", out var items)) continue;
            foreach (var entry in items.Deserialize<List<AccountabilityLedgerEntry>>(AccountabilityDocument.Json) ?? [])
            {
                if (!entry.Automatic || entry.Amount <= 0 || !entry.SourceKey.StartsWith("agreement:", StringComparison.Ordinal)
                    || entry.CoachUserId is not { } coachId || !byPair.TryGetValue((batch.ClientId, coachId), out var pair))
                    continue;
                var id = entry.Id.ToString("N");
                if (!seen.Add(id)) continue;
                var name = ownedClient == batch.ClientId ? pair.CoachName : pair.ClientName;
                var amount = entry.Amount.ToString("C", CultureInfo.GetCultureInfo("en-US"));
                notices.Add(new(id, $"{name} · {amount} · {entry.Description}",
                    $"/clients/{batch.ClientId:D}/coach-ledger?coachUserId={coachId:D}"));
                if (notices.Count == 50) return notices;
            }
        }
        return notices;
    }
}
