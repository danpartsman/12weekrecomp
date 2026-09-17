using WebRecomp.NutritionIntelligence.Contracts;
using WebRecomp.Web.Controllers;

namespace WebRecomp.Web.Tests;

public sealed class NutritionIntelligenceCompositionBoundaryTests
{
    [Fact]
    public void Web_Composes_NutritionIntelligence()
    {
        var webReferences =
            typeof(NutritionController)
                .Assembly
                .GetReferencedAssemblies()
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("WebRecomp.NutritionIntelligence", webReferences);
    }

    [Fact]
    public void NutritionIntelligence_DoesNotReference_WebOrWorkout()
    {
        var nutritionReferences =
            typeof(IFoodCompositionProvider)
                .Assembly
                .GetReferencedAssemblies()
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("WebRecomp.Web", nutritionReferences);
        Assert.DoesNotContain("WebRecomp.WorkoutIntelligence", nutritionReferences);
    }
}
