namespace WebRecomp.Web.ViewModels;

public sealed record AccountabilityPreviewViewModel(Guid ClientId, string ClientName, bool Editable = false);

public static class AccountabilityPreview
{
    // Remains unavailable on LIVE even if these files are later merged.
    public static bool Enabled(Microsoft.Extensions.Configuration.IConfiguration config) =>
        string.Equals(config["WEBSITE_SITE_NAME"], "recomp-test", StringComparison.OrdinalIgnoreCase);
}
