using Microsoft.EntityFrameworkCore;
using WebRecomp.CatalogSync;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.CatalogSync;

public sealed class SqlCatalogSyncStore(RecompDbContext db) : ICatalogSyncStore
{
    public async Task<CatalogSyncSettings> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var row = await db.ExerciseCatalogSyncSettings
            .AsNoTracking()
            .SingleAsync(x => x.SettingsKey == CatalogSyncConstants.DefaultSettingsKey, cancellationToken);

        return Map(row);
    }

    public async Task SaveSettingsAsync(
        CatalogSyncSettingsUpdate update,
        CancellationToken cancellationToken = default)
    {
        var row = await db.ExerciseCatalogSyncSettings
            .SingleAsync(x => x.SettingsKey == CatalogSyncConstants.DefaultSettingsKey, cancellationToken);

        row.ActiveProviderCode = update.ActiveProviderCode;
        row.MediaProviderCode = update.MediaProviderCode;
        row.AutoSyncEnabled = update.AutoSyncEnabled;
        row.RefreshIntervalDays = update.RefreshIntervalDays;
        row.UpdatedByUserId = update.UpdatedByUserId;
        row.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogSyncRunSummary>> GetRecentRunsAsync(
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);

        return await db.ExerciseCatalogSyncRuns
            .AsNoTracking()
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(take)
            .Select(x => new CatalogSyncRunSummary(
                x.ExerciseCatalogSyncRunId,
                x.ProviderCode,
                x.TriggerCode,
                x.StatusCode,
                x.StartedAtUtc,
                x.CompletedAtUtc,
                x.DatasetSha256,
                x.SourceVersion,
                x.AddedCount,
                x.UpdatedCount,
                x.DeactivatedCount,
                x.ActiveCount,
                x.ErrorMessage))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryAcquireLeaseAsync(
        Guid leaseId,
        DateTime nowUtc,
        DateTime leaseUntilUtc,
        CancellationToken cancellationToken = default)
    {
        var affected = await db.ExerciseCatalogSyncSettings
            .Where(x =>
                x.SettingsKey == CatalogSyncConstants.DefaultSettingsKey &&
                (x.SyncLeaseUntilUtc == null || x.SyncLeaseUntilUtc < nowUtc))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.SyncLeaseId, leaseId)
                    .SetProperty(x => x.SyncLeaseUntilUtc, leaseUntilUtc),
                cancellationToken);

        return affected == 1;
    }

    public async Task ReleaseLeaseAsync(
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        await db.ExerciseCatalogSyncSettings
            .Where(x =>
                x.SettingsKey == CatalogSyncConstants.DefaultSettingsKey &&
                x.SyncLeaseId == leaseId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.SyncLeaseId, (Guid?)null)
                    .SetProperty(x => x.SyncLeaseUntilUtc, (DateTime?)null),
                cancellationToken);
    }

    public async Task<Guid> StartRunAsync(
        string providerCode,
        string triggerCode,
        DateTime startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();

        db.ExerciseCatalogSyncRuns.Add(new ExerciseCatalogSyncRunRecord
        {
            ExerciseCatalogSyncRunId = id,
            ProviderCode = providerCode,
            TriggerCode = triggerCode,
            StatusCode = "RUNNING",
            StartedAtUtc = startedAtUtc
        });

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task CompleteRunAsync(
        CatalogSyncRunCompletion completion,
        CancellationToken cancellationToken = default)
    {
        var row = await db.ExerciseCatalogSyncRuns
            .SingleAsync(
                x => x.ExerciseCatalogSyncRunId == completion.RunId,
                cancellationToken);

        row.StatusCode = completion.StatusCode;
        row.CompletedAtUtc = completion.CompletedAtUtc;
        row.DatasetSha256 = completion.DatasetSha256;
        row.SourceVersion = completion.SourceVersion;
        row.AddedCount = completion.AddedCount;
        row.UpdatedCount = completion.UpdatedCount;
        row.DeactivatedCount = completion.DeactivatedCount;
        row.ActiveCount = completion.ActiveCount;
        row.ErrorMessage = Truncate(completion.ErrorMessage, 4000);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateStateAsync(
        CatalogSyncStateUpdate update,
        CancellationToken cancellationToken = default)
    {
        var row = await db.ExerciseCatalogSyncSettings
            .SingleAsync(x => x.SettingsKey == CatalogSyncConstants.DefaultSettingsKey, cancellationToken);

        row.LastCheckedAtUtc = update.CheckedAtUtc;
        row.LastSuccessfulSyncAtUtc = update.SuccessfulSyncAtUtc;
        row.LastDatasetSha256 = update.DatasetSha256;
        row.LastProviderVersion = update.ProviderVersion;
        row.LastProviderEtag = update.ProviderEtag;
        row.LastError = Truncate(update.ErrorMessage, 4000);
        row.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    private static CatalogSyncSettings Map(ExerciseCatalogSyncSettingRecord row)
        => new(
            row.SettingsKey,
            row.ActiveProviderCode,
            row.MediaProviderCode,
            row.AutoSyncEnabled,
            row.RefreshIntervalDays,
            row.LastCheckedAtUtc,
            row.LastSuccessfulSyncAtUtc,
            row.LastDatasetSha256,
            row.LastProviderVersion,
            row.LastProviderEtag,
            row.LastError,
            row.SyncLeaseId,
            row.SyncLeaseUntilUtc,
            row.UpdatedAtUtc);

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maxLength
                ? value
                : value[..maxLength];
}
