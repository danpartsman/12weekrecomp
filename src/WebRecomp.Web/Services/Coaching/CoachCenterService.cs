using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Services.Coaching;

public sealed class CoachCenterService(RecompDbContext db)
{
    public static string HashCode(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant())));
    public Task<bool> CanAccessAsync(Guid coachUserId, Guid clientId, CancellationToken ct = default) =>
        db.CoachClientRelationships.AnyAsync(x => x.CoachUserId == coachUserId && x.ClientId == clientId
            && x.StatusCode == "ACTIVE" && x.RevokedAtUtc == null, ct);

    public async Task<CoachCenterViewModel> BuildAsync(UserAccount user, CancellationToken ct)
    {
        var profile = await db.CoachProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.UserId, ct);
        var ids = db.CoachClientRelationships.Where(x => x.CoachUserId == user.UserId && x.StatusCode == "ACTIVE" && x.RevokedAtUtc == null).Select(x => x.ClientId);
        var clients = await db.Clients.AsNoTracking().Where(x => ids.Contains(x.ClientId) && x.ClientStatusCode == "ACTIVE").OrderBy(x => x.ClientName).ToListAsync(ct);
        var roster = new List<CoachRosterClient>();
        foreach (var client in clients)
        {
            var goal = await db.Programs.Where(x => x.ClientId == client.ClientId && x.ProgramStatusCode == "ACTIVE").OrderByDescending(x => x.StartDate).Select(x => x.GoalCode).FirstOrDefaultAsync(ct);
            var last = await db.WorkoutSessions.Where(x => x.ClientId == client.ClientId && x.CompletedFlag).OrderByDescending(x => x.WorkoutDate).Select(x => (DateOnly?)x.WorkoutDate).FirstOrDefaultAsync(ct);
            roster.Add(new(client.ClientId, client.ClientName, goal, last));
        }
        return new(new() { DisplayName = profile?.DisplayName ?? user.PreferredDisplayName ?? user.DisplayName ?? "Coach", Bio = profile?.Bio, Specialties = profile?.Specialties }, roster,
            await db.CoachClientRelationships.CountAsync(x => x.CoachUserId == user.UserId && x.StatusCode == "INVITED" && x.InviteExpiresAtUtc > DateTime.UtcNow, ct));
    }

    public async Task<string> InviteAsync(Guid coachUserId, CancellationToken ct)
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.CoachClientRelationships.Add(new() { CoachClientRelationshipId = Guid.NewGuid(), CoachUserId = coachUserId,
            InviteTokenHash = HashCode(code), CreatedAtUtc = DateTime.UtcNow, InviteExpiresAtUtc = DateTime.UtcNow.AddDays(14) });
        await db.SaveChangesAsync(ct);
        return code;
    }

    public async Task<bool> AcceptAsync(Guid clientId, string code, CancellationToken ct)
    {
        var hash = HashCode(code);
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var invite = await db.CoachClientRelationships.SingleOrDefaultAsync(x => x.InviteTokenHash == hash && x.StatusCode == "INVITED" && x.InviteExpiresAtUtc > DateTime.UtcNow, ct);
        if (invite is null) return false;
        if (await CanAccessAsync(invite.CoachUserId, clientId, ct)) return false;
        invite.ClientId = clientId;
        invite.StatusCode = "ACTIVE";
        invite.AcceptedAtUtc = DateTime.UtcNow;
        invite.InviteTokenHash = null; // A consumed code can never connect a second client.
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return true;
    }
}
