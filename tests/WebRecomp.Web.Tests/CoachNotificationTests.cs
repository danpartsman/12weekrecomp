using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using WebRecomp.Web.Data;
using WebRecomp.Web.Controllers;
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
    public void FreshAgreementStorageCanSaveReloadAndUpdateEachCoachClientPair()
    {
        using var f = new Fixture();
        var client = Guid.NewGuid(); var coach = Guid.NewGuid(); var otherCoach = Guid.NewGuid();
        var key = $"financial-agreements/{client:N}-{coach:N}";
        var otherKey = $"financial-agreements/{client:N}-{otherCoach:N}";
        var rule = new WebRecomp.Web.Controllers.CoachFinancialRule("Protein penalty", true, "daily", 5m, 0m, 0m,
            "Outside agreed nutrition limits", "system-percent", 5m, 10m);
        var proposal = new WebRecomp.Web.Controllers.CoachFinancialAgreement(1, "pending", DateTime.UtcNow, coach, null, [rule]);
        // This is the same lease/write sequence used by FinancialProposal on a fresh installation.
        using (f.Files.Lease(key)) f.Files.Write(key, proposal);
        var again = new NotificationFiles(f.Environment, f.Config, f.Protection);
        var saved = again.Read<WebRecomp.Web.Controllers.CoachFinancialAgreement>(key)!;
        Assert.Equal("pending", saved.Status); Assert.Equal(1, saved.Revision);
        Assert.Equal(5m, saved.Rules[0].Amount); Assert.Equal("system-percent", saved.Rules[0].LimitMode);
        Assert.Equal(5m, saved.Rules[0].LowerLimit); Assert.Equal(10m, saved.Rules[0].UpperLimit);
        Assert.Null(again.Read<WebRecomp.Web.Controllers.CoachFinancialAgreement>(otherKey));
        var owner = Guid.NewGuid();
        using (again.Lease(key)) again.Write(key, saved with { Status = "accepted", Revision = 2, AcceptedBy = owner, AcceptedUtc = DateTime.UtcNow });
        using (again.Lease(otherKey)) again.Write(otherKey, proposal with { UpdatedBy = otherCoach });
        Assert.Equal("accepted", f.Files.Read<WebRecomp.Web.Controllers.CoachFinancialAgreement>(key)!.Status);
        Assert.Equal("pending", f.Files.Read<WebRecomp.Web.Controllers.CoachFinancialAgreement>(otherKey)!.Status);
        Assert.DoesNotContain("Protein penalty", File.ReadAllText(System.IO.Path.Combine(f.Path, "financial-agreements", $"{client:N}-{coach:N}.json")));
    }
    private static CoachFinancialAgreement FinancialProposal(Fixture f, int revision = 1, bool notify = true) =>
        new(revision, "pending", DateTime.UtcNow, f.Coach, null,
            [new CoachFinancialRule("Protein penalty", true, "daily", 5m, 0m, 0m, "Outside nutrition range", "system-percent", 10m, 10m)], NotifyClient: notify);
    private static void SaveFinancial(Fixture f, CoachFinancialAgreement agreement)
    {
        var key = $"financial-agreements/{f.Client:N}-{f.Coach:N}";
        using(f.Files.Lease(key)) f.Files.Write(key, agreement);
    }
    private static async Task SeedFinancial(Fixture f)
    {
        await f.Seed();
        f.Db.CoachAccountabilityEvents.RemoveRange(f.Db.CoachAccountabilityEvents);
        await f.Db.SaveChangesAsync();
    }
    [Fact]
    public async Task NewAndRevisedFinancialAgreementsNotifyOwnerOnceAndLinkToReview()
    {
        using var f = new Fixture(); await SeedFinancial(f);
        SaveFinancial(f, FinancialProposal(f));
        await f.Processor.RunAsync(default); await f.Processor.RunAsync(default);
        var message = Assert.Single(f.Email.Messages);
        Assert.Equal("owner@example.com", message.ToAddress);
        Assert.Contains("agreement", message.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"/coach-workspace/{f.Client:D}/{f.Coach:D}#financial-agreement", message.Body);
        SaveFinancial(f, FinancialProposal(f, 3));
        await f.Processor.RunAsync(default); await f.Processor.RunAsync(default);
        Assert.Equal(2, f.Email.Messages.Count);
    }
    [Fact]
    public async Task PendingNoticeIncludesExistingProposalButDoesNotRetroactivelyEmailIt()
    {
        using var f = new Fixture(); await SeedFinancial(f);
        SaveFinancial(f, FinancialProposal(f, notify: false));
        var notices = await PendingFinancialAgreements.ReadAsync(f.Db, f.Files, f.Client, f.Owner, default);
        Assert.Single(notices); Assert.Equal(f.Coach, notices[0].CoachId);
        Assert.Empty(await PendingFinancialAgreements.ReadAsync(f.Db, f.Files, f.Client, f.Coach, default));
        await f.Processor.RunAsync(default); Assert.Empty(f.Email.Messages);
        SaveFinancial(f, FinancialProposal(f) with { Status = "accepted", Revision = 2 });
        Assert.Empty(await PendingFinancialAgreements.ReadAsync(f.Db, f.Files, f.Client, f.Owner, default));
    }
    [Fact]
    public async Task AcceptedAgreementSuppressesQueuedRetryAndRevokedCoachGetsNoNotice()
    {
        using var f = new Fixture(); await SeedFinancial(f);
        SaveFinancial(f, FinancialProposal(f)); f.Email.Fail = true;
        await f.Processor.RunAsync(default); Assert.Single(f.Email.Messages);
        var id = CoachNotificationProcessor.FinancialDeliveryId(f.Client, f.Coach, 1);
        var queued = f.Files.Read<NotificationDelivery>($"queue/{id}")!;
        queued.NextAttemptUtc = DateTime.MinValue; f.Files.Write($"queue/{id}", queued);
        SaveFinancial(f, FinancialProposal(f) with { Status = "accepted", Revision = 2 });
        f.Email.Fail = false; await f.Processor.RunAsync(default);
        Assert.Single(f.Email.Messages); Assert.True(f.Files.Read<NotificationDelivery>($"queue/{id}")!.Finished);
        SaveFinancial(f, FinancialProposal(f, 3));
        (await f.Db.CoachClientRelationships.SingleAsync()).RevokedAtUtc = DateTime.UtcNow;
        await f.Db.SaveChangesAsync(); await f.Processor.RunAsync(default);
        Assert.Single(f.Email.Messages);
        Assert.Empty(await PendingFinancialAgreements.ReadAsync(f.Db, f.Files, f.Client, f.Owner, default));
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
    [Fact]
    public async Task MultipleOwnersAreNotGuessed()
    {
        using var f = new Fixture(); await f.Seed();
        var other = Guid.NewGuid();
        f.Db.AddRange(new UserAccount { UserId = other, ExternalSubject = "other", EmailNormalized = "other@example.com" },
            new UserClientAccess { UserClientAccessId = Guid.NewGuid(), ClientId = f.Client, UserId = other });
        await f.Db.SaveChangesAsync(); await f.Processor.RunAsync(default);
        Assert.Empty(f.Email.Messages); Assert.Empty(f.Phone.Subjects);
    }
    [Fact]
    public async Task EmailOptOutStillAllowsOneGenericPhoneAlert()
    {
        using var f = new Fixture(); await f.Seed(); f.Files.BindDevice(f.Owner, Device());
        var settings = f.Files.User(f.Owner); settings.Email = false; f.Files.SaveUser(f.Owner, settings);
        await f.Processor.RunAsync(default); await f.Processor.RunAsync(default);
        Assert.Empty(f.Email.Messages); Assert.Single(f.Phone.Subjects);
        Assert.DoesNotContain(Sample().SnapWorkouts[0].Name, f.Phone.Subjects[0]);
    }
    [Fact]
    public async Task DailyReminderOptOutCancelsAnAlreadyQueuedReminder()
    {
        using var f = new Fixture(); await f.Seed(); f.Files.BindDevice(f.Owner, Device());
        f.Db.CoachAccountabilityEvents.RemoveRange(f.Db.CoachAccountabilityEvents); await f.Db.SaveChangesAsync();
        var settings = f.Files.User(f.Owner); settings.DailyReview = false; f.Files.SaveUser(f.Owner, settings);
        f.Files.Enqueue(new() { Id = "daily-test", ClientId = f.Client, CoachUserId = f.Coach, UserId = f.Owner,
            Subject = "Review today's fees and incentives", Body = "Review", Url = "/notifications", Email = false });
        await f.Processor.RunAsync(default);
        Assert.Empty(f.Email.Messages); Assert.Empty(f.Phone.Subjects);
        Assert.True(f.Files.Read<NotificationDelivery>("queue/daily-test")!.Finished);
    }
    [Fact]
    public async Task RealPushTransportEncryptsAndSignsWithoutExposingMessageText()
    {
        using var f = new Fixture(); using var handler = new RecordingHandler();
        var sender = new WebPushDelivery(f.Files, new RecordingFactory(handler));
        Assert.True(await sender.SendAsync(Device(), "Private test notice", "/notifications", "test", default));
        Assert.Contains("aes128gcm", handler.Encoding);
        Assert.Equal("vapid", handler.Scheme);
        Assert.DoesNotContain("Private test notice", handler.Content);
    }
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string Encoding = "", Scheme = "", Content = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Encoding = string.Join(",", request.Content!.Headers.ContentEncoding);
            Scheme = request.Headers.Authorization?.Scheme ?? "";
            Content = System.Text.Encoding.UTF8.GetString(await request.Content.ReadAsByteArrayAsync(ct));
            return new HttpResponseMessage(System.Net.HttpStatusCode.Created);
        }
    }
    private sealed class RecordingFactory(RecordingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
    private static NotificationDevice Device()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var p = key.ExportParameters(false);
        static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
        return new("https://fcm.googleapis.com/wp/test-token", B64(new byte[]{4}.Concat(p.Q.X!).Concat(p.Q.Y!).ToArray()), B64(RandomNumberGenerator.GetBytes(16)));
    }
    [Fact]
    public async Task CoachNoticesShowClientResponsesAndOnlyTheirCompletedWorkouts()
    {
        using var f=new Fixture();await f.Seed();
        var workout=Sample().SnapWorkouts.First();
        async Task Result(int revision,bool finalized,Guid actor)
        {
            f.Db.CoachAccountabilityEvents.Add(new(){EventId=Guid.NewGuid(),ClientId=f.Client,ActorUserId=actor,Kind="RESULT",Revision=revision,TermsVersion=1,CreatedAtUtc=DateTime.UtcNow,
                PayloadJson=JsonSerializer.Serialize(new{workoutId=workout.Id,result=new AccountabilityResult{Finalized=finalized}},AccountabilityDocument.Json)});
            await f.Db.SaveChangesAsync();
        }
        await Result(2,false,f.Owner);
        Assert.Empty(await CoachActivityNotices.ReadAsync(f.Db,f.Files,f.Coach,default));
        await Result(3,true,f.Coach); // Coach edits do not count as client submissions.
        Assert.Empty(await CoachActivityNotices.ReadAsync(f.Db,f.Files,f.Coach,default));
        await Result(4,true,f.Owner);await Result(5,true,f.Owner);
        var notices=await CoachActivityNotices.ReadAsync(f.Db,f.Files,f.Coach,default);
        Assert.Single(notices);Assert.Contains(workout.Name,notices[0].Title);
        var other=Guid.NewGuid();f.Db.CoachClientRelationships.Add(new(){CoachClientRelationshipId=Guid.NewGuid(),ClientId=f.Client,CoachUserId=other,StatusCode="ACTIVE"});await f.Db.SaveChangesAsync();
        Assert.Empty(await CoachActivityNotices.ReadAsync(f.Db,f.Files,other,default));
        SaveFinancial(f,FinancialProposal(f) with {Status="accepted",UpdatedBy=f.Owner,Revision=2});
        notices=await CoachActivityNotices.ReadAsync(f.Db,f.Files,f.Coach,default);
        Assert.Equal(2,notices.Count);Assert.Contains(notices,x=>x.Title.Contains("accepted the agreement")&&x.Url.EndsWith("#financial-agreement"));
        f.Files.Write(CoachActivityNotices.ReceiptKey(f.Coach),notices.Select(x=>x.Id).ToList());
        Assert.Empty(await CoachActivityNotices.ReadAsync(f.Db,f.Files,f.Coach,default));
        SaveFinancial(f,FinancialProposal(f) with {Status="changes-requested",UpdatedBy=f.Owner,Revision=4});
        Assert.Single(await CoachActivityNotices.ReadAsync(f.Db,f.Files,f.Coach,default));
        var link=await f.Db.CoachClientRelationships.SingleAsync(x=>x.CoachUserId==f.Coach);link.RevokedAtUtc=DateTime.UtcNow;await f.Db.SaveChangesAsync();
        Assert.Empty(await CoachActivityNotices.ReadAsync(f.Db,f.Files,f.Coach,default));
    }
    private sealed class Fixture : IDisposable
    {
        public string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "12wr-notifications-" + Guid.NewGuid().ToString("N"));
        public TestEnvironment Environment = new();
        public IConfiguration Config;
        public IDataProtectionProvider Protection = new EphemeralDataProtectionProvider();
        public NotificationFiles Files;
        public RecompDbContext Db = new(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public FakeEmail Email = new(); public FakePhone Phone = new(); public Guid Client = Guid.NewGuid(), Coach = Guid.NewGuid(), Owner = Guid.NewGuid();
        public CoachNotificationProcessor Processor;
        public Fixture()
        {
            Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Notifications:DataPath"] = Path, ["Notifications:DeliveryEnabled"] = "true" }).Build();
            Files = new(Environment, Config, Protection); _ = Files.Identity;
            Processor = new(Db, Files, Email, Phone, NullLogger<CoachNotificationProcessor>.Instance);
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
        public List<string> Subjects = [];
        public Task<bool> SendAsync(NotificationDevice device, string subject, string url, string tag, CancellationToken ct) { Subjects.Add(subject); return Task.FromResult(true); }
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
