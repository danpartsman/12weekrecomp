using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutIntelligence;

namespace WebRecomp.Web.Services;

/// <summary>Saved plan identity, independent of whether the physiology runtime has an exercise session.</summary>
public static class PlannedWorkoutNames
{
    public static string Resolve(PlannedWorkout plan, string? templateName = null, string? typeName = null)
    {
        if (plan.SourceCode == "WORKOUT_TARGET" && !string.IsNullOrWhiteSpace(plan.Notes)) return plan.Notes;
        if (!string.IsNullOrWhiteSpace(templateName)) return templateName;
        var code = Engine30WorkoutUiCatalogService.NormalizeCode(plan.WorkoutTypeCode);
        SystemWorkoutRecipeCatalog.TryGet(SystemWorkoutRecipeCatalog.NormalizePlanningSystemCode(plan.SystemWorkoutCode), out var system);
        return system?.DisplayName ?? SystemWorkoutRecipeCatalog.GetDefaultPresentationForEngine(code)?.DisplayName
            ?? typeName ?? (code == "REST" ? "Rest / Recovery" : code.Replace('_', ' '));
    }

    public static async Task<IReadOnlyDictionary<DateOnly, string>> LoadAsync(RecompDbContext db, Guid client,
        IReadOnlyList<PlannedWorkout> plans, CancellationToken ct)
    {
        var ids = plans.Where(p => p.WorkoutTemplateId.HasValue).Select(p => p.WorkoutTemplateId!.Value).ToArray();
        var templates = await db.WorkoutTemplates.AsNoTracking().Where(t => t.ClientId == client && ids.Contains(t.WorkoutTemplateId))
            .ToDictionaryAsync(t => t.WorkoutTemplateId, t => t.TemplateName, ct);
        var types = await db.WorkoutTypeDefinitions.AsNoTracking().Where(t => t.IsActive
            && db.ConfigVersions.Any(c => c.ConfigVersionId == t.ConfigVersionId && c.VersionCode == Engine30WorkoutUiCatalogService.ConfigVersionCode))
            .ToDictionaryAsync(t => t.WorkoutTypeCode, t => t.DisplayName, ct);
        return plans.GroupBy(p => p.PlannedDate).ToDictionary(g => g.Key, g =>
        {
            var plan = g.OrderByDescending(p => p.UpdatedAtUtc).First();
            return Resolve(plan, plan.WorkoutTemplateId.HasValue ? templates.GetValueOrDefault(plan.WorkoutTemplateId.Value) : null,
                types.GetValueOrDefault(Engine30WorkoutUiCatalogService.NormalizeCode(plan.WorkoutTypeCode)));
        });
    }
}
