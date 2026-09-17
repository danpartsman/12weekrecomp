using WebRecomp.WorkoutIntelligence.Persistence;

namespace WebRecomp.CatalogSync;

public sealed class CatalogSyncCoordinator(
    IEnumerable<IExerciseCatalogProvider> catalogProviders,
    IEnumerable<IExerciseMediaProvider> mediaProviders,
    IExerciseCatalogStore catalogStore,
    ICatalogSyncStore syncStore)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(30);

    private readonly IReadOnlyDictionary<string, IExerciseCatalogProvider> catalogProviders =
        catalogProviders.ToDictionary(x => x.Descriptor.Code, StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyDictionary<string, IExerciseMediaProvider> mediaProviders =
        mediaProviders.ToDictionary(x => x.Descriptor.Code, StringComparer.OrdinalIgnoreCase);

    public async Task<CatalogSyncStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await syncStore.GetSettingsAsync(cancellationToken);
        var recentRuns = await syncStore.GetRecentRunsAsync(20, cancellationToken);

        return new CatalogSyncStatus(
            settings,
            catalogProviders.Values.Select(x => x.Descriptor).OrderBy(x => x.DisplayName).ToArray(),
            mediaProviders.Values.Select(x => x.Descriptor).OrderBy(x => x.DisplayName).ToArray(),
            recentRuns);
    }

    public async Task SaveSettingsAsync(
        CatalogSyncSettingsUpdate update,
        CancellationToken cancellationToken = default)
    {
        if (!catalogProviders.ContainsKey(update.ActiveProviderCode))
            throw new ArgumentException("Unknown exercise catalog provider.", nameof(update));

        if (!mediaProviders.ContainsKey(update.MediaProviderCode))
            throw new ArgumentException("Unknown exercise media provider.", nameof(update));

        if (!CatalogSyncConstants.IsSupportedRefreshInterval(update.RefreshIntervalDays))
            throw new ArgumentOutOfRangeException(
                nameof(update),
                "Refresh interval must be 1, 3, 7, 14, or 30 days.");

        await syncStore.SaveSettingsAsync(update, cancellationToken);
    }

    public async Task<CatalogSyncResult> RunIfDueAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await syncStore.GetSettingsAsync(cancellationToken);

        if (!CatalogSyncPolicy.IsDue(settings, DateTime.UtcNow))
        {
            return new CatalogSyncResult(
                null,
                "NOT_DUE",
                false,
                0,
                0,
                0,
                0,
                "Automatic catalog refresh is not due.");
        }

        return await RunAsync("SCHEDULE", cancellationToken);
    }

    public Task<CatalogSyncResult> RunNowAsync(
        CancellationToken cancellationToken = default)
        => RunAsync("ADMIN", cancellationToken);

    private async Task<CatalogSyncResult> RunAsync(
        string triggerCode,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var settings = await syncStore.GetSettingsAsync(cancellationToken);

        if (!catalogProviders.TryGetValue(settings.ActiveProviderCode, out var provider))
            throw new InvalidOperationException(
                $"Configured exercise provider '{settings.ActiveProviderCode}' is not registered.");

        var leaseId = Guid.NewGuid();
        var leaseAcquired = await syncStore.TryAcquireLeaseAsync(
            leaseId,
            now,
            now.Add(LeaseDuration),
            cancellationToken);

        if (!leaseAcquired)
        {
            return new CatalogSyncResult(
                null,
                "BUSY",
                false,
                0,
                0,
                0,
                0,
                "Another catalog synchronization is already running.");
        }

        Guid? runId = null;

        try
        {
            runId = await syncStore.StartRunAsync(
                provider.Descriptor.Code,
                triggerCode,
                now,
                cancellationToken);

            var fetch = await provider.FetchAsync(
                new ExerciseCatalogFetchRequest(
                    settings.LastProviderEtag,
                    settings.LastDatasetSha256),
                cancellationToken);

            if (!fetch.Changed)
            {
                var completedAt = DateTime.UtcNow;

                await syncStore.UpdateStateAsync(
                    new CatalogSyncStateUpdate(
                        completedAt,
                        settings.LastSuccessfulSyncAtUtc,
                        fetch.Sha256,
                        fetch.SourceVersion ?? settings.LastProviderVersion,
                        fetch.Etag ?? settings.LastProviderEtag,
                        null),
                    cancellationToken);

                await syncStore.CompleteRunAsync(
                    new CatalogSyncRunCompletion(
                        runId.Value,
                        "NO_CHANGE",
                        completedAt,
                        fetch.Sha256,
                        fetch.SourceVersion,
                        0,
                        0,
                        0,
                        null,
                        null),
                    cancellationToken);

                return new CatalogSyncResult(
                    runId,
                    "NO_CHANGE",
                    false,
                    0,
                    0,
                    0,
                    0,
                    "Provider dataset has not changed.");
            }

            if (fetch.Snapshot is null)
                throw new InvalidOperationException("Provider reported changed data without a snapshot.");

            var import = await catalogStore.ImportSnapshotAsync(
                fetch.Snapshot,
                new ExerciseCatalogProvenance(
                    provider.Descriptor.Code,
                    provider.Descriptor.LicenseCode,
                    fetch.SourceVersion,
                    fetch.Sha256,
                    fetch.CapturedAtUtc),
                cancellationToken);

            var successAt = DateTime.UtcNow;

            await syncStore.UpdateStateAsync(
                new CatalogSyncStateUpdate(
                    successAt,
                    successAt,
                    fetch.Sha256,
                    fetch.SourceVersion,
                    fetch.Etag,
                    null),
                cancellationToken);

            await syncStore.CompleteRunAsync(
                new CatalogSyncRunCompletion(
                    runId.Value,
                    "SUCCESS",
                    successAt,
                    fetch.Sha256,
                    fetch.SourceVersion,
                    import.Added,
                    import.Updated,
                    import.Deactivated,
                    import.ActiveCount,
                    null),
                cancellationToken);

            return new CatalogSyncResult(
                runId,
                "SUCCESS",
                true,
                import.Added,
                import.Updated,
                import.Deactivated,
                import.ActiveCount,
                "Exercise catalog synchronized successfully.");
        }
        catch (Exception ex)
        {
            var failedAt = DateTime.UtcNow;

            await syncStore.UpdateStateAsync(
                new CatalogSyncStateUpdate(
                    failedAt,
                    settings.LastSuccessfulSyncAtUtc,
                    settings.LastDatasetSha256,
                    settings.LastProviderVersion,
                    settings.LastProviderEtag,
                    ex.Message),
                CancellationToken.None);

            if (runId.HasValue)
            {
                await syncStore.CompleteRunAsync(
                    new CatalogSyncRunCompletion(
                        runId.Value,
                        "FAILED",
                        failedAt,
                        settings.LastDatasetSha256,
                        settings.LastProviderVersion,
                        null,
                        null,
                        null,
                        null,
                        ex.Message),
                    CancellationToken.None);
            }

            throw;
        }
        finally
        {
            await syncStore.ReleaseLeaseAsync(leaseId, CancellationToken.None);
        }
    }
}
