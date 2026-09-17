using WebRecomp.NutritionIntelligence.Models;

namespace WebRecomp.NutritionIntelligence.Contracts;

/// <summary>
/// Provider-neutral food composition boundary.
/// Nutrition Intelligence depends on this interface, not on a specific database.
/// </summary>
public interface IFoodCompositionProvider
{
    string ProviderCode { get; }

    Task<FoodSearchPage> SearchAsync(
        FoodSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<FoodComposition> GetFoodAsync(
        string sourceFoodId,
        CancellationToken cancellationToken = default);
}


/// <summary>
/// Optional provider capability used when a provider permits short-lived
/// cached payloads. The Web layer owns persistence and expiry; the provider
/// only knows how to reconstruct its own FoodComposition from the cached JSON.
/// </summary>
public interface ICachedFoodCompositionParser
{
    FoodComposition ParseCachedFood(string rawSourceJson);
}
