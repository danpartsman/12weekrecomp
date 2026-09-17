using System.ComponentModel.DataAnnotations;
using WebRecomp.CatalogSync;

namespace WebRecomp.Web.ViewModels;

public sealed class AdminCatalogSyncViewModel
{
    public required AdminCatalogSyncSettingsInput Settings { get; set; }
    public required IReadOnlyList<ExerciseCatalogProviderDescriptor> CatalogProviders { get; init; }
    public required IReadOnlyList<ExerciseMediaProviderDescriptor> MediaProviders { get; init; }
    public required IReadOnlyList<CatalogSyncRunSummary> RecentRuns { get; init; }
    public DateTime? LastCheckedAtUtc { get; init; }
    public DateTime? LastSuccessfulSyncAtUtc { get; init; }
    public string? LastDatasetSha256 { get; init; }
    public string? LastProviderVersion { get; init; }
    public string? LastError { get; init; }
}

public sealed class AdminCatalogSyncSettingsInput
{
    [Required]
    public string ActiveProviderCode { get; set; } = CatalogSyncConstants.KineticProviderCode;

    [Required]
    public string MediaProviderCode { get; set; } = CatalogSyncConstants.NoMediaProviderCode;

    public bool AutoSyncEnabled { get; set; }

    [Range(1, 30)]
    public int RefreshIntervalDays { get; set; } = 7;
}
