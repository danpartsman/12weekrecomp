using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record RecompEngineParameterValue(
    RecompEngineParameterDefinition Definition,
    decimal EffectiveValue,
    decimal? StoredValue,
    bool Uses12WrDefault);

public sealed record RecompEngineConfigurationSnapshot(
    ConfigVersion ConfigVersion,
    IReadOnlyDictionary<string, decimal> Settings,
    IReadOnlyList<RecompEngineParameterValue> Parameters,
    IReadOnlyList<RecompEngineValidationIssue> ValidationIssues)
{
    public bool IsValid => ValidationIssues.Count == 0;
}

public sealed class RecompEngineConfigurationService(
    RecompDbContext db,
    ConfigurationService configuration)
{
    public async Task<RecompEngineConfigurationSnapshot> LoadByVersionCodeAsync(
        string versionCode,
        CancellationToken cancellationToken = default)
    {
        var version = await db.ConfigVersions
            .SingleOrDefaultAsync(
                x => x.VersionCode == versionCode,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Configuration version {versionCode} was not found.");

        return await LoadAsync(
            version.ConfigVersionId,
            cancellationToken);
    }

    public async Task<RecompEngineConfigurationSnapshot> LoadAsync(
        Guid configVersionId,
        CancellationToken cancellationToken = default)
    {
        var version = await db.ConfigVersions
            .SingleAsync(
                x => x.ConfigVersionId == configVersionId,
                cancellationToken);

        var storedSettings =
            await configuration.GetSettingsAsync(
                configVersionId,
                cancellationToken);

        var effectiveSettings =
            new Dictionary<string, decimal>(
                storedSettings,
                StringComparer.OrdinalIgnoreCase);

        var definitions =
            RecompModelCalibrationCatalog.All
                .Concat(RecompEngineParameterCatalog.All)
                .Concat(RecompTrainingPrescriptionCatalog.All)
                .GroupBy(
                    x => x.Key,
                    StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToArray();

        var parameters =
            new List<RecompEngineParameterValue>();

        foreach (var definition in definitions)
        {
            var hasStoredValue =
                storedSettings.TryGetValue(
                    definition.Key,
                    out var storedValue);

            var effectiveValue = hasStoredValue
                ? storedValue
                : definition.DefaultValue;

            effectiveSettings[definition.Key] =
                effectiveValue;

            parameters.Add(
                new RecompEngineParameterValue(
                    definition,
                    effectiveValue,
                    hasStoredValue
                        ? storedValue
                        : null,
                    !hasStoredValue));
        }

        var issues =
            RecompEngineConfigurationValidator
                .Validate(effectiveSettings)
                .Concat(
                    RecompModelCalibrationValidator
                        .Validate(effectiveSettings))
                .Concat(
                    RecompTrainingPrescriptionValidator
                        .Validate(effectiveSettings))
                .ToArray();

        return new RecompEngineConfigurationSnapshot(
            version,
            effectiveSettings,
            parameters
                .OrderBy(x =>
                    x.Definition.Category.StartsWith(
                        "Model Calibration",
                        StringComparison.OrdinalIgnoreCase)
                            ? 0
                            : 1)
                .ThenBy(x => x.Definition.Category)
                .ThenBy(x => x.Definition.SortOrder)
                .ToArray(),
            issues);
    }
}

