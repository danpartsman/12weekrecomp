using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using WebRecomp.Web.Controllers;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Coaching;
namespace WebRecomp.Web.Tests;

public sealed class CoachCenterServiceTests
{
    private static RecompDbContext Database() => new(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    [Fact]
    public async Task NonOwnerCannotConnectAClientEvenWithAnOtherwiseValidCode()
    {
        using var db = Database(); var service = new CoachCenterService(db);
        var user = new UserAccount { UserId = Guid.NewGuid(), ExternalSubject = "non-owner" };
        var client = new Client { ClientId = Guid.NewGuid(), ClientName = "Managed client" };
        db.AddRange(user, client, new UserClientAccess { UserClientAccessId = Guid.NewGuid(), UserId = user.UserId,
            ClientId = client.ClientId, AccessRoleCode = "COACH" });
        await db.SaveChangesAsync();
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", "non-owner") }, "test")) };
        var controller = new MyCoachController(db, new AppUserContextService(db, new ConfigurationBuilder().Build()), service)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        var code = await service.InviteAsync(Guid.NewGuid(), default);
        Assert.IsType<ForbidResult>(await controller.Connect(new CoachConnectInput { Code = code }, default));
        Assert.False(await db.CoachClientRelationships.AnyAsync(x => x.StatusCode == "ACTIVE"));
    }

    [Fact]
    public async Task CoachClientEndpointRejectsAnUnassignedClientBeforeReadingTheirRecords()
    {
        using var db = Database(); var service = new CoachCenterService(db);
        var user = new UserAccount { UserId = Guid.NewGuid(), ExternalSubject = "unassigned-coach" };
        db.Add(user); await db.SaveChangesAsync();
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", "unassigned-coach") }, "test")) };
        var controller = new CoachController(db, new AppUserContextService(db, new ConfigurationBuilder().Build()), service)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        Assert.IsType<NotFoundResult>(await controller.Client(Guid.NewGuid(), default));
    }
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
        invite = await db.CoachClientRelationships.SingleAsync();
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
    [Fact]
    public async Task NewInvitesUseWordAndTwoDigitsExpireInOneDayAndAreDistinct()
    {
        using var db = Database(); var service = new CoachCenterService(db);
        var first = await service.InviteAsync(Guid.NewGuid(), default);
        var second = await service.InviteAsync(Guid.NewGuid(), default);
        Assert.Matches("^[A-Z]{4}[0-9]{2}$", first);
        Assert.NotEqual(first, second);
        Assert.All(await db.CoachClientRelationships.ToListAsync(), x => Assert.InRange((x.InviteExpiresAtUtc - x.CreatedAtUtc).TotalHours, 23.99, 24.01));
    }
    [Fact]
    public async Task ExpiredCodesAreReleasedWithoutRemovingHistoryAndLegacyCodesStillWork()
    {
        using var db = Database(); var service = new CoachCenterService(db);
        var oldCode = new string('A', 64);
        var legacy = new CoachClientRelationship {CoachClientRelationshipId=Guid.NewGuid(),CoachUserId=Guid.NewGuid(),InviteTokenHash=CoachCenterService.HashCode(oldCode),InviteExpiresAtUtc=DateTime.UtcNow.AddHours(1)};
        var expired = new CoachClientRelationship {CoachClientRelationshipId=Guid.NewGuid(),CoachUserId=Guid.NewGuid(),InviteTokenHash=CoachCenterService.HashCode(new string('B',64)),InviteExpiresAtUtc=DateTime.UtcNow.AddDays(-1)};
        db.AddRange(legacy,expired); await db.SaveChangesAsync();
        await service.InviteAsync(Guid.NewGuid(), default);
        var history = await db.CoachClientRelationships.SingleAsync(x=>x.CoachClientRelationshipId==expired.CoachClientRelationshipId);
        Assert.Equal("REVOKED",history.StatusCode); Assert.Null(history.InviteTokenHash); Assert.NotNull(history.RevokedAtUtc);
        Assert.False(await service.AcceptAsync(Guid.NewGuid(),new string('B',64),default));
        Assert.True(await service.AcceptAsync(Guid.NewGuid(),oldCode,default));
    }

}
