using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class CompletedWorkoutCrossSurfaceWiringTests
{
    [Fact]
    public void CurrentWorkoutSavePathsUseCanonicalActualService()
    {
        var root = FindRepoRoot();

        var workoutsController = Read(root, "Controllers", "WorkoutsController.cs");
        var dailyController = Read(root, "Controllers", "DailyEntryController.cs");
        var detailedController = Read(root, "Controllers", "DetailedWorkoutController.cs");
        var dashboardService = Read(root, "Services", "DashboardService.cs");
        var projectionService = Read(root, "Services", "Engine30", "Engine30DashboardProjectionService.cs");

        Assert.Contains(
            "WorkoutActualService workoutActuals",
            workoutsController,
            StringComparison.Ordinal);

        Assert.Contains(
            "WorkoutActualService workoutActuals",
            dailyController,
            StringComparison.Ordinal);

        Assert.Contains(
            "WorkoutActualService workoutActuals",
            detailedController,
            StringComparison.Ordinal);

        Assert.Contains(
            "WorkoutActualService workoutActuals",
            dashboardService,
            StringComparison.Ordinal);

        Assert.Contains(
            "workoutActuals.ApplyCurrentEngineState",
            workoutsController,
            StringComparison.Ordinal);

        Assert.Contains(
            "workoutActuals.ApplyCurrentEngineState",
            dailyController,
            StringComparison.Ordinal);

        Assert.Contains(
            "workoutActuals.ApplyCurrentEngineState",
            detailedController,
            StringComparison.Ordinal);

        Assert.Contains(
            "workoutActuals.GetCompletedSessionsAsync",
            dailyController,
            StringComparison.Ordinal);

        Assert.Contains(
            "workoutActuals.GetCompletedSessionsAsync",
            dashboardService,
            StringComparison.Ordinal);

        Assert.Contains(
            "CanonicalCompletedSessionsAsync",
            projectionService,
            StringComparison.Ordinal);

        // Completed sessions may alter the nutrition inputs, but the day
        // projection must continue to describe the saved planned workout.
        Assert.Contains(
            "item.WorkoutCode",
            projectionService,
            StringComparison.Ordinal);

        Assert.Contains(
            "item.WorkoutCategoryCode",
            projectionService,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "WorkoutDisplayName:\n                        actualMetrics?.WorkoutSummary",
            projectionService,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "WorkoutCategoryCode:\n                        actualInput?.WorkoutCategoryCode ?? item.WorkoutCategoryCode",
            projectionService,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardKeepsPlannedWorkoutSeparateFromCanonicalActual()
    {
        var root = FindRepoRoot();
        var dashboardService = Read(root, "Services", "DashboardService.cs");

        Assert.Contains(
            "PlannedWorkout = engine30Projection.Today.HasPlannedWorkout ? engine30Projection.Today.WorkoutDisplayName : \"No workout planned\"",
            dashboardService,
            StringComparison.Ordinal);

        Assert.Contains(
            "WorkoutActual = engine30ActualWorkout.HasActualWorkout",
            dashboardService,
            StringComparison.Ordinal);

        Assert.Contains(
            "? engine30ActualWorkout.WorkoutSummary ?? workoutActual",
            dashboardService,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentWorkoutSavePathsDoNotRunEngine2CompatibilityMath()
    {
        var root = FindRepoRoot();

        var workoutsController = Read(root, "Controllers", "WorkoutsController.cs");
        var dailyController = Read(root, "Controllers", "DailyEntryController.cs");

        Assert.DoesNotContain(
            "CalculateActualWorkout",
            workoutsController,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "ToLegacyCompatibilityCode",
            workoutsController,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "CalculateActualWorkout",
            dailyController,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "ToLegacyCompatibilityCode",
            dailyController,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "CalculatedPushLoad =",
            workoutsController,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "CalculatedPushLoad =",
            dailyController,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalStateClearsStaleEngine2DerivedValues()
    {
        var service = new WorkoutActualService(null!);

        var session = new WorkoutSession
        {
            WorkoutSessionId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            WorkoutDate = new DateOnly(2026, 10, 5),
            WorkoutTypeCode = "Z2_40_RPE4",
            DurationMin = 35m,
            SessionRpe = 4m,
            CompletedFlag = false,
            CalculatedWorkoutCalories = 999m,
            CalculatedScaledGlycogenBurnG = 999m,
            CalculatedPushLoad = 9m,
            CalculatedPullLoad = 9m,
            CalculatedLegLoad = 9m,
            CalculatedCardioLoad = 9m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        service.ApplyCurrentEngineState(
            session,
            completed: true);

        Assert.Equal("ZONE2_CARDIO", session.WorkoutTypeCode);
        Assert.True(session.CompletedFlag);
        Assert.Equal("3000.0", session.CalculationEngineVersion);

        Assert.Null(session.CalculatedWorkoutCalories);
        Assert.Null(session.CalculatedScaledGlycogenBurnG);
        Assert.Null(session.CalculatedPushLoad);
        Assert.Null(session.CalculatedPullLoad);
        Assert.Null(session.CalculatedLegLoad);
        Assert.Null(session.CalculatedCardioLoad);
    }

    [Fact]
    public void CompletedWorkoutSurfacesExplainTheSharedActual()
    {
        var root = FindRepoRoot();

        var dailyView = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "Views",
                "DailyEntry",
                "Index.cshtml"));

        var dashboardView = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "Views",
                "Dashboard",
                "Index.cshtml"));

        var completeView = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "Views",
                "DetailedWorkout",
                "Complete.cshtml"));

        Assert.Contains(
            "Completed workout synced from Workouts",
            dailyView,
            StringComparison.Ordinal);

        Assert.Contains(
            "COMPLETED WORKOUT",
            dashboardView,
            StringComparison.Ordinal);

        Assert.Contains(
            "Daily Tracking and Dashboard updated",
            completeView,
            StringComparison.Ordinal);
    }

    private static string Read(
        string root,
        params string[] parts)
        => File.ReadAllText(
            Path.Combine(
                new[]
                {
                    root,
                    "src",
                    "WebRecomp.Web"
                }
                .Concat(parts)
                .ToArray()));

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "src",
                "WebRecomp.Web",
                "WebRecomp.Web.csproj");

            if (File.Exists(candidate))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the 12WR repository root.");
    }
}
