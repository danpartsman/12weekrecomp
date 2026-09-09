namespace WebRecomp.Web.Services;

public static class RecompGuidanceTaxonomy
{
    public const string Version = "1.0";

    public static class Workout
    {
        // Planned workout remains appropriate.
        public const string KeepPlan = "W01_KEEP_PLAN";

        // Planned workout remains appropriate, but additional
        // pre-workout fueling is recommended to support it.
        public const string KeepWithFuelRescue = "W02_KEEP_FUEL_RESCUE";

        // Planned heavy region is insufficiently recovered,
        // but another heavy region can be trained instead.
        public const string SwapHeavyRegion = "W03_SWAP_HEAVY_REGION";

        // Planned region is insufficiently recovered and no
        // appropriate heavy swap is available.
        public const string LightAlternative = "W04_LIGHT_ALTERNATIVE";

        // Fuel readiness remains too low for the planned
        // high-output workout even after a reasonable rescue.
        public const string LowFuelDowngrade = "W05_LOW_FUEL_DOWNGRADE";

        // Pain/injury check-in requires a safety override.
        public const string RecoveryPain = "W06_RECOVERY_PAIN";

        // Illness/systemic-fatigue check-in requires a safety override.
        public const string RecoveryIllness = "W07_RECOVERY_ILLNESS";

        // Training/recovery load warrants a recovery-oriented day.
        public const string RecoveryLoad = "W08_RECOVERY_LOAD";
    }

    public static class Nutrition
    {
        // Standard nutrition target for the recommended day.
        public const string Base = "N01_BASE";

        // Recent glycogen demand has not been completely restored,
        // so additional carbohydrate is allocated today.
        public const string Carryover = "N02_CARRYOVER";

        // A meaningful carbohydrate dose is specifically recommended
        // before today's training session.
        public const string PreWorkoutRescue = "N03_PREWORKOUT_RESCUE";

        // Today's lower-load/recovery day should help restore fuel
        // for a future demanding session.
        public const string RecoveryRefill = "N04_RECOVERY_REFILL";

        // Today's workout has unusually high fueling demand.
        public const string HighDemand = "N05_HIGH_DEMAND";

        // Today's training demand is relatively low.
        public const string LowDemand = "N06_LOW_DEMAND";

        // Multiple recent days indicate materially incomplete fueling.
        public const string RecentUnderfueling = "N07_RECENT_UNDERFUELING";
    }

    public static class Recovery
    {
        // No meaningful recovery limitation identified.
        public const string Ready = "R01_READY";

        // Glycogen restoration is incomplete but not severely limiting.
        public const string PartialFuelRecovery = "R02_PARTIAL_FUEL_RECOVERY";

        // Local muscular recovery is limiting the planned region.
        public const string RegionNotRecovered = "R03_REGION_NOT_RECOVERED";

        // Overall recovery demand is elevated.
        public const string HighRecoveryNeed = "R04_HIGH_RECOVERY_NEED";

        // Pain/injury is the controlling recovery/safety factor.
        public const string PainSafety = "R05_PAIN_SAFETY";

        // Illness/systemic fatigue is the controlling recovery/safety factor.
        public const string IllnessSafety = "R06_ILLNESS_SAFETY";

        // Fuel readiness is the primary limiting factor.
        public const string FuelLimited = "R07_FUEL_LIMITED";
    }

    public static class Tags
    {
        public const string HeavyDay = "HEAVY_DAY";
        public const string LightStrengthDay = "LIGHT_STRENGTH_DAY";
        public const string ConditioningDay = "CONDITIONING_DAY";
        public const string RecoveryDay = "RECOVERY_DAY";

        public const string PlanPreserved = "PLAN_PRESERVED";
        public const string PlanChanged = "PLAN_CHANGED";

        public const string FuelRescueUsed = "FUEL_RESCUE_USED";
        public const string CarryoverPresent = "CARRYOVER_PRESENT";

        public const string PushRecovered = "PUSH_RECOVERED";
        public const string PullRecovered = "PULL_RECOVERED";
        public const string LegsRecovered = "LEGS_RECOVERED";
        public const string CardioRecovered = "CARDIO_RECOVERED";

        public const string SafetyOverride = "SAFETY_OVERRIDE";
    }

    public static string BuildDailyGuidanceKey(
        string workoutScenarioCode,
        string nutritionScenarioCode,
        string recoveryScenarioCode)
    {
        return $"G{Version}-{workoutScenarioCode}-{nutritionScenarioCode}-{recoveryScenarioCode}";
    }
}