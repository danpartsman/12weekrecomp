namespace WebRecomp.Web.Services.ExternalConnections;
public static class ExternalConnectionFeatures
{
    public static bool IsEnabled => IsSupportedSite(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
    public static bool IsSupportedSite(string? siteName) => siteName is not null &&
        (siteName.Equals("recomp-test", StringComparison.OrdinalIgnoreCase) ||
         siteName.Equals("recomp-live", StringComparison.OrdinalIgnoreCase));
}
