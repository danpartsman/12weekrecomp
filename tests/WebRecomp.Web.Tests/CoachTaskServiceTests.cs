using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Coaching;

namespace WebRecomp.Web.Tests;

public sealed class CoachTaskServiceTests
{
    [Theory]
    [InlineData("minute", 1)]
    [InlineData("hour", 60)]
    public async Task TimeWorkPostsOnlyActiveTimeAcrossStopsAndCapsTheFee(string billing, int rate)
    {
        await using var f = new Fixture();
        var party = await f.SeedAsync();
        var id = Guid.NewGuid();
        await f.Tasks.AssignAsync(party, id, "Research", "Check three sources", billing, null, 5, 10, rate, 8,
            f.Clock.Now.UtcDateTime.AddHours(4), default);
        await f.Tasks.UpdateAsync(party, id, 1, "start", null, default);
        f.Clock.Now = f.Clock.Now.AddMinutes(2.5);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Tasks.UpdateAsync(party, id, 2, "complete", null, default));
        await f.Tasks.UpdateAsync(party, id, 2, "stop", null, default);
        var row = await f.Db.CoachAccountabilityAgreements.SingleAsync();
        Assert.Equal(2.50m, Assert.Single(await f.Ledger.ReadAsync(row, default)).Amount);
        f.Clock.Now = f.Clock.Now.AddHours(1);
        await f.Tasks.UpdateAsync(party, id, 3, "start", null, default);
        f.Clock.Now = f.Clock.Now.AddMinutes(20);
        await f.Tasks.UpdateAsync(party, id, 4, "complete", null, default);

        var task = Assert.Single(await f.Tasks.ReadAsync(party, default));
        Assert.Equal(10m, task.WorkFeePosted);
        Assert.Equal(TimeSpan.FromMinutes(10).Ticks, task.ElapsedTicks);
        Assert.Null(task.RunningSinceUtc);
        Assert.All(await f.Ledger.ReadAsync(row, default), entry => Assert.Equal(party.CoachId, entry.CoachUserId));
        Assert.Equal(10m, (await f.Ledger.ReadAsync(row, default)).Sum(entry => entry.Amount));
        Assert.Empty(f.Db.WorkoutSessions);
    }

    [Fact]
    public async Task QuantityTaskPostsOneLatePenaltyAndKeepsItsChargeWithTheAssigningCoach()
    {
        await using var f = new Fixture();
        var party = await f.SeedAsync();
        var other = await f.AddCoachAsync();
        var id = Guid.NewGuid();
        var due = f.Clock.Now.UtcDateTime.AddMinutes(10);
        await f.Tasks.AssignAsync(party, id, "Review", null, "quantity", "pages", 2, 5, 2.50m, 8m, due, default);
        await f.Tasks.AssignAsync(party, id, "Review", null, "quantity", "pages", 2, 5, 2.50m, 8m, due, default);
        Assert.Empty(await f.Tasks.ReadAsync(other, default));
        Assert.False(await f.Tasks.UpdateAsync(other, id, 1, "start", null, default));
        Assert.Equal(0, await f.Tasks.PostOverdueAsync(default));
        f.Clock.Now = f.Clock.Now.AddMinutes(11);
        Assert.Equal(1, await f.Tasks.PostOverdueAsync(default));
        Assert.Equal(0, await f.Tasks.PostOverdueAsync(default));
        await f.Tasks.UpdateAsync(party, id, 1, "start", null, default);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Tasks.UpdateAsync(party, id, 2, "complete", 6, default));
        await f.Tasks.UpdateAsync(party, id, 2, "complete", 3.5m, default);
        await Assert.ThrowsAsync<AccountabilityConflictException>(() => f.Tasks.UpdateAsync(party, id, 2, "complete", 3.5m, default));
        Assert.Equal(0, await f.Tasks.PostOverdueAsync(default));

        var task = Assert.Single(await f.Tasks.ReadAsync(party, default));
        Assert.True(task.LateFeePosted);
        Assert.Equal(3.5m, task.Quantity);
        var entries = await f.Ledger.ReadAsync(await f.Db.CoachAccountabilityAgreements.SingleAsync(), default);
        Assert.Equal(8m, Assert.Single(entries, x => x.Kind == "late-penalty").Amount);
        Assert.Equal(8.75m, Assert.Single(entries, x => x.Kind == "fee").Amount);
        Assert.All(entries, x => Assert.Equal(party.CoachId, x.CoachUserId));
        Assert.Empty(f.Db.WorkoutSessions);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 20, 18, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Guid client = Guid.NewGuid();
        private readonly Guid owner = Guid.NewGuid();
        public readonly RecompDbContext Db = new(new DbContextOptionsBuilder<RecompDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly TestClock Clock = new();
        public AccountabilityLedger Ledger { get; }
        public CoachTaskService Tasks { get; }

        public Fixture()
        {
            var access = new CoachAccountabilityPreviewService(Db, new CoachCenterService(Db), null!);
            Ledger = new(Db, access);
            Tasks = new(Db, new SimpleCoachingService(Db, access, Ledger), Ledger, Clock);
        }

        public async Task<CoachTaskParty> SeedAsync()
        {
            Db.AddRange(new Client { ClientId = client, ClientName = "Client", TimeZone = "UTC" },
                new UserAccount { UserId = owner, ExternalSubject = "owner", EmailNormalized = "owner@example.com" },
                new UserClientAccess { UserClientAccessId = Guid.NewGuid(), UserId = owner, ClientId = client });
            return await AddCoachAsync();
        }

        public async Task<CoachTaskParty> AddCoachAsync()
        {
            var coach = Guid.NewGuid();
            Db.AddRange(new UserAccount { UserId = coach, ExternalSubject = coach.ToString(), EmailNormalized = $"{coach}@example.com" },
                new CoachClientRelationship { CoachClientRelationshipId = Guid.NewGuid(), ClientId = client, CoachUserId = coach, StatusCode = "ACTIVE" });
            await Db.SaveChangesAsync();
            return Assert.IsType<CoachTaskParty>(await Tasks.PartyAsync(client, coach, default));
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
