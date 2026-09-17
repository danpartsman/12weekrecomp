using Xunit;

namespace WebRecomp.Web.Tests;

/// <summary>
/// 12WR v1009 adaptive recommendation layer source-wiring regression.
/// </summary>
public sealed class Engine30AdaptiveRecommendationDashboardWiringTests
{
    [Fact]
    public void DashboardIsWiredToAdaptiveRecommendationLayer()
    {
        var root =
            FindRepoRoot();

        var program =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "src",
                    "WebRecomp.Web",
                    "Program.cs"));

        var service =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "src",
                    "WebRecomp.Web",
                    "Services",
                    "DashboardService.cs"));

        var model =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "src",
                    "WebRecomp.Web",
                    "ViewModels",
                    "AppViewModels.cs"));

        var view =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "src",
                    "WebRecomp.Web",
                    "Views",
                    "Dashboard",
                    "Index.cshtml"));

        Assert.Contains(
            "AddScoped<Engine30AdaptiveRecommendationService>()",
            program,
            StringComparison.Ordinal);

        Assert.Contains(
            "Engine30AdaptiveRecommendationService adaptiveRecommendations",
            service,
            StringComparison.Ordinal);

        Assert.Contains(
            "adaptiveRecommendations.Evaluate",
            service,
            StringComparison.Ordinal);

        Assert.Contains(
            "PreviousEnergyVarianceKcal:",
            service,
            StringComparison.Ordinal);

        Assert.Contains(
            "todayCheckin?.PainFlag",
            service,
            StringComparison.Ordinal);

        Assert.Contains(
            "Engine30AdaptiveRecommendation = engine30Adaptive",
            service,
            StringComparison.Ordinal);

        Assert.Contains(
            "Engine30AdaptiveRecommendationResult? Engine30AdaptiveRecommendation",
            model,
            StringComparison.Ordinal);

        Assert.Contains(
            "ENGINE 3.0 &bull; ADAPTIVE RECOMMENDATION",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "Weekly Plan remains unchanged",
            view,
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
                    "WebRecomp.Web.csproj");

            if (File.Exists(candidate))
                return current.FullName;

            current =
                current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the 12WR repository root.");
    }
}