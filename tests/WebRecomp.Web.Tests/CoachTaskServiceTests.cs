using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.Notifications;

namespace WebRecomp.Web.Tests;

public sealed class CoachTaskServiceTests
{
    [Theory]
    [InlineData("fixed")]
    [InlineData("quantity")]
    [InlineData("minute")]
    [InlineData("hour")]
    public async Task OnlyTimedTasksGetTheDefaultDeadline(string billing)
    {
        await using var f = new Fixture();
        var party = await f.SeedAsync();
        await f.Tasks.AssignAsync(party, Guid.NewGuid(), "Task", null, billing, "pages", 1, 5, 1, 4, null, default);
        var task = Assert.Single(await f.Tasks.ReadAsync(party, default));
        if (billing is "minute" or "hour")
        {
            Assert.Equal(f.Clock.Now.AddHours(2), task.DueUtc);
            Assert.Equal(4m, task.LateFee);
        }
        else
        {
            Assert.Null(task.DueUtc);
            Assert.Equal(0m, task.LateFee);
            f.Clock.Now = f.Clock.Now.AddDays(1);
            Assert.Equal(0, await f.Tasks.PostOverdueAsync(default));
        }
    }

    [Theory]
    [InlineData(null, null, 15)]
    [InlineData(5, null, 15)]
    [InlineData(null, 10, 10)]
    public async Task TimedTasksAcceptEitherOrBothLimitsBlank(int? min, int? max, int billableMinutes)
    {
        await using var f = new Fixture();
        var party = await f.SeedAsync();
        var id = Guid.NewGuid();
        await f.Tasks.AssignAsync(party, id, "Work", null, "hour", null, min, max, 60, 0,
            f.Clock.Now.UtcDateTime.AddHours(4), default);
        await f.Tasks.UpdateAsync(party, id, 1, "start", null, default);
        f.Clock.Now = f.Clock.Now.AddMinutes(15);
        await f.Tasks.UpdateAsync(party, id, 2, "complete", null, default);
        Assert.Equal((decimal)billableMinutes, Assert.Single(await f.Tasks.ReadAsync(party, default)).WorkFeePosted);
    }

    [Fact]
    public async Task ProofIsOptionalAndOnlyTheEnabledTaskPairCanReadIt()
    {
        await using var f = new Fixture();
        var party = await f.SeedAsync();
        var other = await f.AddCoachAsync();
        var id = Guid.NewGuid();
        var due = f.Clock.Now.UtcDateTime.AddHours(2);
        await f.Tasks.AssignAsync(party, id, "Work", null, "fixed", null, null, null, 0, 10, due, default,
            allowProofPhoto: true);
        await f.Tasks.UpdateAsync(party, id, 1, "start", null, default);
        await f.Tasks.UpdateAsync(party, id, 2, "complete", null, default);
        Assert.Null(await f.Tasks.ReadProofAsync(party, id, default));
        await using var invalid = new MemoryStream(new byte[32]);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Tasks.AttachProofAsync(party, id, invalid, invalid.Length, default));
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/2gAAAAASUVORK5CYII=");
        await using var photo = new MemoryStream(bytes);
        Assert.False(await f.Tasks.AttachProofAsync(other, id, photo, photo.Length, default));
        Assert.True(await f.Tasks.AttachProofAsync(party, id, photo, photo.Length, default));
        var proof = Assert.IsType<CoachTaskProof>(await f.Tasks.ReadProofAsync(party, id, default));
        Assert.Equal("image/png", proof.ContentType);
        Assert.Equal(bytes, proof.Image);
        Assert.Null(await f.Tasks.ReadProofAsync(other, id, default));
        var row = await f.Db.CoachAccountabilityAgreements.SingleAsync();
        Assert.Empty(await f.Ledger.ReadAsync(row, default));
    }

    [Fact]
    public async Task AssignmentScheduleUsesTheClientsLocalDaysAndClipsOvernightBlocks()
    {
        await using var f = new Fixture();
        var party = await f.SeedAsync();
        var client = await f.Db.Clients.SingleAsync();
        client.TimeZone = "America/Los_Angeles";
        var today = new DateOnly(2026, 11, 20);
        var program = new ProgramRecord { ProgramId = Guid.NewGuid(), ClientId = party.ClientId,
            SelectedPlanCode = "PLAN_3", GoalCode = "RECOMP" };
        f.Db.AddRange(program, new PlannedWorkout { PlannedWorkoutId = Guid.NewGuid(), ClientId = party.ClientId,
            ProgramId = program.ProgramId, PlannedDate = today.AddDays(1), WorkoutTypeCode = "REST",
            SourceCode = "WORKOUT_TARGET", Notes = "Planned recovery" });
        await f.Db.SaveChangesAsync();
        f.Files.Write($"users/availability-{party.ClientId:N}", new AvailabilityCalendar
        {
            Blocks = [new() { Kind = "sleep", StartDate = today, EndDate = today,
                StartTime = new(22, 0), EndTime = new(6, 0) }]
        });
        var days = await new SnapAvailabilityService(f.Db, f.Files).TodayAndTomorrowAsync(party.ClientId,
            new(2026, 11, 21, 2, 0, 0, TimeSpan.Zero), default);
        Assert.Equal(new[] { today, today.AddDays(1) }, days.Select(day => day.Date));
        Assert.Equal("Planned recovery", days[1].PlannedWorkout);
        var evening = Assert.Single(days[0].Reserved);
        var morning = Assert.Single(days[1].Reserved);
        Assert.Equal(2d, (evening.End - evening.Start).TotalHours);
        Assert.Equal(6d, (morning.End - morning.Start).TotalHours);
    }

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
        var legacyTask = Assert.Single(await f.Tasks.ReadAsync(party, default));
        var agreement = await f.Db.CoachAccountabilityAgreements.SingleAsync();
        f.Ledger.Append(agreement, party.CoachId, [], legacyTask with
        {
            DueUtc = new DateTimeOffset(due), LateFee = 8m
        });
        await f.Db.SaveChangesAsync();
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
        private readonly string directory = Path.Combine(Path.GetTempPath(), $"coach-tasks-{Guid.NewGuid():N}");
        private readonly Guid client = Guid.NewGuid();
        private readonly Guid owner = Guid.NewGuid();
        public readonly RecompDbContext Db = new(new DbContextOptionsBuilder<RecompDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly TestClock Clock = new();
        public AccountabilityLedger Ledger { get; }
        public CoachTaskService Tasks { get; }
        public NotificationFiles Files { get; }

        public Fixture()
        {
            var access = new CoachAccountabilityPreviewService(Db, new CoachCenterService(Db), null!);
            Ledger = new(Db, access);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Notifications:DataPath"] = directory }).Build();
            Files = new NotificationFiles(new TestEnvironment(), config, new EphemeralDataProtectionProvider());
            Tasks = new(Db, new SimpleCoachingService(Db, access, Ledger), Ledger, Clock, new CoachTaskProofStore(Files));
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

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = ".";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
