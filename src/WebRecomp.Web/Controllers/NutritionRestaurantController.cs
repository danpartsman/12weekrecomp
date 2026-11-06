using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.NutritionIntelligence.Models;
using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.NutritionIntelligence.Providers.FatSecret;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

[Authorize, Route("nutrition/restaurants"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NutritionRestaurantController(AppUserContextService users, INutritionFoodLogStore store,
    FatSecretFoodProvider provider, ILogger<NutritionRestaurantController> logger) : Controller
{
    private async Task<bool> Allowed(CancellationToken ct)
    {
        var context = await users.GetAsync(HttpContext, ct);
        return context.Client is not null && context.Program?.NutritionFoodLogEnabled == true
            && (await store.GetProviderPolicyAsync(NutritionProviderCodes.FatSecret, ct))?.IsEnabled == true;
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? q, CancellationToken ct)
    {
        if (!await Allowed(ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(q)) return Json(new { restaurants = Array.Empty<string>() });
        if (q.Length > 100) return BadRequest(new { message = "Use up to 100 characters for the restaurant name." });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try { return Json(new { restaurants = await provider.SearchRestaurantsAsync(q, timeout.Token) }); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogWarning(error, "FatSecret restaurant directory lookup failed");
            return StatusCode(503, new { message = "Restaurant lookup is unavailable. Try again, or check the FatSecret connection and restaurant access." });
        }
    }

    [HttpGet("menu")]
    public async Task<IActionResult> Menu(string? restaurant, string? q, int page = 1, CancellationToken ct = default)
    {
        if (!await Allowed(ct)) return Forbid();
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(restaurant) || restaurant.Length > 100 || (q?.Length ?? 0) > 100 || page is < 1 or > 2000)
            return BadRequest(new { message = "Choose a restaurant and a valid menu page." });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            var result = await provider.RestaurantMenuAsync(restaurant, q, page, timeout.Token);
            return Json(new { items = result.Foods.Select(MenuItem), nextPage = result.NextPage });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogWarning(error, "FatSecret restaurant menu lookup failed");
            return StatusCode(503, new { message = "Menu items could not be loaded. Try again." });
        }
    }

    public static object MenuItem(FoodCatalogItem food)
    {
        var serving = food.Measures?.FirstOrDefault();
        var factor = serving?.BasisQuantityPerSelection ?? food.ServingSize ?? food.NutrientBasisAmount;
        factor /= food.NutrientBasisAmount > 0 ? food.NutrientBasisAmount : 1;
        decimal? Macro(string code) => food.Nutrients.FirstOrDefault(x => x.CanonicalCode == code)?.Amount is decimal value
            ? Math.Round(value * factor, 1) : null;
        return new { providerCode = food.ProviderCode, sourceFoodId = food.SourceFoodId, description = food.Description,
            restaurant = food.BrandOwner, serving = serving?.Description ?? food.HouseholdServingText ?? $"{food.NutrientBasisAmount} {food.NutrientBasisUnit}",
            energyKcal = Macro(CanonicalNutrientCodes.EnergyKcal), proteinG = Macro(CanonicalNutrientCodes.ProteinG),
            carbohydrateG = Macro(CanonicalNutrientCodes.CarbohydrateG), fatG = Macro(CanonicalNutrientCodes.FatG) };
    }
}
