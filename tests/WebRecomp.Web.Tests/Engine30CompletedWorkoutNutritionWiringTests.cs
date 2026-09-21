using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30CompletedWorkoutNutritionWiringTests
{
    [Fact]
    public void DashboardProjection_ActualizesNutritionFromCompletedWorkoutPhysiology()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "Services",
                "Engine30",
                "Engine30DashboardProjectionService.cs"));

        Assert.Contains("B2.35.33 COMPLETED-WORKOUT NUTRITION ACTUALIZATION", source, StringComparison.Ordinal);
        Assert.Contains("completedByDate.TryGetValue", source, StringComparison.Ordinal);
        Assert.Contains("BuildActualizedPrescriptionDayInputAsync", source, StringComparison.Ordinal);
        Assert.Contains("actualized ?? plannedInput", source, StringComparison.Ordinal);
        Assert.Contains("actualNetWorkoutEnergyKcal", source, StringComparison.Ordinal);
        Assert.Contains("actualGlycogenReloadRequirementG", source, StringComparison.Ordinal);
        Assert.Contains("plannedRuntime.Energy.BaseLivingEnergyKcal", source, StringComparison.Ordinal);
        Assert.Contains("plannedRuntime.Energy.HabitualActivityEnergyKcal", source, StringComparison.Ordinal);
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
