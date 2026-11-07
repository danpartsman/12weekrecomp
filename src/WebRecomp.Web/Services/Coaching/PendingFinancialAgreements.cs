using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Controllers;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services.Notifications;

namespace WebRecomp.Web.Services.Coaching;

public sealed record PendingFinancialAgreement(Guid ClientId, Guid CoachId, string CoachName, int Revision);
public static class PendingFinancialAgreements
{
    public static async Task<IReadOnlyList<PendingFinancialAgreement>> ReadAsync(RecompDbContext db,
        NotificationFiles files, Guid clientId, Guid userId, CancellationToken ct)
    {
        var owners = await db.UserClientAccess.AsNoTracking().Where(x => x.ClientId == clientId && x.AccessRoleCode == "OWNER" && x.RevokedAtUtc == null)
            .Select(x => x.UserId).Distinct().Take(2).ToListAsync(ct);
        if (owners.Count != 1 || owners[0] != userId || !await db.Clients.AnyAsync(x => x.ClientId == clientId && x.ClientStatusCode == "ACTIVE", ct)) return [];
        var coaches = await (from link in db.CoachClientRelationships.AsNoTracking()
            join coach in db.UserAccounts.AsNoTracking() on link.CoachUserId equals coach.UserId
            where link.ClientId == clientId && link.StatusCode == "ACTIVE" && link.RevokedAtUtc == null && coach.AccountStatusCode == "ACTIVE"
            select new { Id = coach.UserId, Name = coach.PreferredDisplayName ?? coach.DisplayName ?? "Coach" }).Distinct().ToListAsync(ct);
        var pending = new List<PendingFinancialAgreement>();
        foreach (var coach in coaches)
        {
            try
            {
                var agreement = files.Read<CoachFinancialAgreement>($"financial-agreements/{clientId:N}-{coach.Id:N}");
                if (agreement?.Status == "pending") pending.Add(new(clientId, coach.Id, coach.Name, agreement.Revision));
            }
            catch (Exception e) when (e is IOException or CryptographicException or JsonException) { /* A notice must not prevent the page from loading. */ }
        }
        return pending.OrderBy(x => x.CoachName).ToArray();
    }
}
