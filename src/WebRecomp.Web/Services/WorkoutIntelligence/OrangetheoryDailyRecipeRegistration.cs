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

    internal static SystemWorkoutRecipe Create20260929Recipe()
        => new SystemWorkoutRecipe(
            "OTF_2G_2026_09_29",
            "OTF 2G - 2026-09-29",
            "ORANGETHEORY",
            "Orangetheory 2G power run/row with published 500 m rows, exact tread distances, cluster-set floor work, and two timed member-choice metabolic blocks. Repeat-until-time circuit rounds are explicitly marked as estimates; published distances, reps, and work/rest times remain exact.",
            [
                Slot("OTF_0929_ROW_500_REQUIRED", "Row", 2, 1,
                    "Two required 500 m rows: one before the ascending tread ladder and one before the descending tread ladder.", true, "Tread / Row Block", "Row", null, 500m, null, null, false, "2 x 500 m row"),
                Slot("OTF_0929_TREAD_AO_160", "Treadmill All Out", 2, 1,
                    "160 m / 0.1 mile each; power walkers 80 m / 0.05 miles at 10%+.", true, "Tread / Row Block", "Tread", null, 160m, null, null, false, "2 x 160 m All Out"),
                Slot("OTF_0929_TREAD_PUSH_AO_240", "Treadmill Push to All Out", 2, 1,
                    "240 m / 0.15 mile each; power walkers 120 m / 0.08 miles at 8-10%.", true, "Tread / Row Block", "Tread", null, 240m, null, null, false, "2 x 240 m Push to All Out"),
                Slot("OTF_0929_TREAD_PUSH_320", "Treadmill Push", 2, 1,
                    "320 m / 0.2 mile each; power walkers 160 m / 0.1 miles at 6%+.", true, "Tread / Row Block", "Tread", null, 320m, null, null, false, "2 x 320 m Push"),
                Slot("OTF_0929_TREAD_PUSH_400", "Treadmill Push", 2, 1,
                    "400 m / 0.25 mile each; power walkers 200 m / 0.13 miles at 6%+.", true, "Tread / Row Block", "Tread", null, 400m, null, null, false, "2 x 400 m Push"),
                Slot("OTF_0929_WALKING_RECOVERY", "Walking Recovery", 8, 1,
                    "Walking recovery after every tread effort; continue when ready.", true, "Tread / Row Block", "Recovery", null, null, null, null, false, "8 walking recoveries"),
                Slot("OTF_0929_BONUS_ROW", "Row", 1, 1,
                    "Bonus 500 m row if the full ascending and descending tread ladder is completed.", true, "Tread / Row Bonus", "Row", null, 500m, null, null, false, "Bonus 500 m row"),
                Slot("OTF_0929_BONUS_TREAD", "Treadmill for Distance", 1, 1,
                    "After the bonus 500 m row, tread for distance until time is called.", true, "Tread / Row Bonus", "Tread", null, null, null, null, false, "Tread for distance until time"),

                Slot("OTF_0929_NEUTRAL_THRUSTER", "Single Dumbbell Neutral Grip Thruster", 2, 8,
                    "Floor Block 1 - 6.25 minutes. Do 8 reps, rest about 10 sec, then AMRAP; repeat the circuit until time.", false, "Floor Block 1", "Cluster Set", 375, null, null, 2, true, "8 + AMRAP x ~2 rounds"),
                Slot("OTF_0929_WIDE_BICEP_CURL", "Wide Bicep Curl", 2, 8,
                    "Floor Block 1 - 6.25 minutes. Do 8 reps, rest about 10 sec, then AMRAP; repeat the circuit until time.", false, "Floor Block 1", "Cluster Set", 375, null, null, 2, true, "8 + AMRAP x ~2 rounds"),
                Slot("OTF_0929_HIGH_PLANK_UP_DOWN", "Bodyweight Low Bench High Plank Alternating Up and Down", 2, 8,
                    "8 total per circuit; repeat until time.", false, "Floor Block 1", "Circuit", 375, null, null, 2, true, "8 total x ~2 rounds"),

                Slot("OTF_0929_BOX_JUMP_BLOCK_2", "Low Bench Box Jump", 1, 1,
                    "Floor Block 2 member choice. Perform this OR the reverse-lunge option: 60 sec work, 15 sec rest, 45 sec work, 30 sec rest, 30 sec work. Remove the unchosen exercise row.", false, "Floor Block 2", "Member Choice", 135, null, null, null, false, "60 sec + 45 sec + 30 sec work"),
                Slot("OTF_0929_REVERSE_LUNGE_BLOCK_2", "Dumbbell Low Bench Alternating Step-Down Reverse Lunge to Single Leg Knee Drive", 1, 1,
                    "Floor Block 2 member choice. Perform this OR low bench box jumps: 60 sec work, 15 sec rest, 45 sec work, 30 sec rest, 30 sec work. Remove the unchosen exercise row.", false, "Floor Block 2", "Member Choice", 135, null, null, null, false, "60 sec + 45 sec + 30 sec work"),

                Slot("OTF_0929_HIP_HINGE_SWING", "Hip Hinge Swing", 2, 8,
                    "Floor Block 3 - 8.75 minutes. Do 8 reps, rest about 10 sec, then AMRAP; repeat the circuit until time.", false, "Floor Block 3", "Cluster Set", 525, null, null, 2, true, "8 + AMRAP x ~2 rounds"),
                Slot("OTF_0929_REVERSE_GRIP_LOW_ROW", "Reverse Grip Low Row", 2, 8,
                    "Floor Block 3 - 8.75 minutes. Do 8 reps, rest about 10 sec, then AMRAP; repeat the circuit until time.", false, "Floor Block 3", "Cluster Set", 525, null, null, 2, true, "8 + AMRAP x ~2 rounds"),
                Slot("OTF_0929_STEP_DOWN_SQUAT", "Low Bench Goblet Alternating Step-Down Squat", 2, 8,
                    "8 total, rest about 10 sec, then AMRAP; repeat the circuit until time.", false, "Floor Block 3", "Cluster Set", 525, null, null, 2, true, "8 total + AMRAP x ~2 rounds"),
                Slot("OTF_0929_HIGH_PLANK_KNEE_DRIVE", "Low Bench High Plank Alternating Knee Drive", 2, 16,
                    "16 total with feet on the bench; repeat the circuit until time.", false, "Floor Block 3", "Circuit", 525, null, null, 2, true, "16 total x ~2 rounds"),

                Slot("OTF_0929_BOX_JUMP_BLOCK_4", "Low Bench Box Jump", 1, 1,
                    "Floor Block 4 member choice. Perform this OR the reverse-lunge option: 60 sec work, 15 sec rest, 45 sec work, 30 sec rest, 30 sec work. Remove the unchosen exercise row.", false, "Floor Block 4", "Member Choice", 135, null, null, null, false, "60 sec + 45 sec + 30 sec work"),
                Slot("OTF_0929_REVERSE_LUNGE_BLOCK_4", "Dumbbell Low Bench Alternating Step-Down Reverse Lunge to Single Leg Knee Drive", 1, 1,
                    "Floor Block 4 member choice. Perform this OR low bench box jumps: 60 sec work, 15 sec rest, 45 sec work, 30 sec rest, 30 sec work. Remove the unchosen exercise row.", false, "Floor Block 4", "Member Choice", 135, null, null, null, false, "60 sec + 45 sec + 30 sec work")
            ],
            GroupName: "Orangetheory",
            PlannedMinutes: 60m);


    internal static SystemWorkoutRecipe Create20260930Recipe()
        => new SystemWorkoutRecipe(
            "OTF_2G_2026_09_30",
            "OTF 2G - 2026-09-30",
            "ORANGETHEORY",
            "Orangetheory 2G repeat of the September 14 template. Preserves the publicly reported standard 2G floor reverse-drop-set block with exact movement names, 5-rep loading steps, 300 m push row, and 18.5-minute duration. Three circuit passes are an explicit calculation estimate only.",
            [
                Slot("OTF_0930_SQUAT", "Two Handed Single Dumbbell Squat", 3, 5, "Floor Block 1; 5 reps, then 5 reps heavier, rest; repeat circuit until time.", false, "Floor Block 1", "Reverse Drop Set", 1110, null, null, 3, true, "5 reps + 5 heavier x ~3 rounds"),
                Slot("OTF_0930_BOSU_PULLOVER", "Bosu Bridge Hold With Dumbbell Pullover", 3, 5, "Floor Block 1; 5 reps, then 5 reps heavier, rest; repeat circuit until time.", false, "Floor Block 1", "Reverse Drop Set", 1110, null, null, 3, true, "5 reps + 5 heavier x ~3 rounds"),
                Slot("OTF_0930_DEADLIFT", "Two Handed Single Dumbbell Deadlift", 3, 5, "Floor Block 1; 5 reps, then 5 reps heavier, rest; repeat circuit until time.", false, "Floor Block 1", "Reverse Drop Set", 1110, null, null, 3, true, "5 reps + 5 heavier x ~3 rounds"),
                Slot("OTF_0930_BOSU_TRICEP", "Bosu Kneeling Tricep Extension", 3, 5, "Floor Block 1; 5 reps, then 5 reps heavier, rest; repeat circuit until time.", false, "Floor Block 1", "Reverse Drop Set", 1110, null, null, 3, true, "5 reps + 5 heavier x ~3 rounds"),
                Slot("OTF_0930_PUSH_ROW", "Row", 3, 1, "Floor Block 1; 300 m push row after the four reverse-drop-set pairs; repeat circuit until time.", true, "Floor Block 1", "Row", 1110, 300m, null, 3, true, "300 m push row x ~3 rounds")
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
