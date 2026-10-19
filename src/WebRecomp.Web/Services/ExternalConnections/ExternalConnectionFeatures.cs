namespace WebRecomp.Web.Services.ExternalConnections;
public static class ExternalConnectionFeatures
{
    // Only the known app hosting names are allowed to use Fitdays.
    // We keep manual import available in both LIVE and TEST, while preventing
    // unsupported environments from ever initiating automatic or authenticated traffic.
    public static bool IsEnabled => IsSupportedSite(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));

    public static bool IsSupportedSite(string? siteName) =>
        string.Equals(siteName, "recomp-live", StringComparison.OrdinalIgnoreCase)
        || string.Equals(siteName, "recomp-test", StringComparison.OrdinalIgnoreCase);
}
