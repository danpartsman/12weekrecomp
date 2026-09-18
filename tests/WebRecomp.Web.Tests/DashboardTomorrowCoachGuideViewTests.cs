namespace WebRecomp.Web.Tests;

public sealed class DashboardTomorrowCoachGuideViewTests
{
    [Fact]
    public void DashboardCoachGuideAttachmentCards_AreRetired()
    {
        var repoRoot =
            FindRepoRoot();

        var viewPath =
            Path.Combine(
                repoRoot,
                "src",
                "WebRecomp.Web",
                "Views",
                "Dashboard",
                "Index.cshtml");

        var source =
            File.ReadAllText(viewPath);

        Assert.DoesNotContain(
            "todayCoachGuide",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "tomorrowCoachGuide",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "dash-coach-guide-card",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "_CoachTodayDetail",
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

            current =
                current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the 12WR repository root.");
    }
}
