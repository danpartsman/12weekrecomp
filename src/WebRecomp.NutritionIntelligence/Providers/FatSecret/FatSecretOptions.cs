namespace WebRecomp.NutritionIntelligence.Providers.FatSecret;

public sealed class FatSecretOptions
{
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string TokenUrl { get; init; } =
        "https://oauth.fatsecret.com/connect/token";
    public string ApiBaseUrl { get; init; } =
        "https://platform.fatsecret.com/rest";
    public string Scope { get; init; } = "premier";
    public string Region { get; init; } = "US";

    public static FatSecretOptions FromEnvironment()
        => new()
        {
            ClientId =
                Environment.GetEnvironmentVariable(
                    "FATSECRET_CLIENT_ID")
                ?? "",
            ClientSecret =
                Environment.GetEnvironmentVariable(
                    "FATSECRET_CLIENT_SECRET")
                ?? ""
        };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException(
                "FatSecret Client ID is required.");

        if (string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException(
                "FatSecret Client Secret is required.");

        if (!Uri.TryCreate(
                TokenUrl,
                UriKind.Absolute,
                out var tokenUri)
            || tokenUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "FatSecret token URL must use HTTPS.");
        }

        if (!Uri.TryCreate(
                ApiBaseUrl,
                UriKind.Absolute,
                out var apiUri)
            || apiUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "FatSecret API base URL must use HTTPS.");
        }
    }
}
