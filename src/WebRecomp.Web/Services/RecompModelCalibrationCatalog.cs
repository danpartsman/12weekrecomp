namespace WebRecomp.Web.Services;

public static class RecompModelCalibrationCatalog
{
    public const string CatalogVersion = "1.0";

    // Existing v1001 model settings.
    public const string ReferenceWeightLb =
        "REFERENCE_WEIGHT_LB";

    public const string GlycogenEstimateMultiplier =
        "GLYCOGEN_ESTIMATE_MULTIPLIER";

    public const string BodyWeightGlycogenExponent =
        "BODY_WEIGHT_GLYCOGEN_EXPONENT";

    public const string BackgroundBaseCarbCreditPct =
        "BACKGROUND_BASE_CARB_CREDIT_PCT";

    public const string FuelWindowDays =
        "FUEL_WINDOW_DAYS";

    // Engine 2.0 model coefficients that were previously hardcoded.
    public const string RpeIntensityBase =
        "ENGINE_RPE_INTENSITY_BASE";

    public const string RpeIntensityPerPoint =
        "ENGINE_RPE_INTENSITY_PER_POINT";

    public const string RpeIntensityMinimum =
        "ENGINE_RPE_INTENSITY_MIN";

    public const string RpeIntensityMaximum =
        "ENGINE_RPE_INTENSITY_MAX";

    public const string DensityMinimumRestSeconds =
        "ENGINE_DENSITY_MIN_REST_SEC";

    public const string DensityFactorMinimum =
        "ENGINE_DENSITY_FACTOR_MIN";

    public const string DensityFactorMaximum =
        "ENGINE_DENSITY_FACTOR_MAX";

    public const string DurationFactorMinimum =
        "ENGINE_DURATION_FACTOR_MIN";

    public const string DurationFactorMaximum =
        "ENGINE_DURATION_FACTOR_MAX";

    public const string LoadRpeFactorMinimum =
        "ENGINE_LOAD_RPE_FACTOR_MIN";

    public const string LoadRpeFactorMaximum =
        "ENGINE_LOAD_RPE_FACTOR_MAX";

    // Engine 2.0 acute recovery calibration.
    public const string SleepReferenceHours =
        "ENGINE_SLEEP_REFERENCE_HOURS";

    public const string SleepReducedRatio =
        "ENGINE_SLEEP_REDUCED_RATIO";

    public const string SleepSevereRatio =
        "ENGINE_SLEEP_SEVERE_RATIO";

    public const string EnergyReducedMax =
        "ENGINE_ENERGY_REDUCED_MAX";

    public const string EnergySevereMax =
        "ENGINE_ENERGY_SEVERE_MAX";

    public const string SorenessReducedMin =
        "ENGINE_SORENESS_REDUCED_MIN";

    public const string SorenessSevereMin =
        "ENGINE_SORENESS_SEVERE_MIN";

    private static readonly IReadOnlyDictionary<
        string,
        RecompEngineParameterDefinition> Definitions =
        new Dictionary<
            string,
            RecompEngineParameterDefinition>(
                StringComparer.OrdinalIgnoreCase)
        {
            [ReferenceWeightLb] = new(
                ReferenceWeightLb,
                "Model Calibration - Fuel Demand",
                "Reference Body Weight",
                "Reference body weight used when scaling workout energy and glycogen demand.",
                210m,
                "lb",
                100m,
                400m,
                "MODEL_ASSUMPTION",
                "Provides the reference point from which body-weight scaling is applied. The value affects relative scaling rather than serving as a target body weight.",
                "1.0",
                "1.0",
                10),

            [GlycogenEstimateMultiplier] = new(
                GlycogenEstimateMultiplier,
                "Model Calibration - Fuel Demand",
                "Glycogen Estimate Multiplier",
                "Global calibration multiplier applied to modeled workout glycogen expenditure.",
                0.65m,
                "multiplier",
                0.30m,
                1.00m,
                "12WR_CALIBRATION",
                "Scales raw workout glycogen estimates to the working 12WR whole-session model. This is one of the major calibration variables capable of materially changing calculated fuel readiness.",
                "1.0",
                "1.0",
                20),

            [BodyWeightGlycogenExponent] = new(
                BodyWeightGlycogenExponent,
                "Model Calibration - Fuel Demand",
                "Body-Weight Glycogen Scaling Exponent",
                "Controls how strongly estimated glycogen demand changes with body weight relative to the reference weight.",
                0.50m,
                "exponent",
                0.00m,
                1.00m,
                "MODEL_ASSUMPTION",
                "A value of 0.50 produces square-root scaling rather than assuming glycogen demand rises linearly with body weight.",
                "1.0",
                "1.0",
                30),

            [BackgroundBaseCarbCreditPct] = new(
                BackgroundBaseCarbCreditPct,
                "Model Calibration - Fuel Restoration",
                "Background Carbohydrate Restoration Credit",
                "Fraction of ordinary base carbohydrate intake credited toward modeled glycogen restoration.",
                0.20m,
                "ratio",
                0.00m,
                1.00m,
                "12WR_CALIBRATION",
                "Recognizes that ordinary carbohydrate intake contributes to restoration while avoiding the assumption that every gram of carbohydrate is available exclusively for replacing workout glycogen.",
                "1.0",
                "1.0",
                40),

            [FuelWindowDays] = new(
                FuelWindowDays,
                "Model Calibration - Fuel Restoration",
                "Fuel Recovery Lookback Window",
                "Number of completed days used when calculating recent workout glycogen demand and restoration.",
                4m,
                "days",
                1m,
                10m,
                "PROGRAM_DESIGN",
                "Defines how far back the rolling fuel-recovery model looks. A longer window retains prior demand longer; a shorter window allows old demand to fall out sooner.",
                "1.0",
                "1.0",
                50),

            [RpeIntensityBase] = new(
                RpeIntensityBase,
                "Model Calibration - Workout Intensity",
                "RPE Intensity Base",
                "Starting intensity factor used when converting a reported session RPE into modeled workout intensity.",
                0.70m,
                "factor",
                0.25m,
                1.25m,
                "12WR_CALIBRATION",
                "Forms the intercept of the current RPE-to-intensity conversion. Together with the per-RPE coefficient, this determines how reported effort changes estimated glycogen use and workout load.",
                "1.0",
                "2.0",
                100),

            [RpeIntensityPerPoint] = new(
                RpeIntensityPerPoint,
                "Model Calibration - Workout Intensity",
                "RPE Intensity Increase Per Point",
                "Amount added to modeled intensity for each one-point increase in reported session RPE.",
                0.04m,
                "factor/RPE",
                0.00m,
                0.10m,
                "12WR_CALIBRATION",
                "Controls how sensitive estimated workout demand is to reported RPE. Increasing it makes high-RPE workouts generate more modeled demand relative to low-RPE workouts.",
                "1.0",
                "2.0",
                110),

            [RpeIntensityMinimum] = new(
                RpeIntensityMinimum,
                "Model Calibration - Workout Intensity",
                "Minimum RPE-Derived Intensity",
                "Lower bound applied to the modeled intensity produced from session RPE.",
                0.70m,
                "factor",
                0.25m,
                1.00m,
                "MODEL_GUARDRAIL",
                "Prevents unusually low reported RPE from driving the modeled workout intensity below the intended operating range.",
                "1.0",
                "2.0",
                120),

            [RpeIntensityMaximum] = new(
                RpeIntensityMaximum,
                "Model Calibration - Workout Intensity",
                "Maximum RPE-Derived Intensity",
                "Upper bound applied to the modeled intensity produced from session RPE.",
                1.20m,
                "factor",
                0.75m,
                2.00m,
                "MODEL_GUARDRAIL",
                "Prevents very high reported RPE from producing an unlimited increase in modeled workout demand.",
                "1.0",
                "2.0",
                130),

            [DensityMinimumRestSeconds] = new(
                DensityMinimumRestSeconds,
                "Model Calibration - Workout Density",
                "Minimum Rest Used in Density Model",
                "Minimum rest interval accepted by the workout-density calculation.",
                15m,
                "seconds",
                5m,
                180m,
                "MODEL_GUARDRAIL",
                "Prevents extremely short or erroneous rest entries from causing disproportionately large workout-density adjustments.",
                "1.0",
                "2.0",
                200),

            [DensityFactorMinimum] = new(
                DensityFactorMinimum,
                "Model Calibration - Workout Density",
                "Minimum Workout Density Factor",
                "Lower bound on the adjustment produced by actual rest periods compared with the workout definition.",
                0.75m,
                "factor",
                0.25m,
                1.00m,
                "12WR_CALIBRATION",
                "Limits how much unusually long rest periods can reduce estimated glycogen demand.",
                "1.0",
                "2.0",
                210),

            [DensityFactorMaximum] = new(
                DensityFactorMaximum,
                "Model Calibration - Workout Density",
                "Maximum Workout Density Factor",
                "Upper bound on the adjustment produced by actual rest periods compared with the workout definition.",
                1.25m,
                "factor",
                1.00m,
                2.00m,
                "12WR_CALIBRATION",
                "Limits how much unusually short rest periods can increase estimated glycogen demand.",
                "1.0",
                "2.0",
                220),

            [DurationFactorMinimum] = new(
                DurationFactorMinimum,
                "Model Calibration - Recovery Load",
                "Minimum Duration Load Factor",
                "Lower bound on how much a shorter-than-planned workout can reduce calculated regional training load.",
                0.50m,
                "factor",
                0.10m,
                1.00m,
                "12WR_CALIBRATION",
                "Prevents a short workout from reducing its modeled Push, Pull, Legs, or Cardio recovery load without limit.",
                "1.0",
                "2.0",
                300),

            [DurationFactorMaximum] = new(
                DurationFactorMaximum,
                "Model Calibration - Recovery Load",
                "Maximum Duration Load Factor",
                "Upper bound on how much a longer-than-planned workout can increase calculated regional training load.",
                1.50m,
                "factor",
                1.00m,
                3.00m,
                "12WR_CALIBRATION",
                "Limits the increase in modeled recovery load caused by workouts that run substantially longer than their defined duration.",
                "1.0",
                "2.0",
                310),

            [LoadRpeFactorMinimum] = new(
                LoadRpeFactorMinimum,
                "Model Calibration - Recovery Load",
                "Minimum RPE Load Factor",
                "Lower bound on the RPE-derived adjustment applied to regional training load.",
                0.75m,
                "factor",
                0.25m,
                1.00m,
                "12WR_CALIBRATION",
                "Limits how much a lower-than-expected session RPE can reduce the recovery load assigned to a completed workout.",
                "1.0",
                "2.0",
                320),

            [LoadRpeFactorMaximum] = new(
                LoadRpeFactorMaximum,
                "Model Calibration - Recovery Load",
                "Maximum RPE Load Factor",
                "Upper bound on the RPE-derived adjustment applied to regional training load.",
                1.25m,
                "factor",
                1.00m,
                2.00m,
                "12WR_CALIBRATION",
                "Limits how much a harder-than-expected session RPE can increase the recovery load assigned to a completed workout.",
                "1.0",
                "2.0",
                330),

            [SleepReferenceHours] = new(
                SleepReferenceHours,
                "Model Calibration - Acute Recovery",
                "Default Sleep Reference",
                "Fallback sleep reference used when the client does not yet have a personal baseline sleep value.",
                7.50m,
                "hours",
                5.00m,
                10.00m,
                "MODEL_ASSUMPTION",
                "Provides a deterministic reference for evaluating a single night of sleep until a valid client baseline is available.",
                "1.0",
                "2.0",
                400),

            [SleepReducedRatio] = new(
                SleepReducedRatio,
                "Model Calibration - Acute Recovery",
                "Reduced Sleep Ratio",
                "Sleep-to-reference ratio below which acute sleep recovery is classified as reduced.",
                0.85m,
                "ratio",
                0.50m,
                1.00m,
                "12WR_CALIBRATION",
                "Controls when a short night begins contributing a reduced acute-recovery signal.",
                "1.0",
                "2.0",
                410),

            [SleepSevereRatio] = new(
                SleepSevereRatio,
                "Model Calibration - Acute Recovery",
                "Severely Reduced Sleep Ratio",
                "Sleep-to-reference ratio below which acute sleep recovery is classified as severely reduced.",
                0.70m,
                "ratio",
                0.30m,
                0.95m,
                "12WR_CALIBRATION",
                "Identifies unusually short sleep that should carry greater weight in the same-day recovery decision.",
                "1.0",
                "2.0",
                420),

            [EnergyReducedMax] = new(
                EnergyReducedMax,
                "Model Calibration - Acute Recovery",
                "Reduced Energy Maximum",
                "Highest 1-to-10 energy score classified as reduced rather than adequate.",
                5m,
                "score",
                1m,
                9m,
                "12WR_CALIBRATION",
                "Makes the user-reported energy score an explicit acute-recovery input instead of only a coaching message.",
                "1.0",
                "2.0",
                430),

            [EnergySevereMax] = new(
                EnergySevereMax,
                "Model Calibration - Acute Recovery",
                "Severely Reduced Energy Maximum",
                "Highest 1-to-10 energy score classified as severely reduced.",
                3m,
                "score",
                1m,
                8m,
                "12WR_CALIBRATION",
                "Identifies very low reported energy as a stronger same-day recovery constraint.",
                "1.0",
                "2.0",
                440),

            [SorenessReducedMin] = new(
                SorenessReducedMin,
                "Model Calibration - Acute Recovery",
                "Reduced Recovery Soreness Minimum",
                "Lowest 1-to-10 soreness score classified as reduced acute recovery.",
                6m,
                "score",
                2m,
                10m,
                "12WR_CALIBRATION",
                "Makes elevated self-reported soreness an explicit recovery signal while keeping it separate from the pain safety override.",
                "1.0",
                "2.0",
                450),

            [SorenessSevereMin] = new(
                SorenessSevereMin,
                "Model Calibration - Acute Recovery",
                "Severely Reduced Recovery Soreness Minimum",
                "Lowest 1-to-10 soreness score classified as severely reduced acute recovery.",
                8m,
                "score",
                3m,
                10m,
                "12WR_CALIBRATION",
                "Identifies unusually high soreness as a strong constraint on the same-day training recommendation.",
                "1.0",
                "2.0",
                460)
        };

    public static IReadOnlyCollection<
        RecompEngineParameterDefinition> All =>
        Definitions.Values
            .OrderBy(x => x.Category)
            .ThenBy(x => x.SortOrder)
            .ToArray();

    public static RecompEngineParameterDefinition Get(
        string key)
    {
        if (!Definitions.TryGetValue(key, out var definition))
        {
            throw new KeyNotFoundException(
                $"Unknown Recomp model calibration parameter: {key}");
        }

        return definition;
    }

    public static decimal Default(string key) =>
        Get(key).DefaultValue;
}
