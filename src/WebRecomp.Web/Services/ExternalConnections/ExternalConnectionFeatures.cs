namespace WebRecomp.Web.Services.ExternalConnections;
public static class ExternalConnectionFeatures
{
    // Only LIVE may contact Fitdays. TEST retains imported measurements but cannot
    // authenticate, reconnect, or synchronize against the external service.
    public static bool IsEnabled => IsSupportedSite(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
    public static bool IsSupportedSite(string? siteName) =>
        string.Equals(siteName, "recomp-live", StringComparison.OrdinalIgnoreCase);
}
