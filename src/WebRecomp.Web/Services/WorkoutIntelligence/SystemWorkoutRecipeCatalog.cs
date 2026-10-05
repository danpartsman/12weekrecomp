namespace WebRecomp.Web.Services.WorkoutIntelligence;

/// <summary>
/// Defines built-in System Workout recipes for TEST v1010.
/// These recipes choose exercise rows; they do not define physiology.
/// EngineWorkoutTypeCode always resolves back to one of the calibrated
/// Engine 3.0 / CFG-3000.0 workout types.
///
/// Initial PreferredNameTerms ordering is a curated bootstrap signal derived
/// from public exercise rankings/programming by evidence-oriented coaches such
/// as Jeff Nippard, Jeremy Ethier / Built With Science, ATHLEAN-X, plus
/// Squat University / E3 Rehab for mobility and trunk-control patterns.
/// 12WR is not affiliated with or endorsed by those sources. Platform hearts
/// are designed to overtake this bounded seed as real 12WR preference data
/// accumulates.
/// </summary>
public sealed record SystemWorkoutExerciseSlot(
    string SlotCode,
    IReadOnlyList<string> PreferredNameTerms,
    IReadOnlyList<string> MuscleTerms,
    int Sets,
    int Reps,
    bool PreferCompound = false,
    bool PreferIsolation = false,
    bool AllowStretching = false,
    bool AllowCardio = false,
    bool RequireNameMatch = false,
    string? Notes = null,
    string? BlockName = null,
    string? BlockType = null,
    int? DurationSeconds = null,
    decimal? DistanceMeters = null,
    decimal? InclinePercent = null,
    int? EstimatedRounds = null,
    bool IsEstimated = false,
    string? WorkloadText = null);

public sealed record SystemWorkoutRecipe(
    string SystemCode,
    string DisplayName,
    string EngineWorkoutTypeCode,
    string Description,
    IReadOnlyList<SystemWorkoutExerciseSlot> Slots,
    bool IsSignature = false,
    string GroupName = "Standard",
    decimal DurationMultiplier = 1m,
    decimal RpeOffset = 0m,
    decimal? PlannedMinutes = null);

public static class SystemWorkoutRecipeCatalog
{
    public const string ChestTricepsGlutesHamstringsCode =
        "SYS_CHEST_TRICEPS_GLUTES_HAMS";

    public const string BackBicepsQuadsCode =
        "SYS_BACK_BICEPS_QUADS";

    public const string ShouldersAbsCalvesCode =
        "SYS_SHOULDERS_ABS_CALVES";

    private static SystemWorkoutExerciseSlot Slot(
        string code,
        string[] names,
        string[] muscles,
        int sets,
        int reps,
        bool compound = false,
        bool isolation = false,
        bool stretching = false,
        bool cardio = false,
        bool requireNameMatch = false,
        string? notes = null)
        => new(
            code,
            names,
            muscles,
            sets,
            reps,
            compound,
            isolation,
            stretching,
            cardio,
            requireNameMatch,
            notes);

    private static readonly IReadOnlyList<SystemWorkoutRecipe> Core =
    [
        new(
            "REST",
            "Rest / Recovery",
            "REST",
            "Full recovery day. No exercise rows are intentionally added.",
            []),

        new(
            "MOBILITY_RECOVERY",
            "Mobility / Posture",
            "MOBILITY_RECOVERY",
            "Core-control, hip/pelvic support, thoracic/scapular mobility, and low-intensity recovery. The recipe emphasizes controlled strengthening and movement quality rather than claiming that stretching alone will correct posture.",
            [
                Slot(
                    "GLUTE_BRIDGE",
                    ["glute bridge", "hip bridge", "bridge"],
                    ["glute"],
                    2,
                    10,
                    requireNameMatch: true,
                    notes: "Finish with the glutes without over-arching the low back."),
                Slot(
                    "PELVIC_CONTROL",
                    ["posterior pelvic tilt", "pelvic tilt", "abdominal brace"],
                    ["abdominal", "glute", "core"],
                    2,
                    10,
                    requireNameMatch: true,
                    notes: "Practice controlled pelvic positioning without forcing the low back flat."),
                Slot(
                    "DEAD_BUG",
                    ["dead bug", "heel slide", "heel tap", "tabletop march"],
                    ["abdominal", "core"],
                    2,
                    8,
                    requireNameMatch: true,
                    notes: "Slow exhale and abdominal control; shorten the range if the midline domes or cones."),
                Slot(
                    "BIRD_DOG",
                    ["bird dog", "quadruped opposite arm leg", "quadruped"],
                    ["abdominal", "core", "glute"],
                    2,
                    8,
                    requireNameMatch: true,
                    notes: "Keep the pelvis quiet and use a pain-free range."),
                Slot(
                    "PALLOF",
                    ["pallof press", "anti rotation press", "half kneeling pallof"],
                    ["abdominal", "oblique", "core"],
                    2,
                    8,
                    requireNameMatch: true,
                    notes: "Exhale during the press and resist trunk rotation."),
                Slot(
                    "HIP_FLEXOR_MOBILITY",
                    ["half kneeling hip flexor stretch", "hip flexor stretch", "couch stretch", "world's greatest stretch"],
                    ["hip flexor", "quadricep"],
                    2,
                    8,
                    stretching: true,
                    requireNameMatch: true,
                    notes: "Use a gentle posterior pelvic tilt; this is mobility work, not forced end-range stretching."),
                Slot(
                    "HIP_ROTATION",
                    ["90/90", "hip rotation", "hip airplane", "hip mobility"],
                    ["glute", "hip"],
                    2,
                    8,
                    stretching: true,
                    requireNameMatch: true,
                    notes: "Move slowly through a comfortable hip-rotation range."),
                Slot(
                    "THORACIC_MOBILITY",
                    ["quadruped thoracic rotation", "thoracic rotation", "open book", "thoracic extension"],
                    ["back"],
                    2,
                    8,
                    stretching: true,
                    requireNameMatch: true,
                    notes: "Move through the upper back without forcing the lumbar spine."),
                Slot(
                    "SCAPULAR_POSTURE",
                    ["face pull", "wall slide", "scapular wall slide", "wall angel"],
                    ["shoulder", "back"],
                    2,
                    10,
                    requireNameMatch: true,
                    notes: "Keep ribs stacked and shoulders away from the ears.")
            ]),

        // Continuous cardio presets are duration/intensity driven. We deliberately
        // do not manufacture rep-based lifting rows for these sessions.
        new("GENERAL_CARDIO", "General Cardio", "GENERAL_CARDIO",
            "Continuous cardio. The Engine workout type, duration, and intensity drive the session; no fake lifting sets are added.", []),
        new("EASY_CARDIO", "Easy Cardio", "EASY_CARDIO",
            "Easy continuous cardio / active recovery. No fake lifting sets are added.", []),
        new("ZONE2_CARDIO", "Zone 2 Cardio", "ZONE2_CARDIO",
            "Steady Zone 2 cardio. Duration and intensity are the meaningful prescription, so no rep-based exercise rows are fabricated.", []),
        new("WALKING", "Walking", "WALKING",
            "Walking session. Duration/intensity remain authoritative; no artificial exercise sets are added.", []),
        new("RUNNING", "Running", "RUNNING",
            "Continuous running session. Duration/intensity remain authoritative; no artificial exercise sets are added.", []),
        new("TREADMILL", "Treadmill", "TREADMILL",
            "Treadmill session. Duration/intensity remain authoritative; no artificial exercise sets are added.", []),
        new("CYCLING", "Cycling", "CYCLING",
            "Cycling session. Duration/intensity remain authoritative; no artificial exercise sets are added.", []),
        new("ROWING", "Rowing", "ROWING",
            "Rowing session. Duration/intensity remain authoritative; no artificial exercise sets are added.", []),
        new("ELLIPTICAL", "Elliptical", "ELLIPTICAL",
            "Elliptical session. Duration/intensity remain authoritative; no artificial exercise sets are added.", []),
        new("STAIR_CLIMBER", "Stair Climber", "STAIR_CLIMBER",
            "Stair-climber session. Duration/intensity remain authoritative; no artificial exercise sets are added.", []),
        new("SWIMMING", "Swimming", "SWIMMING",
            "Swimming session. Duration/intensity remain authoritative; no artificial exercise sets are added.", []),

        new(
            "MAJOR_LEGS",
            "Major Legs",
            "MAJOR_LEGS",
            "Heavy lower-body strength built around a squat/press pattern, hinge, glutes, hamstrings, unilateral work, and calves.",
            [
                Slot("SQUAT_PRESS", ["hack squat", "pendulum squat", "leg press", "high bar squat", "front squat", "back squat", "squat"], ["quadricep", "glute"], 4, 8, compound: true),
                Slot("HINGE", ["romanian deadlift", "rdl", "stiff leg deadlift", "deadlift"], ["hamstring", "glute"], 3, 8, compound: true),
                Slot("HIP_THRUST", ["hip thrust", "barbell hip thrust", "glute bridge"], ["glute"], 3, 10, compound: true),
                Slot("LEG_CURL", ["seated leg curl", "lying leg curl", "leg curl", "hamstring curl"], ["hamstring"], 3, 10, isolation: true),
                Slot("UNILATERAL", ["bulgarian split squat", "walking lunge", "split squat", "reverse lunge", "lunge"], ["quadricep", "glute"], 3, 8, compound: true),
                Slot("CALF", ["standing calf raise", "calf press", "seated calf raise", "calf raise"], ["calf"], 3, 12, isolation: true)
            ]),

        new(
            "MAJOR_PUSH",
            "Major Push",
            "MAJOR_PUSH",
            "Heavy chest, shoulder, and triceps training with a bias toward large presses plus targeted finishing work.",
            [
                Slot("CHEST_PRESS", ["machine chest press", "bench press", "flat dumbbell press", "dumbbell bench press", "chest press"], ["chest"], 4, 8, compound: true),
                Slot("INCLINE_PRESS", ["low incline dumbbell press", "incline dumbbell press", "incline bench press", "incline machine press", "incline chest press", "incline press"], ["chest"], 3, 8, compound: true),
                Slot("OVERHEAD_PRESS", ["machine shoulder press", "seated dumbbell shoulder press", "seated overhead press", "overhead press", "shoulder press", "military press"], ["shoulder"], 3, 8, compound: true),
                Slot("CHEST_FLY", ["cable press around", "cable fly", "pec deck", "chest fly"], ["chest"], 3, 12, isolation: true),
                Slot("LATERAL_RAISE", ["cable lateral raise", "machine lateral raise", "dumbbell lateral raise", "lateral raise", "side lateral"], ["shoulder"], 3, 12, isolation: true),
                Slot("TRICEPS", ["overhead cable triceps extension", "katana cable triceps extension", "skullcrusher", "triceps pressdown", "pressdown", "close grip bench press", "dip", "triceps extension", "tricep extension", "pushdown"], ["tricep"], 3, 10, isolation: true)
            ]),

        new(
            "MAJOR_PULL",
            "Major Pull",
            "MAJOR_PULL",
            "Heavy back and biceps training with vertical pulling, rowing, rear-deltoid/scapular work, and curls.",
            [
                Slot("VERTICAL_PULL", ["weighted pull up", "pull up", "pull-up", "lat pulldown", "chin up", "chin-up"], ["lat", "back"], 4, 8, compound: true),
                Slot("ROW", ["chest supported row", "cable row", "seated row", "barbell row", "row"], ["back", "lat"], 4, 8, compound: true),
                Slot("SECOND_ROW", ["one arm dumbbell row", "single arm row", "machine row", "high row", "one arm row"], ["back", "lat"], 3, 10, compound: true),
                Slot("REAR_DELT", ["reverse pec deck", "reverse cable crossover", "face pull", "rear delt fly", "rear delt", "reverse fly"], ["shoulder", "back"], 3, 12, isolation: true),
                Slot("BICEPS", ["dumbbell preacher curl", "machine preacher curl", "preacher curl", "bayesian cable curl", "incline curl", "ez bar curl", "barbell curl", "biceps curl", "bicep curl", "curl"], ["bicep"], 3, 10, isolation: true),
                Slot("HAMMER", ["hammer curl", "preacher hammer curl", "neutral grip curl"], ["bicep", "forearm"], 3, 10, isolation: true)
            ]),

        new(
            "FULL_BODY_STRENGTH",
            "Full Body Strength",
            "FULL_BODY_STRENGTH",
            "Balanced full-body strength with one major pattern for quads/glutes, hinge/hamstrings, chest, back, vertical pull, and shoulders.",
            [
                Slot("SQUAT_PRESS", ["hack squat", "pendulum squat", "leg press", "high bar squat", "front squat", "back squat", "squat"], ["quadricep", "glute"], 3, 8, compound: true),
                Slot("CHEST_PRESS", ["machine chest press", "bench press", "flat dumbbell press", "dumbbell bench press", "chest press"], ["chest"], 3, 8, compound: true),
                Slot("ROW", ["cable row", "chest supported row", "seated row", "row"], ["back"], 3, 8, compound: true),
                Slot("HINGE", ["romanian deadlift", "rdl", "stiff leg deadlift", "deadlift"], ["hamstring", "glute"], 3, 8, compound: true),
                Slot("VERTICAL_PULL", ["weighted pull up", "pull up", "pull-up", "lat pulldown"], ["lat", "back"], 3, 10, compound: true),
                Slot("OVERHEAD_PRESS", ["seated dumbbell shoulder press", "machine shoulder press", "overhead press", "shoulder press"], ["shoulder"], 3, 10, compound: true)
            ]),

        new(
            "ACCESSORY",
            "Accessory Strength",
            "ACCESSORY",
            "Moderate-volume accessory training for delts, arms, hamstrings, calves, and trunk without turning the day into another maximal strength session.",
            [
                Slot("LATERAL_RAISE", ["cable lateral raise", "machine lateral raise", "dumbbell lateral raise", "lateral raise", "side lateral"], ["shoulder"], 3, 12, isolation: true),
                Slot("REAR_DELT", ["reverse pec deck", "reverse cable crossover", "face pull", "rear delt fly", "rear delt", "reverse fly"], ["shoulder", "back"], 3, 12, isolation: true),
                Slot("BICEPS", ["dumbbell preacher curl", "machine preacher curl", "preacher curl", "bayesian cable curl", "incline curl", "ez bar curl", "biceps curl", "curl"], ["bicep"], 3, 12, isolation: true),
                Slot("TRICEPS", ["overhead cable triceps extension", "skullcrusher", "triceps pressdown", "pressdown", "triceps extension", "pushdown"], ["tricep"], 3, 12, isolation: true),
                Slot("LEG_CURL", ["seated leg curl", "lying leg curl", "leg curl", "hamstring curl"], ["hamstring"], 3, 12, isolation: true),
                Slot("CALF", ["standing calf raise", "calf press", "seated calf raise", "calf raise"], ["calf"], 3, 15, isolation: true),
                Slot("CORE", ["pallof press", "dead bug", "cable crunch", "side plank"], ["abdominal", "oblique", "core"], 3, 10)
            ]),

        new(
            "RESISTANCE_CIRCUIT",
            "Resistance Circuit",
            "RESISTANCE_CIRCUIT",
            "Full-body resistance circuit using high-value compound movements plus trunk work.",
            [
                Slot("SQUAT", ["goblet squat", "squat"], ["quadricep", "glute"], 3, 10, compound: true),
                Slot("PUSH", ["push up", "push-up", "chest press"], ["chest"], 3, 10, compound: true),
                Slot("ROW", ["row", "seated row"], ["back"], 3, 10, compound: true),
                Slot("HINGE", ["kettlebell swing", "romanian deadlift", "rdl"], ["hamstring", "glute"], 3, 10, compound: true),
                Slot("LUNGE", ["lunge", "split squat", "step up", "step-up"], ["quadricep", "glute"], 3, 10, compound: true),
                Slot("CORE", ["pallof press", "dead bug", "plank"], ["abdominal", "oblique", "core"], 3, 10)
            ]),

        new("RUNNING_INTERVALS", "Running Intervals", "RUNNING_INTERVALS",
            "Running intervals are interval-duration/intensity driven. No artificial rep-based lifting rows are added.", []),
        new("CYCLING_INTERVALS", "Cycling Intervals", "CYCLING_INTERVALS",
            "Cycling intervals are interval-duration/intensity driven. No artificial rep-based lifting rows are added.", []),
        new("ROWING_INTERVALS", "Rowing Intervals", "ROWING_INTERVALS",
            "Rowing intervals are interval-duration/intensity driven. No artificial rep-based lifting rows are added.", []),

        new(
            "MIXED_CONDITIONING",
            "Mixed Conditioning",
            "MIXED_CONDITIONING",
            "Mixed conditioning with a compact strength circuit. The Engine workout type still owns the conditioning physiology.",
            [
                Slot("SQUAT", ["goblet squat", "squat"], ["quadricep", "glute"], 3, 10, compound: true),
                Slot("PUSH", ["push up", "push-up", "dumbbell press", "chest press"], ["chest"], 3, 10, compound: true),
                Slot("PULL", ["row", "seated row"], ["back"], 3, 10, compound: true),
                Slot("HINGE", ["kettlebell swing", "romanian deadlift"], ["hamstring", "glute"], 3, 10, compound: true),
                Slot("CORE", ["mountain climber", "plank", "pallof press"], ["abdominal", "core"], 3, 12)
            ]),

        new("TRUE_HIIT", "HIIT / Sprint Intervals", "TRUE_HIIT",
            "True HIIT is interval-duration/intensity driven. 12WR intentionally does not invent resistance sets that could distort the canonical interval workload.", []),
        new("ORANGETHEORY", "Orangetheory / Mixed Interval Class", "ORANGETHEORY",
            "Orangetheory is a composite class whose tread, rower, and floor details vary by template. The Engine composite profile is preserved instead of fabricating a fixed floor workout.", []),

        new(
            "MIXED_GYM_SESSION",
            "Mixed Gym Session",
            "MIXED_GYM_SESSION",
            "Balanced gym session with full-body resistance work; optional cardio remains represented by the Engine composite workout type.",
            [
                Slot("SQUAT_PRESS", ["leg press", "squat"], ["quadricep", "glute"], 3, 10, compound: true),
                Slot("CHEST", ["chest press", "bench press"], ["chest"], 3, 10, compound: true),
                Slot("BACK", ["lat pulldown", "row"], ["lat", "back"], 3, 10, compound: true),
                Slot("HINGE", ["romanian deadlift", "rdl"], ["hamstring", "glute"], 3, 10, compound: true),
                Slot("SHOULDER", ["shoulder press", "lateral raise"], ["shoulder"], 3, 10),
                Slot("CORE", ["pallof press", "dead bug", "plank"], ["abdominal", "core"], 3, 10)
            ]),

        new(
            "CUSTOM_COMPOSITE",
            "Custom Multi-Block Workout",
            "CUSTOM_COMPOSITE",
            "General-purpose full-body multi-block starting point. Modify it freely after selection.",
            [
                Slot("LOWER", ["goblet squat", "leg press", "squat"], ["quadricep", "glute"], 3, 10, compound: true),
                Slot("PUSH", ["chest press", "bench press", "push up"], ["chest"], 3, 10, compound: true),
                Slot("PULL", ["lat pulldown", "row"], ["lat", "back"], 3, 10, compound: true),
                Slot("HINGE", ["romanian deadlift", "rdl", "hip thrust"], ["hamstring", "glute"], 3, 10, compound: true),
                Slot("SHOULDERS", ["lateral raise", "shoulder press"], ["shoulder"], 3, 12),
                Slot("CORE", ["pallof press", "dead bug", "plank"], ["abdominal", "core"], 3, 10)
            ])
    ];

    private static readonly IReadOnlyList<SystemWorkoutRecipe> Signature =
    [
        new(
            ChestTricepsGlutesHamstringsCode,
            "Set A - Chest + Triceps + Glutes + Hamstrings",
            "FULL_BODY_STRENGTH",
            "Chest and triceps paired with posterior-chain lower body. Initial exercise order is seeded from public evidence-oriented trainer recommendations; platform hearts can later rotate equally appropriate movements.",
            [
                Slot("UPPER_CHEST", ["low incline dumbbell press", "incline dumbbell press", "incline bench press", "incline machine press", "incline chest press", "incline press"], ["chest"], 3, 8, compound: true),
                Slot("CHEST_PRESS", ["machine chest press", "bench press", "flat dumbbell press", "dumbbell bench press", "chest press"], ["chest"], 3, 8, compound: true),
                Slot("CHEST_FLY", ["cable press around", "cable fly", "pec deck", "chest fly"], ["chest"], 3, 12, isolation: true),
                Slot("TRICEPS_LONG", ["overhead cable triceps extension", "katana cable triceps extension", "skullcrusher", "overhead triceps extension", "overhead tricep extension", "triceps extension"], ["tricep"], 3, 10, isolation: true),
                Slot("TRICEPS_PRESS", ["triceps pressdown", "rope pressdown", "bar pressdown", "pressdown", "close grip bench press", "machine dip", "dip", "pushdown"], ["tricep"], 3, 10, isolation: true),
                Slot("GLUTES", ["hip thrust", "barbell hip thrust", "glute bridge"], ["glute"], 3, 8, compound: true),
                Slot("HINGE", ["romanian deadlift", "rdl"], ["hamstring", "glute"], 3, 8, compound: true),
                Slot("HAMSTRINGS", ["seated leg curl", "lying leg curl", "leg curl", "hamstring curl"], ["hamstring"], 3, 12, isolation: true)
            ],
            IsSignature: true,
            GroupName: "Set A"),

        new(
            BackBicepsQuadsCode,
            "Set A - Back + Biceps + Quads",
            "FULL_BODY_STRENGTH",
            "Vertical and horizontal pulling plus biceps, paired with quad-dominant lower-body work. Initial exercise order is trainer-research seeded, then platform hearts can rotate equally appropriate matches.",
            [
                Slot("VERTICAL_PULL", ["weighted pull up", "pull up", "pull-up", "lat pulldown", "chin up", "chin-up"], ["lat", "back"], 3, 8, compound: true),
                Slot("PRIMARY_ROW", ["chest supported row", "cable row", "seated row", "barbell row", "row"], ["back", "lat"], 3, 8, compound: true),
                Slot("SECOND_ROW", ["one arm dumbbell row", "single arm row", "machine row", "high row", "one arm row"], ["back", "lat"], 3, 10, compound: true),
                Slot("BICEPS", ["dumbbell preacher curl", "machine preacher curl", "preacher curl", "bayesian cable curl", "incline curl", "ez bar curl", "biceps curl", "bicep curl", "curl"], ["bicep"], 3, 10, isolation: true),
                Slot("HAMMER", ["hammer curl", "preacher hammer curl", "neutral grip curl"], ["bicep", "forearm"], 3, 10, isolation: true),
                Slot("QUAD_COMPOUND", ["hack squat", "pendulum squat", "leg press", "front squat", "high bar squat", "squat"], ["quadricep"], 4, 8, compound: true),
                Slot("UNILATERAL_QUAD", ["bulgarian split squat", "walking lunge", "split squat", "reverse lunge", "lunge"], ["quadricep"], 3, 8, compound: true),
                Slot("LEG_EXTENSION", ["leg extension"], ["quadricep"], 3, 12, isolation: true)
            ],
            IsSignature: true,
            GroupName: "Set A"),

        new(
            ShouldersAbsCalvesCode,
            "Set A - Shoulders + Abs + Calves",
            "ACCESSORY",
            "Shoulder-cap/rear-delt work, trunk training, and both straight- and bent-knee calf work. Initial exercise order is trainer-research seeded, then platform hearts can rotate equally appropriate matches.",
            [
                Slot("SHOULDER_PRESS", ["machine shoulder press", "seated dumbbell shoulder press", "seated overhead press", "overhead press", "shoulder press"], ["shoulder"], 3, 8, compound: true),
                Slot("LATERAL_DELT", ["cable lateral raise", "machine lateral raise", "dumbbell lateral raise", "lateral raise", "side lateral"], ["shoulder"], 4, 12, isolation: true),
                Slot("REAR_DELT", ["reverse pec deck", "reverse cable crossover", "face pull", "rear delt fly", "rear delt", "reverse fly"], ["shoulder", "back"], 3, 12, isolation: true),
                Slot("ANTI_EXTENSION", ["dead bug", "ab wheel", "body saw"], ["abdominal", "core"], 3, 10),
                Slot("ANTI_ROTATION", ["pallof press", "anti rotation press"], ["oblique", "abdominal", "core"], 3, 10),
                Slot("FLEXION", ["cable crunch", "reverse crunch", "knee raise"], ["abdominal"], 3, 12),
                Slot("STRAIGHT_KNEE_CALF", ["standing calf raise", "calf press", "calf raise"], ["calf"], 4, 12, isolation: true),
                Slot("BENT_KNEE_CALF", ["seated calf raise"], ["calf"], 3, 15, isolation: true)
            ],
            IsSignature: true,
            GroupName: "Set A")
    ];

    private static SystemWorkoutRecipe CoreRecipe(string engineCode)
        => Core.Single(
            x => x.EngineWorkoutTypeCode.Equals(
                engineCode,
                StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<SystemWorkoutExerciseSlot> ScaleSlots(
        IReadOnlyList<SystemWorkoutExerciseSlot> slots,
        decimal multiplier)
        => slots
            .Select(
                slot =>
                    slot with
                    {
                        Sets = Math.Clamp(
                            (int)Math.Round(
                                slot.Sets * multiplier,
                                MidpointRounding.AwayFromZero),
                            1,
                            20)
                    })
            .ToArray();

    private static SystemWorkoutRecipe Variant(
        string systemCode,
        string displayName,
        string engineCode,
        string groupName,
        decimal durationMultiplier,
        decimal rpeOffset,
        decimal setMultiplier = 1m)
    {
        var source = CoreRecipe(engineCode);

        return new SystemWorkoutRecipe(
            systemCode,
            displayName,
            engineCode,
            source.Description,
            ScaleSlots(source.Slots, setMultiplier),
            GroupName: groupName,
            DurationMultiplier: durationMultiplier,
            RpeOffset: rpeOffset);
    }

    private static readonly IReadOnlyList<SystemWorkoutRecipe> SetB =
    [
        new(
            "SYS_SET_B_HARD_LEGS",
            "Set B - Hard Legs",
            "MAJOR_LEGS",
            CoreRecipe("MAJOR_LEGS").Description,
            CoreRecipe("MAJOR_LEGS").Slots,
            GroupName: "Set B"),
        new(
            "SYS_SET_B_HARD_PULL",
            "Set B - Hard Pull",
            "MAJOR_PULL",
            CoreRecipe("MAJOR_PULL").Description,
            CoreRecipe("MAJOR_PULL").Slots,
            GroupName: "Set B"),
        new(
            "SYS_SET_B_HARD_PUSH",
            "Set B - Hard Push",
            "MAJOR_PUSH",
            CoreRecipe("MAJOR_PUSH").Description,
            CoreRecipe("MAJOR_PUSH").Slots,
            GroupName: "Set B")
    ];

    private static readonly IReadOnlyList<SystemWorkoutRecipe> Tiered =
    [
        Variant("SYS_EASY_ACCESSORY", "Easy Accessory", "ACCESSORY", "Easy", 0.67m, -2m, 0.67m),
        Variant("SYS_STANDARD_ACCESSORY", "Standard Accessory", "ACCESSORY", "Standard", 1m, 0m, 1m),
        Variant("SYS_HARD_ACCESSORY", "Hard Accessory", "ACCESSORY", "Hard", 1.33m, 1m, 1.33m),

        Variant("SYS_EASY_FULL_BODY", "Easy Full Body Strength", "FULL_BODY_STRENGTH", "Easy", 0.67m, -2m, 0.67m),
        Variant("SYS_STANDARD_FULL_BODY", "Standard Full Body Strength", "FULL_BODY_STRENGTH", "Standard", 1m, 0m, 1m),
        Variant("SYS_HARD_FULL_BODY", "Hard Full Body Strength", "FULL_BODY_STRENGTH", "Hard", 1.17m, 1m, 1.17m),

        Variant("SYS_EASY_RESISTANCE_CIRCUIT", "Easy Resistance Circuit", "RESISTANCE_CIRCUIT", "Easy", 0.67m, -2m, 0.67m),
        Variant("SYS_STANDARD_RESISTANCE_CIRCUIT", "Standard Resistance Circuit", "RESISTANCE_CIRCUIT", "Standard", 1m, 0m, 1m),
        Variant("SYS_HARD_RESISTANCE_CIRCUIT", "Hard Resistance Circuit", "RESISTANCE_CIRCUIT", "Hard", 1.25m, 1m, 1.25m),

        Variant("SYS_EASY_CARDIO", "Easy Cardio", "GENERAL_CARDIO", "Easy", 0.67m, -1m),
        Variant("SYS_STANDARD_CARDIO", "Standard Cardio", "GENERAL_CARDIO", "Standard", 1m, 0m),
        Variant("SYS_HARD_CARDIO", "Hard Cardio", "GENERAL_CARDIO", "Hard", 1.33m, 2m),

        Variant("SYS_EASY_CYCLING", "Easy Cycling", "CYCLING", "Easy", 0.67m, -1m),
        Variant("SYS_STANDARD_CYCLING", "Standard Cycling", "CYCLING", "Standard", 1m, 0m),
        Variant("SYS_HARD_CYCLING", "Hard Cycling", "CYCLING", "Hard", 1.33m, 2m),

        Variant("SYS_EASY_ELLIPTICAL", "Easy Elliptical", "ELLIPTICAL", "Easy", 0.67m, -1m),
        Variant("SYS_STANDARD_ELLIPTICAL", "Standard Elliptical", "ELLIPTICAL", "Standard", 1m, 0m),
        Variant("SYS_HARD_ELLIPTICAL", "Hard Elliptical", "ELLIPTICAL", "Hard", 1.33m, 2m),

        Variant("SYS_EASY_ROWING", "Easy Rowing", "ROWING", "Easy", 0.67m, -2m),
        Variant("SYS_STANDARD_ROWING", "Standard Rowing", "ROWING", "Standard", 1m, 0m),
        Variant("SYS_HARD_ROWING", "Hard Rowing", "ROWING", "Hard", 1.33m, 2m),

        Variant("SYS_EASY_RUNNING", "Easy Running", "RUNNING", "Easy", 0.67m, -2m),
        Variant("SYS_STANDARD_RUNNING", "Standard Running", "RUNNING", "Standard", 1m, 0m),
        Variant("SYS_HARD_RUNNING", "Hard Running", "RUNNING", "Hard", 1.33m, 2m),

        Variant("SYS_EASY_STAIRS", "Easy Stair Climber", "STAIR_CLIMBER", "Easy", 0.67m, -2m),
        Variant("SYS_STANDARD_STAIRS", "Standard Stair Climber", "STAIR_CLIMBER", "Standard", 1m, 0m),
        Variant("SYS_HARD_STAIRS", "Hard Stair Climber", "STAIR_CLIMBER", "Hard", 1.5m, 2m),

        Variant("SYS_EASY_SWIMMING", "Easy Swimming", "SWIMMING", "Easy", 0.67m, -1m),
        Variant("SYS_STANDARD_SWIMMING", "Standard Swimming", "SWIMMING", "Standard", 1m, 0m),
        Variant("SYS_HARD_SWIMMING", "Hard Swimming", "SWIMMING", "Hard", 1.33m, 2m),

        Variant("SYS_EASY_TREADMILL", "Easy Treadmill", "TREADMILL", "Easy", 0.67m, -1m),
        Variant("SYS_STANDARD_TREADMILL", "Standard Treadmill", "TREADMILL", "Standard", 1m, 0m),
        Variant("SYS_HARD_TREADMILL", "Hard Treadmill", "TREADMILL", "Hard", 1.33m, 2m),

        Variant("SYS_EASY_WALKING", "Easy Walking", "WALKING", "Easy", 0.67m, -1m),
        Variant("SYS_STANDARD_WALKING", "Standard Walking", "WALKING", "Standard", 1m, 0m),
        Variant("SYS_HARD_WALKING", "Hard Walking", "WALKING", "Hard", 1.33m, 2m)
    ];

    private static readonly SystemWorkoutRecipe CardioPresentationRecipe =
        new(
            CardioActivityCatalog.PresentationSystemCode,
            "Cardio",
            "GENERAL_CARDIO",
            "Choose the cardio activity you actually performed, then enter duration and RPE. 12WR resolves that activity to the existing calibrated Engine 3.0 workout type.",
            [],
            GroupName: "Standard");

    private static readonly IReadOnlyList<SystemWorkoutRecipe> ClassifiedCore =
    [
        CoreRecipe("REST") with { GroupName = "Recovery", DisplayName = "Rest / Recovery" },
        CardioPresentationRecipe,
        CoreRecipe("MOBILITY_RECOVERY") with { GroupName = "Recovery", DisplayName = "Mobility / Posture" },
        CoreRecipe("EASY_CARDIO") with { GroupName = "Easy", DisplayName = "Easy Recovery Cardio" },
        CoreRecipe("ZONE2_CARDIO") with { GroupName = "Easy", DisplayName = "Easy Zone 2 Cardio" },

        CoreRecipe("RUNNING_INTERVALS") with { GroupName = "Hard", DisplayName = "Hard Running Intervals" },
        CoreRecipe("CYCLING_INTERVALS") with { GroupName = "Hard", DisplayName = "Hard Cycling Intervals" },
        CoreRecipe("ROWING_INTERVALS") with { GroupName = "Hard", DisplayName = "Hard Rowing Intervals" },
        CoreRecipe("MIXED_CONDITIONING") with { GroupName = "Hard", DisplayName = "Hard Mixed Conditioning" },
        CoreRecipe("TRUE_HIIT") with { GroupName = "Hard", DisplayName = "Hard HIIT / Sprint Intervals" },

        CoreRecipe("ORANGETHEORY") with
        {
            GroupName = "Orangetheory",
            DisplayName = "Standard Orangetheory 2G",
            Description = "Typical 60-minute treadmill, rower, and floor class for planning. Engine 3.0 estimates calories and glycogen from the calibrated Orangetheory composite profile; actual class details vary.",
            PlannedMinutes = 60m
        },
        new(
            "SYS_OTF_STRENGTH_50",
            "Orangetheory Strength 50",
            "RESISTANCE_CIRCUIT",
            "Typical 50-minute coach-led resistance class for planning. Engine 3.0 estimates calories and glycogen from the resistance-circuit profile; actual movements and workload vary.",
            [],
            GroupName: "Orangetheory",
            PlannedMinutes: 50m),
        OrangetheoryDailyRecipeRegistration.CreateRecipe(),
        OrangetheoryDailyRecipeRegistration.Create20260928Recipe(),
        OrangetheoryDailyRecipeRegistration.Create20260929Recipe(),
        OrangetheoryDailyRecipeRegistration.Create20260930Recipe(),
        OrangetheoryDailyRecipeRegistration.Create20260930Strength50Recipe(),
        OrangetheoryDailyRecipeRegistration.Create20261002HellWeekStrengthRecipe(),
        CoreRecipe("MIXED_GYM_SESSION") with { GroupName = "Standard", DisplayName = "Standard Mixed Gym Session" },
        CoreRecipe("CUSTOM_COMPOSITE") with { GroupName = "Standard", DisplayName = "Standard Custom Multi-Block" }
    ];

    private static readonly IReadOnlyList<SystemWorkoutRecipe> Presentation =
        Signature
            .Concat(SetB)
            .Concat(Tiered)
            .Concat(ClassifiedCore)
            // v1012 presents one Cardio workout. The activity picker resolves
            // the user's actual modality/intensity pattern to the existing
            // calibrated Engine 3.0 workout code at completion time.
            .Where(
                x =>
                    x.SystemCode.Equals(
                        CardioActivityCatalog.PresentationSystemCode,
                        StringComparison.OrdinalIgnoreCase)
                    || !CardioActivityCatalog.IsCollapsedCardioEngineCode(
                        x.EngineWorkoutTypeCode))
            .GroupBy(x => x.SystemCode, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => GroupRank(x.GroupName))
            .ThenBy(x => x.GroupName.Equals("Orangetheory", StringComparison.OrdinalIgnoreCase) ? OrangetheorySortRank(x.SystemCode) : 0)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static int OrangetheorySortRank(string systemCode)
    {
        var parts = systemCode.Split('_');
        if ((systemCode.StartsWith("OTF_2G_", StringComparison.OrdinalIgnoreCase)
             || systemCode.StartsWith("OTF_STRENGTH50_", StringComparison.OrdinalIgnoreCase)
             || systemCode.StartsWith("OTF_STRENGTH60_", StringComparison.OrdinalIgnoreCase))
            && parts.Length >= 5
            && int.TryParse(string.Concat(parts[^3], parts[^2], parts[^1]), out var yyyymmdd))
            return -yyyymmdd;

        return int.MaxValue;
    }

    private static int GroupRank(string groupName)
        => groupName switch
        {
            "Set A" => 0,
            "Set B" => 1,
            "Orangetheory" => 2,
            "Easy" => 3,
            "Standard" => 4,
            "Hard" => 5,
            "Recovery" => 6,
            _ => 7
        };

    private static readonly IReadOnlyDictionary<string, SystemWorkoutRecipe> ByCode =
        Core
            .Concat(Signature)
            .Concat(SetB)
            .Concat(Tiered)
            .Concat(ClassifiedCore)
            .GroupBy(
                x => x.SystemCode,
                StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .ToDictionary(
                x => x.SystemCode,
                StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SystemWorkoutRecipe> CoreRecipes => Core;
    public static IReadOnlyList<SystemWorkoutRecipe> SignatureRecipes => Signature;
    public static IReadOnlyList<SystemWorkoutRecipe> PresentationRecipes => Presentation;

    public static IReadOnlyList<string> PresentationCategoryOrder { get; } =
    [
        "Resistance",
        "Orangetheory",
        "Conditioning",
        "Cardio",
        "Mixed / Composite",
        "Recovery"
    ];

    public static string PresentationCategoryForEngineWorkoutType(
        string? engineWorkoutTypeCode)
    {
        if (string.IsNullOrWhiteSpace(engineWorkoutTypeCode))
            return "Other";

        var normalized =
            engineWorkoutTypeCode
                .Trim()
                .ToUpperInvariant();

        if (normalized.StartsWith(
                "MAJOR_",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Resistance";
        }

        return normalized switch
        {
            "FULL_BODY_STRENGTH"
                or "ACCESSORY" =>
                    "Resistance",

            "GENERAL_CARDIO"
                or "EASY_CARDIO"
                or "ZONE2_CARDIO"
                or "WALKING"
                or "RUNNING"
                or "TREADMILL"
                or "CYCLING"
                or "ROWING"
                or "ELLIPTICAL"
                or "STAIR_CLIMBER"
                or "SWIMMING" =>
                    "Cardio",

            "ORANGETHEORY" =>
                    "Orangetheory",

            "RESISTANCE_CIRCUIT"
                or "RUNNING_INTERVALS"
                or "CYCLING_INTERVALS"
                or "ROWING_INTERVALS"
                or "MIXED_CONDITIONING"
                or "TRUE_HIIT" =>
                    "Conditioning",

            "MIXED_GYM_SESSION"
                or "CUSTOM_COMPOSITE" =>
                    "Mixed / Composite",

            "REST"
                or "MOBILITY_RECOVERY" =>
                    "Recovery",

            _ => "Other"
        };
    }

    public static SystemWorkoutRecipe? GetDefaultPresentationForEngine(
        string? engineWorkoutTypeCode)
    {
        if (string.IsNullOrWhiteSpace(engineWorkoutTypeCode))
            return null;

        var matches =
            Presentation
                .Where(
                    x => x.EngineWorkoutTypeCode.Equals(
                        engineWorkoutTypeCode.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

        if (matches.Length == 0)
            return null;

        return matches.FirstOrDefault(
                   x => x.GroupName.Equals(
                       "Standard",
                       StringComparison.OrdinalIgnoreCase))
               ?? matches.FirstOrDefault(
                   x => x.GroupName.Equals(
                       "Set B",
                       StringComparison.OrdinalIgnoreCase))
               ?? matches.FirstOrDefault(
                   x => x.GroupName.Equals(
                       "Easy",
                       StringComparison.OrdinalIgnoreCase))
               ?? matches[0];
    }

    public static decimal ResolveDefaultMinutes(
        SystemWorkoutRecipe recipe,
        decimal engineDefaultMinutes)
    {
        if (engineDefaultMinutes <= 0m)
            return 0m;

        var raw =
            recipe.PlannedMinutes
            ?? engineDefaultMinutes * recipe.DurationMultiplier;

        return Math.Max(
            5m,
            Math.Round(
                raw / 5m,
                MidpointRounding.AwayFromZero)
            * 5m);
    }

    public static decimal ResolveDefaultRpe(
        SystemWorkoutRecipe recipe,
        decimal engineDefaultRpe)
        => Math.Clamp(
            engineDefaultRpe + recipe.RpeOffset,
            0m,
            10m);

    public static bool TryGet(
        string? systemCode,
        out SystemWorkoutRecipe? recipe)
    {
        recipe = null;

        if (string.IsNullOrWhiteSpace(systemCode))
            return false;

        return ByCode.TryGetValue(
            systemCode.Trim(),
            out recipe);
    }

    public static string ResolveEngineWorkoutTypeCode(
        string systemCode)
        => TryGet(systemCode, out var recipe)
            ? recipe!.EngineWorkoutTypeCode
            : systemCode.Trim();
}
