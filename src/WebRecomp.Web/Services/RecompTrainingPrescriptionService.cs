using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record RecompTrainingPrescription(
    string PrescriptionModeCode,
    string WorkoutTypeCode,
    string WorkoutName,
    string TrainingPurpose,
    string TargetArea,
    decimal RecommendedMinutes,
    int? ApproximateWorkingSets,
    decimal? TargetRpeMinimum,
    decimal? TargetRpeMaximum,
    string RpeScopeCode,
    string IntensityGuidance,
    string PhysiologicalPurpose,
    string ExpectedTrainingCost,
    string VolumeBasis);

public sealed class RecompTrainingPrescriptionService
{
    public RecompTrainingPrescription BuildPlanned(
        WorkoutTypeDefinition workout,
        IReadOnlyDictionary<string, decimal> settings)
    {
        if (IsZone2(workout))
            return BuildZone2(workout, settings);

        if (workout.CountsRecovery)
            return BuildRecovery(workout);

        if (IsClassOrHybrid(workout))
            return BuildClassOrHybrid(workout, settings);

        if (IsTraditionalResistance(workout))
            return BuildResistance(workout, settings);

        if (workout.CountsPrimaryConditioning)
            return BuildConditioning(workout, settings);

        return BuildOtherLoad(workout);
    }


    public int? EstimateActualWorkingSets(
        WorkoutTypeDefinition workout,
        WorkoutSession session,
        IReadOnlyDictionary<string, decimal> settings)
    {
        if (!session.CompletedFlag
            || !IsTraditionalResistance(workout)
            || session.DurationMin <= 0)
        {
            return null;
        }

        var isHeavy = workout.CountsDedicatedHeavy;

        var warmupMinutes = Setting(
            settings,
            isHeavy
                ? RecompTrainingPrescriptionCatalog.HeavyWarmupMinutes
                : RecompTrainingPrescriptionCatalog.LightWarmupMinutes);

        var transitionPercent = Setting(
            settings,
            isHeavy
                ? RecompTrainingPrescriptionCatalog.HeavyTransitionPercent
                : RecompTrainingPrescriptionCatalog.LightTransitionPercent);

        var setExecutionSeconds = Setting(
            settings,
            RecompTrainingPrescriptionCatalog.SetExecutionSeconds);

        var setSetupResetSeconds = Setting(
            settings,
            RecompTrainingPrescriptionCatalog.SetSetupResetSeconds);

        var restSeconds =
            Math.Max(
                15m,
                session.TypicalRestSec
                    ?? workout.DefaultRestSec);

        var usableMinutes =
            Math.Max(
                0m,
                session.DurationMin
                - warmupMinutes
                - session.DurationMin * transitionPercent);

        var setCycleSeconds =
            setExecutionSeconds
            + setSetupResetSeconds
            + restSeconds;

        if (setCycleSeconds <= 0)
            return null;

        var estimatedSets =
            usableMinutes
            * 60m
            / setCycleSeconds;

        return Math.Max(
            0,
            (int)Math.Round(
                estimatedSets,
                MidpointRounding.AwayFromZero));
    }


    private static RecompTrainingPrescription BuildResistance(
        WorkoutTypeDefinition workout,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var isHeavy = workout.CountsDedicatedHeavy;

        var warmupMinutes = Setting(
            settings,
            isHeavy
                ? RecompTrainingPrescriptionCatalog.HeavyWarmupMinutes
                : RecompTrainingPrescriptionCatalog.LightWarmupMinutes);

        var transitionPercent = Setting(
            settings,
            isHeavy
                ? RecompTrainingPrescriptionCatalog.HeavyTransitionPercent
                : RecompTrainingPrescriptionCatalog.LightTransitionPercent);

        var volumeFactor = Setting(
            settings,
            isHeavy
                ? RecompTrainingPrescriptionCatalog.HeavyVolumeFactor
                : RecompTrainingPrescriptionCatalog.LightVolumeFactor);

        var setExecutionSeconds = Setting(
            settings,
            RecompTrainingPrescriptionCatalog.SetExecutionSeconds);

        var setSetupResetSeconds = Setting(
            settings,
            RecompTrainingPrescriptionCatalog.SetSetupResetSeconds);

        var restSeconds =
            Math.Max(
                15m,
                workout.DefaultRestSec);

        var usableMinutes =
            Math.Max(
                0m,
                workout.DefaultMinutes
                - warmupMinutes
                - workout.DefaultMinutes * transitionPercent);

        var setCycleSeconds =
            setExecutionSeconds
            + setSetupResetSeconds
            + restSeconds;

        var theoreticalSetCapacity =
            setCycleSeconds > 0
                ? usableMinutes * 60m / setCycleSeconds
                : 0m;

        var prescribedSets =
            theoreticalSetCapacity * volumeFactor;

        var approximateSets =
            Math.Max(
                1,
                (int)Math.Round(
                    prescribedSets,
                    MidpointRounding.AwayFromZero));

        var rpeMin = Setting(
            settings,
            isHeavy
                ? RecompTrainingPrescriptionCatalog.HeavyRpeMinimum
                : RecompTrainingPrescriptionCatalog.LightRpeMinimum);

        var rpeMax = Setting(
            settings,
            isHeavy
                ? RecompTrainingPrescriptionCatalog.HeavyRpeMaximum
                : RecompTrainingPrescriptionCatalog.LightRpeMaximum);

        return new RecompTrainingPrescription(
            "RESISTANCE_SETS",
            workout.WorkoutTypeCode,
            workout.DisplayName,
            isHeavy
                ? "Primary strength and hypertrophy training"
                : "Productive resistance training with controlled recovery cost",
            ResistanceTargetArea(workout),
            workout.DefaultMinutes,
            approximateSets,
            rpeMin,
            rpeMax,
            "WORKING_SET",
            isHeavy
                ? $"Most working sets at RPE {rpeMin:0}-{rpeMax:0}. Hard, controlled work without making every set maximal."
                : $"Most working sets at RPE {rpeMin:0}-{rpeMax:0}. Maintain useful stimulus while limiting fatigue.",
            isHeavy
                ? "Provide the primary strength and muscle-building stimulus for the targeted region."
                : "Maintain or build strength and muscle while creating less regional and systemic fatigue than a major session.",
            isHeavy
                ? "High muscular recovery demand with meaningful glycogen use."
                : "Moderate muscular recovery demand and lower total training stress than a major session.",
            $"Calculated from {workout.DefaultMinutes:0} minutes, {restSeconds:0}-second typical rest, set execution/setup time, warm-up and exercise-transition allowances, then adjusted by the {(isHeavy ? "major" : "light")} volume factor.");
    }


    private static RecompTrainingPrescription BuildZone2(
        WorkoutTypeDefinition workout,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var rpeMin = Setting(
            settings,
            RecompTrainingPrescriptionCatalog.Zone2RpeMinimum);

        var rpeMax = Setting(
            settings,
            RecompTrainingPrescriptionCatalog.Zone2RpeMaximum);

        return new RecompTrainingPrescription(
            "AEROBIC_DURATION",
            workout.WorkoutTypeCode,
            workout.DisplayName,
            "Aerobic / mitochondrial conditioning",
            "Cardiovascular system and aerobic muscle metabolism",
            workout.DefaultMinutes,
            null,
            rpeMin,
            rpeMax,
            "SESSION",
            $"Sustainable conversational effort, generally around Session RPE {rpeMin:0}-{rpeMax:0}.",
            "Develop aerobic energy-production capacity through mitochondrial and peripheral aerobic adaptations, improving sustainable work capacity, fat oxidation and recovery between harder efforts.",
            "Low muscular recovery cost and generally lower glycogen demand than hard conditioning.",
            "Prescribed by continuous duration and aerobic intensity rather than working-set count.");
    }


    private static RecompTrainingPrescription BuildConditioning(
        WorkoutTypeDefinition workout,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var hardConditioning =
            workout.WorkoutTypeCode.StartsWith(
                "HIIT",
                StringComparison.OrdinalIgnoreCase)
            || workout.WorkoutTypeCode.StartsWith(
                "OTF_STANDARD",
                StringComparison.OrdinalIgnoreCase)
            || workout.WorkoutTypeCode.Contains(
                "RPE9",
                StringComparison.OrdinalIgnoreCase);

        var rpeMin = hardConditioning
            ? Setting(
                settings,
                RecompTrainingPrescriptionCatalog.ConditioningRpeMinimum)
            : Setting(
                settings,
                RecompTrainingPrescriptionCatalog.LightRpeMinimum);

        var rpeMax = hardConditioning
            ? Setting(
                settings,
                RecompTrainingPrescriptionCatalog.ConditioningRpeMaximum)
            : Setting(
                settings,
                RecompTrainingPrescriptionCatalog.LightRpeMaximum);

        return new RecompTrainingPrescription(
            "CONDITIONING_DURATION",
            workout.WorkoutTypeCode,
            workout.DisplayName,
            hardConditioning
                ? "High-output cardiovascular and work-capacity conditioning"
                : "Moderate cardiovascular and work-capacity conditioning",
            workout.PrimaryFocusCode ?? "Conditioning",
            workout.DefaultMinutes,
            null,
            rpeMin,
            rpeMax,
            "SESSION",
            $"Target whole-session RPE approximately {rpeMin:0}-{rpeMax:0}.",
            hardConditioning
                ? "Improve high-output cardiovascular conditioning, repeated-effort tolerance and recovery between hard bouts."
                : "Build general cardiovascular conditioning and sustainable work capacity without requiring an all-out session.",
            hardConditioning
                ? "High cardiovascular and glycogen demand with meaningful systemic recovery cost."
                : "Moderate cardiovascular and systemic recovery demand.",
            "Conditioning is prescribed by session duration and overall effort rather than conventional resistance-training set count.");
    }


    private static RecompTrainingPrescription BuildClassOrHybrid(
        WorkoutTypeDefinition workout,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var rpeMin = Setting(
            settings,
            RecompTrainingPrescriptionCatalog.LightRpeMinimum);

        var rpeMax = Setting(
            settings,
            RecompTrainingPrescriptionCatalog.LightRpeMaximum);

        return new RecompTrainingPrescription(
            "CLASS_OR_HYBRID",
            workout.WorkoutTypeCode,
            workout.DisplayName,
            "Structured mixed or class-based training",
            workout.PrimaryFocusCode ?? "Mixed training",
            workout.DefaultMinutes,
            null,
            rpeMin,
            rpeMax,
            "SESSION",
            $"Use the programmed class structure; target overall Session RPE approximately {rpeMin:0}-{rpeMax:0} unless the workout specifically calls for harder intervals.",
            "Combine resistance and/or conditioning stimulus according to the structure of the programmed session.",
            "Recovery cost depends on the strength and conditioning mix; the engine tracks its configured regional and cardiovascular loads.",
            "No artificial working-set count is generated because class and hybrid formats do not map cleanly to conventional gym sets.");
    }


    private static RecompTrainingPrescription BuildRecovery(
        WorkoutTypeDefinition workout)
    {
        return new RecompTrainingPrescription(
            "RECOVERY",
            workout.WorkoutTypeCode,
            workout.DisplayName,
            "Recovery, mobility and movement quality",
            workout.PrimaryFocusCode ?? "Recovery",
            workout.DefaultMinutes,
            null,
            null,
            null,
            "SESSION",
            "Keep the session easy and restorative. Avoid turning recovery work into another hard training session.",
            "Support movement quality, circulation and recovery while adding minimal additional training stress.",
            "Very low intended training and glycogen cost.",
            "Recovery work is prescribed by purpose and duration rather than working-set volume.");
    }


    private static RecompTrainingPrescription BuildOtherLoad(
        WorkoutTypeDefinition workout)
    {
        return new RecompTrainingPrescription(
            "OTHER_LOAD",
            workout.WorkoutTypeCode,
            workout.DisplayName,
            "Physical activity / workload",
            workout.PrimaryFocusCode ?? "General activity",
            workout.DefaultMinutes,
            null,
            null,
            null,
            "SESSION",
            "Perform the activity as planned and record duration and perceived effort afterward.",
            "Account for meaningful physical workload that contributes to fatigue, energy use and recovery even when it is not a formal gym workout.",
            "Recovery cost is determined from the configured regional and cardiovascular workload.",
            "The engine accounts for workload without translating non-gym activity into an artificial set prescription.");
    }


    private static bool IsTraditionalResistance(
        WorkoutTypeDefinition workout)
    {
        if (!workout.CountsDedicatedHeavy
            && !workout.CountsLightStrength)
        {
            return false;
        }

        var code = workout.WorkoutTypeCode;

        if (code.StartsWith(
                "OTF_",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (code.Contains(
                "PLUS_CARDIO",
                StringComparison.OrdinalIgnoreCase)
            || code.Contains(
                "PLUS_Z2",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }


    private static bool IsZone2(
        WorkoutTypeDefinition workout) =>
        string.Equals(
            workout.WeeklyRoleCode,
            "RECOVERY_CARDIO",
            StringComparison.OrdinalIgnoreCase)
        || workout.WorkoutTypeCode.Contains(
            "ZONE_2",
            StringComparison.OrdinalIgnoreCase)
        || workout.WorkoutTypeCode.StartsWith(
            "Z2_",
            StringComparison.OrdinalIgnoreCase);


    private static bool IsClassOrHybrid(
        WorkoutTypeDefinition workout) =>
        workout.WorkoutTypeCode.StartsWith(
            "OTF_STRENGTH",
            StringComparison.OrdinalIgnoreCase)
        || workout.WorkoutTypeCode.Contains(
            "PLUS_CARDIO",
            StringComparison.OrdinalIgnoreCase)
        || workout.WorkoutTypeCode.Contains(
            "PLUS_Z2",
            StringComparison.OrdinalIgnoreCase);


    private static string ResistanceTargetArea(
        WorkoutTypeDefinition workout)
    {
        var region = workout.RegionCode?.ToUpperInvariant();

        return region switch
        {
            "PUSH" =>
                "Chest, shoulders and triceps",

            "PULL" =>
                "Back, rear shoulders and biceps",

            "LEGS" or "LOWER" =>
                "Quadriceps, hamstrings, glutes and calves",

            "UPPER" =>
                "Chest, back, shoulders and arms",

            "MIXED" =>
                workout.PrimaryFocusCode
                ?? "Full-body resistance training",

            _ =>
                workout.PrimaryFocusCode
                ?? "Resistance training"
        };
    }


    private static decimal Setting(
        IReadOnlyDictionary<string, decimal> settings,
        string key) =>
        ConfigurationService.Setting(
            settings,
            key,
            RecompTrainingPrescriptionCatalog.Default(key));
}
