using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using WebRecomp.Web.Controllers;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.Notifications;

namespace WebRecomp.Web.Tests;
public sealed class SnapLiveTests
{
    [Fact]
    public async Task OpenStartedLegacyAssignmentBeforeAnySetsHaveBeenSaved()
    {
        using var f = new Fixture();
        await f.Seed();
        await f.Live.StartAsync(f.Owner, f.Client, "live-test", default);
        var access = new CoachAccountabilityPreviewService(f.Db, new CoachCenterService(f.Db, f.Config), null!);
        var availability = new SnapAvailabilityService(f.Db, f.Files);
        var service = new SimpleCoachingService(f.Db, access, f.Ledger, availability: availability);
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "owner")], "test"))
        };
        http.Request.QueryString = new QueryString("?assignment=live-test");
        var controller = new SimpleSnapController(new AppUserContextService(f.Db, f.Config), service, access,
            f.Ledger, f.Config, live: f.Live)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        var result = Assert.IsType<ViewResult>(await controller.Index(f.Client, default));
        var page = Assert.IsType<SimpleSnapPage>(result.Model);
        Assert.NotNull(page.LiveSessions["live-test"].StartedUtc);
        Assert.Empty(page.State.SnapResults["live-test"].Sets);
        Assert.Empty(page.AssessedCharges);
        var workout = Assert.Single(page.State.SnapWorkouts);
        Assert.NotNull(workout.TimeLimitMinutes);
        var saved = await f.Live.SaveAsync(f.Owner, f.Client, workout.Id, SnapLiveService.AssignmentToken(workout),
            page.LiveSessions[workout.Id].ProgressRevision, Sets(12), default);
        Assert.Equal(12, saved.Sets["e1"][0].Reps);
    }

    [Fact]
    public async Task OpeningAnotherWorkoutKeepsLegacyCompletedHistoryAndSparseDraftRows()
    {
        using var f = new Fixture();
        await f.Seed();
        var row = await f.Db.CoachAccountabilityAgreements.SingleAsync();
        var original = AccountabilityDocument.Parse(row.StateJson);
        var completed = original.SnapWorkouts.Single();
        completed.DeadlineUtc = null;
        original.SnapResults[completed.Id] = new() { Finalized = true, Sets = Sets(12) };
        var draft = new AccountabilityWorkout
        {
            Id = "legacy-draft", Simple = true, Name = "Legacy draft", Date = completed.Date,
            CreatedAtUtc = completed.CreatedAtUtc,
            Exercises = [new() { Id = "e1", Name = "Push ups", Sets = 2, Reps = 10, MaxWeight = 0 },
                new() { Id = "e2", Name = "Squat", Sets = 1, Reps = 10, MaxWeight = 0 }]
        };
        original.SnapWorkouts.Add(draft);
        original.SnapResults[draft.Id] = new() { Sets = Sets(7) };
        var state = AccountabilityDocument.Parse(AccountabilityDocument.Serialize(original));
        var availability = new SnapAvailabilityService(f.Db, f.Files);
        await availability.ApplyAsync(await f.Db.Clients.SingleAsync(), state, default);

        Assert.Null(state.SnapWorkouts[0].DeadlineUtc);
        Assert.Null(state.SnapResults[completed.Id].CompletedAtUtc);
        Assert.Equal(12, state.SnapResults[completed.Id].Sets["e1"][0].Reps);
        Assert.Equal(7, state.SnapResults[draft.Id].Sets["e1"][0].Reps);
        Assert.Null(state.SnapResults[draft.Id].Sets["e1"][1].Reps);
        Assert.Null(Assert.Single(state.SnapResults[draft.Id].Sets["e2"]).Reps);
        Assert.DoesNotContain(SimpleCoachingService.Charges(state, DateOnly.FromDateTime(DateTime.UtcNow)),
            charge => charge.Key == $"snap:{completed.Id}:late");
    }

    [Fact] public async Task StartIsIdempotentAndAlertsOnlyTheAssigningCoach()
    {
        using var f = new Fixture(); await f.Seed();
        var a = await f.Live.StartAsync(f.Owner, f.Client, "live-test", default);
        var b = await f.Live.StartAsync(f.Owner, f.Client, "live-test", default);
        Assert.Equal(a.StartedUtc, b.StartedUtc); Assert.Single(f.Files.QueueIds());
        var notices = await CoachActivityNotices.ReadAsync(f.Db, f.Files, f.Coach, default);
        Assert.Equal("View workout progression", Assert.Single(notices).ActionLabel);
        Assert.Empty(await CoachActivityNotices.ReadAsync(f.Db, f.Files, f.OtherCoach, default));
        await f.Processor.RunAsync(default); await f.Processor.RunAsync(default);
        var mail = Assert.Single(f.Email.Messages); Assert.Equal("coach@example.com", mail.ToAddress);
        Assert.Contains("started", mail.Subject); Assert.Contains("#live-live-test", mail.Body);
    }
    [Fact] public async Task DraftsAreDurableAndDoNotPostFeesOrCompleteTraining()
    {
        using var f = new Fixture(); await f.Seed();
        var a = await f.Live.StartAsync(f.Owner, f.Client, "live-test", default);
        var saved = await f.Live.SaveAsync(f.Owner, f.Client, "live-test", a.AssignmentToken, 0, Sets(12), default);
        Assert.Equal(12, saved.Sets["e1"][0].Reps); Assert.Equal(1, saved.ProgressRevision);
        Assert.Empty(f.Db.WorkoutSessions); Assert.Empty(await f.Ledger.ReadAsync(await f.Db.CoachAccountabilityAgreements.SingleAsync(), default));
        Assert.Equal(1, (await f.Db.CoachAccountabilityAgreements.SingleAsync()).Revision);
        var again = new SnapLiveService(f.Db, new NotificationFiles(f.Environment, f.Config, f.Protection), f.Simple);
        var fromCoach = again.View(await again.AllowedAsync(f.Coach, f.Client, "live-test", default));
        Assert.Equal(12, fromCoach.Sets["e1"][0].Reps);
        Assert.DoesNotContain("Reps", File.ReadAllText(Path.Combine(f.Path, "snap-live", $"{f.Client:N}-live-test.json")));
    }
    [Fact] public async Task OtherCoachAndOtherClientCannotReadStartSaveOrChat()
    {
        using var f = new Fixture(); await f.Seed();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Live.AllowedAsync(f.OtherCoach, f.Client, "live-test", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Live.StartAsync(f.Coach, f.Client, "live-test", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Live.SaveAsync(f.Coach, f.Client, "live-test", "x", 0, Sets(1), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Live.MessageAsync(f.OtherCoach, f.Client, "live-test", Guid.NewGuid(), "hi", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Live.AllowedAsync(f.Owner, Guid.NewGuid(), "live-test", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Live.AllowedAsync(f.Owner, f.Client, "missing", default));
        Assert.Throws<ArgumentException>(() => SnapLiveService.Key(f.Client, "../private"));
    }
    [Fact] public async Task ChatAppendsBothAuthorsAndRetryDoesNotDuplicateOrChangeDraftRevision()
    {
        using var f = new Fixture(); await f.Seed();
        var id = Guid.NewGuid();
        await f.Live.MessageAsync(f.Owner, f.Client, "live-test", id, "How long should I rest?", default);
        await f.Live.MessageAsync(f.Owner, f.Client, "live-test", id, "How long should I rest?", default);
        var data = await f.Live.MessageAsync(f.Coach, f.Client, "live-test", Guid.NewGuid(), "Take another minute.", default);
        Assert.Equal(2, data.Messages.Count); Assert.False(data.Messages[0].Coach); Assert.True(data.Messages[1].Coach);
        Assert.Equal(0, data.ProgressRevision); Assert.Null(data.StartedUtc);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Live.MessageAsync(f.Owner, f.Client, "live-test", id, "changed", default));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Live.MessageAsync(f.Owner, f.Client, "live-test", Guid.NewGuid(), new string('x',2001), default));
    }
    [Fact] public async Task TwoTabsCannotSilentlyOverwriteDifferentProgressAndLostResponsesCanRetry()
    {
        using var f = new Fixture(); await f.Seed(); var started = await f.Live.StartAsync(f.Owner, f.Client, "live-test", default);
        await f.Live.SaveAsync(f.Owner, f.Client, "live-test", started.AssignmentToken, 0, Sets(10), default);
        var retry = await f.Live.SaveAsync(f.Owner, f.Client, "live-test", started.AssignmentToken, 0, Sets(10), default);
        Assert.Equal(1, retry.ProgressRevision);
        await Assert.ThrowsAsync<AccountabilityConflictException>(() => f.Live.SaveAsync(f.Owner, f.Client, "live-test", started.AssignmentToken, 0, Sets(20), default));
        Assert.Equal(10, f.Live.View(await f.Live.AllowedAsync(f.Owner, f.Client, "live-test", default)).Sets["e1"][0].Reps);
    }
    [Fact] public async Task CalendarDeadlineAdjustmentDoesNotInvalidateProgressButPrescriptionEditDoes()
    {
        using var f = new Fixture(); await f.Seed(); var started = await f.Live.StartAsync(f.Owner, f.Client, "live-test", default);
        var row = await f.Db.CoachAccountabilityAgreements.SingleAsync(); var state = AccountabilityDocument.Parse(row.StateJson);
        state.SnapWorkouts[0].DeadlineUtc = state.SnapWorkouts[0].DeadlineUtc!.Value.AddHours(8);
        Assert.Equal(started.AssignmentToken, SnapLiveService.AssignmentToken(state.SnapWorkouts[0]));
        state.SnapWorkouts[0].Exercises[0].Reps = 20; row.StateJson = AccountabilityDocument.Serialize(state); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Live.SaveAsync(f.Owner, f.Client, "live-test", started.AssignmentToken, 0, Sets(10), default));
    }
    [Fact] public async Task FinishCreatesCanonicalTrainingAndFeesOnceWhileChatRemainsAvailable()
    {
        using var f = new Fixture(); await f.Seed(); var a = await f.Live.StartAsync(f.Owner, f.Client, "live-test", default);
        var b = await f.Live.SaveAsync(f.Owner, f.Client, "live-test", a.AssignmentToken, 0, Sets(12), default);
        await f.Live.FinishAsync(f.Owner, f.Client, "live-test", a.AssignmentToken, b.ProgressRevision, new() { Sets=Sets(12) }, default);
        var final = f.Live.View(await f.Live.AllowedAsync(f.Owner, f.Client, "live-test", default));
        Assert.True(final.Finished); Assert.Single(f.Db.WorkoutSessions.Where(x=>x.CompletedFlag));
        var row = await f.Db.CoachAccountabilityAgreements.SingleAsync(); var posted = await f.Ledger.ReadAsync(row, default);
        Assert.Equal(10m, posted.Sum(x=>x.Amount));
        await f.Live.FinishAsync(f.Owner, f.Client, "live-test", a.AssignmentToken, final.ProgressRevision, new() { Sets=Sets(12) }, default);
        Assert.Equal(posted.Count, (await f.Ledger.ReadAsync(row, default)).Count); Assert.Single(f.Db.WorkoutSessions);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Live.SaveAsync(f.Owner, f.Client, "live-test", a.AssignmentToken, final.ProgressRevision, Sets(20), default));
        Assert.Single((await f.Live.MessageAsync(f.Coach, f.Client, "live-test", Guid.NewGuid(), "Good work.", default)).Messages);
    }
    [Fact] public async Task DeadlineAloneDoesNotPostFeesForNewFinishOnlyAssignments()
    {
        using var f = new Fixture(); await f.Seed();
        var row = await f.Db.CoachAccountabilityAgreements.SingleAsync(); var state = AccountabilityDocument.Parse(row.StateJson);
        state.SnapWorkouts[0].DeadlineUtc = DateTimeOffset.UtcNow.AddHours(-1); row.StateJson=AccountabilityDocument.Serialize(state); await f.Db.SaveChangesAsync();
        await f.Simple.RefreshAsync(f.Owner,f.Client,false,default);
        Assert.Empty(await f.Ledger.ReadAsync(row,default));
        Assert.Empty(SimpleCoachingService.Charges(state,DateOnly.FromDateTime(DateTime.UtcNow),DateTimeOffset.UtcNow));
        state.SnapWorkouts[0].FeeOnFinishOnly=false;
        Assert.Equal(20m,SimpleCoachingService.Charges(state,DateOnly.FromDateTime(DateTime.UtcNow),DateTimeOffset.UtcNow).Sum(x=>x.Amount));
    }
    [Fact] public async Task RevocationImmediatelyStopsAccessAndQueuedCoachDelivery()
    {
        using var f=new Fixture();await f.Seed();await f.Live.StartAsync(f.Owner,f.Client,"live-test",default);
        var relation=await f.Db.CoachClientRelationships.SingleAsync(x=>x.CoachUserId==f.Coach);relation.RevokedAtUtc=DateTime.UtcNow;await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Live.AllowedAsync(f.Owner,f.Client,"live-test",default));
        await f.Processor.RunAsync(default);Assert.Empty(f.Email.Messages);
    }
    [Fact] public async Task FirstFinishSeparatesLateFeeAndCorrectionsPreserveThatPolicy()
    {
        using var f=new Fixture();await f.Seed();
        var row=await f.Db.CoachAccountabilityAgreements.SingleAsync();
        var state=AccountabilityDocument.Parse(row.StateJson);
        state.SnapWorkouts[0].DeadlineUtc=DateTimeOffset.UtcNow.AddHours(-1);
        row.StateJson=AccountabilityDocument.Serialize(state);await f.Db.SaveChangesAsync();
        var started=await f.Live.StartAsync(f.Owner,f.Client,"live-test",default);
        await f.Live.FinishAsync(f.Owner,f.Client,"live-test",started.AssignmentToken,0,new(){Sets=Sets(50)},default);
        var result=AccountabilityDocument.Parse(row.StateJson).SnapResults["live-test"];
        Assert.True(result.LateFeeOutsideCap);
        Assert.Equal(50m,(await f.Ledger.ReadAsync(row,default)).Sum(x=>x.Amount));
        var final=f.Live.View(await f.Live.AllowedAsync(f.Owner,f.Client,"live-test",default));
        await f.Live.FinishAsync(f.Owner,f.Client,"live-test",final.AssignmentToken,final.ProgressRevision,new(){Sets=Sets(50)},default);
        Assert.True(AccountabilityDocument.Parse(row.StateJson).SnapResults["live-test"].LateFeeOutsideCap);
        Assert.Equal(50m,(await f.Ledger.ReadAsync(row,default)).Sum(x=>x.Amount));
    }
    [Fact] public async Task OpeningAndLegacyCompletionNotesJoinTheSameThreadOnceWithoutStartingOrBilling()
    {
        using var f=new Fixture();await f.Seed();
        var row=await f.Db.CoachAccountabilityAgreements.SingleAsync();
        var state=AccountabilityDocument.Parse(row.StateJson);
        state.SnapWorkouts[0].CoachNote="Keep your shoulders steady.";
        state.SnapResults["live-test"]=new(){ClientNote="That helped, thanks.",Sets=Sets(10)};
        row.StateJson=AccountabilityDocument.Serialize(state);await f.Db.SaveChangesAsync();
        var first=f.Live.View(await f.Live.AllowedAsync(f.Owner,f.Client,"live-test",default));
        Assert.Equal(2,first.Messages.Count);
        var opening=Assert.Single(first.Messages,m=>m.Coach);
        Assert.Equal(f.Coach,opening.AuthorId);Assert.Equal("Coach",opening.AuthorName);
        Assert.Equal("Keep your shoulders steady.",opening.Body);
        Assert.Equal(f.Owner,Assert.Single(first.Messages,m=>!m.Coach).AuthorId);
        Assert.Null(first.StartedUtc);Assert.Equal(0,first.ProgressRevision);Assert.Null(first.SavedUtc);
        Assert.Equal(1,row.Revision);Assert.Empty(f.Files.QueueIds());Assert.Empty(f.Db.WorkoutSessions);
        Assert.Empty(await f.Ledger.ReadAsync(row,default));
        Assert.Equal(2,f.Live.View(await f.Live.AllowedAsync(f.Coach,f.Client,"live-test",default)).Messages.Count);
        await f.Live.MessageAsync(f.Owner,f.Client,"live-test",Guid.NewGuid(),"Should I add weight?",default);
        var reply=await f.Live.MessageAsync(f.Coach,f.Client,"live-test",Guid.NewGuid(),"Stay at this weight for today.",default);
        Assert.Equal(4,reply.Messages.Count);Assert.Equal(opening.Id,reply.Messages[0].Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Live.AllowedAsync(f.OtherCoach,f.Client,"live-test",default));
    }
    [Fact] public async Task ChangedOpeningNoteIsAppendedAndExistingRepliesAreKept()
    {
        using var f=new Fixture();await f.Seed();
        var row=await f.Db.CoachAccountabilityAgreements.SingleAsync();var state=AccountabilityDocument.Parse(row.StateJson);
        state.SnapWorkouts[0].CoachNote="Start easy.";row.StateJson=AccountabilityDocument.Serialize(state);await f.Db.SaveChangesAsync();
        await f.Live.MessageAsync(f.Owner,f.Client,"live-test",Guid.NewGuid(),"Ready.",default);
        state.SnapWorkouts[0].CoachNote="Use the lighter dumbbells.";row.StateJson=AccountabilityDocument.Serialize(state);await f.Db.SaveChangesAsync();
        var notes=f.Live.View(await f.Live.AllowedAsync(f.Owner,f.Client,"live-test",default));
        Assert.Equal(new[]{"Start easy.","Ready.","Use the lighter dumbbells."},notes.Messages.Select(m=>m.Body));
        Assert.Equal(3,f.Live.View(await f.Live.AllowedAsync(f.Coach,f.Client,"live-test",default)).Messages.Count);
    }
    private static Dictionary<string,List<AccountabilitySet>> Sets(int reps)=>new(){["e1"]=[new(){Reps=reps,Weight=0}]};
    private sealed class Fixture:IDisposable
    {
        public readonly string Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"12wr-snap-live-"+Guid.NewGuid().ToString("N"));
        public readonly RecompDbContext Db=new(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly Guid Client=Guid.NewGuid(),Owner=Guid.NewGuid(),Coach=Guid.NewGuid(),OtherCoach=Guid.NewGuid();
        public readonly TestEnvironment Environment=new();public readonly IDataProtectionProvider Protection=new EphemeralDataProtectionProvider();
        public readonly IConfiguration Config;public readonly NotificationFiles Files;public readonly SimpleCoachingService Simple;public readonly SnapLiveService Live;
        public readonly AccountabilityLedger Ledger;public readonly CoachNotificationProcessor Processor;public readonly FakeEmail Email=new();
        public Fixture()
        {
            Config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Notifications:DataPath"]=Path,["Notifications:DeliveryEnabled"]="true",["SimpleCoaching:Enabled"]="true"}).Build();
            Files=new(Environment,Config,Protection);_=Files.Identity;
            var access=new CoachAccountabilityPreviewService(Db,new CoachCenterService(Db,Config),null!);
            Ledger=new(Db,access,Config);Simple=new(Db,access,Ledger);
            Processor=new(Db,Files,Email,new FakePhone(),NullLogger<CoachNotificationProcessor>.Instance,Config);
            Live=new(Db,Files,Simple,Processor);
        }
        public async Task Seed()
        {
            var state=new AccountabilityState{Rules=AccountabilityDocument.RuleIds.Select(id=>new AccountabilityRule{Id=id}).ToList(),Agreement=new(){Start=DateTime.UtcNow.ToString("yyyy-MM-dd")},
                SnapWorkouts=[new(){Id="live-test",Simple=true,FeeOnFinishOnly=true,Name="Live workout",Date=DateTime.UtcNow.ToString("yyyy-MM-dd"),Cap=30,MinimumCharge=10,LatePenalty=20,CreatedAtUtc=DateTimeOffset.UtcNow.AddDays(-1),DeadlineUtc=DateTimeOffset.UtcNow.AddHours(1),
                    Exercises=[new(){Id="e1",Name="Push ups",Sets=1,Reps=10,MaxWeight=0,Penalty=5,ExtraRepRate=1}]}]};
            var json=AccountabilityDocument.Serialize(state);var terms=AccountabilityDocument.ExpectedTerms(state);
            Db.AddRange(new Client{ClientId=Client,ClientName="Client",TimeZone="UTC"},
                new UserAccount{UserId=Owner,ExternalSubject="owner",EmailNormalized="owner@example.com",DisplayName="Client"},
                new UserAccount{UserId=Coach,ExternalSubject="coach",EmailNormalized="coach@example.com",DisplayName="Coach"},
                new UserAccount{UserId=OtherCoach,ExternalSubject="other",EmailNormalized="other@example.com"},
                new UserClientAccess{UserClientAccessId=Guid.NewGuid(),ClientId=Client,UserId=Owner},
                new CoachClientRelationship{CoachClientRelationshipId=Guid.NewGuid(),ClientId=Client,CoachUserId=Coach,StatusCode="ACTIVE"},
                new CoachClientRelationship{CoachClientRelationshipId=Guid.NewGuid(),ClientId=Client,CoachUserId=OtherCoach,StatusCode="ACTIVE"},
                new CoachAccountabilityAgreement{ClientId=Client,CoachUserId=Coach,StateJson=json,TermsJson=terms,TermsHash=AccountabilityDocument.HashTerms(terms),Revision=1,TermsVersion=1},
                new CoachAccountabilityEvent{EventId=Guid.NewGuid(),ClientId=Client,ActorUserId=Coach,Kind="CONFIG",Revision=1,PayloadJson=json,CreatedAtUtc=Files.Identity.ActiveSinceUtc.AddDays(-1)},
                new ProgramRecord{ProgramId=Guid.NewGuid(),ClientId=Client,StartDate=DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),SelectedPlanCode="TEST",GoalCode="ATHLETIC_RECOMP"});
            await Db.SaveChangesAsync();
        }
        public void Dispose(){Db.Dispose();Directory.Delete(Path,true);}
    }
    private sealed class FakeEmail:IEmailService
    {
        public readonly List<OutboundEmail> Messages=[];
        public Task<EmailSendResult> SendAsync(OutboundEmail message,CancellationToken ct=default){Messages.Add(message);return Task.FromResult(new EmailSendResult(true));}
    }
    private sealed class FakePhone:IPhoneNotificationSender
    { public Task<bool> SendAsync(NotificationDevice device,string subject,string url,string tag,CancellationToken ct)=>Task.FromResult(true); }
    private sealed class TestEnvironment:IWebHostEnvironment
    {
        public string ApplicationName{get;set;}="Tests";public string EnvironmentName{get;set;}="Testing";public string ContentRootPath{get;set;}=".";public string WebRootPath{get;set;}=".";
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
    }
}
