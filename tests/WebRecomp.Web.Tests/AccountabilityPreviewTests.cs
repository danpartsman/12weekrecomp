using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Tests;

public sealed class AccountabilityPreviewTests
{
    [Fact]
    public void ApprovalIsBoundToClientTermsAndExpiryAndRejectsTampering()
    {
        var time = new PreviewClock();
        var receipts = new AccountabilityApprovalReceipt(new EphemeralDataProtectionProvider(), time);
        var client = Guid.NewGuid(); var owner = Guid.NewGuid(); const string terms = "{\"version\":2,\"enabled\":true}";
        var token = receipts.Issue(owner, client, terms, "owner@example.com");
        Assert.Equal(owner, receipts.Read(token, client, terms)!.UserId);
        Assert.Null(receipts.Read(token, Guid.NewGuid(), terms));
        Assert.Null(receipts.Read(token, client, terms + " "));
        Assert.Null(receipts.Read("modified" + token, client, terms));
        time.Now = time.Now.AddDays(31); Assert.Null(receipts.Read(token, client, terms));
    }
    [Fact]
    public async Task ApprovalRequiresClientOwnershipAndAnActiveNonRevokedCoach()
    {
        using var db = new RecompDbContext(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var owner = Guid.NewGuid(); var coach = Guid.NewGuid(); var client = Guid.NewGuid();
        db.Clients.Add(new() { ClientId = client, ClientName = "Client" });
        db.UserClientAccess.Add(new() { UserClientAccessId = Guid.NewGuid(), UserId = owner, ClientId = client, AccessRoleCode = "OWNER" });
        var relationship = new CoachClientRelationship { CoachClientRelationshipId = Guid.NewGuid(), CoachUserId = coach, ClientId = client, StatusCode = "ACTIVE" };
        db.CoachClientRelationships.Add(relationship); await db.SaveChangesAsync();
        var service = new CoachAccountabilityPreviewService(db, new CoachCenterService(db), null!);
        Assert.True(await service.CanApproveAsync(owner, client, default));
        Assert.False(await service.CanApproveAsync(coach, client, default));
        Assert.True(await service.CanViewAsync(coach, client, true, default));
        Assert.False(await service.CanViewAsync(coach, client, false, default));
        Assert.False(await service.CanViewAsync(Guid.NewGuid(), client, true, default));
        relationship.RevokedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync();
        Assert.False(await service.CanApproveAsync(owner, client, default));
        Assert.False(await service.CanViewAsync(coach, client, true, default));
    }
    [Fact]
    public async Task SnapExerciseChoicesExcludeOtherClientsPrivateAndInactiveExercises()
    {
        using var db = new RecompDbContext(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var client = Guid.NewGuid(); var other = Guid.NewGuid();
        ExerciseCatalogItemRecord Exercise(string name, string source, string external, bool active = true) => new() {
            ExerciseCatalogItemId = Guid.NewGuid(), Name = name, SourceCode = source, SourceExternalId = external, IsActive = active, MeasurementType = "REPS" };
        db.ExerciseCatalogItems.AddRange(Exercise("Global press", "GLOBAL", "global"),
            Exercise("Own press", "12WR_CSV_UNMAPPED", client.ToString("N") + ":own"),
            Exercise("Other private press", "12WR_CSV_UNMAPPED", other.ToString("N") + ":private"), Exercise("Inactive press", "GLOBAL", "inactive", false));
        await db.SaveChangesAsync();
        var service = new CoachAccountabilityPreviewService(db, new CoachCenterService(db), null!);
        var choices = System.Text.Json.JsonSerializer.SerializeToElement(await service.ExercisesAsync(client, "press", default));
        var names = choices.EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToArray();
        Assert.Equal(new[] { "Global press", "Own press" }, names);
    }

    [Fact]
    public void PreviewGateIsTestOnly()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WEBSITE_SITE_NAME"] = "recomp-live" }).Build();
        Assert.False(AccountabilityPreview.Enabled(config)); config["WEBSITE_SITE_NAME"] = "recomp-test"; Assert.True(AccountabilityPreview.Enabled(config));
    }
    private sealed class PreviewClock : TimeProvider { public DateTimeOffset Now { get; set; } = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
}
