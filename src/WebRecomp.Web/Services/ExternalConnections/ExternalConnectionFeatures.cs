namespace WebRecomp.Web.Services.ExternalConnections;
public static class ExternalConnectionFeatures
{
    public static bool IsTestSite => string.Equals(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"), "recomp-test", StringComparison.OrdinalIgnoreCase);
}
