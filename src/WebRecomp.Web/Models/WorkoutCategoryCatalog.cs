namespace WebRecomp.Web.Models;

public static class WorkoutCategoryCatalog
{
    public const string MajorStrength = "MAJOR_STRENGTH";
    public const string AccessoryStrength = "ACCESSORY_STRENGTH";
    public const string ConditioningCardio = "CONDITIONING_CARDIO";
    public const string RestRecoveryOther = "REST_RECOVERY_OTHER";

    public static readonly IReadOnlyList<string> All =
    [
        MajorStrength,
        AccessoryStrength,
        ConditioningCardio,
        RestRecoveryOther
    ];

    public static string DisplayName(string categoryCode) =>
        categoryCode.ToUpperInvariant() switch
        {
            MajorStrength => "Major Strength",
            AccessoryStrength => "Accessory Strength",
            ConditioningCardio => "Conditioning & Cardio",
            RestRecoveryOther => "Rest, Recovery & Other",
            _ => "Unknown"
        };

    public static int SortOrder(string categoryCode) =>
        categoryCode.ToUpperInvariant() switch
        {
            MajorStrength => 10,
            AccessoryStrength => 20,
            ConditioningCardio => 30,
            RestRecoveryOther => 40,
            _ => 999
        };

    public static bool IsValid(string? categoryCode) =>
        !string.IsNullOrWhiteSpace(categoryCode)
        && All.Contains(categoryCode, StringComparer.OrdinalIgnoreCase);
}
