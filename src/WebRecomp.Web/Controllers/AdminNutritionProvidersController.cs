using Microsoft.AspNetCore.Authorization;
using WebRecomp.Web.Services.Nutrition;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.NutritionIntelligence.Contracts;
using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin/nutrition-providers")]
public sealed class AdminNutritionProvidersController(
    INutritionFoodLogStore nutritionStore,
    IEnumerable<IFoodCompositionProvider> providers,UsdaConnection usda) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
        => View(await BuildModelAsync(cancellationToken));

    [HttpPost("usda-key"),ValidateAntiForgeryToken]
    public IActionResult SaveUsdaKey(string? apiKey)
    {
        if(string.IsNullOrWhiteSpace(apiKey))
        {
            TempData["NutritionProviderSuccess"]="USDA key left unchanged.";
            return RedirectToAction(nameof(Index));
        }
        try
        {
            usda.Save(apiKey);
            TempData["NutritionProviderSuccess"]="USDA key saved securely. Use Test saved USDA key to verify it. No restart is needed.";
        }
        catch(ArgumentException){TempData["NutritionProviderError"]="Enter a valid USDA API key (8–128 letters, numbers, underscores or hyphens).";}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {TempData["NutritionProviderError"]="Could not save the USDA key securely. Check the server credential storage configuration.";}
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(
        AdminNutritionProviderOrderInput input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.PrimaryProviderCode))
        {
            ModelState.AddModelError(
                nameof(input.PrimaryProviderCode),
                "Choose a primary nutrition provider.");
        }

        if (string.IsNullOrWhiteSpace(input.SecondaryProviderCode))
        {
            ModelState.AddModelError(
                nameof(input.SecondaryProviderCode),
                "Choose a secondary nutrition provider.");
        }

        if (string.Equals(
                input.PrimaryProviderCode,
                input.SecondaryProviderCode,
                StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(
                string.Empty,
                "Primary and secondary providers must be different.");
        }

        if (!ModelState.IsValid)
        {
            var invalid =
                await BuildModelAsync(cancellationToken);

            invalid.PrimaryProviderCode =
                input.PrimaryProviderCode;

            invalid.SecondaryProviderCode =
                input.SecondaryProviderCode;

            return View("Index", invalid);
        }

        try
        {
            await nutritionStore.SaveProviderOrderAsync(
                input.PrimaryProviderCode,
                input.SecondaryProviderCode,
                cancellationToken);

            TempData["NutritionProviderSuccess"] =
                "Nutrition provider order saved.";
        }
        catch (Exception ex)
        {
            TempData["NutritionProviderError"] =
                ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestProvider(
        string providerCode,
        CancellationToken cancellationToken)
    {
        var provider =
            providers.FirstOrDefault(x =>
                string.Equals(
                    x.ProviderCode,
                    providerCode,
                    StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            TempData["NutritionProviderError"] =
                $"Provider '{providerCode}' is not registered.";

            return RedirectToAction(nameof(Index));
        }

        try
        {
            var result =
                await provider.SearchAsync(
                    new(
                        "chicken",
                        PageNumber: 1,
                        PageSize: 1),
                    cancellationToken);

            var first =
                result.Foods.FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "Provider search returned no foods.");

            var detail =
                await provider.GetFoodAsync(
                    first.SourceFoodId,
                    cancellationToken);

            if (detail.Food.Nutrients.Count == 0)
            {
                throw new InvalidOperationException(
                    "Provider detail returned no nutrients.");
            }

            TempData["NutritionProviderSuccess"] =
                $"{providerCode} test passed: search and food detail are available.";
        }
        catch (Exception ex)
        {
            TempData["NutritionProviderError"] =
                $"{providerCode} test failed: {(providerCode=="USDA_FDC"?"Check that the saved API key is correct and USDA is available.":SafeMessage(ex))}";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<AdminNutritionProvidersViewModel> BuildModelAsync(
        CancellationToken cancellationToken)
    {
        var policies =
            await nutritionStore.GetProviderPoliciesAsync(
                cancellationToken);

        var enabled =
            policies
                .Where(x => x.IsEnabled)
                .OrderBy(x => x.SearchPriority)
                .ToArray();

        return new AdminNutritionProvidersViewModel
        {
            Providers = policies,
            UsdaKeyConfigured=usda.Ready,
            UsdaKeySource=usda.Source,
            PrimaryProviderCode =
                enabled.ElementAtOrDefault(0)?.ProviderCode
                ?? policies.ElementAtOrDefault(0)?.ProviderCode
                ?? "",
            SecondaryProviderCode =
                enabled.ElementAtOrDefault(1)?.ProviderCode
                ?? policies.ElementAtOrDefault(1)?.ProviderCode
                ?? ""
        };
    }

    private static string SafeMessage(Exception ex)
    {
        if (ex.Message.Contains(
                "Client Secret",
                StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains(
                "Client ID",
                StringComparison.OrdinalIgnoreCase))
        {
            return "FatSecret Premier credentials are not configured.";
        }

        return ex.Message.Length <= 220
            ? ex.Message
            : "Provider test failed.";
    }
}
