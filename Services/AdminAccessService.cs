namespace TwelveWeekRecomp.Live.Services;

public sealed record AdminAccessResult(
    bool IsAuthenticated,
    bool IsAuthorized,
    string? ObjectId,
    string? Email);

public sealed class AdminAccessService
{
    private readonly IConfiguration _configuration;

    public AdminAccessService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string DirectAdminBaseUrl =>
        (_configuration["Admin:DirectAdminBaseUrl"] ?? "https://recomp-live.azurewebsites.net").TrimEnd('/');

    public AdminAccessResult Evaluate(HttpContext context)
    {
        var objectId = context.Request.Headers["X-MS-CLIENT-PRINCIPAL-ID"]
            .FirstOrDefault()?.Trim();
        var email = context.Request.Headers["X-MS-CLIENT-PRINCIPAL-NAME"]
            .FirstOrDefault()?.Trim();
        var provider = context.Request.Headers["X-MS-CLIENT-PRINCIPAL-IDP"]
            .FirstOrDefault()?.Trim();

        var authenticated =
            !string.IsNullOrWhiteSpace(objectId) ||
            !string.IsNullOrWhiteSpace(email);

        if (!authenticated)
            return new(false, false, objectId, email);

        if (!string.IsNullOrWhiteSpace(provider) &&
            !string.Equals(provider, "aad", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, false, objectId, email);
        }

        var allowedObjectId = _configuration["Admin:AllowedObjectId"]?.Trim();
        var allowedEmail = _configuration["Admin:AllowedEmail"]?.Trim();

        var objectMatch =
            !string.IsNullOrWhiteSpace(allowedObjectId) &&
            !string.IsNullOrWhiteSpace(objectId) &&
            string.Equals(allowedObjectId, objectId, StringComparison.OrdinalIgnoreCase);

        var emailMatch =
            !string.IsNullOrWhiteSpace(allowedEmail) &&
            !string.IsNullOrWhiteSpace(email) &&
            string.Equals(allowedEmail, email, StringComparison.OrdinalIgnoreCase);

        return new(true, objectMatch || emailMatch, objectId, email);
    }
}
