using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.Services.Engine30;

public sealed record Engine30OccupationActivityOption(
    string Code,
    string DisplayName,
    decimal EffectiveOccupationalActivityFactor,
    string Description);

public sealed record Engine30LifestyleActivityOption(
    string Code,
    string DisplayName,
    string Description);

/// <summary>
/// DRAFT Engine 3.0 classification catalog for recurring non-workout activity.
/// These factors are versionable calibration inputs, not user-entered calorie values.
/// </summary>
public static class Engine30ActivityProfileCatalog
{
    private static readonly Engine30OccupationActivityOption[] OccupationOptions =
    [
        new("HIGHLY_SEDENTARY", "Mostly seated / desk work", 0.30m,
            "Nearly all work time is seated with only brief routine movement."),
        new("SEDENTARY_LIGHT", "Mostly seated + light movement", 0.40m,
            "Mostly seated, with some standing and short walking during the work period."),
        new("SEDENTARY_MIXED", "Seated + regular standing/walking", 0.50m,
            "Substantial seated time with recurring standing, walking, setup, inspections, or similar movement."),
        new("TYPICAL_MIXED", "Mixed sitting / standing / walking", 0.60m,
            "Work regularly alternates among sitting, standing, and walking."),
        new("ACTIVE_LIGHT", "On feet most of shift / light physical", 0.75m,
            "Most work time is standing or walking with light physical tasks."),
        new("VERY_ACTIVE", "Active physical work", 0.95m,
            "Frequent walking, carrying, lifting, or sustained physical activity."),
        new("HEAVY_PHYSICAL", "Heavy manual work", 1.00m,
            "Sustained heavy manual labor such as construction or similarly demanding work.")
    ];

    private static readonly Engine30LifestyleActivityOption[] LifestyleOptions =
    [
        new("MINIMAL", "Minimal", "Little recurring activity outside work and formal workouts."),
        new("LIGHT", "Light", "Some recurring walking, errands, yardwork, or household activity."),
        new("MODERATE", "Moderate", "Regular meaningful activity outside work and formal workouts."),
        new("VERY_ACTIVE", "Very active", "A consistently active non-work lifestyle in addition to work and formal workouts.")
    ];

    public static IReadOnlyList<Engine30OccupationActivityOption> Occupations => OccupationOptions;
    public static IReadOnlyList<Engine30LifestyleActivityOption> Lifestyles => LifestyleOptions;

    public static Engine30OccupationActivityOption ResolveOccupation(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Occupation activity level is required.", nameof(code));

        var normalized = code.Trim().ToUpperInvariant();
        return OccupationOptions.SingleOrDefault(x => x.Code == normalized)
            ?? throw new ArgumentException($"Unsupported occupation activity level '{code}'.", nameof(code));
    }

    public static Engine30LifestyleActivityOption ResolveLifestyle(string? code)
    {
        var normalized = string.IsNullOrWhiteSpace(code)
            ? "MINIMAL"
            : code.Trim().ToUpperInvariant();

        return LifestyleOptions.SingleOrDefault(x => x.Code == normalized)
            ?? throw new ArgumentException($"Unsupported lifestyle activity level '{code}'.", nameof(code));
    }
}

public sealed record Engine30ClientOccupationProfileRow(
    Guid ClientOccupationalActivityId,
    string OccupationName,
    string OccupationActivityCode,
    string OccupationActivityDisplayName,
    decimal TypicalHoursPerWeek,
    bool IsPrimary,
    decimal EffectiveOccupationalActivityFactor,
    DateOnly EffectiveFrom);

public sealed record Engine30ClientHabitualActivityProfile(
    string LifestyleActivityCode,
    string LifestyleActivityDisplayName,
    IReadOnlyList<Engine30ClientOccupationProfileRow> Roles);

/// <summary>
/// Reads the user's current habitual-activity profile and converts the stored
/// occupation classifications into the explicit Engine 3.0 role inputs used
/// by Bucket #2. Steps are intentionally not emitted as a core energy input.
/// </summary>
public sealed class Engine30ClientActivityProfileService(RecompDbContext db)
{
    public async Task<Engine30ClientHabitualActivityProfile> GetCurrentProfileAsync(
        Guid clientId,
        DateOnly actionDate,
        string? lifestyleActivityCode,
        CancellationToken cancellationToken = default)
    {
        var lifestyle = Engine30ActivityProfileCatalog.ResolveLifestyle(lifestyleActivityCode);

        var roles = await db.ClientOccupationalActivities
            .AsNoTracking()
            .Where(x =>
                x.ClientId == clientId
                && x.EffectiveFrom <= actionDate
                && (x.EffectiveTo == null || x.EffectiveTo >= actionDate))
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.OccupationName)
            .ToListAsync(cancellationToken);

        var rows = roles.Select(x =>
        {
            var option = Engine30ActivityProfileCatalog.ResolveOccupation(x.OccupationActivityCode);
            return new Engine30ClientOccupationProfileRow(
                x.ClientOccupationalActivityId,
                x.OccupationName,
                option.Code,
                option.DisplayName,
                x.TypicalHoursPerWeek,
                x.IsPrimary,
                option.EffectiveOccupationalActivityFactor,
                x.EffectiveFrom);
        }).ToArray();

        return new Engine30ClientHabitualActivityProfile(
            lifestyle.Code,
            lifestyle.DisplayName,
            rows);
    }

    public async Task<IReadOnlyList<Engine30OccupationalRoleInput>> BuildRuntimeRolesAsync(
        Guid clientId,
        DateOnly actionDate,
        CancellationToken cancellationToken = default)
    {
        var roles = await db.ClientOccupationalActivities
            .AsNoTracking()
            .Where(x =>
                x.ClientId == clientId
                && x.EffectiveFrom <= actionDate
                && (x.EffectiveTo == null || x.EffectiveTo >= actionDate))
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.OccupationName)
            .ToListAsync(cancellationToken);

        return roles.Select(x =>
        {
            var option = Engine30ActivityProfileCatalog.ResolveOccupation(x.OccupationActivityCode);
            return new Engine30OccupationalRoleInput(
                x.OccupationName,
                option.Code,
                x.TypicalHoursPerWeek,
                option.EffectiveOccupationalActivityFactor);
        }).ToArray();
    }
}
