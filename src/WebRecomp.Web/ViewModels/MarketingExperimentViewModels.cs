namespace WebRecomp.Web.ViewModels;

public sealed class MarketingExperimentDashboardViewModel
{
    public IReadOnlyList<MarketingExperimentVariantRow> Variants { get; set; } =
        Array.Empty<MarketingExperimentVariantRow>();

    public DateTime GeneratedAtUtc { get; set; }
}

public sealed class MarketingExperimentVariantRow
{
    public string VariantCode { get; set; } = "";
    public int WelcomeVisitors { get; set; }
    public int StartFreeVisitors { get; set; }
    public int EvidenceClickVisitors { get; set; }
    public int EvidenceVisitors { get; set; }
    public int EvidenceStartVisitors { get; set; }

    public decimal StartFreeCtr =>
        WelcomeVisitors == 0
            ? 0m
            : (decimal)StartFreeVisitors / WelcomeVisitors;

    public decimal EvidenceCtr =>
        WelcomeVisitors == 0
            ? 0m
            : (decimal)EvidenceClickVisitors / WelcomeVisitors;
}
