namespace WebRecomp.NutritionIntelligence.Providers.Usda;

public sealed class UsdaFoodDataCentralOptions
{
    public const string DefaultBaseUrl = "https://api.nal.usda.gov/fdc/v1";

    public string BaseUrl { get; init; } = DefaultBaseUrl;

    /// <summary>
    /// USDA FDC data.gov API key. Keep this outside source control.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException(
                "USDA FoodData Central API key is required.");

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "USDA FoodData Central BaseUrl must be an absolute HTTPS URL.");
        }
    }

    public static UsdaFoodDataCentralOptions FromEnvironment()
        => new()
        {
            ApiKey =
                Environment.GetEnvironmentVariable("USDA_FDC_API_KEY")
                ?? string.Empty
        };
}
