namespace WebRecomp.Web.Services.WorkoutIntelligence;

public sealed record CardioActivityDefinition(
    string ActivityCode,
    string DisplayName,
    string EngineWorkoutTypeCode,
    string SearchTerms);

/// <summary>
/// Canonical v1012 cardio activity picker. The UI presents one Cardio workout;
/// this catalog resolves the user's actual activity back to an existing,
/// calibrated Engine 3.0 workout type. No Engine 3.0 physiology is changed.
/// </summary>
public static class CardioActivityCatalog
{
    public const string PresentationSystemCode = "CARDIO";

    private static readonly IReadOnlyList<CardioActivityDefinition> Definitions =
    [
        new("GENERAL", "General cardio", "GENERAL_CARDIO", "general cardio machine"),
        new("EASY", "Easy / recovery cardio", "EASY_CARDIO", "easy recovery low intensity"),
        new("ZONE2", "Zone 2 cardio", "ZONE2_CARDIO", "zone 2 aerobic steady"),
        new("WALK", "Walking", "WALKING", "walk walking outdoor indoor"),
        new("HIKE", "Hiking", "WALKING", "hike hiking trail"),
        new("RUN", "Running", "RUNNING", "run running road track"),
        new("TREADMILL", "Treadmill", "TREADMILL", "treadmill incline walking running"),
        new("CYCLE", "Cycling / stationary bike", "CYCLING", "cycle cycling bike bicycle spin stationary"),
        new("ROW", "Rowing", "ROWING", "row rowing erg rower"),
        new("ELLIPTICAL", "Elliptical", "ELLIPTICAL", "elliptical cross trainer"),
        new("STAIRS", "Stair climber / stepper", "STAIR_CLIMBER", "stairs stair climber stepper stepmill"),
        new("SWIM", "Swimming", "SWIMMING", "swim swimming pool laps"),
        new("RUN_INTERVALS", "Running intervals", "RUNNING_INTERVALS", "running intervals sprint run"),
        new("CYCLE_INTERVALS", "Cycling intervals", "CYCLING_INTERVALS", "cycling intervals bike spin"),
        new("ROW_INTERVALS", "Rowing intervals", "ROWING_INTERVALS", "rowing intervals erg"),
        new("HIIT", "HIIT / sprint intervals", "TRUE_HIIT", "hiit sprint intervals conditioning")
    ];

    private static readonly IReadOnlyDictionary<string, CardioActivityDefinition> ByCode =
        Definitions.ToDictionary(x => x.ActivityCode, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> PresentationCollapsedEngineCodes =
        Definitions.Select(x => x.EngineWorkoutTypeCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<CardioActivityDefinition> All => Definitions;

    public static bool TryGet(string? activityCode, out CardioActivityDefinition? definition)
    {
        definition = null;
        return !string.IsNullOrWhiteSpace(activityCode)
            && ByCode.TryGetValue(activityCode.Trim(), out definition);
    }

    public static bool IsCollapsedCardioEngineCode(string? engineWorkoutTypeCode)
        => !string.IsNullOrWhiteSpace(engineWorkoutTypeCode)
           && PresentationCollapsedEngineCodes.Contains(engineWorkoutTypeCode.Trim());

    public static string? InferActivityCode(string? engineWorkoutTypeCode, string? workoutName = null)
    {
        if (!string.IsNullOrWhiteSpace(workoutName))
        {
            var exact = Definitions.FirstOrDefault(x =>
                x.DisplayName.Equals(workoutName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact.ActivityCode;
        }

        if (string.IsNullOrWhiteSpace(engineWorkoutTypeCode))
            return null;

        return Definitions.FirstOrDefault(x =>
            x.EngineWorkoutTypeCode.Equals(engineWorkoutTypeCode.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.ActivityCode;
    }
}
