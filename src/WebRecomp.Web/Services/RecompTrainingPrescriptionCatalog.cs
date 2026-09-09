namespace WebRecomp.Web.Services;

public static class RecompTrainingPrescriptionCatalog
{
    public const string CatalogVersion = "1.0";

    // ---------------------------------------------------------
    // Resistance-training volume estimation
    // ---------------------------------------------------------

    public const string SetExecutionSeconds =
        "ENGINE_TRAINING_SET_EXECUTION_SEC";

    public const string SetSetupResetSeconds =
        "ENGINE_TRAINING_SET_SETUP_RESET_SEC";

    public const string HeavyWarmupMinutes =
        "ENGINE_TRAINING_HEAVY_WARMUP_MIN";

    public const string LightWarmupMinutes =
        "ENGINE_TRAINING_LIGHT_WARMUP_MIN";

    public const string HeavyTransitionPercent =
        "ENGINE_TRAINING_HEAVY_TRANSITION_PCT";

    public const string LightTransitionPercent =
        "ENGINE_TRAINING_LIGHT_TRANSITION_PCT";

    public const string HeavyVolumeFactor =
        "ENGINE_TRAINING_HEAVY_VOLUME_FACTOR";

    public const string LightVolumeFactor =
        "ENGINE_TRAINING_LIGHT_VOLUME_FACTOR";


    // ---------------------------------------------------------
    // Recommended working-set RPE
    // RPE = Rate of Perceived Exertion
    // ---------------------------------------------------------

    public const string HeavyRpeMinimum =
        "ENGINE_TRAINING_HEAVY_RPE_MIN";

    public const string HeavyRpeMaximum =
        "ENGINE_TRAINING_HEAVY_RPE_MAX";

    public const string LightRpeMinimum =
        "ENGINE_TRAINING_LIGHT_RPE_MIN";

    public const string LightRpeMaximum =
        "ENGINE_TRAINING_LIGHT_RPE_MAX";


    // ---------------------------------------------------------
    // Aerobic / conditioning prescriptions
    // ---------------------------------------------------------

    public const string Zone2RpeMinimum =
        "ENGINE_ZONE2_RPE_MIN";

    public const string Zone2RpeMaximum =
        "ENGINE_ZONE2_RPE_MAX";

    public const string ConditioningRpeMinimum =
        "ENGINE_CONDITIONING_RPE_MIN";

    public const string ConditioningRpeMaximum =
        "ENGINE_CONDITIONING_RPE_MAX";


    private static readonly IReadOnlyDictionary<
        string,
        RecompEngineParameterDefinition> Definitions =
        new Dictionary<
            string,
            RecompEngineParameterDefinition>(
                StringComparer.OrdinalIgnoreCase)
        {
            [SetExecutionSeconds] = new(
                SetExecutionSeconds,
                "Model Calibration - Training Volume",
                "Average Set Execution Time",
                "Estimated time spent actually performing one working set.",
                35m,
                "seconds",
                20m,
                60m,
                "12WR_CALIBRATION",
                "Used when translating resistance-training session duration into an approximate number of working sets.",
                "1.0",
                "2.0",
                500),

            [SetSetupResetSeconds] = new(
                SetSetupResetSeconds,
                "Model Calibration - Training Volume",
                "Set Setup / Reset Time",
                "Average additional time around each working set for positioning, loading, adjusting equipment and resetting.",
                15m,
                "seconds",
                5m,
                45m,
                "12WR_CALIBRATION",
                "Prevents set-volume estimates from assuming that all non-rest time is actual lifting time.",
                "1.0",
                "2.0",
                510),

            [HeavyWarmupMinutes] = new(
                HeavyWarmupMinutes,
                "Model Calibration - Training Volume",
                "Major Strength Warm-up",
                "Minutes reserved for general and exercise-specific warm-up before the main working volume.",
                6m,
                "minutes",
                0m,
                20m,
                "12WR_CALIBRATION",
                "Subtracts warm-up time before estimating the working-set capacity of a major resistance session.",
                "1.0",
                "2.0",
                520),

            [LightWarmupMinutes] = new(
                LightWarmupMinutes,
                "Model Calibration - Training Volume",
                "Light Strength Warm-up",
                "Minutes reserved for warm-up before a lighter or accessory resistance session.",
                5m,
                "minutes",
                0m,
                15m,
                "12WR_CALIBRATION",
                "Allows lighter training sessions to reserve less time for preparation than major strength sessions.",
                "1.0",
                "2.0",
                530),

            [HeavyTransitionPercent] = new(
                HeavyTransitionPercent,
                "Model Calibration - Training Volume",
                "Major Session Transition Allowance",
                "Fraction of total session time reserved for changing exercises, loading equipment, moving stations and other between-exercise setup.",
                0.10m,
                "ratio",
                0.00m,
                0.30m,
                "12WR_CALIBRATION",
                "Accounts for session-level setup time that is not captured by the normal between-set rest interval.",
                "1.0",
                "2.0",
                540),

            [LightTransitionPercent] = new(
                LightTransitionPercent,
                "Model Calibration - Training Volume",
                "Light Session Transition Allowance",
                "Fraction of a light resistance session reserved for exercise changes and equipment setup.",
                0.10m,
                "ratio",
                0.00m,
                0.30m,
                "12WR_CALIBRATION",
                "Separates exercise-change time from the time available for actual working sets.",
                "1.0",
                "2.0",
                550),

            [HeavyVolumeFactor] = new(
                HeavyVolumeFactor,
                "Model Calibration - Training Volume",
                "Major Strength Volume Factor",
                "Multiplier applied to calculated working-set capacity for a major strength prescription.",
                1.00m,
                "factor",
                0.60m,
                1.20m,
                "12WR_CALIBRATION",
                "Lets 12WR calibrate the final recommended volume without changing the underlying time-and-rest calculation.",
                "1.0",
                "2.0",
                560),

            [LightVolumeFactor] = new(
                LightVolumeFactor,
                "Model Calibration - Training Volume",
                "Light Strength Volume Factor",
                "Multiplier applied to calculated working-set capacity for a lighter or accessory strength prescription.",
                0.75m,
                "factor",
                0.40m,
                1.00m,
                "12WR_CALIBRATION",
                "Deliberately reduces total working volume on lighter sessions even when session duration could physically accommodate more sets.",
                "1.0",
                "2.0",
                570),

            [HeavyRpeMinimum] = new(
                HeavyRpeMinimum,
                "Model Calibration - Training Intensity",
                "Major Strength RPE Minimum",
                "Lower end of the recommended working-set Rate of Perceived Exertion range for major strength sessions.",
                7m,
                "RPE",
                1m,
                10m,
                "12WR_CALIBRATION",
                "Defines the lower bound of the normal working-set effort prescription for a major training day.",
                "1.0",
                "2.0",
                580),

            [HeavyRpeMaximum] = new(
                HeavyRpeMaximum,
                "Model Calibration - Training Intensity",
                "Major Strength RPE Maximum",
                "Upper end of the recommended working-set Rate of Perceived Exertion range for major strength sessions.",
                8m,
                "RPE",
                1m,
                10m,
                "12WR_CALIBRATION",
                "Provides a hard-training stimulus without making routine major sessions maximum-effort sessions.",
                "1.0",
                "2.0",
                590),

            [LightRpeMinimum] = new(
                LightRpeMinimum,
                "Model Calibration - Training Intensity",
                "Light Strength RPE Minimum",
                "Lower end of the recommended working-set Rate of Perceived Exertion range for lighter strength sessions.",
                5m,
                "RPE",
                1m,
                10m,
                "12WR_CALIBRATION",
                "Maintains meaningful training stimulus while keeping fatigue below a major strength session.",
                "1.0",
                "2.0",
                600),

            [LightRpeMaximum] = new(
                LightRpeMaximum,
                "Model Calibration - Training Intensity",
                "Light Strength RPE Maximum",
                "Upper end of the recommended working-set Rate of Perceived Exertion range for lighter strength sessions.",
                7m,
                "RPE",
                1m,
                10m,
                "12WR_CALIBRATION",
                "Prevents a planned light session from unintentionally becoming another major high-fatigue training day.",
                "1.0",
                "2.0",
                610),

            [Zone2RpeMinimum] = new(
                Zone2RpeMinimum,
                "Model Calibration - Aerobic Conditioning",
                "Zone 2 RPE Minimum",
                "Lower end of the whole-session Rate of Perceived Exertion range typically associated with the 12WR Zone 2 prescription.",
                3m,
                "RPE",
                1m,
                10m,
                "12WR_CALIBRATION",
                "Provides a simple perceived-effort companion to conversational and aerobic Zone 2 guidance.",
                "1.0",
                "2.0",
                620),

            [Zone2RpeMaximum] = new(
                Zone2RpeMaximum,
                "Model Calibration - Aerobic Conditioning",
                "Zone 2 RPE Maximum",
                "Upper end of the whole-session Rate of Perceived Exertion range for Zone 2 work.",
                4m,
                "RPE",
                1m,
                10m,
                "12WR_CALIBRATION",
                "Helps prevent an aerobic development session from drifting into substantially harder conditioning.",
                "1.0",
                "2.0",
                630),

            [ConditioningRpeMinimum] = new(
                ConditioningRpeMinimum,
                "Model Calibration - Aerobic Conditioning",
                "Hard Conditioning RPE Minimum",
                "Lower end of the whole-session Rate of Perceived Exertion range for hard conditioning.",
                7m,
                "RPE",
                1m,
                10m,
                "12WR_CALIBRATION",
                "Defines the minimum intended effort for a session whose purpose is hard cardiovascular or mixed conditioning.",
                "1.0",
                "2.0",
                640),

            [ConditioningRpeMaximum] = new(
                ConditioningRpeMaximum,
                "Model Calibration - Aerobic Conditioning",
                "Hard Conditioning RPE Maximum",
                "Upper end of the whole-session Rate of Perceived Exertion range for hard conditioning.",
                9m,
                "RPE",
                1m,
                10m,
                "12WR_CALIBRATION",
                "Allows genuinely hard conditioning while distinguishing routine training from an all-out maximum session.",
                "1.0",
                "2.0",
                650)
        };


    public static IReadOnlyCollection<
        RecompEngineParameterDefinition> All =>
        Definitions.Values.ToArray();


    public static decimal Default(string key)
    {
        if (!Definitions.TryGetValue(key, out var definition))
            throw new KeyNotFoundException(
                $"Unknown Training Prescription parameter: {key}");

        return definition.DefaultValue;
    }


    public static RecompEngineParameterDefinition? Find(
        string key) =>
        Definitions.TryGetValue(key, out var definition)
            ? definition
            : null;
}
