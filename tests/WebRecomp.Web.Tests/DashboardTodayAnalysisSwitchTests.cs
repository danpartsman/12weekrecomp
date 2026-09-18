using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Tests;

public sealed class DashboardTodayAnalysisSwitchTests
{
    [Fact]
    public void NoCurrentActualData_ShowsYesterday()
    {
        var model = new DashboardViewModel();

        Assert.False(model.ShowTodayAnalysis);
    }

    [Fact]
    public void NutritionLogged_ShowsToday()
    {
        var model =
            new DashboardViewModel
            {
                NutritionActualLogged = true
            };

        Assert.True(model.ShowTodayAnalysis);
    }

    [Fact]
    public void CompletedWorkoutLogged_ShowsToday()
    {
        var model =
            new DashboardViewModel
            {
                ActualWorkoutDurationMin = 45m
            };

        Assert.True(model.ShowTodayAnalysis);
    }

    [Fact]
    public void DashboardView_PreservesTimingAndNullFixContracts()
    {
        var repoRoot = FindRepoRoot();

        var viewPath =
            Path.Combine(
                repoRoot,
                "src",
                "WebRecomp.Web",
                "Views",
                "Dashboard",
                "Index.cshtml");

        var source = File.ReadAllText(viewPath);

        Assert.Contains(
            "V1009_DASHBOARD_TODAY_SWITCH_V2",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "<span class=\"dash-cell-label\">TODAY</span>",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "<span class=\"dash-cell-label\">YESTERDAY</span>",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "<strong>Morning scale response</strong>",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "This morning's scale response still belongs to yesterday's training and nutrition.",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "dash-coach-guide-card",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "coach-today-detail.js",
            source,
            StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate =
                Path.Combine(
                    current.FullName,
                    "src",
                    "WebRecomp.Web",
                    "Views",
                    "Dashboard",
                    "Index.cshtml");

            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate repository root.");
    }
}