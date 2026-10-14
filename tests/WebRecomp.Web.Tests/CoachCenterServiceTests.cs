using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Coaching;
namespace WebRecomp.Web.Tests;

public sealed class CoachCenterServiceTests
{
    private static RecompDbContext Database() => new(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    [Fact]
    public async Task InviteIsHashedSingleUseAndOnlyGrantsTheInvitingCoachAccess()
    {
        using var db = Database();
        var service = new CoachCenterService(db);
        var coach = Guid.NewGuid(); var otherCoach = Guid.NewGuid(); var client = Guid.NewGuid(); var otherClient = Guid.NewGuid();
        var code = await service.InviteAsync(coach, default);
        var invite = await db.CoachClientRelationships.SingleAsync();
        Assert.NotEqual(code, invite.InviteTokenHash);
        Assert.False(await service.CanAccessAsync(coach, client));
        Assert.True(await service.AcceptAsync(client, code.ToLowerInvariant(), default));
        Assert.True(await service.CanAccessAsync(coach, client));
        Assert.False(await service.CanAccessAsync(otherCoach, client));
        Assert.False(await service.CanAccessAsync(coach, otherClient));
        Assert.False(await service.AcceptAsync(otherClient, code, default));
        Assert.Empty(await db.UserClientAccess.ToListAsync()); // Existing client ownership/access is never repurposed.
        invite.StatusCode = "REVOKED"; invite.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.False(await service.CanAccessAsync(coach, client));
    }
    [Fact]
    public async Task ExpiredAndDuplicateConnectionsDoNotGrantAccess()
    {
        using var db = Database(); var service = new CoachCenterService(db);
        var coach = Guid.NewGuid(); var client = Guid.NewGuid();
        var expired = await service.InviteAsync(coach, default);
        (await db.CoachClientRelationships.SingleAsync()).InviteExpiresAtUtc = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        Assert.False(await service.AcceptAsync(client, expired, default));
        var active = await service.InviteAsync(coach, default);
        Assert.True(await service.AcceptAsync(client, active, default));
        var duplicate = await service.InviteAsync(coach, default);
        Assert.False(await service.AcceptAsync(client, duplicate, default));
        Assert.Single(await db.CoachClientRelationships.Where(x => x.StatusCode == "ACTIVE").ToListAsync());
    }
    [Fact]
    public async Task RosterDoesNotRevealUnassignedClients()
    {
        using var db = Database(); var service = new CoachCenterService(db);
        var coach = new UserAccount { UserId = Guid.NewGuid(), ExternalSubject = "coach" };
        var mine = new Client { ClientId = Guid.NewGuid(), ClientName = "Connected client" };
        var other = new Client { ClientId = Guid.NewGuid(), ClientName = "Private client" };
        db.AddRange(mine, other);
        await db.SaveChangesAsync();
        Assert.Empty((await service.BuildAsync(coach, default)).Clients);
        Assert.True(await service.AcceptAsync(mine.ClientId, await service.InviteAsync(coach.UserId, default), default));
        var roster = (await service.BuildAsync(coach, default)).Clients;
        Assert.Equal(mine.ClientId, Assert.Single(roster).ClientId);
    }
}
