using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30DashboardWorkoutMetricsTests
{
    [Fact]
    public void DayProjection_ExposesGrossWorkoutEnergy()
    {
        var property =
            typeof(Engine30DashboardDayProjection)
                .GetProperty("GrossWorkoutEnergyKcal");

        Assert.NotNull(property);
        Assert.Equal(
            typeof(decimal),
            property!.PropertyType);
    }

    [Fact]
    public void ActualWorkoutProjection_ExposesCaloriesAndGlycogen()
    {
        var type =
            typeof(Engine30DashboardActualWorkoutProjection);

        Assert.NotNull(
            type.GetProperty("GrossWorkoutEnergyKcal"));

        Assert.NotNull(
            type.GetProperty("NetWorkoutEnergyKcal"));

        Assert.NotNull(
            type.GetProperty("GlycogenDemandG"));

        Assert.NotNull(
            type.GetProperty("TotalDurationMinutes"));

        Assert.NotNull(
            type.GetProperty("DurationWeightedRpe"));
    }
}
