using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Single user-facing workout catalog for Engine 3.0.
///
/// Weekly Plan and Daily Entry must use the same CFG-3000.0 vocabulary that
/// the Engine 3.0 Admin calibration editor and runtime profile provider use.
/// Legacy Engine 2.0 workout definitions remain untouched and are used only
/// through the explicit compatibility mapping where an older display/calculation
/// path still requires a legacy definition during the Engine 3.0 shadow phase.
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

    /// <summary>
    /// Temporary shadow-phase bridge for legacy display math only.
    /// Engine 3.0 runtime calculations must use the active Admin profile instead.
    /// </summary>
    public static string ToLegacyCompatibilityCode(string? engine30Code)
    {
        var code = NormalizeCode(engine30Code);

        return code switch
        {
            "REST" => "RECOVERY_0_RPE2",
            "MOBILITY_RECOVERY" => "MOBILITY__POSTURE",

            "EASY_CARDIO"
                or "ZONE2_CARDIO"
                or "WALKING" => "Z2_40_RPE4",

            "GENERAL_CARDIO"
                or "RUNNING"
                or "TREADMILL"
                or "CYCLING"
                or "ROWING"
                or "ELLIPTICAL"
                or "STAIR_CLIMBER"
                or "SWIMMING" => "PURE_CARDIO",

            "MAJOR_LEGS" => "MAJOR_LEGS",
            "MAJOR_PUSH" => "MAJOR_PUSH",
            "MAJOR_PULL" => "MAJOR_PULL",
            "FULL_BODY_STRENGTH" => "MAJOR_60_RPE8",

            "ACCESSORY"
                or "RESISTANCE_CIRCUIT" => "ACCESSORY_45_RPE7",

            "RUNNING_INTERVALS"
                or "CYCLING_INTERVALS"
                or "ROWING_INTERVALS"
                or "MIXED_CONDITIONING"
                or "TRUE_HIIT" => "HIIT_45_RPE9",

            "ORANGETHEORY" => "OTF_STANDARD",
            "MIXED_GYM_SESSION" => "MAJOR_60_RPE8",
            "CUSTOM_COMPOSITE" => "OTF_STANDARD",

            _ => code
        };
    }
}
