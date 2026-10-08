using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class DashboardCoachResponsiveLayoutTests
{
    [Fact]
    public void CoachHeadline_IsAllowedToWrapInsidePrescriptionPanel()
    {
        var root = FindRepoRoot();
        var css = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "wwwroot",
                "css",
                "journey-v1012-pass10-opaque.css"));

        Assert.Contains(
            ".journey-coach-p8__title",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            "white-space: normal !important",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            "font-size: clamp(2.7rem, 3.75vw, 4.25rem) !important",
            css,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MobileCoachCard_IsContainedHorizontalSwipeSurface()
    {
        var root = FindRepoRoot();
        var css = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "wwwroot",
                "css",
                "journey-v1012-pass10-opaque.css"));

        Assert.Contains(
            "overflow-x: auto !important",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            "width: 980px !important",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            "touch-action: pan-x pan-y",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            "grid-template-columns: repeat(2, minmax(0, 1fr)) !important",
            css,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Pass10_InitializesUsefulMobileSwipePosition_AndOwnsArtwork()
    {
        var root = FindRepoRoot();

        var pass10 = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "wwwroot",
                "js",
                "journey-v1012-pass10.js"));

        var vibrance = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "wwwroot",
                "js",
                "test-live-vibrance.js"));

        Assert.Contains(
            "journey-coach-horizontal-swipe",
            pass10,
            StringComparison.Ordinal);

        Assert.Contains(
            "scrollHost.scrollLeft",
            pass10,
            StringComparison.Ordinal);

        Assert.Contains(
            "journey-coach-horizontal-swipe",
            vibrance,
            StringComparison.Ordinal);
    }


    [Fact]
    public void MobileCoachSwipe_KeepsMarisolArtworkVisible()
    {
        var root = FindRepoRoot();

        var pass10 = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "wwwroot",
                "js",
                "journey-v1012-pass10.js"));

        var css = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "wwwroot",
                "css",
                "journey-v1012-pass10-opaque.css"));

        Assert.Contains(
            "marisol-coach-avatar.webp",
            pass10,
            StringComparison.Ordinal);

        Assert.Contains(
            "document.createElement(\"img\")",
            pass10,
            StringComparison.Ordinal);

        Assert.Contains(
            "mobileMarisol.src",
            pass10,
            StringComparison.Ordinal);

        Assert.Contains(
            "img.journey-coach-mobile-marisol",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            "object-fit: cover !important",
            css,
            StringComparison.Ordinal);

        Assert.Contains(
            "scrollHost.scrollLeft = 0",
            pass10,
            StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        current.FullName,
                        "src",
                        "WebRecomp.Web",
                        "WebRecomp.Web.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the 12WR repository root.");
    }
}
