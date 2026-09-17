using WebRecomp.NutritionIntelligence.Models;

namespace WebRecomp.NutritionIntelligence.Persistence;

/// <summary>
/// Storage policy codes are intentionally provider-neutral.
/// Provider adapters must obey the configured policy before persisting
/// source payloads or nutrient records.
/// </summary>
public static class NutritionCachePolicyCodes
{
    public const string Persistent = "PERSISTENT";
    public const string ReferenceOnly = "REFERENCE_ONLY";
    public const string SessionOnly = "SESSION_ONLY";
}

public static class NutritionSnapshotPolicyCodes
{
    public const string Persisted = "PERSISTED";
    public const string ReferenceOnly = "REFERENCE_ONLY";
    public const string UserEntered = "USER_ENTERED";
}

public static class NutritionCompletenessCodes
{
    public const string Complete = "COMPLETE";
    public const string Partial = "PARTIAL";
    public const string ReferenceOnly = "REFERENCE_ONLY";
}

public sealed record NutritionProviderPolicy(
    string ProviderCode,
    string CachePolicyCode,
    bool IsEnabled,
    bool RequiresAttribution,
    bool TermsReviewRequired);

public sealed record NutritionFoodReference(
    Guid? ExternalFoodId = null,
    Guid? CustomFoodId = null,
    Guid? RecipeId = null)
{
    public int TargetCount =>
        (ExternalFoodId.HasValue ? 1 : 0)
        + (CustomFoodId.HasValue ? 1 : 0)
        + (RecipeId.HasValue ? 1 : 0);

    public void Validate(bool allowRecipe = true)
    {
        if (TargetCount != 1)
            throw new InvalidOperationException(
                "Exactly one nutrition food reference target is required.");

        if (!allowRecipe && RecipeId.HasValue)
            throw new InvalidOperationException(
                "Recipe references are not allowed in this context.");
    }
}

public static class NutritionPersistencePolicy
{
    public static bool MayPersistProviderPayload(string cachePolicyCode)
        => string.Equals(
            cachePolicyCode,
            NutritionCachePolicyCodes.Persistent,
            StringComparison.Ordinal);

    public static bool MayPersistProviderNutrients(string cachePolicyCode)
        => MayPersistProviderPayload(cachePolicyCode);

    public static string SnapshotPolicyFor(
        string cachePolicyCode,
        bool isUserEntered)
    {
        if (isUserEntered)
            return NutritionSnapshotPolicyCodes.UserEntered;

        return MayPersistProviderPayload(cachePolicyCode)
            ? NutritionSnapshotPolicyCodes.Persisted
            : NutritionSnapshotPolicyCodes.ReferenceOnly;
    }
}

public interface INutritionPersistenceStore
{
    Task<NutritionProviderPolicy?> GetProviderPolicyAsync(
        string providerCode,
        CancellationToken cancellationToken = default);

    Task<Guid> SaveCustomFoodAsync(
        Guid clientId,
        CustomFoodWriteModel food,
        CancellationToken cancellationToken = default);

    Task<Guid> SaveRecipeAsync(
        Guid clientId,
        RecipeWriteModel recipe,
        CancellationToken cancellationToken = default);

    Task<Guid> SaveMealAsync(
        Guid clientId,
        SavedMealWriteModel meal,
        CancellationToken cancellationToken = default);

    Task<Guid> SaveDailyLogAsync(
        Guid clientId,
        DailyLogWriteModel log,
        CancellationToken cancellationToken = default);
}

public sealed record CanonicalNutrientWriteModel(
    string CanonicalCode,
    decimal Amount,
    string UnitName);

public sealed record CustomFoodWriteModel(
    string FoodName,
    string? BrandName,
    decimal ServingSize,
    string ServingUnitCode,
    string? HouseholdServingText,
    IReadOnlyList<CanonicalNutrientWriteModel> Nutrients,
    string? Notes = null);

public sealed record RecipeItemWriteModel(
    int ItemOrder,
    NutritionFoodReference Food,
    decimal Quantity,
    string QuantityUnitCode,
    decimal? GramWeight = null,
    string? Notes = null);

public sealed record RecipeWriteModel(
    string RecipeName,
    decimal YieldServings,
    IReadOnlyList<RecipeItemWriteModel> Items,
    string? Notes = null);

public sealed record SavedMealItemWriteModel(
    int ItemOrder,
    NutritionFoodReference Food,
    decimal Quantity,
    string QuantityUnitCode,
    decimal? GramWeight = null);

public sealed record SavedMealWriteModel(
    string MealName,
    string? MealTypeCode,
    IReadOnlyList<SavedMealItemWriteModel> Items,
    string? Notes = null);

public sealed record DailyLogItemWriteModel(
    string MealSlotCode,
    int ItemOrder,
    NutritionFoodReference Food,
    string DescriptionSnapshot,
    decimal Quantity,
    string QuantityUnitCode,
    decimal? GramWeight,
    string NutrientSnapshotPolicyCode,
    decimal? EnergyKcal,
    decimal? ProteinG,
    decimal? CarbohydrateG,
    decimal? FatG,
    decimal? FiberG,
    decimal? SugarG,
    decimal? SodiumMg,
    decimal? SaturatedFatG,
    Guid? SourceSavedMealId = null);

public sealed record DailyLogWriteModel(
    DateOnly LogDate,
    string? TimeZone,
    IReadOnlyList<DailyLogItemWriteModel> Items,
    string? Notes = null);


/// <summary>
/// Interactive food-log persistence boundary used by the Web composition layer.
/// </summary>
public interface INutritionFoodLogStore
{
    Task<NutritionProviderPolicy?> GetProviderPolicyAsync(
        string providerCode,
        CancellationToken cancellationToken = default);

    Task<Guid> UpsertExternalFoodAsync(
        FoodComposition food,
        CancellationToken cancellationToken = default);

    Task<Guid> SaveCustomFoodAsync(
        Guid clientId,
        CustomFoodWriteModel food,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NutritionCustomFoodReadModel>> GetCustomFoodsAsync(
        Guid clientId,
        CancellationToken cancellationToken = default);

    Task<NutritionDailyLogReadModel> GetDailyLogAsync(
        Guid clientId,
        DateOnly logDate,
        CancellationToken cancellationToken = default);

    Task<Guid> AddDailyLogItemAsync(
        Guid clientId,
        DateOnly logDate,
        string? timeZone,
        DailyLogItemWriteModel item,
        CancellationToken cancellationToken = default);

    Task DeleteDailyLogItemAsync(
        Guid clientId,
        Guid dailyLogItemId,
        CancellationToken cancellationToken = default);
}

public sealed record NutritionCustomFoodReadModel(
    Guid CustomFoodId,
    string FoodName,
    string? BrandName,
    decimal ServingSize,
    string ServingUnitCode,
    string? HouseholdServingText,
    decimal? EnergyKcal,
    decimal? ProteinG,
    decimal? CarbohydrateG,
    decimal? FatG,
    decimal? FiberG,
    decimal? SugarG,
    decimal? SodiumMg,
    decimal? SaturatedFatG);

public sealed record NutritionDailyLogItemReadModel(
    Guid DailyLogItemId,
    string MealSlotCode,
    int ItemOrder,
    string DescriptionSnapshot,
    decimal Quantity,
    string QuantityUnitCode,
    decimal? GramWeight,
    decimal? EnergyKcal,
    decimal? ProteinG,
    decimal? CarbohydrateG,
    decimal? FatG,
    decimal? FiberG,
    decimal? SugarG,
    decimal? SodiumMg,
    decimal? SaturatedFatG);

public sealed record NutritionDailyTotalsReadModel(
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbohydrateG,
    decimal FatG,
    decimal? FiberG,
    decimal? SugarG,
    decimal? SodiumMg,
    decimal? SaturatedFatG,
    string CompletenessCode);

public sealed record NutritionDailyLogReadModel(
    Guid? DailyLogId,
    DateOnly LogDate,
    IReadOnlyList<NutritionDailyLogItemReadModel> Items,
    NutritionDailyTotalsReadModel? Totals);
