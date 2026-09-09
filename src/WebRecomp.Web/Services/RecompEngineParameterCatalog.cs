namespace WebRecomp.Web.Services;

public sealed record RecompEngineParameterDefinition(
    string Key,
    string Category,
    string DisplayName,
    string Description,
    decimal DefaultValue,
    string Unit,
    decimal? MinimumValue,
    decimal? MaximumValue,
    string BasisType,
    string Rationale,
    string DefaultVersion,
    string IntroducedEngineVersion,
    int SortOrder);

public static class RecompEngineParameterCatalog
{
    public const string CatalogVersion = "1.0";

    // Readiness thresholds
    public const string ReadinessAdequate =
        "READINESS_ADEQUATE_RATIO";

    public const string ReadinessSlightlyReduced =
        "READINESS_SLIGHTLY_REDUCED_RATIO";

    public const string ReadinessModeratelyLow =
        "READINESS_MODERATELY_LOW_RATIO";

    public const string ReadinessLow =
        "READINESS_LOW_RATIO";

    // Functional glycogen targets
    public const string Hard3TargetBonus =
        "RESTORATION_BONUS_3_HARD_DAYS";

    public const string Hard4TargetBonus =
        "RESTORATION_BONUS_4_HARD_DAYS";

    public const string MaxFunctionalRestorationTarget =
        "MAX_FUNCTIONAL_RESTORATION_TARGET";

    // Recovery carbohydrate allocation caps
    public const string AllocationCapFuel5 =
        "ENGINE_ALLOCATION_CAP_FUEL5";

    public const string AllocationCapFuel4 =
        "ENGINE_ALLOCATION_CAP_FUEL4";

    public const string AllocationCapFuel3 =
        "ENGINE_ALLOCATION_CAP_FUEL3";

    public const string AllocationCapFuel25 =
        "ENGINE_ALLOCATION_CAP_FUEL25";

    public const string AllocationCapFuel2 =
        "ENGINE_ALLOCATION_CAP_FUEL2";

    public const string AllocationCapFuel1 =
        "ENGINE_ALLOCATION_CAP_FUEL1";

    public const string AllocationCapDefault =
        "ENGINE_ALLOCATION_CAP_DEFAULT";

    // Pre-workout rescue
    public const string PreWorkoutMinimumFuelScore =
        "ENGINE_PREWORKOUT_MIN_FUEL_SCORE";

    public const string PreWorkoutCapFuel5 =
        "ENGINE_PREWORKOUT_CAP_FUEL5";

    public const string PreWorkoutCapFuel4 =
        "ENGINE_PREWORKOUT_CAP_FUEL4";

    public const string PreWorkoutCapFuel3 =
        "ENGINE_PREWORKOUT_CAP_FUEL3";

    public const string PreWorkoutCapDefault =
        "ENGINE_PREWORKOUT_CAP_DEFAULT";

    private static readonly IReadOnlyDictionary<string, RecompEngineParameterDefinition>
        Definitions =
            new Dictionary<string, RecompEngineParameterDefinition>(
                StringComparer.OrdinalIgnoreCase)
            {
                [ReadinessAdequate] = new(
                    ReadinessAdequate,
                    "Readiness",
                    "Adequate Readiness Threshold",
                    "Minimum target-achievement ratio classified as ADEQUATE.",
                    1.00m,
                    "ratio",
                    0.80m,
                    1.20m,
                    "12WR_CALIBRATION",
                    "Represents full achievement of the workout-specific functional fueling target. This is a 12WR operating threshold, not a physiological hard boundary.",
                    "1.0",
                    "2.0",
                    10),

                [ReadinessSlightlyReduced] = new(
                    ReadinessSlightlyReduced,
                    "Readiness",
                    "Slightly Reduced Readiness Threshold",
                    "Minimum target-achievement ratio classified as SLIGHTLY_REDUCED.",
                    0.85m,
                    "ratio",
                    0.70m,
                    1.00m,
                    "12WR_CALIBRATION",
                    "Creates a modest buffer below full target attainment where training can generally remain intact while fueling and recovery are monitored.",
                    "1.0",
                    "2.0",
                    20),

                [ReadinessModeratelyLow] = new(
                    ReadinessModeratelyLow,
                    "Readiness",
                    "Moderately Low Readiness Threshold",
                    "Minimum target-achievement ratio classified as MODERATELY_LOW.",
                    0.70m,
                    "ratio",
                    0.50m,
                    0.95m,
                    "12WR_CALIBRATION",
                    "Separates a meaningful fueling deficit from more severe under-restoration. It is intentionally treated as a tunable calibration value.",
                    "1.0",
                    "2.0",
                    30),

                [ReadinessLow] = new(
                    ReadinessLow,
                    "Readiness",
                    "Low Readiness Threshold",
                    "Minimum target-achievement ratio classified as LOW; values below this are VERY_LOW.",
                    0.50m,
                    "ratio",
                    0.25m,
                    0.80m,
                    "12WR_CALIBRATION",
                    "Marks the boundary below which the modeled fuel deficit is considered substantial enough to materially affect the workout decision.",
                    "1.0",
                    "2.0",
                    40),

                [Hard3TargetBonus] = new(
                    Hard3TargetBonus,
                    "Fuel Restoration",
                    "Three Hard Days Restoration Bonus",
                    "Additional functional restoration target applied when three recent hard sessions are present.",
                    0.05m,
                    "ratio",
                    0.00m,
                    0.20m,
                    "PROGRAM_DESIGN",
                    "Adds a modest recovery requirement when recent training density is high rather than automatically cancelling another hard session.",
                    "1.0",
                    "2.0",
                    100),

                [Hard4TargetBonus] = new(
                    Hard4TargetBonus,
                    "Fuel Restoration",
                    "Four Hard Days Restoration Bonus",
                    "Additional functional restoration target applied when four recent moderate/hard days are present.",
                    0.10m,
                    "ratio",
                    0.00m,
                    0.30m,
                    "PROGRAM_DESIGN",
                    "Raises the restoration expectation when accumulated recent training demand is unusually high.",
                    "1.0",
                    "2.0",
                    110),

                [MaxFunctionalRestorationTarget] = new(
                    MaxFunctionalRestorationTarget,
                    "Fuel Restoration",
                    "Maximum Functional Restoration Target",
                    "Upper limit placed on the workout-specific functional glycogen restoration target.",
                    0.90m,
                    "ratio",
                    0.50m,
                    1.00m,
                    "12WR_CALIBRATION",
                    "Prevents the model from requiring modeled full glycogen restoration before permitting productive training.",
                    "1.0",
                    "2.0",
                    120),

                [AllocationCapFuel5] = new(
                    AllocationCapFuel5,
                    "Fuel Rescue",
                    "Fuel Score 5 Daily Rescue Cap",
                    "Maximum carbohydrate carryover allocation for the highest-demand workout class.",
                    90m,
                    "g",
                    0m,
                    150m,
                    "12WR_CALIBRATION",
                    "Limits the amount of historical carbohydrate shortfall transferred into today's target while still allowing substantial support for very high-demand training.",
                    "1.0",
                    "2.0",
                    200),

                [AllocationCapFuel4] = new(
                    AllocationCapFuel4,
                    "Fuel Rescue",
                    "Fuel Score 4 Daily Rescue Cap",
                    "Maximum carbohydrate carryover allocation for Fuel Score 4 workouts.",
                    75m,
                    "g",
                    0m,
                    125m,
                    "12WR_CALIBRATION",
                    "Provides a meaningful but bounded daily correction for a high-demand workout.",
                    "1.0",
                    "2.0",
                    210),

                [AllocationCapFuel3] = new(
                    AllocationCapFuel3,
                    "Fuel Rescue",
                    "Fuel Score 3 Daily Rescue Cap",
                    "Maximum carbohydrate carryover allocation for Fuel Score 3 workouts.",
                    65m,
                    "g",
                    0m,
                    110m,
                    "12WR_CALIBRATION",
                    "Balances restoration of recent fuel debt against the need to keep the daily nutrition target practical.",
                    "1.0",
                    "2.0",
                    220),

                [AllocationCapFuel25] = new(
                    AllocationCapFuel25,
                    "Fuel Rescue",
                    "Fuel Score 2.5 Daily Rescue Cap",
                    "Maximum carbohydrate carryover allocation for Fuel Score 2.5 workouts.",
                    55m,
                    "g",
                    0m,
                    100m,
                    "12WR_CALIBRATION",
                    "Provides a smaller rescue allowance as expected workout carbohydrate demand decreases.",
                    "1.0",
                    "2.0",
                    230),

                [AllocationCapFuel2] = new(
                    AllocationCapFuel2,
                    "Fuel Rescue",
                    "Fuel Score 2 Daily Rescue Cap",
                    "Maximum carbohydrate carryover allocation for Fuel Score 2 workouts.",
                    50m,
                    "g",
                    0m,
                    90m,
                    "12WR_CALIBRATION",
                    "Allows moderate restoration without treating a lower-demand session like a major training day.",
                    "1.0",
                    "2.0",
                    240),

                [AllocationCapFuel1] = new(
                    AllocationCapFuel1,
                    "Fuel Rescue",
                    "Fuel Score 1 Daily Rescue Cap",
                    "Maximum carbohydrate carryover allocation for low-demand training.",
                    40m,
                    "g",
                    0m,
                    80m,
                    "PROGRAM_DESIGN",
                    "Keeps recovery support available while limiting unnecessary carbohydrate escalation on low-demand days.",
                    "1.0",
                    "2.0",
                    250),

                [AllocationCapDefault] = new(
                    AllocationCapDefault,
                    "Fuel Rescue",
                    "Default Daily Rescue Cap",
                    "Fallback carbohydrate carryover cap when no higher Fuel Score rule applies.",
                    35m,
                    "g",
                    0m,
                    75m,
                    "PROGRAM_DESIGN",
                    "Provides a conservative fallback restoration allowance for very low-demand or uncategorized days.",
                    "1.0",
                    "2.0",
                    260),

                [PreWorkoutMinimumFuelScore] = new(
                    PreWorkoutMinimumFuelScore,
                    "Pre-Workout Fueling",
                    "Minimum Fuel Score for Pre-Workout Rescue",
                    "Minimum workout Fuel Score at which Engine 2.0 may prescribe a specific pre-workout rescue allocation.",
                    2m,
                    "score",
                    0m,
                    5m,
                    "PROGRAM_DESIGN",
                    "Reserves explicit pre-workout rescue recommendations for sessions with enough expected carbohydrate demand to justify targeted fueling.",
                    "1.0",
                    "2.0",
                    300),

                [PreWorkoutCapFuel5] = new(
                    PreWorkoutCapFuel5,
                    "Pre-Workout Fueling",
                    "Fuel Score 5 Pre-Workout Rescue Cap",
                    "Maximum rescue carbohydrate credited before a Fuel Score 5 workout.",
                    55m,
                    "g",
                    0m,
                    100m,
                    "12WR_CALIBRATION",
                    "Provides a substantial but practical pre-workout carbohydrate dose without assuming the entire historical shortfall can be corrected immediately before training.",
                    "1.0",
                    "2.0",
                    310),

                [PreWorkoutCapFuel4] = new(
                    PreWorkoutCapFuel4,
                    "Pre-Workout Fueling",
                    "Fuel Score 4 Pre-Workout Rescue Cap",
                    "Maximum rescue carbohydrate credited before a Fuel Score 4 workout.",
                    45m,
                    "g",
                    0m,
                    90m,
                    "12WR_CALIBRATION",
                    "Scales the pre-workout intervention downward with expected workout demand.",
                    "1.0",
                    "2.0",
                    320),

                [PreWorkoutCapFuel3] = new(
                    PreWorkoutCapFuel3,
                    "Pre-Workout Fueling",
                    "Fuel Score 3 Pre-Workout Rescue Cap",
                    "Maximum rescue carbohydrate credited before a Fuel Score 3 workout.",
                    35m,
                    "g",
                    0m,
                    75m,
                    "12WR_CALIBRATION",
                    "Serves as a moderate pre-workout intervention for demanding but not maximal-output sessions.",
                    "1.0",
                    "2.0",
                    330),

                [PreWorkoutCapDefault] = new(
                    PreWorkoutCapDefault,
                    "Pre-Workout Fueling",
                    "Default Pre-Workout Rescue Cap",
                    "Fallback maximum pre-workout rescue amount.",
                    25m,
                    "g",
                    0m,
                    60m,
                    "PROGRAM_DESIGN",
                    "Provides a conservative fallback when the workout does not match a higher-demand rescue tier.",
                    "1.0",
                    "2.0",
                    340)
            };

    public static IReadOnlyCollection<RecompEngineParameterDefinition> All =>
        Definitions.Values
            .OrderBy(x => x.Category)
            .ThenBy(x => x.SortOrder)
            .ToArray();

    public static RecompEngineParameterDefinition Get(string key)
    {
        if (!Definitions.TryGetValue(key, out var definition))
            throw new KeyNotFoundException(
                $"Unknown Recomp Engine parameter: {key}");

        return definition;
    }

    public static decimal Default(string key) =>
        Get(key).DefaultValue;
}

