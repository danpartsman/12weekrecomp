using WebRecomp.Web.Services.WorkoutIntelligence;
using WebRecomp.WorkoutIntelligence.Persistence;

namespace WebRecomp.Web.Tests;

public sealed class WorkoutIntelligencePersistenceBoundaryTests
{
    [Fact]
    public void SqlAdapters_ImplementWorkoutIntelligencePersistenceContracts()
    {
        Assert.True(typeof(IExerciseCatalogStore).IsAssignableFrom(typeof(SqlExerciseCatalogStore)));
        Assert.True(typeof(IDetailedWorkoutStore).IsAssignableFrom(typeof(SqlDetailedWorkoutStore)));
    }

    [Fact]
    public void PersistenceContracts_LiveInWorkoutIntelligenceAssembly()
    {
        Assert.Equal("WebRecomp.WorkoutIntelligence", typeof(IExerciseCatalogStore).Assembly.GetName().Name);
        Assert.Equal("WebRecomp.WorkoutIntelligence", typeof(IDetailedWorkoutStore).Assembly.GetName().Name);
    }
}
