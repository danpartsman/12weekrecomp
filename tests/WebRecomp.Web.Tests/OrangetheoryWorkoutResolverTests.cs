using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.WorkoutIntelligence;

namespace WebRecomp.Web.Tests;

public sealed class OrangetheoryWorkoutResolverTests
{
    private static readonly DateOnly Date = new(2026, 10, 31);
    private static OtfPublishedWorkout Workout() => new("2026-10-31", "S50", true,
        "https://www.reddit.com/r/orangetheory/comments/test/strength_50/", "upper",
        [new("Unfamiliar Chest Press", 3, 8, null, "Floor 1", "8 reps; repeat until time; 3 rounds estimated", true, ["chest"], ["triceps"])]);

    [Fact]
    public async Task SelectionCachesDatedLookupAndPreservesCanonicalEngineTypeAndSource()
    {
        var source = new Source { Result = Workout() };
        var resolver = Resolver(source);
        var recipe = await resolver.ResolveAsync("SYS_OTF_STRENGTH_50", Date, true, default);
        var again = await resolver.ResolveAsync("SYS_OTF_STRENGTH_50", Date, true, default);
        Assert.Same(recipe, again); Assert.Equal(1, source.Calls);
        Assert.Equal("RESISTANCE_CIRCUIT", recipe!.EngineWorkoutTypeCode);
        Assert.Equal(50m, recipe.PlannedMinutes); Assert.Equal(Workout().SourceUrl, recipe.SourceUrl);
        Assert.Equal(["chest"], recipe.Slots.Single().MuscleTerms);
        Assert.Equal(["triceps"], recipe.Slots.Single().SecondaryMuscleTerms!);
        Assert.True(recipe.Slots.Single().IsEstimated);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DisabledMissingOrFailingLookupStillGivesUsableGeneric(bool automatic, bool fail)
    {
        var source = new Source { Fail = fail };
        var recipe = await Resolver(source).ResolveAsync("ORANGETHEORY", Date, automatic, default);
        Assert.Equal("ORANGETHEORY", recipe!.EngineWorkoutTypeCode);
        Assert.Equal(60m, recipe.PlannedMinutes); Assert.Contains("Generic estimate", recipe.DisplayName);
        Assert.Empty(recipe.Slots); Assert.Null(recipe.SourceUrl);
        Assert.Equal(automatic ? 1 : 0, source.Calls);
    }

    [Fact]
    public async Task BuiltInDatedWorkoutUsesExactDateWithoutExternalCallAndOtherWorkoutsAreUntouched()
    {
        var source = new Source(); var resolver = Resolver(source);
        var recipe = await resolver.ResolveAsync("ORANGETHEORY", new(2026, 10, 16), true, default);
        Assert.Contains("2026-10-16", recipe!.DisplayName); Assert.NotEmpty(recipe.Slots);
        Assert.Null(await resolver.ResolveAsync("MAJOR_PUSH", Date, true, default)); Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void ValidationRejectsWrongDatesTypesMusclesAndUntrustedUrls()
    {
        var workout = Workout();
        Assert.True(OpenAiOrangetheoryWorkoutSource.IsValid(workout, Date, "S50"));
        Assert.False(OpenAiOrangetheoryWorkoutSource.IsValid(workout with { Date = "2026-10-30" }, Date, "S50"));
        Assert.False(OpenAiOrangetheoryWorkoutSource.IsValid(workout, Date, "2G"));
        Assert.False(OpenAiOrangetheoryWorkoutSource.IsValid(workout with { SourceUrl = "https://reddit.com.evil.test/a" }, Date, "S50"));
        Assert.False(OpenAiOrangetheoryWorkoutSource.IsValid(workout with { Exercises = [workout.Exercises[0] with { Sets = 900 }] }, Date, "S50"));
        Assert.False(OpenAiOrangetheoryWorkoutSource.IsValid(workout with { Exercises = [workout.Exercises[0] with { PrimaryMuscles = ["made-up-muscle"] }] }, Date, "S50"));
    }

    [Fact]
    public async Task ImportedMovementHasPersistentPrimaryAndSecondaryTargetsAndRepeatedSelectionsDoNotDuplicateThem()
    {
        using var db = new RecompDbContext(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var config = Guid.NewGuid(); var client = Guid.NewGuid();
        db.ConfigVersions.Add(new() { ConfigVersionId = config, VersionCode = "CFG-3000.0", StatusCode = "PUBLISHED" });
        db.WorkoutTypeDefinitions.Add(new() { WorkoutTypeDefinitionId = Guid.NewGuid(), ConfigVersionId = config,
            WorkoutTypeCode = "RESISTANCE_CIRCUIT", DisplayName = "Circuit", WorkoutCategoryCode = "STRENGTH",
            WeeklyRoleCode = "MODERATE", FuelPriorityCode = "MIXED", SchedulePrincipleCode = "STRENGTH", IsActive = true, IsSelectable = true, DefaultMinutes = 50 });
        await db.SaveChangesAsync();
        var recipe = OrangetheoryWorkoutResolver.Published("SYS_OTF_STRENGTH_50", Date, Workout());
        var preview = new SystemWorkoutPreviewService(db);
        var first = await preview.BuildAsync(client, config, recipe.SystemCode, "LB", default, recipeOverride: recipe);
        var second = await preview.BuildAsync(client, config, recipe.SystemCode, "LB", default, recipeOverride: recipe);
        Assert.Single(first!.Exercises); Assert.Single(second!.Exercises);
        var targets = await db.ExerciseMuscleTargets.ToListAsync();
        Assert.Equal(2, targets.Count); Assert.Contains(targets, x => x.MuscleSlug == "chest" && x.RoleCode == "primary");
        Assert.Contains(targets, x => x.MuscleSlug == "triceps" && x.RoleCode == "secondary");
        Assert.Equal("OTF_MAPPED_ESTIMATED", first.Exercises.Single().Mechanics);
        Assert.Contains("Source:", first.Exercises.Single().Notes);
    }

    private static OrangetheoryWorkoutResolver Resolver(Source source) => new(source, new MemoryCache(new MemoryCacheOptions()),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["OtfWorkouts:DataPath"] = Path.Combine(Path.GetTempPath(), "otf-tests", Guid.NewGuid().ToString("N")) }).Build(),
        new Host(), NullLogger<OrangetheoryWorkoutResolver>.Instance);
    private sealed class Source : IOrangetheoryWorkoutSource
    {
        public bool Ready => true;
        public int Calls; public bool Fail; public OtfPublishedWorkout? Result;
        public Task<OtfPublishedWorkout?> FindAsync(DateOnly date, string type, CancellationToken ct)
        { Calls++; if (Fail) throw new HttpRequestException("test failure"); return Task.FromResult(Result); }
    }
    private sealed class Host : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Test";
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
