using Xunit;

namespace WebRecomp.Web.Tests;

/// <summary>
/// v1014A regression: the Dashboard consumes the engine-neutral recommendation
/// contract and Engine 3.0 is the active implementation.
/// </summary>
public sealed class Engine30AdaptiveRecommendationDashboardWiringTests
{
    [Fact]
    public void DashboardUsesNeutralRecommendationContractWithEngine30Implementation()
    {
        var root = FindRepoRoot();

        var program = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "Program.cs"));

        var service = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "Services",
                "DashboardService.cs"));

        var model = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "ViewModels",
                "AppViewModels.cs"));

        var view = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "Views",
                "Dashboard",
                "Index.cshtml"));

        Assert.Contains(
            "AddScoped<IRecompRecommendationEngine, Engine30RecompRecommendationEngine>()",
            program,
            StringComparison.Ordinal);

        Assert.Contains(
            "IRecompRecommendationEngine recommendationEngine",
            service,
            StringComparison.Ordinal);

        Assert.Contains(
            "recommendationEngine.Evaluate",
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
            "RecompRecommendation = recommendation",
            service,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "RecommendationService recommendationService",
            service,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "WorkoutMathService math",
            service,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Engine30LegacyWorkoutCompatibility",
            service,
            StringComparison.Ordinal);

        Assert.Contains(
            "RecompRecommendationResult? RecompRecommendation",
            model,
            StringComparison.Ordinal);

        Assert.Contains(
            "RECOMP ENGINE @adaptive.EngineVersionCode",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "Weekly Plan remains unchanged",
            view,
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
