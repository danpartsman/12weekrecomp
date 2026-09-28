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
            "Orangetheory 2G elevation-challenge template for September 28, 2026. The 23-minute tread sequence, elevation targets, fixed floor reps, repeat-until-time blocks, recoveries, and timed finishers are preserved in the exercise rows and notes while Engine 3.0 retains the calibrated Orangetheory composite physiology.",
            [
                Slot("OTF_0928_TREAD_BASE", "Treadmill Base at 3%", 5, 1,
                    "Tread Block - 23 minutes. Five parts; 90 sec base at 3% in each part (power walkers at 5%+).", allowCardio: true),
                Slot("OTF_0928_ELEVATION_SURGE", "Treadmill Elevation Surge", 5, 1,
                    "Member choice speed and incline. Sequential elevation targets: 6 m / 20 ft, 9 m / 30 ft, 12 m / 40 ft, 9 m / 30 ft, 6 m / 20 ft.", allowCardio: true),
                Slot("OTF_0928_WALKING_RECOVERY", "Walking Recovery", 5, 1,
                    "After each elevation target, take walking recovery, return to base as quickly as possible, and maintain base until the next part.", allowCardio: true),
                Slot("OTF_0928_TREAD_FINISHER", "Treadmill All Out", 1, 1,
                    "Tread finisher: 1 minute all out; power walkers at 10%+.", allowCardio: true),
                Slot("OTF_0928_MID_BAND_SQUAT", "Dumbbell Mid Band Squat", 1, 8,
                    "Floor Block 1 - 9 minutes; back-to-back with dumbbell mid band bridge, then rest; repeat the full block until time."),
                Slot("OTF_0928_MID_BAND_BRIDGE", "Dumbbell Mid Band Bridge", 1, 8,
                    "Floor Block 1 - 9 minutes; shoulders on bench, stick it; back-to-back with dumbbell mid band squat, then rest; repeat until time."),
                Slot("OTF_0928_SIDE_PLANK_LEG_LIFT", "Mid Band Low Side Plank Leg Lift", 1, 4,
                    "Floor Block 1 - 9 minutes; 4 each side; back-to-back with mid band high plank toe tap, then rest; repeat until time."),
                Slot("OTF_0928_HIGH_PLANK_TOE_TAP", "Mid Band High Plank Toe Tap", 1, 4,
                    "Floor Block 1 - 9 minutes; 4 each side, stick it; repeat the full block until time. Follow with 90 sec recovery."),
                Slot("OTF_0928_LATERAL_STEP_UP", "Dumbbell Single Arm Single Leg Lateral Step-Up With Shift", 1, 8,
                    "Floor Block 2 - 13 minutes; 8 each side; back-to-back with goblet alternating lateral shift, then rest; repeat until finisher."),
                Slot("OTF_0928_GOBLET_LATERAL_SHIFT", "Goblet Alternating Lateral Shift", 1, 8,
                    "Floor Block 2 - 13 minutes; 8 total, stick it; paired with dumbbell single arm single leg lateral step-up with shift."),
                Slot("OTF_0928_ARNOLD_PRESS", "Tall Kneeling Arnold Press", 1, 8,
                    "Floor Block 2 - 13 minutes; back-to-back with tall kneeling bicep curl, then rest; repeat until finisher."),
                Slot("OTF_0928_BICEP_CURL", "Tall Kneeling Bicep Curl", 1, 8,
                    "Floor Block 2 - 13 minutes; stick it; paired with tall kneeling Arnold press."),
                Slot("OTF_0928_BRIDGE_CHEST_PRESS", "Bridge Hold With Chest Press", 1, 8,
                    "Floor Block 2 - 13 minutes; back-to-back with 4 point single arm low row, then rest; repeat until finisher."),
                Slot("OTF_0928_FOUR_POINT_LOW_ROW", "4 Point Single Arm Low Row", 1, 4,
                    "Floor Block 2 - 13 minutes; 4 each side, stick it; paired with bridge hold with chest press."),
                Slot("OTF_0928_BICEP_FINISHER", "Tall Kneeling Bicep Curl", 1, 1,
                    "Floor finisher: 30 seconds."),
                Slot("OTF_0928_ARNOLD_FINISHER", "Tall Kneeling Arnold Press", 1, 1,
                    "Floor finisher: 30 seconds.")
            ],
            GroupName: "Orangetheory",
            PlannedMinutes: 60m);

    private static SystemWorkoutExerciseSlot Slot(
        string slotCode,
        string exerciseName,
        int sets,
        int reps,
        string notes,
        bool allowCardio = false)
        => new(
            slotCode,
            [exerciseName],
            [],
            sets,
            reps,
            AllowCardio: allowCardio,
            RequireNameMatch: true,
            Notes: notes);
}
