using WebRecomp.WorkoutIntelligence.Catalog;

namespace WebRecomp.CatalogSync;

public static class CatalogSyncConstants
{
    public const string DefaultSettingsKey = "DEFAULT";
    public const string KineticProviderCode = "KINETIC";
    public const string NoMediaProviderCode = "NONE";

    public static readonly int[] AllowedRefreshIntervalsDays = [1, 3, 7, 14, 30];

    public static bool IsSupportedRefreshInterval(int days)
        => AllowedRefreshIntervalsDays.Contains(days);
}

public sealed record ExerciseCatalogProviderDescriptor(
    string Code,
    string DisplayName,
    string LicenseCode,
    bool SupportsAutomaticSync);

public sealed record ExerciseMediaProviderDescriptor(
    string Code,
    string DisplayName);

public sealed record ExerciseCatalogFetchRequest(
    string? PreviousEtag,
    string? PreviousSha256);

public sealed record ExerciseCatalogFetchResult(
    bool Changed,
    string? Etag,
    string Sha256,
    string? SourceVersion,
    DateTime CapturedAtUtc,
    ExerciseCatalogSnapshot? Snapshot);

public interface IExerciseCatalogProvider
{
    ExerciseCatalogProviderDescriptor Descriptor { get; }

    Task<ExerciseCatalogFetchResult> FetchAsync(
        ExerciseCatalogFetchRequest request,
        CancellationToken cancellationToken = default);
}

public interface IExerciseMediaProvider
{
    ExerciseMediaProviderDescriptor Descriptor { get; }
}

public sealed class NoExerciseMediaProvider : IExerciseMediaProvider
{
    public ExerciseMediaProviderDescriptor Descriptor { get; } =
        new(
            CatalogSyncConstants.NoMediaProviderCode,
            "None / 12WR-managed media only");
}

public sealed record CatalogSyncSettings(
    string SettingsKey,
    string ActiveProviderCode,
    string MediaProviderCode,
    bool AutoSyncEnabled,
    int RefreshIntervalDays,
    DateTime? LastCheckedAtUtc,
    DateTime? LastSuccessfulSyncAtUtc,
    string? LastDatasetSha256,
    string? LastProviderVersion,
    string? LastProviderEtag,
    string? LastError,
    Guid? SyncLeaseId,
    DateTime? SyncLeaseUntilUtc,
    DateTime UpdatedAtUtc);

public sealed record CatalogSyncRunSummary(
    Guid RunId,
    string ProviderCode,
    string TriggerCode,
    string StatusCode,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? DatasetSha256,
    string? SourceVersion,
    int? AddedCount,
    int? UpdatedCount,
    int? DeactivatedCount,
    int? ActiveCount,
    string? ErrorMessage);

public sealed record CatalogSyncResult(
    Guid? RunId,
    string StatusCode,
    bool Changed,
    int Added,
    int Updated,
    int Deactivated,
    int ActiveCount,
    string? Message);

public sealed record CatalogSyncStatus(
    CatalogSyncSettings Settings,
    IReadOnlyList<ExerciseCatalogProviderDescriptor> CatalogProviders,
    IReadOnlyList<ExerciseMediaProviderDescriptor> MediaProviders,
    IReadOnlyList<CatalogSyncRunSummary> RecentRuns);

public sealed record CatalogSyncSettingsUpdate(
    string ActiveProviderCode,
    string MediaProviderCode,
    bool AutoSyncEnabled,
    int RefreshIntervalDays,
    Guid? UpdatedByUserId);

public sealed record CatalogSyncRunCompletion(
    Guid RunId,
    string StatusCode,
    DateTime CompletedAtUtc,
    string? DatasetSha256,
    string? SourceVersion,
    int? AddedCount,
    int? UpdatedCount,
    int? DeactivatedCount,
    int? ActiveCount,
    string? ErrorMessage);

public sealed record CatalogSyncStateUpdate(
    DateTime CheckedAtUtc,
    DateTime? SuccessfulSyncAtUtc,
    string? DatasetSha256,
    string? ProviderVersion,
    string? ProviderEtag,
    string? ErrorMessage);

public interface ICatalogSyncStore
{
    Task<CatalogSyncSettings> GetSettingsAsync(
        CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(
        CatalogSyncSettingsUpdate update,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogSyncRunSummary>> GetRecentRunsAsync(
        int take = 20,
        CancellationToken cancellationToken = default);

    Task<bool> TryAcquireLeaseAsync(
        Guid leaseId,
        DateTime nowUtc,
        DateTime leaseUntilUtc,
        CancellationToken cancellationToken = default);

    Task ReleaseLeaseAsync(
        Guid leaseId,
        CancellationToken cancellationToken = default);

    Task<Guid> StartRunAsync(
        string providerCode,
        string triggerCode,
        DateTime startedAtUtc,
        CancellationToken cancellationToken = default);

    Task CompleteRunAsync(
        CatalogSyncRunCompletion completion,
        CancellationToken cancellationToken = default);

    Task UpdateStateAsync(
        CatalogSyncStateUpdate update,
        CancellationToken cancellationToken = default);
}

public static class CatalogSyncPolicy
{
    public static bool IsDue(CatalogSyncSettings settings, DateTime nowUtc)
    {
        if (!settings.AutoSyncEnabled)
            return false;

        if (!CatalogSyncConstants.IsSupportedRefreshInterval(settings.RefreshIntervalDays))
            return false;

        if (settings.LastCheckedAtUtc is null)
            return true;

        return settings.LastCheckedAtUtc.Value
            .AddDays(settings.RefreshIntervalDays) <= nowUtc;
    }
}
