using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class CompletedWorkoutCrossSurfaceWiringTests
{
    [Fact]
    public void CompletedWorkout_IsTheSharedActualForDailyTrackingAndDashboard()
    {
        var root = FindRepoRoot();

        var detailedController = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "Controllers",
                "DetailedWorkoutController.cs"));

        var dailyController = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "Controllers",
                "DailyEntryController.cs"));

        var dashboardService = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "Services",
                "DashboardService.cs"));

        Assert.Contains(
            "session.CompletedFlag = true;",
            detailedController,
            StringComparison.Ordinal);

        Assert.Contains(
            "RecompInputSourceCodes.DetailedWorkout",
            detailedController,
            StringComparison.Ordinal);

        Assert.Contains(
            "x.WorkoutDate == date",
            dailyController,
            StringComparison.Ordinal);

        Assert.Contains(
            "&& x.CompletedFlag",
            dailyController,
            StringComparison.Ordinal);

        Assert.Contains(
            "x.WorkoutDate == actionDate",
            dashboardService,
            StringComparison.Ordinal);

        Assert.Contains(
            "&& x.CompletedFlag",
            dashboardService,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompletedWorkout_SurfacesExplainTheSharedActual()
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
            "Dashboard updated",
            dailyView,
            StringComparison.Ordinal);

        Assert.Contains(
            "COMPLETED WORKOUT",
            dashboardView,
            StringComparison.Ordinal);

        Assert.Contains(
            "Synced from the completed WorkoutSession",
            dashboardView,
            StringComparison.Ordinal);

        Assert.Contains(
            "Daily Tracking and Dashboard updated",
            completeView,
            StringComparison.Ordinal);
    }

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
