using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
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
    [Theory]
    [InlineData("coach",true,true)]
    [InlineData("other",true,false)]
    [InlineData("owner",false,true)]
    public async Task LedgerPageOpensForEveryAssignedCoachAndClient(string subject,bool asCoach,bool canReviewAgreement)
    {
        var f=await Fixture.Create();using var db=f.Db();
        await f.Store(db).SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        var config=new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["WEBSITE_SITE_NAME"]="recomp-test" }).Build();
        var access=new CoachAccountabilityPreviewService(db,new CoachCenterService(db),null!);
        var ledger=new AccountabilityLedger(db,access);
        var controller=new WebRecomp.Web.Controllers.AccountabilityLedgerController(new AppUserContextService(db,config),ledger,access,config);
        var claims=new List<System.Security.Claims.Claim>{new("sub",subject)};
        if(asCoach)claims.Add(new(System.Security.Claims.ClaimTypes.Role,"Coach"));
        controller.ControllerContext=new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext { User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims,"test")) } };
        var result=Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Index(f.Client,default));
        var page=Assert.IsType<WebRecomp.Web.Controllers.AccountabilityLedgerPage>(result.Model);
        Assert.Equal(f.Client,page.ClientId);Assert.Equal(asCoach,page.CanRecord);Assert.Equal(canReviewAgreement,page.CanReviewAgreement);Assert.Empty(page.Entries);
    }
    [Fact]
    public async Task SecondAssignedCoachCanRecordSharedPaymentsButRevokedCoachCannot()
    {
        var f=await Fixture.Create();using var db=f.Db();
        var row=await f.Store(db).SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        var access=new CoachAccountabilityPreviewService(db,new CoachCenterService(db),null!);var ledger=new AccountabilityLedger(db,access);
        Assert.NotNull(await ledger.AccountAsync(f.OtherCoach,f.Client,true,default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ledger.AccountAsync(f.OtherCoach,f.Client,false,default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ledger.AccountAsync(Guid.NewGuid(),f.Client,true,default));
        await ledger.RecordAsync(f.OtherCoach,row,row.Revision,Guid.NewGuid(),"payment-client",DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),10,"Payment received",default);
        var entry=Assert.Single(await ledger.ReadAsync(row,default));Assert.Equal(f.OtherCoach,entry.ActorUserId);Assert.Equal(-10,entry.Amount);
        await ledger.ReverseAsync(f.OtherCoach,row,row.Revision,entry.Id,"Correction",default);
        Assert.Equal(0,(await ledger.ReadAsync(row,default)).Sum(x=>x.Amount));
        var link=await db.CoachClientRelationships.SingleAsync(x=>x.CoachUserId==f.OtherCoach);link.RevokedAtUtc=DateTime.UtcNow;await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ledger.AccountAsync(f.OtherCoach,f.Client,true,default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ledger.RecordAsync(f.OtherCoach,row,row.Revision,Guid.NewGuid(),"fee",entry.Date,10,"No longer assigned",default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ledger.ReverseAsync(f.OtherCoach,row,row.Revision,entry.Id,"No longer assigned",default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Store(db).ReadAsync(f.OtherCoach,f.Client,true,default));
    }
    [Theory]
    [InlineData("coach",true)]
    [InlineData("other",true)]
    [InlineData("owner",false)]
    public async Task LedgerHttpRouteRendersForBothCoachesAndClient(string subject,bool canRecord)
    {
        var f=await Fixture.Create();
        using(var db=f.Db())await f.Store(db).SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        using var server=CreateServer(f);
        using var http=server.CreateClient();http.DefaultRequestHeaders.Add("X-Test-Actor",subject);
        var response=await http.GetAsync($"/clients/{f.Client}/coach-ledger");
        Assert.Equal(System.Net.HttpStatusCode.OK,response.StatusCode);
        var html=await response.Content.ReadAsStringAsync();Assert.Contains("Running balance",html);Assert.Contains("Account history",html);
        Assert.Equal(canRecord,html.Contains("Record a payment or adjustment"));
        Assert.Equal(System.Net.HttpStatusCode.NotFound,(await http.GetAsync($"/clients/{Guid.NewGuid()}/coach-ledger")).StatusCode);
        http.DefaultRequestHeaders.Remove("X-Test-Actor");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,(await http.GetAsync($"/clients/{f.Client}/coach-ledger")).StatusCode);
    }
    private static Microsoft.AspNetCore.TestHost.TestServer CreateServer(Fixture f,bool simpleMode=false)
    {
        return new Microsoft.AspNetCore.TestHost.TestServer(new Microsoft.AspNetCore.Hosting.WebHostBuilder()
            .UseSetting(Microsoft.AspNetCore.Hosting.WebHostDefaults.ApplicationKey,typeof(WebRecomp.Web.Controllers.AccountabilityLedgerController).Assembly.GetName().Name)
            .ConfigureAppConfiguration((_,builder)=>builder.AddInMemoryCollection(new Dictionary<string,string?>{["WEBSITE_SITE_NAME"]="recomp-test",["SimpleCoaching:Enabled"]=simpleMode.ToString()}))
            .ConfigureServices(services=>
            {
                services.AddScoped<RecompDbContext>(_=>f.Db());
                services.AddScoped<AppUserContextService>();services.AddScoped<CoachCenterService>();
                services.AddScoped<CoachAccountabilityPreviewService>(_=>new CoachAccountabilityPreviewService(_.GetRequiredService<RecompDbContext>(),_.GetRequiredService<CoachCenterService>(),null!));
                services.AddScoped<CoachAccountabilityStore>();services.AddScoped<AccountabilityLedger>();services.AddScoped<SimpleCoachingService>();
                services.AddRateLimiter(o=>o.AddConcurrencyLimiter("access-code",p=>p.PermitLimit=100));
                services.AddAuthentication("ledger-test").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,LedgerTestAuthentication>("ledger-test",_=>{});
                services.AddAuthorization();services.AddControllersWithViews().AddApplicationPart(typeof(WebRecomp.Web.Controllers.AccountabilityLedgerController).Assembly).AddControllersAsServices();
                services.AddTransient<WebRecomp.Web.Controllers.AccountabilityPreviewController>(provider=>new(provider.GetRequiredService<AppUserContextService>(),provider.GetRequiredService<CoachAccountabilityPreviewService>(),null!,null!,provider.GetRequiredService<IConfiguration>(),provider.GetRequiredService<CoachAccountabilityStore>()));
            })
            .Configure(app=>{app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();app.UseEndpoints(endpoints=>endpoints.MapControllers());}));
    }
    [Fact]
    public async Task SecondCoachAssignmentAppearsInClientPortalAndStaysUntilCompleted()
    {
        var f=await Fixture.Create();var workout=f.State.SnapWorkouts.Single();f.State.SnapWorkouts=[];
        using(var db=f.Db())await f.Store(db).SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        using var server=CreateServer(f);using var http=server.CreateClient();
        void Actor(string name){http.DefaultRequestHeaders.Remove("X-Test-Actor");http.DefaultRequestHeaders.Add("X-Test-Actor",name);}
        Actor("other");
        var page=await http.GetAsync($"/clients/{f.Client}/coach-ledger");var html=await page.Content.ReadAsStringAsync();
        var token=System.Text.RegularExpressions.Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;Assert.NotEmpty(token);
        http.DefaultRequestHeaders.Add("Cookie",string.Join("; ",page.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0])));
        var stateResponse=await http.GetAsync($"/accountability-preview/clients/{f.Client}/snap-state");Assert.Equal(System.Net.HttpStatusCode.OK,stateResponse.StatusCode);
        f.State.SnapWorkouts.Add(workout);
        var save=await http.PostAsync($"/accountability-preview/clients/{f.Client}/state/snap",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=token,["ExpectedRevision"]="1",["StateJson"]=AccountabilityDocument.Serialize(f.State),["Terms"]=AccountabilityDocument.ExpectedTerms(f.State)}));
        Assert.Equal(System.Net.HttpStatusCode.OK,save.StatusCode);
        using(var db=f.Db()){var row=await db.CoachAccountabilityAgreements.SingleAsync();Assert.Equal(f.Coach,row.CoachUserId);Assert.Contains(await db.CoachAccountabilityEvents.ToListAsync(),x=>x.ActorUserId==f.OtherCoach&&x.Kind=="CONFIG");}
        Actor("owner");
        async Task<int> Pending()
        {
            var response=await http.GetAsync("/my-coach/pending-snaps");Assert.Equal(System.Net.HttpStatusCode.OK,response.StatusCode);
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(f.Client,json.RootElement.GetProperty("clientId").GetGuid());return json.RootElement.GetProperty("workouts").GetArrayLength();
        }
        Assert.Equal(1,await Pending());
        var portal=await http.GetStringAsync($"/clients/{f.Client}/coach-ledger");Assert.Contains("data-pending-workout=\"workout-1\"",portal);Assert.Contains("pending-snap.js",portal);
        async Task Results(int reps)
        {
            Actor("other");f.Result.Sets["exercise-1"][0].Reps=reps;
            int revision;using(var db=f.Db())revision=(await db.CoachAccountabilityAgreements.SingleAsync()).Revision;
            var response=await http.PostAsync($"/accountability-preview/clients/{f.Client}/state/result",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=token,["ExpectedRevision"]=revision.ToString(),["WorkoutId"]=workout.Id,["ResultJson"]=JsonSerializer.Serialize(f.Result,AccountabilityDocument.Json)}));
            Assert.Equal(System.Net.HttpStatusCode.OK,response.StatusCode);Actor("owner");
        }
        await Results(9);Assert.Equal(1,await Pending());await Results(10);Assert.Equal(0,await Pending());
        using(var db=f.Db())
        {
            Assert.Empty(await db.CoachAccountabilityApprovals.ToListAsync());
            foreach(var coach in new[]{f.Coach,f.OtherCoach})
            {
                var status=(await new CoachCenterService(db).SnapStatusesAsync(coach,default))[f.Client];
                Assert.Equal(1,status.Submitted);Assert.Equal(1,status.Completed);Assert.Equal("Completed",status.Status);
            }
        }
        Assert.DoesNotContain("data-pending-workout=",await http.GetStringAsync($"/clients/{f.Client}/coach-ledger"));
    }
    [Fact]
    public async Task SnapAssignmentCannotChangeFeesAndRevokedCoachCannotAssign()
    {
        var f=await Fixture.Create();using var db=f.Db();var store=f.Store(db);
        var row=await store.SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);
        var altered=AccountabilityDocument.Parse(row.StateJson);altered.Payment.Amount=999;
        await Assert.ThrowsAsync<ArgumentException>(()=>store.SaveCoachAsync(f.OtherCoach,f.Client,true,row.Revision,AccountabilityDocument.Serialize(altered),AccountabilityDocument.ExpectedTerms(altered),default,snapOnly:true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.ReadSnapAsync(Guid.NewGuid(),f.Client,true,default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.SaveCoachAsync(f.Owner,f.Client,false,row.Revision,row.StateJson,row.TermsJson,default,snapOnly:true));
        var link=await db.CoachClientRelationships.SingleAsync(x=>x.CoachUserId==f.OtherCoach);link.RevokedAtUtc=DateTime.UtcNow;await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.ReadSnapAsync(f.OtherCoach,f.Client,true,default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.SaveCoachAsync(f.OtherCoach,f.Client,true,row.Revision,row.StateJson,row.TermsJson,default,snapOnly:true));
    }
    [Fact]
    public async Task FeesAndPaymentsAreSeparateForEachClient()
    {
        var f=await Fixture.Create();using var db=f.Db();var second=Guid.NewGuid();
        db.Clients.Add(new(){ClientId=second,ClientName="Separate client"});db.CoachClientRelationships.Add(new(){CoachClientRelationshipId=Guid.NewGuid(),CoachUserId=f.Coach,ClientId=second,StatusCode="ACTIVE"});await db.SaveChangesAsync();
        var store=f.Store(db);var state=AccountabilityDocument.Serialize(f.State);var terms=AccountabilityDocument.ExpectedTerms(f.State);
        var firstRow=await store.SaveCoachAsync(f.Coach,f.Client,true,0,state,terms,default);var secondRow=await store.SaveCoachAsync(f.Coach,second,true,0,state,terms,default);
        var ledger=new AccountabilityLedger(db,new CoachAccountabilityPreviewService(db,new CoachCenterService(db),null!));var day=DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));var sameRequest=Guid.NewGuid();
        await ledger.RecordAsync(f.Coach,firstRow,firstRow.Revision,sameRequest,"fee",day,20,"First client fee",default);
        await ledger.RecordAsync(f.Coach,secondRow,secondRow.Revision,sameRequest,"fee",day,75,"Second client fee",default);
        await ledger.RecordAsync(f.Coach,firstRow,firstRow.Revision,Guid.NewGuid(),"payment-client",day,5,"First client payment",default);
        Assert.Equal(15,(await ledger.ReadAsync(firstRow,default)).Sum(x=>x.Amount));Assert.Equal(75,(await ledger.ReadAsync(secondRow,default)).Sum(x=>x.Amount));
        Assert.All(await ledger.ReadAsync(firstRow,default),x=>Assert.DoesNotContain("Second client",x.Description));Assert.Single(await ledger.ReadAsync(secondRow,default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ledger.AccountAsync(f.Owner,second,false,default));
    }
    private sealed class LedgerTestAuthentication(Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,Microsoft.Extensions.Logging.ILoggerFactory logger,System.Text.Encodings.Web.UrlEncoder encoder)
        :Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject=Request.Headers["X-Test-Actor"].ToString();
            if(subject=="")return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());
            var claims=new List<System.Security.Claims.Claim>{new("sub",subject)};
            if(subject!="owner")claims.Add(new(System.Security.Claims.ClaimTypes.Role,"Coach"));
            var principal=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims,Scheme.Name));
            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal,Scheme.Name)));
        }
    }
    [Fact]
    public async Task SimpleWorkoutsPostWithoutApprovalCorrectWithoutDuplicatesAndSettleWithPayment()
    {
        var f=await Fixture.Create();using var db=f.Db();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["SimpleCoaching:Enabled"]="true"}).Build();
        var access=new CoachAccountabilityPreviewService(db,new CoachCenterService(db),null!);var ledger=new AccountabilityLedger(db,access,config);var simple=new SimpleCoachingService(db,access,ledger);
        var today=AppUserContextService.GetClientToday(await db.Clients.SingleAsync());var request=Guid.NewGuid();
        var workout=new AccountabilityWorkout{Name="Simple strength",Date=today.ToString("yyyy-MM-dd"),Cap=100,CompletionReward=20,Exercises=[new(){Name="Shoulder press",Sets=3,Reps=10,MaxWeight=35,ExtraRepRate=1}]};
        await simple.AssignAsync(f.OtherCoach,f.Client,0,request,workout,default);
        var row=await db.CoachAccountabilityAgreements.SingleAsync();var id="simple-"+request.ToString("N");var saved=AccountabilityDocument.Parse(row.StateJson).SnapWorkouts.Single();
        Assert.True(saved.Simple);Assert.Equal(f.OtherCoach,row.CoachUserId);Assert.Empty(await db.CoachAccountabilityApprovals.ToListAsync());
        var result=new AccountabilityResult{Finalized=true,Sets=new(){[saved.Exercises[0].Id]=[new(){Reps=20,Weight=35},new(){Reps=20,Weight=35},new(){Reps=20,Weight=35}]}};
        await simple.ResultAsync(f.Owner,f.Client,false,row.Revision,id,result,default);Assert.Equal(-50,(await ledger.ReadAsync(row,default)).Sum(x=>x.Amount));
        await simple.ResultAsync(f.Owner,f.Client,false,row.Revision,id,result,default);Assert.Equal(-50,(await ledger.ReadAsync(row,default)).Sum(x=>x.Amount));Assert.Equal(2,(await ledger.ReadAsync(row,default)).Count);
        result.Sets[saved.Exercises[0].Id].ForEach(x=>x.Reps=15);await simple.ResultAsync(f.Owner,f.Client,false,row.Revision,id,result,default);Assert.Equal(-35,(await ledger.ReadAsync(row,default)).Sum(x=>x.Amount));
        await ledger.RecordAsync(f.Coach,row,row.Revision,Guid.NewGuid(),"payment-coach",today,35,"Paid incentive",default);Assert.Equal(0,(await ledger.ReadAsync(row,default)).Sum(x=>x.Amount));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>simple.AssignAsync(f.Owner,f.Client,row.Revision,Guid.NewGuid(),workout,default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>simple.ResultAsync(Guid.NewGuid(),f.Client,false,row.Revision,id,result,default));
    }
    [Fact]
    public async Task SimpleFlowLeavesOldTermsAndChargesArchivedEvenWhenOldAgreementHasEnded()
    {
        var f=await Fixture.Create();using var db=f.Db();f.State.Agreement.EndedOn=AppUserContextService.GetClientToday(await db.Clients.SingleAsync()).AddDays(-1).ToString("yyyy-MM-dd");
        var row=await f.Store(db).SaveCoachAsync(f.Coach,f.Client,true,0,AccountabilityDocument.Serialize(f.State),AccountabilityDocument.ExpectedTerms(f.State),default);var oldTerms=row.TermsJson;
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["SimpleCoaching:Enabled"]="true"}).Build();var access=new CoachAccountabilityPreviewService(db,new CoachCenterService(db),null!);var ledger=new AccountabilityLedger(db,access,config);var simple=new SimpleCoachingService(db,access,ledger);var today=AppUserContextService.GetClientToday(await db.Clients.SingleAsync());
        await simple.AssignAsync(f.OtherCoach,f.Client,row.Revision,Guid.NewGuid(),new(){Name="Simple workout",Date=today.ToString("yyyy-MM-dd"),Exercises=[new(){Name="Squat",Sets=1,Reps=10,MaxWeight=0,Type="fine",Basis="incomplete-reps",Rate=2}]},default);
        var state=AccountabilityDocument.Parse(row.StateJson);var workout=state.SnapWorkouts.Single(x=>x.Simple);
        await simple.ResultAsync(f.Owner,f.Client,false,row.Revision,workout.Id,new(){Finalized=true,Sets=new(){[workout.Exercises[0].Id]=[new(){Reps=7,Weight=0}]}},default);
        Assert.Equal(oldTerms,row.TermsJson);Assert.Equal(f.State.Agreement.EndedOn,AccountabilityDocument.Parse(row.StateJson).Agreement.EndedOn);
        var entry=Assert.Single(await ledger.ReadAsync(row,default));Assert.Equal(6,entry.Amount);Assert.Contains(workout.Id,entry.SourceKey);Assert.DoesNotContain("workout-1",entry.SourceKey);
        await ledger.RefreshAsync(f.Owner,row,await db.UserAccounts.SingleAsync(x=>x.UserId==f.Owner),default);Assert.Single(await ledger.ReadAsync(row,default));
    }
    [Fact]
    public async Task ActiveSimpleHttpFormsAssignFinishAndPostClientLedgerWithoutAgreement()
    {
        var f=await Fixture.Create();using var server=CreateServer(f,true);using var http=server.CreateClient();
        void Actor(string actor){http.DefaultRequestHeaders.Remove("X-Test-Actor");http.DefaultRequestHeaders.Add("X-Test-Actor",actor);http.DefaultRequestHeaders.Remove("Cookie");}
        async Task<string> Token(string url)
        {
            var response=await http.GetAsync(url);Assert.Equal(System.Net.HttpStatusCode.OK,response.StatusCode);var html=await response.Content.ReadAsStringAsync();
            if(response.Headers.TryGetValues("Set-Cookie",out var cookies)){http.DefaultRequestHeaders.Remove("Cookie");http.DefaultRequestHeaders.Add("Cookie",string.Join("; ",cookies.Select(x=>x.Split(';')[0])));}
            return System.Text.RegularExpressions.Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        }
        Actor("other");var token=await Token($"/clients/{f.Client}/snap");Assert.NotEmpty(token);var request=Guid.NewGuid();
        DateOnly today;using(var db=f.Db())today=AppUserContextService.GetClientToday(await db.Clients.SingleAsync());
        var response=await http.PostAsync($"/clients/{f.Client}/snap/assign",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=token,["RequestId"]=request.ToString(),["Revision"]="0",["Name"]="Quick test workout",["Date"]=today.ToString("yyyy-MM-dd"),["Cap"]="100",["CompletionReward"]="20",["Exercises[0].Name"]="Squat",["Exercises[0].Sets"]="1",["Exercises[0].Reps"]="10",["Exercises[0].MaxWeight"]="0",["Exercises[0].ExtraRepRate"]="1"}));
        Assert.Equal(System.Net.HttpStatusCode.Redirect,response.StatusCode);
        Actor("owner");token=await Token($"/clients/{f.Client}/snap");Assert.NotEmpty(token);
        string id,exercise;int revision;using(var db=f.Db()){var row=await db.CoachAccountabilityAgreements.SingleAsync();revision=row.Revision;var workout=AccountabilityDocument.Parse(row.StateJson).SnapWorkouts.Single();id=workout.Id;exercise=workout.Exercises[0].Id;}
        response=await http.PostAsync($"/clients/{f.Client}/snap/result",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=token,["Revision"]=revision.ToString(),["WorkoutId"]=id,["Action"]="finish",["Exercises[0].Id"]=exercise,["Exercises[0].Sets[0].Reps"]="40",["Exercises[0].Sets[0].Weight"]="0"}));
        Assert.Equal(System.Net.HttpStatusCode.Redirect,response.StatusCode);
        var account=await http.GetStringAsync($"/clients/{f.Client}/coach-ledger");Assert.Contains("$50.00",account);Assert.DoesNotContain("waiting for the client's approval",account);
        using(var db=f.Db()){Assert.Empty(await db.CoachAccountabilityApprovals.ToListAsync());Assert.True(AccountabilityDocument.Parse((await db.CoachAccountabilityAgreements.SingleAsync()).StateJson).SnapResults[id].Finalized);}
        Actor("other");Assert.Contains("Completed",await http.GetStringAsync("/coach"));Assert.Contains("Old coaching screens",await http.GetStringAsync("/coach/old"));
    }
    [Fact]
    public async Task SimpleAccountAcceptsManualFeeBeforeAnyWorkoutOrAgreement()
    {
        var f=await Fixture.Create();using var db=f.Db();var access=new CoachAccountabilityPreviewService(db,new CoachCenterService(db),null!);var ledger=new AccountabilityLedger(db,access);var simple=new SimpleCoachingService(db,access,ledger);
        var row=await simple.EnsureAsync(f.Coach,f.Client,default);var today=AppUserContextService.GetClientToday(await db.Clients.SingleAsync());
        await ledger.RecordAsync(f.Coach,row,row.Revision,Guid.NewGuid(),"fee",today,6,"3 occurrences at $2",default);
        Assert.Equal(6,Assert.Single(await ledger.ReadAsync(row,default)).Amount);Assert.Empty(AccountabilityDocument.Parse(row.StateJson).SnapWorkouts);Assert.Empty(await db.CoachAccountabilityApprovals.ToListAsync());
        await ledger.RecordAsync(f.OtherCoach,row,row.Revision,Guid.NewGuid(),"payment-client",today,6,"Payment received",default);Assert.Equal(0,(await ledger.ReadAsync(row,default)).Sum(x=>x.Amount));
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
