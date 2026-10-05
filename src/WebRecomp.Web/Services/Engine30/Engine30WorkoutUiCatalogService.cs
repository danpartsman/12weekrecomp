using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Single user-facing workout catalog for Engine 3.0.
///
/// Weekly Plan, Daily Tracking, Workouts, and Dashboard use the same
/// CFG-3000.0 vocabulary. Historical Engine 2.x workout codes are normalized
/// forward into current canonical codes when old records are read; current
/// runtime code is never translated backward into Engine 2.x.
/// </summary>
public sealed class Engine30WorkoutUiCatalogService(RecompDbContext db)
{
    public const string ConfigVersionCode = "CFG-3000.0";

    public async Task<Guid> GetConfigVersionIdAsync(
        CancellationToken cancellationToken = default)
    {
        var id = await db.ConfigVersions
            .AsNoTracking()
            .Where(x => x.VersionCode == ConfigVersionCode)
            .Select(x => (Guid?)x.ConfigVersionId)
            .SingleOrDefaultAsync(cancellationToken);

        return id
            ?? throw new InvalidOperationException(
                $"{ConfigVersionCode} was not found. Run the Engine 3.0 workout catalog migration before using the Engine 3.0 workout UI.");
    }

    public async Task<IReadOnlyList<WorkoutTypeDefinition>> GetActiveListAsync(
        CancellationToken cancellationToken = default)
    {
        var configVersionId =
            await GetConfigVersionIdAsync(cancellationToken);

        return await db.WorkoutTypeDefinitions
            .AsNoTracking()
            .Where(x =>
                x.ConfigVersionId == configVersionId
                && x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, WorkoutTypeDefinition>> GetActiveMapAsync(
        CancellationToken cancellationToken = default)
    {
        var rows =
            await GetActiveListAsync(cancellationToken);

        return rows.ToDictionary(
            x => x.WorkoutTypeCode,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<HashSet<string>> GetActiveCodeSetAsync(
        CancellationToken cancellationToken = default)
        => (await GetActiveListAsync(cancellationToken))
            .Select(x => x.WorkoutTypeCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps historical Engine 2.x workout codes into the nearest Engine 3.0
    /// taxonomy code for UI presentation and future-week copying.
    ///
    /// This does not rewrite historical raw workout records by itself.
    /// </summary>
    public static string NormalizeCode(string? workoutTypeCode)
    {
        var code =
            (workoutTypeCode ?? "")
                .Trim()
                .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(code))
            return "";

        return code switch
        {
            // Recovery / mobility
            "RECOVERY_0_RPE2" => "REST",
            "MOBILITY__POSTURE" => "MOBILITY_RECOVERY",

            // Continuous cardio
            "ZONE_2" or "Z2_40_RPE4" => "ZONE2_CARDIO",
            "PURE_CARDIO"
                or "PICKLEBALL_120_RPE6"
                or "YARDWORK_MODERATE"
                or "YARDWORK_HEAVY" => "GENERAL_CARDIO",

            // Resistance
            "MAJOR_MIXED"
                or "MAJOR_60_RPE8"
                or "OTF_STRENGTH_TOTAL" => "FULL_BODY_STRENGTH",

            "ACCESSORY_UPPER"
                or "ACCESSORY_LOWER"
                or "ACCESSORY_MIXED"
                or "ACCESSORY_45_RPE7"
                or "OTF_STRENGTH_UPPER"
                or "OTF_STRENGTH_LOWER" => "ACCESSORY",

            "ACC_PLUS_Z2_75_RPE6"
                or "ACCESSORY_MIXED_PLUS_CARDIO" => "MIXED_GYM_SESSION",

            // Intervals / composite
            "HIIT_45_RPE9" => "TRUE_HIIT",
            "LONG_MIXED_75_RPE9" => "MIXED_CONDITIONING",
            "OTF_STANDARD" => "ORANGETHEORY",

            // Codes already shared with Engine 3.0 pass through unchanged.
            _ => code
        };
    }


}
