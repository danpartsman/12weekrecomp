using System.Text.RegularExpressions;

namespace WebRecomp.Web.Tests;

public sealed class DashboardTomorrowCoachGuideViewTests
{
    [Fact]
    public void TomorrowCoachGuideBlock_DoesNotDereferenceTodayCoachGuide()
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

        Assert.Contains(
            "@if (tomorrowCoachGuide is not null)",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "src=\"@Url.Content(tomorrowCoachGuide.AvatarPath)\"",
            source,
            StringComparison.Ordinal);

        var match =
            Regex.Match(
                source,
                @"@if\s*\(tomorrowCoachGuide is not null\).*?Preview tomorrow's workout \+ nutrition guide",
                RegexOptions.Singleline);

        Assert.True(
            match.Success,
            "Could not isolate the tomorrow coach-guide card.");

        Assert.DoesNotContain(
            "todayCoachGuide.",
            match.Value,
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