using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Coaching;
namespace WebRecomp.Web.Tests;
public sealed class CoachPortalToolsTests
{
    private static RecompDbContext Db(string name) => new(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(name).Options);
    [Fact]
    public async Task DefaultsPersistWithoutRewritingAnExistingAgreement()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var protection = new EphemeralDataProtectionProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CoachDefaults:DataPath"] = directory }).Build();
        try
        {
            var store = new AccountabilityDefaultsStore(new EnvironmentStub(), config, protection);
            var defaults = await store.ReadAsync(default); var stale = await store.ReadAsync(default);
            defaults.Rules[0].Amount = 3.25m; defaults.Rules[0].Min = 1.5m; defaults.Rules[0].Max = 55;
            defaults.SnapRate = .27m; defaults.SnapCap = 75; defaults.LateCap = 12;
            await store.SaveAsync(defaults, default);
            // A fresh store instance reads the durable data; simultaneous admin edits cannot overwrite it silently.
            var fresh = new AccountabilityDefaultsStore(new EnvironmentStub(), config, protection);
            var saved = await fresh.ReadAsync(default);
            Assert.Equal(3.25m, saved.Rules[0].Amount); Assert.Equal(55, saved.Rules[0].Max); Assert.Equal(.27m, saved.SnapRate);
            Assert.Equal(75, saved.SnapCap); Assert.Equal(12, saved.LateCap);
            await Assert.ThrowsAsync<ArgumentException>(() => fresh.SaveAsync(stale, default));
            Assert.DoesNotContain("SnapCap", await File.ReadAllTextAsync(Path.Combine(directory, "defaults.json")));
            using var fixture = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/accountability-js-document.json")));
            var existing = AccountabilityDocument.Parse(fixture.RootElement.GetProperty("state").GetRawText());
            // A new default does not invalidate or change a pre-existing client document.
            AccountabilityDocument.ValidateTerms(existing, fixture.RootElement.GetProperty("terms").GetString()!);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class EnvironmentStub : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "WebRecomp.Web";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
    [Fact]
    public void DefaultsRejectInvertedLimitsNegativeAmountsAndUnsupportedRules()
    {
        var d = new AccountabilityDefaults(); d.Rules[0].Min = 101;
        Assert.Throws<ArgumentException>(d.Validate); d.Rules[0].Min = 0; d.SnapRate = 100.01m;
        Assert.Throws<ArgumentException>(d.Validate); d.SnapRate = .1m; d.Rules[8].Amount = 0;
        Assert.Throws<ArgumentException>(d.Validate); d.Rules[8].Amount = 2; d.LateCap = -1;
        Assert.Throws<ArgumentException>(d.Validate); d.LateCap = 100; d.Rules.RemoveAt(0);
        Assert.Throws<ArgumentException>(d.Validate);
    }
    [Fact]
    public async Task LibraryIsCoachScopedDeduplicatesVersionsAndRetainsRemovedAssignments()
    {
        using var db = Db(Guid.NewGuid().ToString()); var coach = Guid.NewGuid(); var other = Guid.NewGuid(); var client = Guid.NewGuid(); var exercise = Guid.NewGuid();
        db.Clients.Add(new() { ClientId = client, ClientName = "Client", ClientStatusCode = "ACTIVE" });
        db.CoachClientRelationships.Add(new() { CoachClientRelationshipId = Guid.NewGuid(), ClientId = client, CoachUserId = coach, StatusCode = "ACTIVE" });
        db.ExerciseCatalogItems.Add(new() { ExerciseCatalogItemId = exercise, Name = "Curl", SourceCode = "TEST", SourceExternalId = "curl", IsActive = true, MeasurementType = "REPS" });
        var state = State(exercise); var stamp = DateTime.UtcNow;
        foreach (var (actor, offset) in new[] { (coach, 0), (coach, 1), (other, 2) })
        {
            state.SnapWorkouts[0].Id = "assignment-" + offset;
            db.CoachAccountabilityEvents.Add(new() { EventId = Guid.NewGuid(), ClientId = client, ActorUserId = actor, Kind = "CONFIG", CreatedAtUtc = stamp.AddMinutes(offset), PayloadJson = AccountabilityDocument.Serialize(state) });
        }
        // A later save removes the assignment from active work; immutable past work stays reusable.
        db.CoachAccountabilityEvents.Add(new() { EventId = Guid.NewGuid(), ClientId = client, ActorUserId = coach, Kind = "CONFIG", CreatedAtUtc = stamp.AddMinutes(3), PayloadJson = AccountabilityDocument.Serialize(State(exercise, false)) });
        await db.SaveChangesAsync(); var library = new SnapWorkoutLibrary(db, new CoachCenterService(db));
        var templates = await library.ReadAsync(coach, client, default);
        var template = Assert.Single(templates); Assert.Equal("Curl session", template.Name); Assert.Equal("", template.Id); Assert.Null(template.DeadlineUtc); Assert.Equal("", template.Date);
        Assert.Equal(.25m, template.Exercises[0].Rate);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => library.ReadAsync(other, client, default));
        db.ExerciseCatalogItems.Single().IsActive = false; await db.SaveChangesAsync();
        Assert.Empty(await library.ReadAsync(coach, client, default));
    }
    [Fact]
    public void NoticeRemainsForDraftOrIncompleteWorkAndClearsForCompletedTargets()
    {
        var workout = State(Guid.NewGuid()).SnapWorkouts[0];
        var result = new AccountabilityResult { Finalized = false, Sets = new() { ["e1"] = [new() { Reps = 10, Weight = 20 }, new() { Reps = 10, Weight = 20 }] } };
        Assert.False(SnapWorkoutLibrary.Completed(workout, null)); Assert.False(SnapWorkoutLibrary.Completed(workout, result));
        result.Finalized = true; Assert.True(SnapWorkoutLibrary.Completed(workout, result));
        result.Sets["e1"][0].Reps = 9; Assert.False(SnapWorkoutLibrary.Completed(workout, result));
        result.Sets["e1"][0].Reps = 12; result.Sets["e1"][0].Weight = 19; Assert.False(SnapWorkoutLibrary.Completed(workout, result));
    }
    private static AccountabilityState State(Guid exercise, bool assigned = true) => new()
    {
        Agreement = new() { Start = "2026-10-01" }, Rules = AccountabilityDocument.RuleIds.Select(id => new AccountabilityRule { Id = id, Amount = 2 }).ToList(),
        SnapWorkouts = assigned ? [new() { Id = "w1", Name = "Curl session", Date = "2026-10-27", Cap = 50,
            Exercises = [new() { Id = "e1", CatalogId = exercise.ToString(), Name = "Curl", Sets = 2, Reps = 10, Weight = 20, Rate = .25m }] }] : []
    };
}
