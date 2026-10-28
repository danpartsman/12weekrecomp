using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
namespace WebRecomp.Web.Tests;
public sealed class CoachAccountabilityStoreTests
{
    [Fact]
    public void RealBrowserDocumentAndDisplayedTermsPassServerValidation()
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/accountability-js-document.json")));
        var state=AccountabilityDocument.Parse(fixture.RootElement.GetProperty("state").GetRawText());
        AccountabilityDocument.ValidateTerms(state,fixture.RootElement.GetProperty("terms").GetString()!);
        var reordered=AccountabilityDocument.ExpectedTerms(state);
        Assert.Equal(AccountabilityDocument.HashTerms(reordered),AccountabilityDocument.HashTerms(fixture.RootElement.GetProperty("terms").GetString()!));
    }
    [Fact]
    public async Task AgreementAndResultsAreVisibleFromFreshDatabaseContext()
    {
        var f=await Fixture.Create();
        using(var db=f.Db()){var store=f.Store(db);await store.SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);}
        using(var db=f.Db()){var store=f.Store(db);var row=await store.ReadAsync(f.Owner,f.Client,false,default);Assert.NotNull(row);await store.SaveResultAsync(f.Owner,f.Client,false,row!.Revision,"workout-1",JsonSerializer.Serialize(f.Result,AccountabilityDocument.Json),default);}
        using(var db=f.Db()){var row=await f.Store(db).ReadAsync(f.Coach,f.Client,true,default);var state=AccountabilityDocument.Parse(row!.StateJson);Assert.True(state.SnapResults["workout-1"].Finalized);Assert.Equal(1,row.TermsVersion);Assert.Equal(2,await db.CoachAccountabilityEvents.CountAsync());}
    }
    [Fact]
    public async Task ClientCannotChangeConfigurationAndOtherCoachCannotReadAgreement()
    {
        var f=await Fixture.Create();using var db=f.Db();var store=f.Store(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.SaveCoachAsync(f.Owner,f.Client,false,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default));
        await store.SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.ReadAsync(f.OtherCoach,f.Client,true,default));
    }
    [Fact]
    public async Task ApprovalPersistsAndTermsChangesRequireFreshApprovalWithoutDeletingHistory()
    {
        var f=await Fixture.Create();
        using(var db=f.Db()){var store=f.Store(db);var row=await store.SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);await store.ApproveAsync(f.Owner,f.Client,row.Revision,row.TermsJson,"owner@example.com",default);}
        using(var db=f.Db()){var store=f.Store(db);var row=await store.ReadAsync(f.Owner,f.Client,false,default);var envelope=JsonSerializer.SerializeToElement(await store.EnvelopeAsync(row,default));Assert.NotEqual(JsonValueKind.Null,envelope.GetProperty("approval").ValueKind);f.State.Rules[0].Amount=10;await store.SaveCoachAsync(f.Coach,f.Client,true,row!.Revision,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);}
        using(var db=f.Db()){var store=f.Store(db);var row=await store.ReadAsync(f.Owner,f.Client,false,default);var envelope=JsonSerializer.SerializeToElement(await store.EnvelopeAsync(row,default));Assert.Equal(JsonValueKind.Null,envelope.GetProperty("approval").ValueKind);Assert.Equal(2,row!.TermsVersion);Assert.Single(await db.CoachAccountabilityApprovals.ToListAsync());Assert.Equal(3,await db.CoachAccountabilityEvents.CountAsync());}
    }
    [Fact]
    public async Task ExceptionsPreserveApprovalAndCoachSettingsDoNotOverwriteClientResults()
    {
        var f=await Fixture.Create();using var db=f.Db();var store=f.Store(db);
        var row=await store.SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        await store.ApproveAsync(f.Owner,f.Client,row.Revision,row.TermsJson,"owner@example.com",default);
        await store.SaveResultAsync(f.Owner,f.Client,false,row.Revision,"workout-1",JsonSerializer.Serialize(f.Result,AccountabilityDocument.Json),default);
        f.State.Excused[f.State.Agreement.Start]=true;
        await store.SaveCoachAsync(f.Coach,f.Client,true,row.Revision,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        Assert.Equal(1,row.TermsVersion);Assert.True(AccountabilityDocument.Parse(row.StateJson).SnapResults["workout-1"].Finalized);
        var envelope=JsonSerializer.SerializeToElement(await store.EnvelopeAsync(row,default));Assert.NotEqual(JsonValueKind.Null,envelope.GetProperty("approval").ValueKind);
    }
    [Fact]
    public async Task StaleRevisionCannotOverwriteNewerConfiguration()
    {
        var f=await Fixture.Create();using var db=f.Db();var store=f.Store(db);var row=await store.SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        var old=row.Revision;f.State.Payment.Amount=20;await store.SaveCoachAsync(f.Coach,f.Client,true,old,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        await Assert.ThrowsAsync<AccountabilityConflictException>(()=>store.SaveResultAsync(f.Owner,f.Client,false,old,"workout-1",JsonSerializer.Serialize(f.Result,AccountabilityDocument.Json),default));
        Assert.Empty(AccountabilityDocument.Parse(row.StateJson).SnapResults);
    }
    [Fact]
    public async Task TamperedDisplayedTermsAndUnassignedResultsAreRejected()
    {
        var f=await Fixture.Create();using var db=f.Db();var store=f.Store(db);
        await Assert.ThrowsAsync<ArgumentException>(()=>store.SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),"{}",default));
        var row=await store.SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        await Assert.ThrowsAsync<ArgumentException>(()=>store.SaveResultAsync(f.Owner,f.Client,false,row.Revision,"not-assigned",JsonSerializer.Serialize(f.Result,AccountabilityDocument.Json),default));
        await Assert.ThrowsAsync<AccountabilityConflictException>(()=>store.ApproveAsync(f.Owner,f.Client,row.Revision,"{}","owner@example.com",default));
    }
    [Fact]
    public async Task RevokingFinancialCoachConnectionBlocksBothApprovalAndResults()
    {
        var f=await Fixture.Create();using var db=f.Db();var store=f.Store(db);var row=await store.SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        var link=await db.CoachClientRelationships.SingleAsync(x=>x.CoachUserId==f.Coach);link.RevokedAtUtc=DateTime.UtcNow;await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.ApproveAsync(f.Owner,f.Client,row.Revision,row.TermsJson,"owner@example.com",default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.SaveResultAsync(f.Owner,f.Client,false,row.Revision,"workout-1",JsonSerializer.Serialize(f.Result,AccountabilityDocument.Json),default));
    }
    [Fact]
    public async Task SimultaneousTrackedContextsUseDatabaseRevisionConcurrency()
    {
        var f=await Fixture.Create();using(var initial=f.Db())await f.Store(initial).SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        using var first=f.Db();using var second=f.Db();
        await first.CoachAccountabilityAgreements.SingleAsync();await second.CoachAccountabilityAgreements.SingleAsync();
        f.State.Payment.Amount=30;await f.Store(first).SaveCoachAsync(f.Coach,f.Client,true,1,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        f.State.Payment.Amount=40;
        await Assert.ThrowsAsync<AccountabilityConflictException>(()=>f.Store(second).SaveCoachAsync(f.Coach,f.Client,true,1,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default));
        using var verify=f.Db();Assert.Equal(30,AccountabilityDocument.Parse((await verify.CoachAccountabilityAgreements.SingleAsync()).StateJson).Payment.Amount);
    }
    [Fact]
    public async Task RecordedWorkoutTargetsCannotBeRewrittenByConfigurationSave()
    {
        var f=await Fixture.Create();using var db=f.Db();var store=f.Store(db);var row=await store.SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        await store.SaveResultAsync(f.Owner,f.Client,false,row.Revision,"workout-1",JsonSerializer.Serialize(f.Result,AccountabilityDocument.Json),default);
        f.State.SnapWorkouts[0].Exercises[0].Reps=20;
        await Assert.ThrowsAsync<ArgumentException>(()=>store.SaveCoachAsync(f.Coach,f.Client,true,row.Revision,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default));
        Assert.Equal(10,AccountabilityDocument.Parse(row.StateJson).SnapWorkouts[0].Exercises[0].Reps);
    }
    [Fact]
    public async Task TypedExercisePersistsReusesAndCompletesWithoutCreatingACatalogItem()
    {
        var f=await Fixture.Create();var e=f.State.SnapWorkouts[0].Exercises[0];
        e.CatalogId="";e.Name="Coach's custom movement";e.Weight=0;e.MaxWeight=25;
        using(var db=f.Db())
        {
            var before=await db.ExerciseCatalogItems.CountAsync();
            await f.Store(db).SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
            Assert.Equal(before,await db.ExerciseCatalogItems.CountAsync());
        }
        using(var db=f.Db())
        {
            var row=await f.Store(db).ReadAsync(f.Owner,f.Client,false,default);
            var saved=AccountabilityDocument.Parse(row!.StateJson);Assert.Equal(25,saved.SnapWorkouts[0].Exercises[0].MaxWeight);
            var library=await new SnapWorkoutLibrary(db,new CoachCenterService(db)).ReadAsync(f.Coach,f.Client,default);
            Assert.Equal(25,Assert.Single(library).Exercises[0].MaxWeight);
            Assert.True(SnapWorkoutLibrary.Completed(saved.SnapWorkouts[0],f.Result));
            f.Result.Sets["exercise-1"][0].Weight=26;
            Assert.False(SnapWorkoutLibrary.Completed(saved.SnapWorkouts[0],f.Result));
        }
    }
    [Fact]
    public async Task TypedExerciseRequiresValidNameAndCeilingWhileCatalogIdsRemainValidated()
    {
        var f=await Fixture.Create();var e=f.State.SnapWorkouts[0].Exercises[0];
        e.CatalogId="";e.Weight=0;
        Assert.Throws<ArgumentException>(()=>AccountabilityDocument.Validate(f.State));
        e.MaxWeight=-1;Assert.Throws<ArgumentException>(()=>AccountabilityDocument.Validate(f.State));
        e.MaxWeight=25;e.Name=" ";Assert.Throws<ArgumentException>(()=>AccountabilityDocument.Validate(f.State));
        e.Name="Typed exercise";e.CatalogId=Guid.NewGuid().ToString();
        using var db=f.Db();
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Store(db).SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default));
    }
    [Fact]
    public async Task LedgerPostsIncentiveOnceAppendsCorrectionAndRecordsPaymentIdempotently()
    {
        var f=await Fixture.Create();var w=f.State.SnapWorkouts[0];var e=w.Exercises[0];
        w.Cap=100;w.CompletionReward=20;e.CompletionReward=0;e.ExtraRepRate=1;e.Sets=3;e.Reps=10;
        f.Result.Sets[e.Id]=[new(){Reps=20,Weight=e.Weight},new(){Reps=20,Weight=e.Weight},new(){Reps=20,Weight=e.Weight}];
        using var db=f.Db();var access=new CoachAccountabilityPreviewService(db,new CoachCenterService(db),null!);
        var ledger=new AccountabilityLedger(db,access);var store=new CoachAccountabilityStore(db,access,ledger:ledger);
        var row=await store.SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        await store.SaveResultAsync(f.Owner,f.Client,false,row.Revision,w.Id,JsonSerializer.Serialize(f.Result,AccountabilityDocument.Json),default);
        Assert.Empty(await ledger.ReadAsync(row,default)); // Unapproved previews are not posted as debt.
        await store.ApproveAsync(f.Owner,f.Client,row.Revision,row.TermsJson,"Owner",default);
        Assert.Equal(-50,(await ledger.ReadAsync(row,default)).Sum(x=>x.Amount));
        await store.SaveResultAsync(f.Owner,f.Client,false,row.Revision,w.Id,JsonSerializer.Serialize(f.Result,AccountabilityDocument.Json),default);
        Assert.Equal(2,(await ledger.ReadAsync(row,default)).Count);
        f.Result.Sets[e.Id].ForEach(set=>set.Reps=15);
        await store.SaveResultAsync(f.Owner,f.Client,false,row.Revision,w.Id,JsonSerializer.Serialize(f.Result,AccountabilityDocument.Json),default);
        var entries=await ledger.ReadAsync(row,default);Assert.Equal(-35,entries.Sum(x=>x.Amount));Assert.Contains(entries,x=>x.Amount==15&&x.Description.StartsWith("Adjustment"));
        var request=Guid.NewGuid();var revision=row.Revision;var date=AppUserContextService.GetClientToday(await db.Clients.SingleAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ledger.RecordAsync(f.Owner,row,revision,request,"payment-coach",date,35,"Paid client",default));
        await ledger.RecordAsync(f.Coach,row,revision,request,"payment-coach",date,35,"Paid client",default);
        await ledger.RecordAsync(f.Coach,row,revision,request,"payment-coach",date,35,"Paid client",default);
        Assert.Equal(0,(await ledger.ReadAsync(row,default)).Sum(x=>x.Amount));
        await Assert.ThrowsAsync<AccountabilityConflictException>(()=>ledger.RecordAsync(f.Coach,row,revision,request,"payment-coach",date,36,"Paid client",default));
    }
    [Fact]
    public async Task LedgerMatchesScreenshotCapAndNeverDropsPerRepReward()
    {
        var f=await Fixture.Create();var w=f.State.SnapWorkouts[0];var e=w.Exercises[0];w.Cap=20;e.Sets=3;e.Reps=10;e.CompletionReward=20;e.ExtraRepRate=1;
        f.State.SnapResults[w.Id]=new(){Finalized=true,CompletedAtUtc=DateTimeOffset.UtcNow,Sets=new(){[e.Id]=[new(){Reps=20,Weight=e.Weight},new(){Reps=20,Weight=e.Weight},new(){Reps=20,Weight=e.Weight}]}};
        var charges=AccountabilityAssessment.Snap(f.State,AccountabilityDocument.Date(w.Date));var reward=Assert.Single(charges.Where(x=>x.Kind=="incentive"));
        Assert.Equal(50,reward.RawAmount);Assert.Equal(20,reward.Amount);
        w.Cap=100;e.Reps=15;Assert.Equal(35,Assert.Single(AccountabilityAssessment.Snap(f.State,AccountabilityDocument.Date(w.Date)).Where(x=>x.Kind=="incentive")).Amount);
    }
    [Fact]
    public async Task LedgerPaymentsAndReversalsPersistAcrossRequestsAndNeverEraseHistory()
    {
        var f=await Fixture.Create();var request=Guid.NewGuid();Guid entryId;var today=DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        using(var db=f.Db())await f.Store(db).SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        using(var db=f.Db())
        {
            var access=new CoachAccountabilityPreviewService(db,new CoachCenterService(db),null!);var ledger=new AccountabilityLedger(db,access);
            var row=await f.Store(db).ReadAsync(f.Coach,f.Client,true,default);
            await ledger.RecordAsync(f.Coach,row!,row!.Revision,request,"fee",today,20,"Reviewed fee",default);
            entryId=Assert.Single(await ledger.ReadAsync(row,default)).Id;
            Assert.Equal(2,(await db.CoachAccountabilityAgreements.AsNoTracking().SingleAsync()).Revision);
        }
        using(var db=f.Db())
        {
            var access=new CoachAccountabilityPreviewService(db,new CoachCenterService(db),null!);var ledger=new AccountabilityLedger(db,access);
            var row=await f.Store(db).ReadAsync(f.Coach,f.Client,true,default);
            await ledger.RecordAsync(f.Coach,row!,1,request,"fee",today,20,"Reviewed fee",default);
            await ledger.ReverseAsync(f.Coach,row!,row!.Revision,entryId,"Entered in error",default);
            await ledger.ReverseAsync(f.Coach,row!,1,entryId,"Entered in error",default);
            var entries=await ledger.ReadAsync(row!,default);Assert.Equal(2,entries.Count);Assert.Equal(0,entries.Sum(x=>x.Amount));
            Assert.Equal(20,entries[0].Amount);Assert.Equal(-20,entries[1].Amount);
        }
    }
    private sealed class Fixture
    {
        public Guid Coach=Guid.NewGuid(),OtherCoach=Guid.NewGuid(),Owner=Guid.NewGuid(),Client=Guid.NewGuid();
        private readonly DbContextOptions<RecompDbContext> options=new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public AccountabilityState State=new();public AccountabilityResult Result=new();
        public RecompDbContext Db()=>new(options);
        public CoachAccountabilityStore Store(RecompDbContext db)=>new(db,new CoachAccountabilityPreviewService(db,new CoachCenterService(db),null!));
        public static async Task<Fixture> Create()
        {
            var f=new Fixture();using var db=f.Db();var client=new Client{ClientId=f.Client,ClientName="Persistent client"};var today=AppUserContextService.GetClientToday(client);var exercise=Guid.NewGuid();
            db.AddRange(client,new UserAccount{UserId=f.Coach,ExternalSubject="coach"},new UserAccount{UserId=f.Owner,ExternalSubject="owner"},new UserAccount{UserId=f.OtherCoach,ExternalSubject="other"},new UserClientAccess{UserClientAccessId=Guid.NewGuid(),UserId=f.Owner,ClientId=f.Client,AccessRoleCode="OWNER"},new CoachClientRelationship{CoachClientRelationshipId=Guid.NewGuid(),CoachUserId=f.Coach,ClientId=f.Client,StatusCode="ACTIVE"},new CoachClientRelationship{CoachClientRelationshipId=Guid.NewGuid(),CoachUserId=f.OtherCoach,ClientId=f.Client,StatusCode="ACTIVE"},new ExerciseCatalogItemRecord{ExerciseCatalogItemId=exercise,Name="Curl",SourceCode="TEST",SourceExternalId="curl",MeasurementType="REPS"});await db.SaveChangesAsync();
            f.State=new(){Enabled=true,Agreement=new(){Start=today.AddDays(-7).ToString("yyyy-MM-dd")},Rules=AccountabilityDocument.RuleIds.Select((id,i)=>new AccountabilityRule{Id=id,Amount=2,Lower=i<4?-5:0,Upper=i<4?5:0}).ToList(),SnapWorkouts=[new(){Id="workout-1",Date=today.ToString("yyyy-MM-dd"),Name="Quick workout",CreatedAtUtc=DateTimeOffset.UtcNow.AddHours(-1),DeadlineUtc=DateTimeOffset.UtcNow.AddHours(1),Exercises=[new(){Id="exercise-1",CatalogId=exercise.ToString(),Name="Curl",Sets=2,Reps=10,Weight=20,Rate=.1m,CompletionReward=5,ExtraRepRate=.1m}]}]};
            f.Result=new(){Finalized=true,CompletedAtUtc=DateTimeOffset.UtcNow,Sets=new(){["exercise-1"]=[new(){Reps=12,Weight=20},new(){Reps=12,Weight=20}]}};return f;
        }
    }
}
