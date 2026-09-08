using System.Text;
using System.Text.Json;

namespace TwelveWeekRecomp.Live.Services;

public static class StaffRoles
{
    public const string Administrator = "Administrator";
    public const string Trainer = "Trainer";
}

public sealed record StaffAccessResult(
    bool IsAuthenticated,
    string? ObjectId,
    string? Email,
    IReadOnlySet<string> Roles)
{
    public bool IsAdministrator => Roles.Contains(StaffRoles.Administrator);
    public bool IsTrainer => Roles.Contains(StaffRoles.Trainer);
}

public sealed class StaffAccessService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<StaffAccessService> _logger;

    public StaffAccessService(
        IConfiguration configuration,
        ILogger<StaffAccessService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public string DirectBaseUrl =>
        (_configuration["Staff:DirectBaseUrl"] ?? "https://recomp-live.azurewebsites.net")
            .TrimEnd('/');

    public StaffAccessResult Evaluate(HttpContext context)
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
            return new(false, objectId, email, EmptyRoles());

        if (!string.IsNullOrWhiteSpace(provider) &&
            !string.Equals(provider, "aad", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, objectId, email, EmptyRoles());
        }

        var roles = ParseRoles(context.Request.Headers["X-MS-CLIENT-PRINCIPAL"].FirstOrDefault());
        return new(true, objectId, email, roles);
    }

    private IReadOnlySet<string> ParseRoles(string? encodedPrincipal)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(encodedPrincipal))
            return roles;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPrincipal));
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("claims", out var claims) ||
                claims.ValueKind != JsonValueKind.Array)
            {
                return roles;
            }

            foreach (var claim in claims.EnumerateArray())
            {
                if (!claim.TryGetProperty("typ", out var typeElement) ||
                    !claim.TryGetProperty("val", out var valueElement))
                {
                    continue;
                }

                var type = typeElement.GetString();
                var value = valueElement.GetString();

                if (string.IsNullOrWhiteSpace(type) ||
                    string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (IsRoleClaim(type))
                    roles.Add(value.Trim());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to parse App Service Authentication principal claims.");
        }

        return roles;
    }

    private static bool IsRoleClaim(string type)
    {
        return string.Equals(type, "roles", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "role", StringComparison.OrdinalIgnoreCase) ||
               type.EndsWith("/role", StringComparison.OrdinalIgnoreCase) ||
               type.EndsWith("/roles", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<string> EmptyRoles() =>
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
