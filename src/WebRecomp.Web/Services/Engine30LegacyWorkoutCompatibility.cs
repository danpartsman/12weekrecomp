using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public static class Engine30LegacyWorkoutCompatibility
{
    private static readonly IReadOnlyDictionary<string, string> Engine30ToEngine2 =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["REST"] = "RECOVERY_0_RPE2",
            ["MOBILITY_RECOVERY"] = "MOBILITY__POSTURE",

            ["GENERAL_CARDIO"] = "PURE_CARDIO",
            ["EASY_CARDIO"] = "Z2_40_RPE4",
            ["ZONE2_CARDIO"] = "Z2_40_RPE4",
            ["WALKING"] = "Z2_40_RPE4",
            ["RUNNING"] = "PURE_CARDIO",
            ["TREADMILL"] = "PURE_CARDIO",
            ["CYCLING"] = "PURE_CARDIO",
            ["ROWING"] = "PURE_CARDIO",
            ["ELLIPTICAL"] = "PURE_CARDIO",
            ["STAIR_CLIMBER"] = "PURE_CARDIO",
            ["SWIMMING"] = "PURE_CARDIO",

            ["MAJOR_LEGS"] = "MAJOR_LEGS",
            ["MAJOR_PUSH"] = "MAJOR_PUSH",
            ["MAJOR_PULL"] = "MAJOR_PULL",
            ["FULL_BODY_STRENGTH"] = "MAJOR_MIXED",
            ["ACCESSORY"] = "ACCESSORY_MIXED",
            ["RESISTANCE_CIRCUIT"] = "ACCESSORY_MIXED",

            ["RUNNING_INTERVALS"] = "HIIT_45_RPE9",
            ["CYCLING_INTERVALS"] = "HIIT_45_RPE9",
            ["ROWING_INTERVALS"] = "HIIT_45_RPE9",
            ["MIXED_CONDITIONING"] = "HIIT_45_RPE9",
            ["TRUE_HIIT"] = "HIIT_45_RPE9",

            ["ORANGETHEORY"] = "OTF_STANDARD",
            ["MIXED_GYM_SESSION"] = "ACCESSORY_MIXED_PLUS_CARDIO",
            ["CUSTOM_COMPOSITE"] = "ACC_PLUS_Z2_75_RPE6"
        };

    public static IReadOnlyCollection<string> Engine30Codes =>
        Engine30ToEngine2.Keys.ToArray();

    public static IReadOnlyCollection<string> Engine2TargetCodes =>
        Engine30ToEngine2.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static string ToEngine2(string? workoutTypeCode)
    {
        if (string.IsNullOrWhiteSpace(workoutTypeCode))
            return string.Empty;

        var normalized = workoutTypeCode.Trim();

        return Engine30ToEngine2.TryGetValue(
            normalized,
            out var mapped)
                ? mapped
                : normalized;
    }

    public static bool TryResolve(
        IReadOnlyDictionary<string, WorkoutTypeDefinition> definitions,
        string? workoutTypeCode,
        out WorkoutTypeDefinition type)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        if (!string.IsNullOrWhiteSpace(workoutTypeCode)
            && definitions.TryGetValue(
                workoutTypeCode.Trim(),
                out var exact))
        {
            type = exact;
            return true;
        }

        var mapped = ToEngine2(workoutTypeCode);

        if (!string.IsNullOrWhiteSpace(mapped)
            && definitions.TryGetValue(
                mapped,
                out var fallback))
        {
            type = fallback;
            return true;
        }

        type = null!;
        return false;
    }
}
