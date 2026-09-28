namespace WebRecomp.Web.Services.WorkoutIntelligence;

/// <summary>
/// Builds the separately selectable dated 2G workout. Its verified exercise
/// rows do not change the reusable class planning presets.
/// </summary>
internal static class OrangetheoryDailyRecipeRegistration
{
    internal static SystemWorkoutRecipe CreateRecipe()
        => new SystemWorkoutRecipe(
            "OTF_2G_2026_09_27",
            "OTF 2G - 2026-09-27",
            "ORANGETHEORY",
            "Orangetheory 2G template for September 27, 2026. Fixed floor reps are preserved directly; timed work and the 250 m row are preserved in the exercise notes while Engine 3.0 retains the calibrated Orangetheory composite physiology.",
            [
                Slot("OTF_LATERAL_HOP_OVER", "Lateral Hop Over", 1, 8,
                    "Floor Block 1; alternating/total reps; back-to-back with chest fly; repeat sequence until time."),
                Slot("OTF_DB_CHEST_FLY", "Dumbbell Chest Fly", 1, 8,
                    "Floor Block 1; back-to-back with lateral hop over; repeat sequence until time."),
                Slot("OTF_SUMO_SQUAT_FROGGER", "Dumbbell Sumo Squat to Frogger", 1, 8,
                    "Floor Block 1; back-to-back with bench pullover; repeat sequence until time."),
                Slot("OTF_BENCH_PULLOVER", "Bench Pullover", 1, 8,
                    "Floor Block 1; back-to-back with sumo squat to frogger; repeat sequence until time."),
                Slot("OTF_ROW_250M", "Row", 1, 0,
                    "Floor Block 1; 250 m row at 20-24 strokes/min; repeat sequence until time for 1-minute push-up buy-out.", allowCardio: true),
                Slot("OTF_PUSHUP_BUYOUT", "Push-Up", 1, 0,
                    "Floor Block 1 buy-out for 1 minute."),
                Slot("OTF_TAP_SQUAT_JUMP", "Bodyweight Tap Squat Jump", 3, 10,
                    "Floor Block 2 rounds 1, 3 and 5; each round is 1 minute with remaining time used for seated overhead tricep extension."),
                Slot("OTF_OH_TRICEP", "Dumbbell Seated Overhead Tricep Extension", 3, 0,
                    "Floor Block 2 rounds 1, 3 and 5 after 10 tap squat jumps; continue until time is called; 30 sec rest after each round."),
                Slot("OTF_SKIER_SWING", "Skier Swing", 3, 10,
                    "Floor Block 2 rounds 2, 4 and 6; each round is 1 minute with remaining time used for seated low row."),
                Slot("OTF_SEATED_LOW_ROW", "Seated Low Row", 3, 0,
                    "Floor Block 2 rounds 2, 4 and 6 after 10 skier swings; continue until time is called; 30 sec rest after each round."),
                Slot("OTF_HIGH_PLANK_KNEE_TUCK", "High Plank Knee Tuck", 1, 0,
                    "Floor Block 2 finisher for 1 minute."),
                Slot("OTF_PUSHUP_FINISHER", "Push-Up", 1, 0,
                    "Floor Block 2 finisher for 1 minute.")
            ],
            GroupName: "Orangetheory");

    internal static SystemWorkoutRecipe Create20260928Recipe()
        => new SystemWorkoutRecipe(
            "OTF_2G_2026_09_28",
            "OTF 2G - 2026-09-28",
            "ORANGETHEORY",
            "Orangetheory 2G elevation challenge. 12WR preserves the published block structure and uses three estimated rounds for repeat-until-time floor circuits so the workout can be completed without in-class phone logging.",
            [
                Slot("OTF_0928_TREAD_BASE", "Treadmill Base at 3%", 5, 1,
                    "Five 90 sec base efforts at 3% (PW 5%+).", true, "Tread Block", "Tread", 450, null, 3m, null, false, "5 x 90 sec base @ 3%"),
                Slot("OTF_0928_ELEVATION_SURGE", "Treadmill Elevation Surge", 5, 1,
                    "First published elevation-tracking version used for calculation: 20 / 30 / 40 / 30 / 20 ft.", true, "Tread Block", "Tread", null, null, null, null, false, "Elevation targets: 20 / 30 / 40 / 30 / 20 ft"),
                Slot("OTF_0928_WALKING_RECOVERY", "Walking Recovery", 5, 1,
                    "Walking recovery after each surge; return to base AQAP.", true, "Tread Block", "Recovery", null, null, null, null, false, "WR after each surge; base AQAP"),
                Slot("OTF_0928_TREAD_FINISHER", "Treadmill All Out", 1, 1,
                    "Power walkers at 10%+.", true, "Tread Finisher", "Tread", 60, null, null, null, false, "60 sec All Out (PW 10%+)"),

                Slot("OTF_0928_MID_BAND_SQUAT", "Dumbbell Mid Band Squat", 3, 8,
                    "Floor Block 1; back-to-back; repeat until time.", false, "Floor Block 1", "Circuit", 540, null, null, 3, true, "8 reps x ~3 rounds"),
                Slot("OTF_0928_MID_BAND_BRIDGE", "Dumbbell Mid Band Bridge", 3, 8,
                    "Shoulders on bench; stick it.", false, "Floor Block 1", "Circuit", 540, null, null, 3, true, "8 reps x ~3 rounds"),
                Slot("OTF_0928_SIDE_PLANK_LEG_LIFT", "Mid Band Low Side Plank Leg Lift", 3, 4,
                    "4 each side.", false, "Floor Block 1", "Circuit", 540, null, null, 3, true, "4/side x ~3 rounds"),
                Slot("OTF_0928_HIGH_PLANK_TOE_TAP", "Mid Band High Plank Toe Tap", 3, 4,
                    "4 each side; stick it.", false, "Floor Block 1", "Circuit", 540, null, null, 3, true, "4/side x ~3 rounds"),

                Slot("OTF_0928_LATERAL_STEP_UP", "Dumbbell Single Arm Single Leg Lateral Step-Up With Shift", 3, 8,
                    "8 each side.", false, "Floor Block 2", "Circuit", 780, null, null, 3, true, "8/side x ~3 rounds"),
                Slot("OTF_0928_GOBLET_LATERAL_SHIFT", "Goblet Alternating Lateral Shift", 3, 8,
                    "8 total; stick it.", false, "Floor Block 2", "Circuit", 780, null, null, 3, true, "8 total x ~3 rounds"),
                Slot("OTF_0928_ARNOLD_PRESS", "Tall Kneeling Arnold Press", 3, 8,
                    "Back-to-back with curl.", false, "Floor Block 2", "Circuit", 780, null, null, 3, true, "8 reps x ~3 rounds"),
                Slot("OTF_0928_BICEP_CURL", "Tall Kneeling Bicep Curl", 3, 8,
                    "Stick it.", false, "Floor Block 2", "Circuit", 780, null, null, 3, true, "8 reps x ~3 rounds"),
                Slot("OTF_0928_BRIDGE_CHEST_PRESS", "Bridge Hold With Chest Press", 3, 8,
                    "Back-to-back with low row.", false, "Floor Block 2", "Circuit", 780, null, null, 3, true, "8 reps x ~3 rounds"),
                Slot("OTF_0928_FOUR_POINT_LOW_ROW", "4 Point Single Arm Low Row", 3, 4,
                    "4 each side; stick it.", false, "Floor Block 2", "Circuit", 780, null, null, 3, true, "4/side x ~3 rounds"),

                Slot("OTF_0928_BICEP_FINISHER", "Tall Kneeling Bicep Curl", 1, 1,
                    "Timed finisher.", false, "Floor Finisher", "Finisher", 30, null, null, null, false, "30 sec"),
                Slot("OTF_0928_ARNOLD_FINISHER", "Tall Kneeling Arnold Press", 1, 1,
                    "Timed finisher.", false, "Floor Finisher", "Finisher", 30, null, null, null, false, "30 sec")
            ],
            GroupName: "Orangetheory",
            PlannedMinutes: 60m);

    private static SystemWorkoutExerciseSlot Slot(
        string slotCode,
        string exerciseName,
        int sets,
        int reps,
        string notes,
        bool allowCardio = false,
        string? blockName = null,
        string? blockType = null,
        int? durationSeconds = null,
        decimal? distanceMeters = null,
        decimal? inclinePercent = null,
        int? estimatedRounds = null,
        bool isEstimated = false,
        string? workloadText = null)
        => new(
            slotCode,
            [exerciseName],
            [],
            sets,
            reps,
            AllowCardio: allowCardio,
            RequireNameMatch: true,
            Notes: notes,
            BlockName: blockName,
            BlockType: blockType,
            DurationSeconds: durationSeconds,
            DistanceMeters: distanceMeters,
            InclinePercent: inclinePercent,
            EstimatedRounds: estimatedRounds,
            IsEstimated: isEstimated,
            WorkloadText: workloadText);
}
