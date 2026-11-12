using System.Security.Cryptography;
using System.Reflection;
using WebRecomp.NutritionIntelligence.Persistence;
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
    [Fact]
    public void EditingCalendarRetainsElapsedTimeAndRejectsStaleChanges()
    {
        using var f = new Fixture();
        var date = new DateOnly(2026,11,12);
        var clock = new SettlementClock { Now = new DateTimeOffset(2026,11,12,9,0,0,TimeSpan.Zero) };
        var service = new SnapAvailabilityService(f.Db,f.Files,clock);
        service.Add(f.Client,0,new() { Kind="work", StartDate=date, StartTime=new(9,0), EndTime=new(17,0) });
        var original = Assert.Single(service.Read(f.Client).Blocks);
        clock.Now = clock.Now.AddHours(2);
        service.Add(f.Client,1,new() { Kind="work", StartDate=date, StartTime=new(13,0), EndTime=new(18,0) },original.Id);
        var calendar = service.Read(f.Client);
        Assert.Equal(2,calendar.Revision);
        Assert.Single(calendar.Blocks.Where(b=>b.CancelledUtc==null));
        var windows = SnapAvailabilityClock.Expand(calendar.Blocks,TimeZoneInfo.Utc,date,date);
        Assert.Equal(2,windows.Count);
        Assert.Contains(windows,w=>w.Start.Hour==9&&w.End.Hour==11);
        Assert.Contains(windows,w=>w.Start.Hour==13&&w.End.Hour==18);
        var replacement = calendar.Blocks.Single(b=>b.CancelledUtc==null);
        Assert.Throws<AccountabilityConflictException>(()=>service.Remove(f.Client,1,replacement.Id));
        Assert.Single(service.Read(f.Client).Blocks.Where(b=>b.CancelledUtc==null));
    }

    [Fact]
    public async Task SnapClockUsesCalendarAndIllnessAndReversesFalseLateFeesWithoutDuplicates()
    {
        using var f = new Fixture();
        await SeedFinancial(f);
        f.Config["SimpleCoaching:Enabled"] = "true";
        var client = await f.Db.Clients.SingleAsync(); client.TimeZone = "UTC";
        await f.Db.SaveChangesAsync();
        var today = AppUserContextService.GetClientToday(client);
        var clock = new SettlementClock { Now = new DateTimeOffset(today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero) };
        var availability = new SnapAvailabilityService(f.Db, f.Files, clock);
        availability.Add(f.Client, 0, new() { Kind = "work", StartDate = today, StartTime = new(10, 0), EndTime = new(12, 0) });
        var access = new CoachAccountabilityPreviewService(f.Db, new CoachCenterService(f.Db), null!);
        var ledger = new AccountabilityLedger(f.Db, access, f.Config);
        var simple = new SimpleCoachingService(f.Db, access, ledger, clock: clock, availability: availability);
        await simple.AssignAsync(f.Coach, f.Client, 0, Guid.NewGuid(), new()
        {
            Name = "Calendar workout", Date = today.ToString("yyyy-MM-dd"), TimeLimitMinutes = 120, LatePenalty = 25,
            Exercises = [new() { Name = "Squat", Sets = 1, Reps = 10, MaxWeight = 0 }]
        }, default);
        var row = await f.Db.CoachAccountabilityAgreements.SingleAsync();
        var state = AccountabilityDocument.Parse(row.StateJson);
        var workout = Assert.Single(state.SnapWorkouts);
        Assert.Equal(clock.Now.AddHours(4), workout.DeadlineUtc);
        clock.Now = clock.Now.AddHours(2.5);
        await simple.RefreshAsync(f.Owner, f.Client, false, default);
        Assert.Empty(await ledger.ReadAsync(row, default));
        clock.Now = clock.Now.AddHours(2);
        await simple.PostDueAsync(default);
        Assert.Equal(25m, (await ledger.ReadAsync(row, default)).Sum(e => e.Amount));

        f.Db.DailyCheckins.Add(new() { CheckinId = Guid.NewGuid(), ClientId = f.Client,
            ProgramId = Guid.NewGuid(), CheckinDate = today, IllnessFlag = true });
        await f.Db.SaveChangesAsync();
        await simple.RefreshAsync(f.Owner, f.Client, false, default);
        await simple.RefreshAsync(f.Owner, f.Client, false, default);
        var corrected = await ledger.ReadAsync(row, default);
        Assert.Equal(2, corrected.Count);
        Assert.Equal(0m, corrected.Sum(e => e.Amount));
        await simple.ApplyAvailabilityAsync(f.Client, state, default);
        Assert.Equal(new DateTimeOffset(today.AddDays(1).ToDateTime(new TimeOnly(2, 0)), TimeSpan.Zero), workout.DeadlineUtc);
        Assert.Equal("Unavailable", workout.Timing!.Reason); // Illness overlaps the saved work block.
        var stable = workout.DeadlineUtc;
        await simple.ApplyAvailabilityAsync(f.Client, state, default);
        Assert.Equal(stable, workout.DeadlineUtc);
        Assert.Empty(await availability.WindowsAsync(new Client { ClientId = Guid.NewGuid(), ClientName = "Other", TimeZone = "UTC" }, today, today, default));
    }

    [Fact]
    public async Task ScheduledSettlementPostsOnlyClosedPeriodsToEachCoachAndReconcilesEdits()
    {
        using var f = new Fixture();
        await SeedFinancial(f);
        f.Config["SimpleCoaching:Enabled"] = "true";
        var client = await f.Db.Clients.SingleAsync();
        client.TimeZone = "UTC";
        var other = Guid.NewGuid();
        f.Db.CoachClientRelationships.Add(new()
        {
            CoachClientRelationshipId = Guid.NewGuid(), ClientId = f.Client, CoachUserId = other, StatusCode = "ACTIVE"
        });
        var date = new DateOnly(2026, 11, 2);
        var day = new NutritionDay
        {
            NutritionDayId = Guid.NewGuid(), ClientId = f.Client, ProgramId = Guid.NewGuid(), NutritionDate = date,
            ActualCalories = 1800, ActualProteinG = 100, ActualCarbsG = 200, ActualFatG = 60
        };
        f.Db.NutritionDays.Add(day);
        await f.Db.SaveChangesAsync();
        var acceptedAt = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
        CoachFinancialAgreement Accepted(decimal amount) => new(2, "accepted", acceptedAt, f.Owner, null,
            Enumerable.Range(0, 10).Select(i => new CoachFinancialRule("Protein penalty", i == 0, "daily", amount,
                0, 0, "Protein outside range", "actual", 150, 210)).ToArray(), acceptedAt, f.Owner);

        SaveFinancial(f, Accepted(5) with { Status = "pending", Revision = 3, AcceptedUtc = null, AcceptedBy = null, AcceptedHistory = [Accepted(5)] });
        f.Files.Write($"financial-agreements/{f.Client:N}-{other:N}", Accepted(8));
        var access = new CoachAccountabilityPreviewService(f.Db, new CoachCenterService(f.Db), null!);
        var ledger = new AccountabilityLedger(f.Db, access, f.Config);
        var clock = new SettlementClock { Now = new DateTimeOffset(2026, 11, 1, 20, 0, 0, TimeSpan.Zero) };
        var job = new AgreementSettlementJob(f.Db, f.Files, null!, null!, new SimpleCoachingService(f.Db, access, ledger),
            ledger, f.Config, NullLogger<AgreementSettlementJob>.Instance, clock);
        await job.RunAsync(default);
        clock.Now = new(2026, 11, 2, 23, 59, 0, TimeSpan.Zero);
        await job.RunAsync(default);
        Assert.False(await f.Db.CoachAccountabilityAgreements.AnyAsync());
        clock.Now = clock.Now.AddMinutes(1);
        await job.RunAsync(default);
        await job.RunAsync(default);
        var row = await f.Db.CoachAccountabilityAgreements.SingleAsync();
        var entries = await ledger.ReadAsync(row, default);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Equal(date, entry.Date));
        Assert.Equal(5m, entries.Single(entry => entry.CoachUserId == f.Coach).Amount);
        Assert.Equal(8m, entries.Single(entry => entry.CoachUserId == other).Amount);

        var correction = await f.Db.NutritionDays.SingleAsync();
        correction.ActualProteinG = 180;
        await f.Db.SaveChangesAsync();
        clock.Now = clock.Now.AddMinutes(15);
        await job.RunAsync(default);
        await job.RunAsync(default);
        entries = await ledger.ReadAsync(row, default);
        Assert.Equal(4, entries.Count);
        Assert.All(entries.GroupBy(entry => entry.CoachUserId), group => Assert.Equal(0m, group.Sum(entry => entry.Amount)));
    }

    [Theory]
    [InlineData(true, 5)]
    [InlineData(false, 0)]
    public async Task AcceptedReplacementAppliesOnLocalAcceptanceDateAndCorrectsOldFees(bool enabled, int expected)
    {
        using var f = new Fixture();
        await SeedFinancial(f);
        f.Config["SimpleCoaching:Enabled"] = "true";
        (await f.Db.Clients.SingleAsync()).TimeZone = "America/Los_Angeles";
        await f.Db.SaveChangesAsync();
        var originalAt = new DateTime(2026, 11, 2, 17, 0, 0, DateTimeKind.Utc);
        CoachFinancialAgreement Terms(int revision, DateTime accepted, decimal amount, bool active) =>
            new(revision, "accepted", accepted, f.Owner, null,
                Enumerable.Range(0, 10).Select(i => new CoachFinancialRule("Missing nutrition", i == 5 && active,
                    "daily", amount, 0, 0, "Incomplete nutrition", "event")).ToArray(), accepted, f.Owner);
        var original = Terms(2, originalAt, 20, true);
        SaveFinancial(f, original);
        var access = new CoachAccountabilityPreviewService(f.Db, new CoachCenterService(f.Db), null!);
        var ledger = new AccountabilityLedger(f.Db, access, f.Config);
        var clock = new SettlementClock { Now = new DateTimeOffset(2026, 11, 9, 20, 0, 0, TimeSpan.Zero) };
        var job = new AgreementSettlementJob(f.Db, f.Files, null!, null!, new SimpleCoachingService(f.Db, access, ledger),
            ledger, f.Config, NullLogger<AgreementSettlementJob>.Instance, clock);
        await job.RunAsync(default);
        clock.Now = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);
        await job.RunAsync(default);
        var row = await f.Db.CoachAccountabilityAgreements.SingleAsync();
        var date = new DateOnly(2026, 11, 11);
        Assert.Equal(20m, (await ledger.ReadAsync(row, default)).Where(x => x.Date == date).Sum(x => x.Amount));

        // 01:00 UTC November 12 is still November 11 for this client.
        var replacement = Terms(5, new DateTime(2026, 11, 12, 1, 0, 0, DateTimeKind.Utc), 5, enabled);
        SaveFinancial(f, replacement with { AcceptedHistory = [original] });
        clock.Now = clock.Now.AddMinutes(15);
        await job.RunAsync(default);
        var corrected = await ledger.ReadAsync(row, default);
        Assert.Equal((decimal)expected, corrected.Where(x => x.Date == date).Sum(x => x.Amount));
        Assert.Equal(20m, corrected.Where(x => x.Date == date.AddDays(-1)).Sum(x => x.Amount));
        Assert.Contains(corrected, x => x.Date == date && x.TermsVersion == 5 && x.Description.StartsWith("Adjustment"));
        clock.Now = clock.Now.AddMinutes(15);
        await job.RunAsync(default);
        Assert.Equal(corrected.Count, (await ledger.ReadAsync(row, default)).Count);

        // A subsequent proposal must leave the latest accepted revision in force.
        SaveFinancial(f, replacement with { Revision = 6, Status = "pending", AcceptedUtc = null,
            AcceptedBy = null, AcceptedHistory = [original, replacement] });
        clock.Now = new(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);
        await job.RunAsync(default);
        Assert.Equal((decimal)expected, (await ledger.ReadAsync(row, default))
            .Where(x => x.Date == date.AddDays(1)).Sum(x => x.Amount));
    }

    [Fact]
    public async Task ScheduledCheatFoodFeeUsesDetectedCountThenSavedCountThenCoachOverride()
    {
        using var f = new Fixture();
        await SeedFinancial(f);
        f.Config["SimpleCoaching:Enabled"] = "true";
        (await f.Db.Clients.SingleAsync()).TimeZone = "UTC";
        var date = new DateOnly(2026, 11, 2);
        var program = new ProgramRecord
        {
            ProgramId = Guid.NewGuid(), ClientId = f.Client, StartDate = date.AddDays(-20),
            SelectedPlanCode = "TEST", GoalCode = "RECOMP", NutritionFoodLogEnabled = true
        };
        f.Db.Programs.Add(program);
        await f.Db.SaveChangesAsync();
        var acceptedAt = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
        var agreement = new CoachFinancialAgreement(2, "accepted", acceptedAt, f.Owner, null,
            Enumerable.Range(0, 10).Select(i => new CoachFinancialRule("Cheat-food occurrence penalty", i == 7,
                "daily", 15, 0, 0, "Per portion", "event")).ToArray(), acceptedAt, f.Owner);
        SaveFinancial(f, agreement);
        var foodLog = DispatchProxy.Create<INutritionFoodLogStore, TreatLogStub>();
        var access = new CoachAccountabilityPreviewService(f.Db, new CoachCenterService(f.Db), null!);
        var ledger = new AccountabilityLedger(f.Db, access, f.Config);
        var clock = new SettlementClock { Now = new DateTimeOffset(2026, 11, 1, 20, 0, 0, TimeSpan.Zero) };
        var job = new AgreementSettlementJob(f.Db, f.Files, null!, foodLog, new SimpleCoachingService(f.Db, access, ledger),
            ledger, f.Config, NullLogger<AgreementSettlementJob>.Instance, clock);
        await job.RunAsync(default);
        clock.Now = new(2026, 11, 3, 2, 0, 0, TimeSpan.Zero);
        await job.RunAsync(default);
        var row = await f.Db.CoachAccountabilityAgreements.SingleAsync();
        var entry = Assert.Single(await ledger.ReadAsync(row, default));
        Assert.Equal(f.Coach, entry.CoachUserId);
        Assert.Equal(15m, entry.Amount);

        f.Db.AnalysisRuns.Add(new()
        {
            AnalysisRunId = Guid.NewGuid(), ClientId = f.Client, ProgramId = program.ProgramId,
            ActionDate = date, EngineVersion = "RECOMP_FLEX_V1", DiagnosticJson = "{\"Count\":2}",
            RunStatusCode = "COMPLETED", StartedAtUtc = clock.Now.UtcDateTime
        });
        await f.Db.SaveChangesAsync();
        clock.Now = clock.Now.AddMinutes(15);
        await job.RunAsync(default);
        Assert.Equal(30m, (await ledger.ReadAsync(row, default)).Sum(x => x.Amount));

        SaveFinancial(f, agreement with { Reviews = new() { [date] = new(false, 0) } });
        clock.Now = clock.Now.AddMinutes(15);
        await job.RunAsync(default);
        await job.RunAsync(default);
        var entries = await ledger.ReadAsync(row, default);
        Assert.Equal(3, entries.Count);
        Assert.Equal(0m, entries.Sum(x => x.Amount));
    }

    [Fact]
    public async Task WeeklySettlementPostsAtLocalMidnightOnThePaymentDay()
    {
        using var f = new Fixture();
        await SeedFinancial(f);
        f.Config["SimpleCoaching:Enabled"] = "true";
        (await f.Db.Clients.SingleAsync()).TimeZone = "America/Los_Angeles";
        await f.Db.SaveChangesAsync();
        var acceptedAt = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        SaveFinancial(f, new(2, "accepted", acceptedAt, f.Owner, null,
            Enumerable.Range(0, 10).Select(i => new CoachFinancialRule("Missing nutrition", i == 5, "weekly",
                15, 0, 0, "Incomplete nutrition", "event")).ToArray(), acceptedAt, f.Owner, PaymentDay: DayOfWeek.Friday));
        var access = new CoachAccountabilityPreviewService(f.Db, new CoachCenterService(f.Db), null!);
        var ledger = new AccountabilityLedger(f.Db, access, f.Config);
        var clock = new SettlementClock { Now = new DateTimeOffset(2026, 11, 1, 20, 0, 0, TimeSpan.Zero) };
        var job = new AgreementSettlementJob(f.Db, f.Files, null!, null!, new SimpleCoachingService(f.Db, access, ledger),
            ledger, f.Config, NullLogger<AgreementSettlementJob>.Instance, clock);
        await job.RunAsync(default);
        clock.Now = new(2026, 11, 13, 7, 59, 0, TimeSpan.Zero);
        await job.RunAsync(default);
        Assert.False(await f.Db.CoachAccountabilityAgreements.AnyAsync());
        clock.Now = clock.Now.AddMinutes(1);
        await job.RunAsync(default);
        await job.RunAsync(default);
        var row = await f.Db.CoachAccountabilityAgreements.SingleAsync();
        var entry = Assert.Single(await ledger.ReadAsync(row, default));
        Assert.Equal(15m, entry.Amount);
        Assert.Equal(new DateOnly(2026, 11, 12), entry.Date);
        Assert.EndsWith(":2026-11-06:2026-11-12", entry.SourceKey);
    }

    public class TreatLogStub : DispatchProxy
    {
        protected override object Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(INutritionFoodLogStore.GetDailyLogAsync))
                throw new NotSupportedException();
            return Task.FromResult(new NutritionDailyLogReadModel(Guid.NewGuid(), (DateOnly)args![1]!,
                [new(Guid.NewGuid(), "SNACK", 1, "The Original", 1, "SERVING", null,
                    300, 3, 35, 15, null, 25, null, null)],
                new(300, 3, 35, 15, null, 25, null, null, NutritionCompletenessCodes.Complete)));
        }
    }

    [Fact]
    public async Task AutomaticFeeNoticesReachOnlyTheOwnerAndPostingCoachWithSeparateReceipts()
    {
        using var f = new Fixture();
        await f.Seed();
        var otherCoach = Guid.NewGuid();
        f.Db.UserAccounts.Add(new UserAccount { UserId = otherCoach, ExternalSubject = "other", EmailNormalized = "other@example.com" });
        f.Db.CoachClientRelationships.Add(new()
        {
            CoachClientRelationshipId = Guid.NewGuid(), ClientId = f.Client, CoachUserId = otherCoach, StatusCode = "ACTIVE"
        });
        var entry = new AccountabilityLedgerEntry
        {
            CoachUserId = f.Coach, Automatic = true, SourceKey = "agreement:test", Amount = 15, Description = "Cheat-food fee"
        };
        f.Db.CoachAccountabilityEvents.Add(new()
        {
            EventId = Guid.NewGuid(), ClientId = f.Client, ActorUserId = f.Coach, Kind = "RESULT", Revision = 2,
            CreatedAtUtc = DateTime.UtcNow, PayloadJson = JsonSerializer.Serialize(new
            {
                ledgerEntries = new[] { entry,
                    new AccountabilityLedgerEntry { CoachUserId = f.Coach, Amount = 10, SourceKey = "manual" },
                    new AccountabilityLedgerEntry { CoachUserId = f.Coach, Amount = -5, Automatic = true, SourceKey = "agreement:credit" } }
            }, AccountabilityDocument.Json)
        });
        await f.Db.SaveChangesAsync();

        var clientNotice = Assert.Single(await AutomaticFeeNotices.ReadAsync(f.Db, f.Files, f.Owner, f.Client, false, default));
        var coachNotice = Assert.Single(await AutomaticFeeNotices.ReadAsync(f.Db, f.Files, f.Coach, null, true, default));
        Assert.Contains("Coach · $15.00", clientNotice.Title);
        Assert.Contains("Client · $15.00", coachNotice.Title);
        Assert.False(clientNotice.ForCoach);
        Assert.True(coachNotice.ForCoach);
        Assert.NotEqual(clientNotice.Message, coachNotice.Message);
        Assert.Equal(clientNotice.Id, coachNotice.Id);
        Assert.Equal($"/clients/{f.Client:D}/coach-ledger?coachUserId={f.Coach:D}", coachNotice.Url);
        Assert.Empty(await AutomaticFeeNotices.ReadAsync(f.Db, f.Files, otherCoach, f.Client, true, default));
        Assert.Empty(await AutomaticFeeNotices.ReadAsync(f.Db, f.Files, Guid.NewGuid(), f.Client, false, default));

        f.Files.Write(AutomaticFeeNotices.ReceiptKey(f.Owner), new[] { clientNotice.Id });
        Assert.Empty(await AutomaticFeeNotices.ReadAsync(f.Db, f.Files, f.Owner, f.Client, false, default));
        Assert.Single(await AutomaticFeeNotices.ReadAsync(f.Db, f.Files, f.Coach, null, true, default));
    }

    private sealed class SettlementClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
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
