using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed class ConfigurationService(RecompDbContext db)
{
    public async Task<ConfigVersion> GetActiveVersionAsync(CancellationToken cancellationToken = default)
    {
        return await db.ConfigVersions
            .Where(x => x.StatusCode == "PUBLISHED")
            .OrderByDescending(x => x.EffectiveFromUtc)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetSettingsAsync(
        Guid configVersionId,
        CancellationToken cancellationToken = default)
    {
        return await db.GlobalSettings
            .Where(x => x.ConfigVersionId == configVersionId && x.DecimalValue != null)
            .ToDictionaryAsync(x => x.SettingCode, x => x.DecimalValue!.Value, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, WorkoutTypeDefinition>> GetWorkoutTypesAsync(
        Guid configVersionId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.WorkoutTypeDefinitions
            .Where(x => x.ConfigVersionId == configVersionId && x.IsActive)
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.WorkoutTypeCode, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<WorkoutTypeDefinition>> GetWorkoutTypeListAsync(
        Guid configVersionId,
        CancellationToken cancellationToken = default)
    {
        return await db.WorkoutTypeDefinitions
            .Where(x => x.ConfigVersionId == configVersionId && x.IsActive)
            .OrderByDescending(x => x.CountsDedicatedHeavy)
            .ThenByDescending(x => x.CountsLightStrength)
            .ThenByDescending(x => x.CountsPrimaryConditioning)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<PlanDefinition> GetPlanAsync(
        Guid configVersionId,
        string planCode,
        CancellationToken cancellationToken = default)
    {
        return await db.PlanDefinitions.SingleAsync(
            x => x.ConfigVersionId == configVersionId && x.PlanCode == planCode && x.IsActive,
            cancellationToken);
    }

    public static decimal Setting(IReadOnlyDictionary<string, decimal> settings, string code, decimal fallback)
        => settings.TryGetValue(code, out var value) ? value : fallback;
}
