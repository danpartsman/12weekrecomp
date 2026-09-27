using System.Runtime.CompilerServices;

namespace WebRecomp.Web.Services.WorkoutIntelligence;

/// <summary>
/// Publishes the verified dated OTF template in the existing System Workouts -> Orangetheory list.
/// The underlying ORANGETHEORY recipe intentionally remains exercise-row neutral when the
/// public source does not expose exact movements/reps; Engine 3.0 still uses its calibrated
/// Orangetheory composite physiology instead of inventing workload.
/// </summary>
internal static class OtfDatedWorkoutCatalogBootstrap
{
    [ModuleInitializer]
    internal static void Register()
    {
        // Presentation is materialized as an array by SystemWorkoutRecipeCatalog. Mutating the
        // existing Orangetheory presentation entry keeps its stable SystemCode/engine mapping,
        // so CopySystemTemplate continues through the normal tested path while the UI exposes
        // the dated workout the user can actually select.
        if (SystemWorkoutRecipeCatalog.PresentationRecipes is not SystemWorkoutRecipe[] recipes)
            return;

        for (var i = 0; i < recipes.Length; i++)
        {
            if (!recipes[i].SystemCode.Equals("ORANGETHEORY", StringComparison.OrdinalIgnoreCase))
                continue;

            recipes[i] = recipes[i] with
            {
                DisplayName = "Orangetheory 2G — Sep 27, 2026",
                Description = "September 27, 2026 Orangetheory 2G system workout. Exact exercise rows are added only when verified source workload is available; 12WR does not invent reps, sets, distances, or timed work."
            };
            break;
        }
    }
}
