namespace WebRecomp.Web.Services;

public sealed record RecompEngineCalculationDefinition(
    string Category,
    string Title,
    string Summary,
    IReadOnlyList<string> FormulaLines,
    IReadOnlyList<string> DependencyLines,
    IReadOnlyDictionary<string, string> ParameterEffects);

public static class RecompEngineCalculationCatalog
{
    private static readonly IReadOnlyDictionary<
        string,
        RecompEngineCalculationDefinition> Definitions =
        new Dictionary<
            string,
            RecompEngineCalculationDefinition>(
                StringComparer.OrdinalIgnoreCase)
        {
            ["Model Calibration - Fuel Demand"] = new(
                "Model Calibration - Fuel Demand",
                "Workout Fuel Demand",
                "Estimates the glycogen demand created by a planned or completed workout.",
                new[]
                {
                    "Body Scale = (Current Weight / Reference Weight) ^ Body Weight Glycogen Exponent",
                    "",
                    "Glycogen Demand =",
                    "Duration × Raw Glycogen/min × Body Scale × Intensity × Density × Glycogen Estimate Multiplier"
                },
                new[]
                {
                    "Workout inputs",
                    "↓",
                    "Estimated workout glycogen demand",
                    "↓",
                    "Rolling fuel demand",
                    "↓",
                    "Fuel restoration ratio",
                    "↓",
                    "Fuel readiness",
                    "↓",
                    "Carbohydrate rescue",
                    "↓",
                    "Workout recommendation"
                },
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [RecompModelCalibrationCatalog.ReferenceWeightLb] =
                        "Increasing the reference weight reduces the body-weight scaling applied to users below or near that reference and can reduce modeled fuel demand; decreasing it generally increases scaling for the same user weight.",

                    [RecompModelCalibrationCatalog.BodyWeightGlycogenExponent] =
                        "Controls how strongly body weight changes glycogen demand. A larger exponent makes differences from the reference body weight matter more.",

                    [RecompModelCalibrationCatalog.GlycogenEstimateMultiplier] =
                        "Directly scales estimated glycogen expenditure. Increasing it raises modeled glycogen demand and can lower calculated fuel readiness; decreasing it does the reverse."
                }),

            ["Model Calibration - Fuel Restoration"] = new(
                "Model Calibration - Fuel Restoration",
                "Fuel Restoration",
                "Estimates how much recent carbohydrate intake should be credited toward restoring workout-related glycogen demand.",
                new[]
                {
                    "Restoration Credit =",
                    "min(",
                    "    Prior Glycogen Demand,",
                    "    Carbs Above Base Rest Carbs",
                    "    + Base Rest Carbs × Background Carb Credit %",
                    ")",
                    "",
                    "Absolute Restoration Ratio =",
                    "Rolling Restoration Credit / Rolling Glycogen Demand"
                },
                new[]
                {
                    "Recent carbohydrate intake",
                    "↓",
                    "Restoration credit",
                    "↓",
                    "Rolling restoration ratio",
                    "↓",
                    "Fuel readiness",
                    "↓",
                    "Rescue requirement",
                    "↓",
                    "Workout recommendation"
                },
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [RecompModelCalibrationCatalog.BackgroundBaseCarbCreditPct] =
                        "Controls how much of baseline carbohydrate intake counts toward restoration. Increasing it credits more carbohydrate toward recovery and generally raises calculated fuel readiness.",

                    [RecompModelCalibrationCatalog.FuelWindowDays] =
                        "Controls how many completed days contribute to the rolling fuel state. A longer window retains older demand and restoration information for longer; a shorter window makes the model respond more quickly to recent days."
                }),

            ["Model Calibration - Workout Intensity"] = new(
                "Model Calibration - Workout Intensity",
                "RPE-Derived Workout Intensity",
                "Converts reported session RPE into the intensity factor used by workout demand calculations.",
                new[]
                {
                    "Intensity =",
                    "Clamp(",
                    "    RPE Intensity Base + Session RPE × RPE Increase Per Point,",
                    "    Minimum RPE-Derived Intensity,",
                    "    Maximum RPE-Derived Intensity",
                    ")"
                },
                new[]
                {
                    "Session RPE",
                    "↓",
                    "Modeled workout intensity",
                    "↓",
                    "Glycogen demand + workout calories",
                    "↓",
                    "Recovery load",
                    "↓",
                    "Future fuel and recovery state"
                },
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [RecompModelCalibrationCatalog.RpeIntensityBase] =
                        "Raises or lowers the starting point of the RPE-derived intensity curve.",

                    [RecompModelCalibrationCatalog.RpeIntensityPerPoint] =
                        "Controls how much each additional RPE point changes modeled intensity. Increasing it makes the model more sensitive to reported effort.",

                    [RecompModelCalibrationCatalog.RpeIntensityMinimum] =
                        "Sets the lowest intensity factor the RPE model may produce.",

                    [RecompModelCalibrationCatalog.RpeIntensityMaximum] =
                        "Sets the highest intensity factor the RPE model may produce."
                }),

            ["Model Calibration - Workout Density"] = new(
                "Model Calibration - Workout Density",
                "Workout Density",
                "Adjusts glycogen demand according to actual rest periods relative to the workout definition.",
                new[]
                {
                    "Rest Used = max(Minimum Rest Seconds, Actual Rest)",
                    "",
                    "Density Factor =",
                    "Clamp(",
                    "    Default Workout Rest / Rest Used,",
                    "    Minimum Density Factor,",
                    "    Maximum Density Factor",
                    ")"
                },
                new[]
                {
                    "Actual rest interval",
                    "↓",
                    "Workout density factor",
                    "↓",
                    "Estimated glycogen demand",
                    "↓",
                    "Fuel state",
                    "↓",
                    "Recommendation"
                },
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [RecompModelCalibrationCatalog.DensityMinimumRestSeconds] =
                        "Prevents extremely short rest entries from creating an excessive density multiplier.",

                    [RecompModelCalibrationCatalog.DensityFactorMinimum] =
                        "Limits how much long rest periods can reduce estimated workout demand.",

                    [RecompModelCalibrationCatalog.DensityFactorMaximum] =
                        "Limits how much short rest periods can increase estimated workout demand."
                }),

            ["Model Calibration - Recovery Load"] = new(
                "Model Calibration - Recovery Load",
                "Regional Recovery Load",
                "Adjusts the Push, Pull, Legs and Cardio load assigned to a completed workout using actual duration and effort.",
                new[]
                {
                    "Duration Factor =",
                    "Clamp(Actual Duration / Default Duration, Duration Minimum, Duration Maximum)",
                    "",
                    "RPE Load Factor =",
                    "Clamp(Actual Intensity / Default Intensity, RPE Load Minimum, RPE Load Maximum)",
                    "",
                    "Adjusted Regional Load =",
                    "Base Regional Load × Duration Factor × RPE Load Factor"
                },
                new[]
                {
                    "Completed workout",
                    "↓",
                    "Adjusted Push / Pull / Legs / Cardio load",
                    "↓",
                    "Recent regional workload",
                    "↓",
                    "Regional recovery state",
                    "↓",
                    "Keep / swap / modify workout decision"
                },
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [RecompModelCalibrationCatalog.DurationFactorMinimum] =
                        "Limits how much a short workout can reduce its assigned regional recovery load.",

                    [RecompModelCalibrationCatalog.DurationFactorMaximum] =
                        "Limits how much a long workout can increase its assigned regional recovery load.",

                    [RecompModelCalibrationCatalog.LoadRpeFactorMinimum] =
                        "Limits how much lower-than-expected effort can reduce regional recovery load.",

                    [RecompModelCalibrationCatalog.LoadRpeFactorMaximum] =
                        "Limits how much higher-than-expected effort can increase regional recovery load."
                }),

            ["Model Calibration - Acute Recovery"] = new(
                "Model Calibration - Acute Recovery",
                "Acute Recovery State",
                "Evaluates today's sleep, energy and soreness separately from fuel readiness and safety overrides.",
                new[]
                {
                    "Sleep Reference = Personal Baseline Sleep when available; otherwise Default Sleep Reference",
                    "",
                    "Sleep Ratio = Sleep Hours / Sleep Reference",
                    "",
                    "Sleep State = Adequate / Reduced / Severely Reduced",
                    "Energy State = Adequate / Reduced / Severely Reduced",
                    "Soreness State = Adequate / Reduced / Severely Reduced",
                    "",
                    "Acute Recovery State = most restrictive known acute-recovery signal"
                },
                new[]
                {
                    "Sleep + energy + soreness",
                    "↓",
                    "Acute recovery state",
                    "↓",
                    "Engine 2.0 session decision",
                    "",
                    "Pain / injury and illness remain separate safety gates."
                },
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [RecompModelCalibrationCatalog.SleepReferenceHours] =
                        "Used only when no valid client sleep baseline is available. Increasing it makes the same number of sleep hours represent a lower sleep ratio.",

                    [RecompModelCalibrationCatalog.SleepReducedRatio] =
                        "Sets the point below which sleep becomes a Reduced recovery signal. Increasing it makes the model more sensitive to sleep loss.",

                    [RecompModelCalibrationCatalog.SleepSevereRatio] =
                        "Sets the point below which sleep becomes Severely Reduced.",

                    [RecompModelCalibrationCatalog.EnergyReducedMax] =
                        "Sets the highest energy score treated as Reduced rather than Adequate.",

                    [RecompModelCalibrationCatalog.EnergySevereMax] =
                        "Sets the highest energy score treated as Severely Reduced.",

                    [RecompModelCalibrationCatalog.SorenessReducedMin] =
                        "Sets the soreness score at which acute recovery first becomes Reduced.",

                    [RecompModelCalibrationCatalog.SorenessSevereMin] =
                        "Sets the soreness score at which acute recovery becomes Severely Reduced."
                })
        };

    public static IReadOnlyCollection<
        RecompEngineCalculationDefinition> All =>
        Definitions.Values.ToArray();

    public static RecompEngineCalculationDefinition? Find(
        string category) =>
        Definitions.TryGetValue(category, out var definition)
            ? definition
            : null;

    public static string? ParameterEffect(
        string category,
        string parameterKey)
    {
        var calculation = Find(category);

        if (calculation is null)
            return null;

        return calculation.ParameterEffects.TryGetValue(
            parameterKey,
            out var effect)
                ? effect
                : null;
    }
}
