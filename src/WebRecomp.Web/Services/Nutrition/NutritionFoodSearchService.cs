using WebRecomp.NutritionIntelligence.Contracts;
using WebRecomp.NutritionIntelligence.Models;
using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Services.Nutrition;

public static class NutritionFoodSortCodes
{
    public const string BestMatch = "BEST_MATCH";
    public const string GenericFirst = "GENERIC_FIRST";
    public const string BrandedFirst = "BRANDED_FIRST";
    public const string NameAsc = "NAME_ASC";
    public const string NameDesc = "NAME_DESC";

    public static string Normalize(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            GenericFirst => GenericFirst,
            BrandedFirst => BrandedFirst,
            NameAsc => NameAsc,
            NameDesc => NameDesc,
            _ => BestMatch
        };
}

public sealed record NutritionFoodSearchResponse(
    IReadOnlyList<NutritionFoodSearchResultViewModel> Results,
    IReadOnlyList<string> Warnings);

public sealed record NutritionResolvedFood(
    FoodComposition Food,
    NutritionProviderPolicy Policy,
    Guid? ExternalFoodId,
    bool FromCache);

public sealed class NutritionFoodSearchService(
    IEnumerable<IFoodCompositionProvider> providers,
    INutritionFoodLogStore store)
{
    public async Task<NutritionFoodSearchResponse> SearchAsync(
        string query,
        string? sortCode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new NutritionFoodSearchResponse([], []);

        var results = new List<NutritionFoodSearchResultViewModel>();
        var warnings = new List<string>();

        var policies =
            await store.GetProviderPoliciesAsync(
                cancellationToken);

        var policyByCode =
            policies.ToDictionary(
                x => x.ProviderCode,
                StringComparer.OrdinalIgnoreCase);

        var orderedProviders =
            providers
                .Where(x =>
                    policyByCode.TryGetValue(
                        x.ProviderCode,
                        out var policy)
                    && policy.IsEnabled)
                .OrderBy(x =>
                    policyByCode[x.ProviderCode].SearchPriority)
                .ThenBy(x => x.ProviderCode)
                .ToArray();

        foreach (var provider in orderedProviders)
        {
            var policy =
                policyByCode[provider.ProviderCode];

            try
            {
                var page =
                    await provider.SearchAsync(
                        new FoodSearchRequest(
                            query.Trim(),
                            PageNumber: 1,
                            PageSize: 40),
                        cancellationToken);

                foreach (var food in page.Foods)
                {
                    var defaultGrams =
                        food.ServingSize.HasValue
                        && string.Equals(
                            food.ServingSizeUnit,
                            "g",
                            StringComparison.OrdinalIgnoreCase)
                            ? food.ServingSize.Value
                            : 100m;

                    results.Add(
                        new NutritionFoodSearchResultViewModel
                        {
                            ProviderCode = food.ProviderCode,
                            SourceFoodId = food.SourceFoodId,
                            Description = food.Description,
                            BrandOwner = food.BrandOwner,
                            DataType = food.DataType,
                            GtinUpc = food.GtinUpc,
                            ServingSize = food.ServingSize,
                            ServingSizeUnit = food.ServingSizeUnit,
                            HouseholdServingText =
                                food.HouseholdServingText,
                            NutrientBasisAmount =
                                food.NutrientBasisAmount,
                            NutrientBasisUnit =
                                food.NutrientBasisUnit,
                            DefaultGramAmount = defaultGrams,
                            EnergyKcal = Nutrient(
                                food,
                                CanonicalNutrientCodes.EnergyKcal),
                            ProteinG = Nutrient(
                                food,
                                CanonicalNutrientCodes.ProteinG),
                            CarbohydrateG = Nutrient(
                                food,
                                CanonicalNutrientCodes.CarbohydrateG),
                            FatG = Nutrient(
                                food,
                                CanonicalNutrientCodes.FatG),
                            ProviderPriority =
                                policy.SearchPriority
                        });
                }
            }
            catch (Exception ex)
            {
                warnings.Add(
                    $"{provider.ProviderCode}: {SafeMessage(ex)}");
            }
        }

        return new NutritionFoodSearchResponse(
            ApplySort(
                results,
                NutritionFoodSortCodes.Normalize(sortCode)),
            warnings);
    }

    public async Task<NutritionResolvedFood> GetFoodAsync(
        string providerCode,
        string sourceFoodId,
        CancellationToken cancellationToken = default)
    {
        var provider =
            providers.FirstOrDefault(x =>
                string.Equals(
                    x.ProviderCode,
                    providerCode,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Nutrition provider '{providerCode}' is not registered.");

        var policy =
            await store.GetProviderPolicyAsync(
                provider.ProviderCode,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Nutrition provider '{provider.ProviderCode}' is not configured.");

        if (!policy.IsEnabled)
        {
            throw new InvalidOperationException(
                $"Nutrition provider '{provider.ProviderCode}' is disabled.");
        }

        if (provider is ICachedFoodCompositionParser parser
            && NutritionPersistencePolicy.MayPersistProviderPayload(
                policy.CachePolicyCode))
        {
            var cached =
                await store.GetFreshExternalFoodCacheAsync(
                    provider.ProviderCode,
                    sourceFoodId,
                    cancellationToken);

            if (cached is not null)
            {
                try
                {
                    var parsed =
                        parser.ParseCachedFood(
                            cached.RawSourceJson);

                    return new NutritionResolvedFood(
                        parsed,
                        policy,
                        cached.ExternalFoodId,
                        FromCache: true);
                }
                catch
                {
                    // A malformed cache entry falls through to a provider refresh.
                }
            }
        }

        var food =
            await provider.GetFoodAsync(
                sourceFoodId,
                cancellationToken);

        return new NutritionResolvedFood(
            food,
            policy,
            ExternalFoodId: null,
            FromCache: false);
    }

    private static IReadOnlyList<NutritionFoodSearchResultViewModel> ApplySort(
        IReadOnlyList<NutritionFoodSearchResultViewModel> source,
        string sortCode)
    {
        IEnumerable<NutritionFoodSearchResultViewModel> ordered =
            sortCode switch
            {
                NutritionFoodSortCodes.GenericFirst =>
                    source
                        .OrderBy(x => x.ProviderPriority)
                        .ThenBy(x => GenericRank(x.DataType))
                        .ThenBy(
                            x => x.Description,
                            StringComparer.OrdinalIgnoreCase),

                NutritionFoodSortCodes.BrandedFirst =>
                    source
                        .OrderBy(x => x.ProviderPriority)
                        .ThenBy(x =>
                            IsBranded(x.DataType)
                                ? 0
                                : 1)
                        .ThenBy(
                            x => x.Description,
                            StringComparer.OrdinalIgnoreCase),

                NutritionFoodSortCodes.NameAsc =>
                    source
                        .OrderBy(x => x.ProviderPriority)
                        .ThenBy(
                            x => x.Description,
                            StringComparer.OrdinalIgnoreCase),

                NutritionFoodSortCodes.NameDesc =>
                    source
                        .OrderBy(x => x.ProviderPriority)
                        .ThenByDescending(
                            x => x.Description,
                            StringComparer.OrdinalIgnoreCase),

                _ => source
                    .OrderBy(x => x.ProviderPriority)
            };

        return ordered.ToArray();
    }

    private static int GenericRank(string? dataType)
    {
        var value = dataType?.Trim() ?? string.Empty;

        if (value.Equals(
                "Foundation",
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (value.Contains(
                "FNDDS",
                StringComparison.OrdinalIgnoreCase)
            || value.Contains(
                "Survey",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (value.Contains(
                "SR Legacy",
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (IsBranded(value))
            return 4;

        return 3;
    }

    private static bool IsBranded(string? dataType)
        => dataType?.Contains(
            "Branded",
            StringComparison.OrdinalIgnoreCase) == true;

    private static decimal? Nutrient(
        FoodCatalogItem food,
        string canonicalCode)
        => food.Nutrients
            .Where(x =>
                string.Equals(
                    x.CanonicalCode,
                    canonicalCode,
                    StringComparison.Ordinal)
                && x.Amount.HasValue)
            .Select(x => x.Amount)
            .FirstOrDefault();

    private static string SafeMessage(Exception ex)
    {
        if (ex.Message.Contains(
                "API key",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Provider API key is not configured.";
        }

        if (ex.Message.Contains(
                "Client ID",
                StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains(
                "Client Secret",
                StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains(
                "OAuth",
                StringComparison.OrdinalIgnoreCase))
        {
            return "FatSecret Premier credentials are not configured or authentication failed.";
        }

        return ex.Message.Length <= 220
            ? ex.Message
            : "Provider request failed.";
    }
}
