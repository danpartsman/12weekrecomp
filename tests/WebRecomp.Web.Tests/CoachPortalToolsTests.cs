using Microsoft.EntityFrameworkCore;
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
        var name = Guid.NewGuid().ToString(); var client = Guid.NewGuid();
        using (var db = Db(name))
        {
            db.ConfigVersions.Add(new() { ConfigVersionId = Guid.NewGuid(), VersionCode = "CFG-3000.0", StatusCode = "ACTIVE" });
            db.CoachAccountabilityAgreements.Add(new() { ClientId = client, StateJson = "original saved terms", Revision = 9 });
            await db.SaveChangesAsync();
            var defaults = new AccountabilityDefaults(); defaults.Rules[0].Amount = 3.25m; defaults.Rules[0].Min = 1.5m; defaults.Rules[0].Max = 55;
            defaults.SnapRate = .27m; defaults.SnapCap = 75; defaults.LateCap = 12;
            await new AccountabilityDefaultsStore(db).SaveAsync(defaults, default);
        }
        using (var db = Db(name))
        {
            var saved = await new AccountabilityDefaultsStore(db).ReadAsync(default);
            Assert.Equal(3.25m, saved.Rules[0].Amount); Assert.Equal(55, saved.Rules[0].Max); Assert.Equal(.27m, saved.SnapRate);
            Assert.Equal(75, saved.SnapCap); Assert.Equal(12, saved.LateCap);
            Assert.Equal("original saved terms", (await db.CoachAccountabilityAgreements.SingleAsync()).StateJson);
        }
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
