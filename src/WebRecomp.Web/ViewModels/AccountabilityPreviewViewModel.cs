namespace WebRecomp.Web.ViewModels;

public sealed record AccountabilityPreviewViewModel(Guid ClientId, string ClientName, bool Editable = false, bool CanApprove = false, bool ReviewPage = false, bool CanAssignSnap = false, bool SimpleSnap = false, bool SnapPage = false);

public static class AccountabilityPreview
{
    // Available only on the two explicitly deployed 12WR environments.
    public static bool Enabled(Microsoft.Extensions.Configuration.IConfiguration config) =>
        string.Equals(config["WEBSITE_SITE_NAME"], "recomp-test", StringComparison.OrdinalIgnoreCase)
        || string.Equals(config["WEBSITE_SITE_NAME"], "recomp-live", StringComparison.OrdinalIgnoreCase);
}
