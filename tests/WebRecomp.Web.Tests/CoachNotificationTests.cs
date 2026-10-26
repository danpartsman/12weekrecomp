using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.Notifications;

namespace WebRecomp.Web.Tests;

public sealed class CoachNotificationTests
{
    private static AccountabilityState Sample()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/accountability-js-document.json")));
        return AccountabilityDocument.Parse(fixture.RootElement.GetProperty("state").GetRawText());
    }
    [Fact]
    public void OnlyMeaningfulCoachChangesProduceAlerts()
    {
        var before = Sample(); var original = AccountabilityDocument.Serialize(before);
        Assert.False(CoachNotificationChange.Compare(original, original).HasChanges);
        before.Excused[before.Agreement.Start] = true;
        Assert.False(CoachNotificationChange.Compare(original, AccountabilityDocument.Serialize(before)).HasChanges);
        before.Payment.Amount += 1;
        var agreement = CoachNotificationChange.Compare(original, AccountabilityDocument.Serialize(before));
        Assert.True(agreement.AgreementChanged); Assert.Empty(agreement.Workouts);
    }
    [Fact]
    public void AddedEditedAndRemovedSnapWorkoutsAreDistinguished()
    {
        var state = Sample(); Assert.NotEmpty(state.SnapWorkouts);
        var before = AccountabilityDocument.Serialize(state);
        state.SnapWorkouts[0].Name += " updated";
        var update = CoachNotificationChange.Compare(before, AccountabilityDocument.Serialize(state));
        Assert.Single(update.Workouts); Assert.False(update.AgreementChanged);
        state.SnapWorkouts.Clear();
        var removed = CoachNotificationChange.Compare(before, AccountabilityDocument.Serialize(state));
        Assert.True(removed.RemovedWorkouts); Assert.Empty(removed.Workouts);
    }
    [Fact]
    public void DailyReminderUsesLocalTimeAndAgreementDatesWithoutInventingFees()
    {
        var state = Sample(); state.Enabled = true; state.Agreement = new() { Start = "2026-10-26", Mode = "weeks", Weeks = 1 };
        Assert.False(CoachNotificationProcessor.DailyReviewDue(state, new(2026,10,26), new(19,59,0), "20:00"));
        Assert.True(CoachNotificationProcessor.DailyReviewDue(state, new(2026,10,26), new(20,0,0), "20:00"));
        Assert.False(CoachNotificationProcessor.DailyReviewDue(state, new(2026,10,25), new(21,0,0), "20:00"));
        Assert.False(CoachNotificationProcessor.DailyReviewDue(state, new(2026,11,2), new(21,0,0), "20:00"));
        state.Agreement.EndedOn = "2026-10-27";
        Assert.False(CoachNotificationProcessor.DailyReviewDue(state, new(2026,10,28), new(21,0,0), "20:00"));
    }
    [Fact]
    public void PrivateFilesPersistPreferencesAndDeviceOwnershipAcrossInstances()
    {
        using var f = new Fixture(); var user = Guid.NewGuid(); var other = Guid.NewGuid(); var device = Device();
        f.Files.BindDevice(user, device); Assert.True(f.Files.OwnsDevice(user, device.Endpoint));
        f.Files.BindDevice(other, device); Assert.False(f.Files.OwnsDevice(user, device.Endpoint)); Assert.True(f.Files.OwnsDevice(other, device.Endpoint));
        f.Files.RemoveDevice(user, device.Endpoint); Assert.True(f.Files.OwnsDevice(other, device.Endpoint));
        var again = new NotificationFiles(f.Environment, f.Config, f.Protection);
        Assert.Equal(f.Files.Identity.PublicKey, again.Identity.PublicKey);
        Assert.Single(again.User(other).Devices);
        var data = File.ReadAllText(Path.Combine(f.Path, "users", other.ToString("N") + ".json"));
        Assert.DoesNotContain(device.Endpoint, data);
    }
    [Fact]
    public void PushSubscriptionsCannotTargetPrivateOrArbitraryServers()
    {
        var device = Device(); NotificationFiles.ValidateDevice(device);
        foreach (var endpoint in new[] { "http://fcm.googleapis.com/a", "https://localhost/a", "https://169.254.169.254/a", "https://fcm.googleapis.com.attacker.example/a", "https://fcm.googleapis.com:8443/a" })
            Assert.Throws<ArgumentException>(() => NotificationFiles.ValidateDevice(device with { Endpoint = endpoint }));
        Assert.Throws<ArgumentException>(() => NotificationFiles.ValidateDevice(device with { Auth = "invalid" }));
    }
    [Fact]
    public async Task AcceptedEmailIsNotRepeatedAndChangesGoOnlyToTheClientOwner()
    {
        using var f = new Fixture(); await f.Seed();
        await f.Processor.RunAsync(default); await f.Processor.RunAsync(default);
        Assert.Single(f.Email.Messages); Assert.Equal("owner@example.com", f.Email.Messages[0].ToAddress);
        Assert.Contains("/accountability-preview/clients/", f.Email.Messages[0].Body);
        Assert.DoesNotContain("coach@example.com", f.Email.Messages[0].Body);
    }
    [Fact]
    public async Task ARevokedOrAmbiguousOwnerDoesNotReceiveAnAlert()
    {
        using var f = new Fixture(); await f.Seed();
        await f.Processor.QueueConfigAsync(f.Client, 1, default);
        var access = await f.Db.UserClientAccess.SingleAsync(); access.RevokedAtUtc = DateTime.UtcNow; await f.Db.SaveChangesAsync();
        await f.Processor.RunAsync(default); Assert.Empty(f.Email.Messages);
    }
    [Fact]
    public async Task FailedEmailStaysPendingAndCanRetryAfterRestart()
    {
        using var f = new Fixture(); await f.Seed(); f.Email.Fail = true;
        await f.Processor.RunAsync(default); Assert.Single(f.Email.Messages);
        var id = Assert.Single(f.Files.QueueIds()); var pending = f.Files.Read<NotificationDelivery>($"queue/{id}")!;
        Assert.False(pending.EmailDone); Assert.True(pending.PushDone);
        pending.NextAttemptUtc = DateTime.UtcNow.AddMinutes(-1); f.Files.Write($"queue/{id}", pending); f.Email.Fail = false;
        await f.Processor.RunAsync(default); await f.Processor.RunAsync(default); Assert.Equal(2, f.Email.Messages.Count);
        Assert.True(f.Files.Read<NotificationDelivery>($"queue/{id}")!.Finished);
    }
    private static NotificationDevice Device()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var p = key.ExportParameters(false);
        static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
        return new("https://fcm.googleapis.com/wp/test-token", B64(new byte[]{4}.Concat(p.Q.X!).Concat(p.Q.Y!).ToArray()), B64(RandomNumberGenerator.GetBytes(16)));
    }
    private sealed class Fixture : IDisposable
    {
        public string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "12wr-notifications-" + Guid.NewGuid().ToString("N"));
        public TestEnvironment Environment = new();
        public IConfiguration Config;
        public IDataProtectionProvider Protection = new EphemeralDataProtectionProvider();
        public NotificationFiles Files;
        public RecompDbContext Db = new(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public FakeEmail Email = new(); public Guid Client = Guid.NewGuid(), Coach = Guid.NewGuid(), Owner = Guid.NewGuid();
        public CoachNotificationProcessor Processor;
        public Fixture()
        {
            Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Notifications:DataPath"] = Path, ["Notifications:DeliveryEnabled"] = "true" }).Build();
            Files = new(Environment, Config, Protection); _ = Files.Identity;
            Processor = new(Db, Files, Email, new FakePhone(), NullLogger<CoachNotificationProcessor>.Instance);
        }
        public async Task Seed()
        {
            Db.AddRange(new Client { ClientId = Client, ClientName = "Client" },
                new UserAccount { UserId = Owner, ExternalSubject = "owner", EmailNormalized = "owner@example.com" },
                new UserAccount { UserId = Coach, ExternalSubject = "coach", EmailNormalized = "coach@example.com", DisplayName = "Coach" },
                new UserClientAccess { UserClientAccessId = Guid.NewGuid(), UserId = Owner, ClientId = Client },
                new CoachClientRelationship { CoachClientRelationshipId = Guid.NewGuid(), ClientId = Client, CoachUserId = Coach, StatusCode = "ACTIVE" },
                new CoachAccountabilityEvent { EventId = Guid.NewGuid(), ClientId = Client, ActorUserId = Coach, Kind = "CONFIG", Revision = 1, TermsVersion = 1, CreatedAtUtc = DateTime.UtcNow.AddSeconds(1), PayloadJson = AccountabilityDocument.Serialize(Sample()) });
            await Db.SaveChangesAsync();
        }
        public void Dispose() { Db.Dispose(); Directory.Delete(Path, true); }
    }
    private sealed class FakeEmail : IEmailService
    {
        public bool Fail; public List<OutboundEmail> Messages = [];
        public Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken cancellationToken = default) { Messages.Add(email); return Task.FromResult(new EmailSendResult(!Fail)); }
    }
    private sealed class FakePhone : IPhoneNotificationSender
    {
        public Task<bool> SendAsync(NotificationDevice device, string subject, string url, string tag, CancellationToken ct) => Task.FromResult(true);
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
