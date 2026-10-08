using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Learning;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class LearningCoachModuleTests
{
    [Fact]
    public void CoachCatalog_HasFourPersistentCoachChoices_WithFiveGallerySlotsEach()
    {
        Assert.Equal(4, CoachCatalog.All.Count);

        Assert.Equal(
            new[] { "MARISOL", "CASSIE", "MARCUS", "NINA" },
            CoachCatalog.All.Select(x => x.Code).ToArray());

        Assert.All(
            CoachCatalog.All,
            coach => Assert.Equal(5, coach.GalleryImages.Count));
    }

    [Theory]
    [InlineData("DASHBOARD", "AEROBIC_ENERGY", 8)]
    [InlineData("NUTRITION", "FOOD_AND_FUEL", 8)]
    [InlineData("WORKOUT", "TRAINING_ADAPTATION", 8)]
    public void LearningSeries_AreContiguousAndSurfaceSpecific(
        string surface,
        string series,
        int expectedModules)
    {
        var lessons =
            LearningContentCatalog.ForSurface(surface)
                .Where(x => x.SeriesCode == series)
                .OrderBy(x => x.ModuleNumber)
                .ToArray();

        Assert.Equal(expectedModules, lessons.Length);

        Assert.Equal(
            Enumerable.Range(1, expectedModules).ToArray(),
            lessons.Select(x => x.ModuleNumber).ToArray());

        Assert.All(
            lessons,
            lesson => Assert.Equal(expectedModules, lesson.TotalModules));
    }

    [Fact]
    public void CoreSurfaces_ContainLearningCoachComponents()
    {
        var root = FindRepoRoot();

        var dashboard =
            File.ReadAllText(Path.Combine(root, "src", "WebRecomp.Web", "Views", "Dashboard", "Index.cshtml"));

        var nutrition =
            File.ReadAllText(Path.Combine(root, "src", "WebRecomp.Web", "Views", "Nutrition", "Index.cshtml"));

        var workout =
            File.ReadAllText(Path.Combine(root, "src", "WebRecomp.Web", "Views", "DetailedWorkout", "Index.cshtml"));

        Assert.Contains("surfaceCode = \"DASHBOARD\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("displayMode = \"full\"", dashboard, StringComparison.Ordinal);

        Assert.Contains("surfaceCode = \"NUTRITION\"", nutrition, StringComparison.Ordinal);
        Assert.Contains("displayMode = \"thumbnail\"", nutrition, StringComparison.Ordinal);

        Assert.Contains("surfaceCode = \"WORKOUT\"", workout, StringComparison.Ordinal);
        Assert.Contains("displayMode = \"thumbnail\"", workout, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_AddsPersistentCoachAndLearningState()
    {
        var root = FindRepoRoot();

        var sql =
            File.ReadAllText(Path.Combine(root, "database", "043_v1015c_learning_coach_foundation.sql"));

        Assert.Contains("preferred_coach_code", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LearningSeriesProgress", sql, StringComparison.Ordinal);
        Assert.Contains("LearningInteractions", sql, StringComparison.Ordinal);
    }


    [Theory]
    [InlineData(typeof(LearningCoachPreference), "LearningCoachPreferences", "UserId", "user_id")]
    [InlineData(typeof(LearningSeriesProgress), "LearningSeriesProgress", "LearningSeriesProgressId", "learning_series_progress_id")]
    [InlineData(typeof(LearningInteraction), "LearningInteractions", "LearningInteractionId", "learning_interaction_id")]
    public void SqlServerModel_ValidatesLearningEntitiesAgainstPersistentSchema(
        Type entityType,
        string tableName,
        string keyProperty,
        string keyColumn)
    {
        // Accessing Model runs the same provider validation used at startup,
        // without opening a database connection.
        var options = new DbContextOptionsBuilder<RecompDbContext>()
            .UseSqlServer("Server=localhost;Database=ModelValidation;Integrated Security=true;TrustServerCertificate=true")
            .Options;

        using var db = new RecompDbContext(options);
        var entity = db.Model.FindEntityType(entityType);
        Assert.NotNull(entity);
        Assert.Equal("app", entity.GetSchema());
        Assert.Equal(tableName, entity.GetTableName());

        var key = entity.FindPrimaryKey();
        Assert.NotNull(key);
        var property = Assert.Single(key.Properties);
        Assert.Equal(keyProperty, property.Name);
        Assert.Equal(keyColumn, property.GetColumnName(StoreObjectIdentifier.Table(tableName, "app")));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "WebRecomp.Web", "WebRecomp.Web.csproj")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
