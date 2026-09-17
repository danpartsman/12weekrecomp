using WebRecomp.CatalogSync;
using WebRecomp.WorkoutIntelligence.Catalog;
using WebRecomp.WorkoutIntelligence.Persistence;

namespace WebRecomp.CatalogSync.Tests;

public sealed class CatalogSyncCoordinatorTests
{
    [Fact]
    public async Task No_change_provider_does_not_import_catalog()
    {
        var store = new FakeSyncStore();
        var catalog = new FakeCatalogStore();
        var provider = new FakeProvider(changed: false);
        var coordinator = new CatalogSyncCoordinator(
            [provider],
            [new NoExerciseMediaProvider()],
            catalog,
            store);

        var result = await coordinator.RunNowAsync();

        Assert.Equal("NO_CHANGE", result.StatusCode);
        Assert.Equal(0, catalog.ImportCalls);
        Assert.Single(store.Runs);
        Assert.Equal("NO_CHANGE", store.Runs[0].StatusCode);
    }

    [Fact]
    public async Task Changed_provider_imports_through_catalog_store()
    {
        var store = new FakeSyncStore();
        var catalog = new FakeCatalogStore();
        var provider = new FakeProvider(changed: true);
        var coordinator = new CatalogSyncCoordinator(
            [provider],
            [new NoExerciseMediaProvider()],
            catalog,
            store);

        var result = await coordinator.RunNowAsync();

        Assert.Equal("SUCCESS", result.StatusCode);
        Assert.Equal(1, catalog.ImportCalls);
        Assert.True(result.Changed);
    }

    private sealed class FakeProvider(bool changed) : IExerciseCatalogProvider
    {
        public ExerciseCatalogProviderDescriptor Descriptor { get; } =
            new(CatalogSyncConstants.KineticProviderCode, "Kinetic", "MIT", true);

        public Task<ExerciseCatalogFetchResult> FetchAsync(
            ExerciseCatalogFetchRequest request,
            CancellationToken cancellationToken = default)
        {
            var snapshot = changed
                ? new ExerciseCatalogSnapshot(
                    CatalogSyncConstants.KineticProviderCode,
                    "MIT",
                    [new ExerciseCatalogItem(
                        Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        CatalogSyncConstants.KineticProviderCode,
                        "1",
                        "Test Exercise",
                        "reps",
                        null,
                        null,
                        "compound",
                        "strength",
                        [],
                        [],
                        [])])
                : null;

            return Task.FromResult(new ExerciseCatalogFetchResult(
                changed,
                "\"etag-1\"",
                changed ? new string('B', 64) : new string('A', 64),
                "etag-1",
                DateTime.UtcNow,
                snapshot));
        }
    }

    private sealed class FakeCatalogStore : IExerciseCatalogStore
    {
        public int ImportCalls { get; private set; }

        public Task<ExerciseCatalogImportResult> ImportSnapshotAsync(
            ExerciseCatalogSnapshot snapshot,
            ExerciseCatalogProvenance provenance,
            CancellationToken cancellationToken = default)
        {
            ImportCalls++;
            return Task.FromResult(new ExerciseCatalogImportResult(
                Guid.NewGuid(),
                provenance.SourceCode,
                1,
                0,
                0,
                snapshot.Exercises.Count));
        }

        public Task<IReadOnlyList<ExerciseCatalogSearchItem>> SearchAsync(
            string? query,
            int take = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ExerciseCatalogSearchItem>>([]);

        public Task<ExerciseCatalogItem?> GetAsync(
            Guid exerciseId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<ExerciseCatalogItem?>(null);
    }

    private sealed class FakeSyncStore : ICatalogSyncStore
    {
        private CatalogSyncSettings settings = new(
            CatalogSyncConstants.DefaultSettingsKey,
            CatalogSyncConstants.KineticProviderCode,
            CatalogSyncConstants.NoMediaProviderCode,
            false,
            7,
            null,
            null,
            new string('A', 64),
            null,
            null,
            null,
            null,
            null,
            DateTime.UtcNow);

        public List<CatalogSyncRunSummary> Runs { get; } = [];

        public Task<CatalogSyncSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(settings);

        public Task SaveSettingsAsync(CatalogSyncSettingsUpdate update, CancellationToken cancellationToken = default)
        {
            settings = settings with
            {
                ActiveProviderCode = update.ActiveProviderCode,
                MediaProviderCode = update.MediaProviderCode,
                AutoSyncEnabled = update.AutoSyncEnabled,
                RefreshIntervalDays = update.RefreshIntervalDays
            };
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CatalogSyncRunSummary>> GetRecentRunsAsync(int take = 20, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CatalogSyncRunSummary>>(Runs.Take(take).ToArray());

        public Task<bool> TryAcquireLeaseAsync(Guid leaseId, DateTime nowUtc, DateTime leaseUntilUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task ReleaseLeaseAsync(Guid leaseId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<Guid> StartRunAsync(string providerCode, string triggerCode, DateTime startedAtUtc, CancellationToken cancellationToken = default)
        {
            var id = Guid.NewGuid();
            Runs.Insert(0, new CatalogSyncRunSummary(
                id, providerCode, triggerCode, "RUNNING", startedAtUtc, null,
                null, null, null, null, null, null, null));
            return Task.FromResult(id);
        }

        public Task CompleteRunAsync(CatalogSyncRunCompletion completion, CancellationToken cancellationToken = default)
        {
            var index = Runs.FindIndex(x => x.RunId == completion.RunId);
            var current = Runs[index];
            Runs[index] = current with
            {
                StatusCode = completion.StatusCode,
                CompletedAtUtc = completion.CompletedAtUtc,
                DatasetSha256 = completion.DatasetSha256,
                SourceVersion = completion.SourceVersion,
                AddedCount = completion.AddedCount,
                UpdatedCount = completion.UpdatedCount,
                DeactivatedCount = completion.DeactivatedCount,
                ActiveCount = completion.ActiveCount,
                ErrorMessage = completion.ErrorMessage
            };
            return Task.CompletedTask;
        }

        public Task UpdateStateAsync(CatalogSyncStateUpdate update, CancellationToken cancellationToken = default)
        {
            settings = settings with
            {
                LastCheckedAtUtc = update.CheckedAtUtc,
                LastSuccessfulSyncAtUtc = update.SuccessfulSyncAtUtc,
                LastDatasetSha256 = update.DatasetSha256,
                LastProviderVersion = update.ProviderVersion,
                LastProviderEtag = update.ProviderEtag,
                LastError = update.ErrorMessage
            };
            return Task.CompletedTask;
        }
    }
}
