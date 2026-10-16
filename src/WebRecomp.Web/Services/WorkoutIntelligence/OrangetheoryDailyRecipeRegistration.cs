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
            "Complete September 30 2G repeat of September 14: four published tread blocks, the 18.5-minute reverse-drop-set floor block with 300 m push row, and the 3.5-minute BOSU 360-core block with finisher.",
            [
                Slot("OTF_0930_T1_BASE_4", "Treadmill Base at 4%", 1, 1, "Tread Block 1.", true, "Tread Block 1", "Tread", 120, null, 4m, null, false, "2 min base @ 4% (PW 6%+)"),
                Slot("OTF_0930_T1_BASE", "Treadmill Base", 1, 1, "Tread Block 1.", true, "Tread Block 1", "Tread", 60, null, null, null, false, "1 min base"),
                Slot("OTF_0930_T1_PUSH_4", "Treadmill Push at 4%", 1, 1, "Tread Block 1.", true, "Tread Block 1", "Tread", 60, null, 4m, null, false, "1 min push @ 4% (PW 6%+)"),
                Slot("OTF_0930_T1_BASE_2", "Treadmill Base", 1, 1, "Tread Block 1.", true, "Tread Block 1", "Tread", 60, null, null, null, false, "1 min base"),
                Slot("OTF_0930_T1_AO_4", "Treadmill All Out at 4%", 1, 1, "Tread Block 1.", true, "Tread Block 1", "Tread", 30, null, 4m, null, false, "30 sec AO @ 4% (PW 10%+)"),
                Slot("OTF_0930_WR_1", "Walking Recovery", 1, 1, "Between Tread Blocks 1 and 2; get to base when ready.", true, "Tread Recovery 1", "Recovery", 60, null, null, null, false, "1 min WR"),

                Slot("OTF_0930_T2_PUSH_1", "Treadmill Push", 1, 1, "Tread Block 2.", true, "Tread Block 2", "Tread", 120, null, null, null, false, "2 min push (PW 5%+)"),
                Slot("OTF_0930_T2_BASE_1", "Treadmill Base", 1, 1, "Tread Block 2.", true, "Tread Block 2", "Tread", 60, null, null, null, false, "1 min base"),
                Slot("OTF_0930_T2_PUSH_2", "Treadmill Push", 1, 1, "Tread Block 2.", true, "Tread Block 2", "Tread", 60, null, null, null, false, "1 min push (PW 6%+)"),
                Slot("OTF_0930_T2_BASE_2", "Treadmill Base", 1, 1, "Tread Block 2.", true, "Tread Block 2", "Tread", 60, null, null, null, false, "1 min base"),
                Slot("OTF_0930_T2_AO", "Treadmill All Out", 1, 1, "Tread Block 2.", true, "Tread Block 2", "Tread", 30, null, null, null, false, "30 sec AO (PW 10%+)"),
                Slot("OTF_0930_WR_2", "Walking Recovery", 1, 1, "Between Tread Blocks 2 and 3; get to base when ready.", true, "Tread Recovery 2", "Recovery", 60, null, null, null, false, "1 min WR"),

                Slot("OTF_0930_T3_PUSH_AO", "Treadmill Push to All Out", 1, 1, "Tread Block 3.", true, "Tread Block 3", "Tread", 120, null, null, null, false, "2 min push to AO (PW 5-10%)"),
                Slot("OTF_0930_T3_WR_1", "Walking Recovery", 1, 1, "Tread Block 3.", true, "Tread Block 3", "Recovery", 60, null, null, null, false, "1 min WR"),
                Slot("OTF_0930_T3_AO_1", "Treadmill All Out", 1, 1, "Tread Block 3.", true, "Tread Block 3", "Tread", 60, null, null, null, false, "1 min AO (PW 10%+)"),
                Slot("OTF_0930_T3_WR_2", "Walking Recovery", 1, 1, "Tread Block 3.", true, "Tread Block 3", "Recovery", 60, null, null, null, false, "1 min WR"),
                Slot("OTF_0930_T3_AO_2", "Treadmill All Out", 1, 1, "Tread Block 3.", true, "Tread Block 3", "Tread", 30, null, null, null, false, "30 sec AO (PW 10%+)"),
                Slot("OTF_0930_WR_3", "Walking Recovery", 1, 1, "Between Tread Blocks 3 and 4; get to base when ready.", true, "Tread Recovery 3", "Recovery", 60, null, null, null, false, "1 min WR"),

                Slot("OTF_0930_T4_BASE_4", "Treadmill Base at 4%", 1, 1, "Tread Block 4.", true, "Tread Block 4", "Tread", 120, null, 4m, null, false, "2 min base @ 4% (PW 6%+)"),
                Slot("OTF_0930_T4_PUSH", "Treadmill Push", 1, 1, "Tread Block 4.", true, "Tread Block 4", "Tread", 60, null, null, null, false, "1 min push (PW 6%+)"),
                Slot("OTF_0930_T4_FINISHER", "Treadmill All Out", 1, 1, "Tread Block 4 finisher.", true, "Tread Block 4", "Finisher", 30, null, null, null, false, "30 sec AO (PW 10%+)"),

                Slot("OTF_0930_SQUAT", "Two Handed Single Dumbbell Squat", 3, 5, "Floor Block 1; 5 reps, then 5 reps heavier, rest; repeat circuit until time.", false, "Floor Block 1", "Reverse Drop Set", 1110, null, null, 3, true, "5 reps + 5 heavier x ~3 rounds"),
                Slot("OTF_0930_BOSU_PULLOVER", "Bosu Bridge Hold With Dumbbell Pullover", 3, 5, "Floor Block 1; 5 reps, then 5 reps heavier, rest; repeat circuit until time.", false, "Floor Block 1", "Reverse Drop Set", 1110, null, null, 3, true, "5 reps + 5 heavier x ~3 rounds"),
                Slot("OTF_0930_DEADLIFT", "Two Handed Single Dumbbell Deadlift", 3, 5, "Floor Block 1; 5 reps, then 5 reps heavier, rest; repeat circuit until time.", false, "Floor Block 1", "Reverse Drop Set", 1110, null, null, 3, true, "5 reps + 5 heavier x ~3 rounds"),
                Slot("OTF_0930_BOSU_TRICEP", "Bosu Kneeling Tricep Extension", 3, 5, "Floor Block 1; 5 reps, then 5 reps heavier, rest; repeat circuit until time.", false, "Floor Block 1", "Reverse Drop Set", 1110, null, null, 3, true, "5 reps + 5 heavier x ~3 rounds"),
                Slot("OTF_0930_PUSH_ROW", "Row", 3, 1, "Floor Block 1; 300 m push row after the four reverse-drop-set pairs; repeat circuit until time.", true, "Floor Block 1", "Row", 1110, 300m, null, 3, true, "300 m push row x ~3 rounds"),

                Slot("OTF_0930_BOSU_CRUNCH", "BOSU Crunch", 1, 10, "Floor Block 2; 360 core circuit; repeat until finisher.", false, "Floor Block 2", "360 Core", 210, null, null, null, false, "10 reps"),
                Slot("OTF_0930_SIDE_PLANK_DIP", "BOSU Low Side Plank Dip", 1, 10, "Floor Block 2; 10 each side; repeat until finisher.", false, "Floor Block 2", "360 Core", 210, null, null, null, false, "10 each"),
                Slot("OTF_0930_BACK_EXTENSION", "Back Extension", 1, 10, "Floor Block 2; repeat until finisher.", false, "Floor Block 2", "360 Core", 210, null, null, null, false, "10 reps"),
                Slot("OTF_0930_CORE_FINISHER", "BOSU High Plank Alternating Knee Drive", 1, 1, "Floor Block 2 finisher.", false, "Floor Block 2", "Finisher", 30, null, null, null, false, "30 sec")
            ],
            GroupName: "Orangetheory",
            PlannedMinutes: 60m);


    internal static SystemWorkoutRecipe Create20260930Strength50Recipe()
        => new SystemWorkoutRecipe(
            "OTF_STRENGTH50_2026_09_30",
            "OTF Strength 50 Total - 2026-09-30",
            "RESISTANCE_CIRCUIT",
            "Complete September 30 Strength 50 Total Body template. Each 12-minute set is represented as two distinct blocks: A = 8 minutes strength, B = 4 minutes power. Three A/B pairs total 36 minutes, followed by the published 60-second finisher.",
            [
                Slot("OTF_S50_0930_SPLIT_SQUAT_BOSU", "Dumbbell Split Squat With Leg On BOSU", 1, 8, "Block 1A; repeat strength work for 8 minutes.", false, "Block 1A", "Strength", 480, null, null, null, false, "8 reps"),
                Slot("OTF_S50_0930_REVERSE_FLY", "Reverse Fly", 1, 8, "Block 1A; repeat strength work for 8 minutes.", false, "Block 1A", "Strength", 480, null, null, null, false, "8 reps"),
                Slot("OTF_S50_0930_SPEED_SKATER", "Speed Skater With Power", 1, 4, "Block 1B; power work for 4 minutes.", false, "Block 1B", "Power", 240, null, null, null, false, "4-8 reps"),
                Slot("OTF_S50_0930_BOSU_PUSHUP", "BOSU Push-Up", 1, 4, "Block 1B; power work for 4 minutes.", false, "Block 1B", "Power", 240, null, null, null, false, "4-8 reps"),

                Slot("OTF_S50_0930_SQUAT", "Dumbbell Squat", 1, 8, "Block 2A; repeat strength work for 8 minutes.", false, "Block 2A", "Strength", 480, null, null, null, false, "8 reps"),
                Slot("OTF_S50_0930_LOW_ROW_BOSU", "Split Stance Single Arm Low Row With Leg On BOSU", 1, 8, "Block 2A; repeat strength work for 8 minutes.", false, "Block 2A", "Strength", 480, null, null, null, false, "8 reps"),
                Slot("OTF_S50_0930_PLANK_POP", "BOSU Plank Pop", 1, 4, "Block 2B; power work for 4 minutes.", false, "Block 2B", "Power", 240, null, null, null, false, "4-8 reps"),
                Slot("OTF_S50_0930_CLEAN", "Single Arm Clean", 1, 4, "Block 2B; power work for 4 minutes.", false, "Block 2B", "Power", 240, null, null, null, false, "4-8 reps"),

                Slot("OTF_S50_0930_DEADLIFT", "Single Arm Split Stance Deadlift", 1, 8, "Block 3A; repeat strength work for 8 minutes.", false, "Block 3A", "Strength", 480, null, null, null, false, "8 reps"),
                Slot("OTF_S50_0930_UPRIGHT_ROW", "Upright Row", 1, 8, "Block 3A; repeat strength work for 8 minutes.", false, "Block 3A", "Strength", 480, null, null, null, false, "8 reps"),
                Slot("OTF_S50_0930_HIP_HINGE_SWING", "Single Arm Hip Hinge Swing", 1, 4, "Block 3B; power work for 4 minutes.", false, "Block 3B", "Power", 240, null, null, null, false, "4-8 reps"),
                Slot("OTF_S50_0930_SNATCH", "Alternating Single Arm Snatch", 1, 4, "Block 3B; power work for 4 minutes.", false, "Block 3B", "Power", 240, null, null, null, false, "4-8 reps"),
                Slot("OTF_S50_0930_FINISHER_LEFT", "Single Arm Hip Hinge Swing - Left", 1, 1, "Timed finisher, left arm.", false, "Finisher", "Power", 30, null, null, null, false, "30 sec left"),
                Slot("OTF_S50_0930_FINISHER_RIGHT", "Single Arm Hip Hinge Swing - Right", 1, 1, "Timed finisher, right arm.", false, "Finisher", "Power", 30, null, null, null, false, "30 sec right")
            ],
            GroupName: "Orangetheory",
            PlannedMinutes: 50m);


    internal static SystemWorkoutRecipe Create20261002HellWeekStrengthRecipe()
        => new SystemWorkoutRecipe(
            "OTF_STRENGTH60_2026_10_02",
            "OTF Hell Week Strength 60 - 2026-10-02",
            "RESISTANCE_CIRCUIT",
            "Hell Week 60-minute strength template focused on glutes/hamstrings, upper chest/triceps, delts/posture, and glutes/posture. Published sets, rep ranges, and rest periods are preserved in the workload text and notes. The member-choice bonus and surprise-card penalties are retained as coaching notes rather than fabricated exercise rows.",
            [
                Slot("OTF_HW_1002_RDL", "Barbell Romanian Deadlift", 3, 10,
                    "Block 1 - Glutes + Hamstrings. 3 x 8-10; rest 75 sec.", false, "Block 1 - Glutes + Hamstrings", "Strength", null, null, null, null, false, "3 x 8-10; rest 75 sec"),
                Slot("OTF_HW_1002_HIP_THRUST", "Barbell Hip Thrust", 3, 10,
                    "Block 1 - Glutes + Hamstrings. 3 x 8-10; rest 75 sec.", false, "Block 1 - Glutes + Hamstrings", "Strength", null, null, null, null, false, "3 x 8-10; rest 75 sec"),
                Slot("OTF_HW_1002_SUMO_SQUAT", "Dumbbell Sumo Squat", 3, 10,
                    "Block 1 - Glutes + Hamstrings. 3 x 10; rest 60 sec.", false, "Block 1 - Glutes + Hamstrings", "Strength", null, null, null, null, false, "3 x 10; rest 60 sec"),

                Slot("OTF_HW_1002_INCLINE_PRESS", "Incline Dumbbell Press", 3, 10,
                    "Block 2 - Upper Chest + Triceps. 3 x 8-10; rest 75 sec.", false, "Block 2 - Upper Chest + Triceps", "Strength", null, null, null, null, false, "3 x 8-10; rest 75 sec"),
                Slot("OTF_HW_1002_LOW_HIGH_FLY", "Low-to-High Cable Fly", 3, 10,
                    "Block 2 - Upper Chest + Triceps. 3 x 10; rest 60 sec.", false, "Block 2 - Upper Chest + Triceps", "Strength", null, null, null, null, false, "3 x 10; rest 60 sec"),
                Slot("OTF_HW_1002_PRESSDOWN", "Cable Triceps Pressdown", 3, 10,
                    "Block 2 - Upper Chest + Triceps. 3 x 10; rest 60 sec.", false, "Block 2 - Upper Chest + Triceps", "Strength", null, null, null, null, false, "3 x 10; rest 60 sec"),

                Slot("OTF_HW_1002_LATERAL_RAISE", "Dumbbell Lateral Raise", 3, 10,
                    "Block 3 - Delts + Posture. 3 x 10; rest 60 sec.", false, "Block 3 - Delts + Posture", "Strength", null, null, null, null, false, "3 x 10; rest 60 sec"),
                Slot("OTF_HW_1002_REAR_DELT_FLY", "Dumbbell Rear-Delt Fly", 3, 10,
                    "Block 3 - Delts + Posture. 3 x 10; rest 60 sec.", false, "Block 3 - Delts + Posture", "Strength", null, null, null, null, false, "3 x 10; rest 60 sec"),
                Slot("OTF_HW_1002_FACE_PULL", "Cable Face Pull", 3, 10,
                    "Block 3 - Delts + Posture. 3 x 10; rest 60 sec.", false, "Block 3 - Delts + Posture", "Strength", null, null, null, null, false, "3 x 10; rest 60 sec"),

                Slot("OTF_HW_1002_SPLIT_SQUAT", "Split Squat", 2, 5,
                    "Block 4 - Glutes + Posture. Heavy split squat, 2 x 5 each side; rest 60 sec.", false, "Block 4 - Glutes + Posture", "Strength", null, null, null, null, false, "2 x 5/side; rest 60 sec"),
                Slot("OTF_HW_1002_BENT_ROW", "Barbell Bent-Over Row", 2, 10,
                    "Block 4 - Glutes + Posture. Heavy barbell bent-over row, 2 x 10; rest 60 sec.", false, "Block 4 - Glutes + Posture", "Strength", null, null, null, null, false, "2 x 10; rest 60 sec")
            ],
            GroupName: "Orangetheory",
            PlannedMinutes: 60m);


    internal static SystemWorkoutRecipe Create20261006Recipe()
        => new SystemWorkoutRecipe(
            "OTF_2G_2026_10_06",
            "OTF 2G - 2026-10-06",
            "ORANGETHEORY",
            "October 6 E-P-S 2G. Published treadmill timing and inclines are preserved exactly. Coach-choice lift-or-row buy-ins remain explicit alternatives rather than being double-counted; repeat-until-time floor work is represented conservatively for workload estimation.",
            [
                Slot("OTF_1006_T1_PUSH_2", "Treadmill Push", 1, 1, "Tread Block 1.", true, "Tread Block 1", "Endurance", 120, null, 5m, null, false, "2 min push (PW 5%+)"),
                Slot("OTF_1006_T1_BASE_1", "Treadmill Base", 1, 1, "Tread Block 1.", true, "Tread Block 1", "Endurance", 60, null, null, null, false, "1 min base"),
                Slot("OTF_1006_T1_PUSH_1", "Treadmill Push", 1, 1, "Tread Block 1.", true, "Tread Block 1", "Endurance", 60, null, 5m, null, false, "1 min push (PW 5%+)"),
                Slot("OTF_1006_T1_BASE_2", "Treadmill Base", 1, 1, "Tread Block 1.", true, "Tread Block 1", "Endurance", 60, null, null, null, false, "1 min base"),
                Slot("OTF_1006_T1_PUSH_2B", "Treadmill Push", 1, 1, "Tread Block 1.", true, "Tread Block 1", "Endurance", 120, null, 5m, null, false, "2 min push (PW 5%+)"),
                Slot("OTF_1006_WR_1", "Walking Recovery", 1, 1, "90 sec walking recovery; return to base when ready.", true, "Tread Recovery 1", "Recovery", 90, null, null, null, false, "90 sec WR"),

                Slot("OTF_1006_T2_AO_60A", "Treadmill All Out", 1, 1, "Tread Block 2.", true, "Tread Block 2", "Power", 60, null, 10m, null, false, "1 min AO (PW 10%+)"),
                Slot("OTF_1006_T2_WR_60A", "Walking Recovery", 1, 1, "Tread Block 2.", true, "Tread Block 2", "Recovery", 60, null, null, null, false, "1 min WR"),
                Slot("OTF_1006_T2_AO_45A", "Treadmill All Out", 1, 1, "Tread Block 2.", true, "Tread Block 2", "Power", 45, null, 10m, null, false, "45 sec AO (PW 10%+)"),
                Slot("OTF_1006_T2_WR_45A", "Walking Recovery", 1, 1, "Tread Block 2.", true, "Tread Block 2", "Recovery", 45, null, null, null, false, "45 sec WR"),
                Slot("OTF_1006_T2_AO_30", "Treadmill All Out", 1, 1, "Tread Block 2.", true, "Tread Block 2", "Power", 30, null, 10m, null, false, "30 sec AO (PW 10%+)"),
                Slot("OTF_1006_T2_WR_30", "Walking Recovery", 1, 1, "Tread Block 2.", true, "Tread Block 2", "Recovery", 30, null, null, null, false, "30 sec WR"),
                Slot("OTF_1006_T2_AO_45B", "Treadmill All Out", 1, 1, "Tread Block 2.", true, "Tread Block 2", "Power", 45, null, 10m, null, false, "45 sec AO (PW 10%+)"),
                Slot("OTF_1006_T2_WR_45B", "Walking Recovery", 1, 1, "Tread Block 2.", true, "Tread Block 2", "Recovery", 45, null, null, null, false, "45 sec WR"),
                Slot("OTF_1006_T2_AO_60B", "Treadmill All Out", 1, 1, "Tread Block 2.", true, "Tread Block 2", "Power", 60, null, 10m, null, false, "1 min AO (PW 10%+)"),
                Slot("OTF_1006_WR_2", "Walking Recovery", 1, 1, "90 sec walking recovery; return to base when ready.", true, "Tread Recovery 2", "Recovery", 90, null, null, null, false, "90 sec WR"),

                Slot("OTF_1006_T3_INC_6A", "Treadmill Incline", 1, 1, "Tread Block 3.", true, "Tread Block 3", "Strength", 60, null, 6m, null, false, "1 min @ 6%"),
                Slot("OTF_1006_T3_INC_4A", "Treadmill Incline", 1, 1, "Tread Block 3.", true, "Tread Block 3", "Strength", 60, null, 4m, null, false, "1 min @ 4%"),
                Slot("OTF_1006_T3_INC_2", "Treadmill Incline", 1, 1, "Tread Block 3.", true, "Tread Block 3", "Strength", 60, null, 2m, null, false, "1 min @ 2%"),
                Slot("OTF_1006_T3_INC_4B", "Treadmill Incline", 1, 1, "Tread Block 3.", true, "Tread Block 3", "Strength", 60, null, 4m, null, false, "1 min @ 4%"),
                Slot("OTF_1006_T3_INC_6B", "Treadmill Incline", 1, 1, "Tread Block 3.", true, "Tread Block 3", "Strength", 60, null, 6m, null, false, "1 min @ 6%"),
                Slot("OTF_1006_T3_FLAT", "Treadmill", 1, 1, "Tread Block 3.", true, "Tread Block 3", "Strength", 90, null, 1m, null, false, "90 sec @ 1%"),

                Slot("OTF_1006_F1_VUP", "Bodyweight Full V-Up", 1, 25, "Floor Block 1 coach-choice buy-in: perform this OR the 500 m push row once.", false, "Floor Block 1", "Coach Choice", null, null, null, null, false, "25 reps OR 500 m push row"),
                Slot("OTF_1006_F1_ROW", "Row", 1, 1, "Floor Block 1 coach-choice buy-in: perform this OR 25 full V-ups once.", true, "Floor Block 1", "Coach Choice", null, 500m, null, null, false, "500 m push row OR 25 V-ups"),
                Slot("OTF_1006_F1_TAP_FRONT_SQUAT", "Dumbbell Tap Front Squat", 3, 8, "AMRAP with rest; repeat until the 7-minute block ends. Three rounds are an estimation anchor, not a published round count.", false, "Floor Block 1", "Work / Rest", 420, null, null, 3, true, "AMRAP + rest; ~3 rounds for calculation"),

                Slot("OTF_1006_F2_SKIER", "Skier Swing", 1, 25, "Floor Block 2 coach-choice buy-in: perform this OR the 250 m all-out row once.", false, "Floor Block 2", "Coach Choice", null, null, null, null, false, "25 reps OR 250 m AO row"),
                Slot("OTF_1006_F2_ROW", "Row", 1, 1, "Floor Block 2 coach-choice buy-in: perform this OR 25 skier swings once.", true, "Floor Block 2", "Coach Choice", null, 250m, null, null, false, "250 m AO row OR 25 skier swings"),
                Slot("OTF_1006_F2_SNATCH", "Single Arm Snatch", 3, 10, "5 each side; paired with squat jumps; repeat circuit until time. Three rounds used only as an estimation anchor.", false, "Floor Block 2", "Circuit", 420, null, null, 3, true, "5/side x ~3 rounds"),
                Slot("OTF_1006_F2_SQUAT_JUMP", "Bodyweight Squat Jump", 3, 5, "Back-to-back with snatches; rest after pair.", false, "Floor Block 2", "Circuit", 420, null, null, 3, true, "5 reps x ~3 rounds"),
                Slot("OTF_1006_F2_LUNGE_CLEAN", "Dumbbell Lateral Lunge to Single Arm Clean", 3, 10, "5 each side; paired with speed skaters; repeat circuit until time.", false, "Floor Block 2", "Circuit", 420, null, null, 3, true, "5/side x ~3 rounds"),
                Slot("OTF_1006_F2_SPEED_SKATER", "Speed Skater", 3, 10, "10 total; back-to-back with lateral lunge to clean; rest after pair.", false, "Floor Block 2", "Circuit", 420, null, null, 3, true, "10 total x ~3 rounds"),

                Slot("OTF_1006_F3_CHEST_PRESS", "Chest Press", 1, 25, "Floor Block 3 coach-choice buy-in: perform this OR the 250 m row once.", false, "Floor Block 3", "Coach Choice", null, null, null, null, false, "25 reps OR 250 m row"),
                Slot("OTF_1006_F3_ROW", "Row", 1, 1, "Floor Block 3 coach-choice buy-in: perform this OR 25 chest presses once; row at 20-24 spm.", true, "Floor Block 3", "Coach Choice", null, 250m, null, null, false, "250 m @ 20-24 spm OR 25 chest presses"),
                Slot("OTF_1006_F3_PULLOVER", "Dumbbell Bench Pullover", 3, 6, "6 reps, rest; repeat until finisher. Three rounds used only as an estimation anchor.", false, "Floor Block 3", "Work / Rest", 390, null, null, 3, true, "6 reps + rest; ~3 rounds"),
                Slot("OTF_1006_F3_BURPEE", "Full Burpee", 1, 1, "Finisher work interval.", false, "Floor Block 3", "Finisher", 60, null, null, null, false, "1 min"),
                Slot("OTF_1006_F3_WALKOUT", "Walkout", 1, 1, "Finisher active-recovery interval.", false, "Floor Block 3", "Finisher", 90, null, null, null, false, "90 sec")
            ],
            GroupName: "Orangetheory",
            PlannedMinutes: 60m);

    internal static SystemWorkoutRecipe Create20261006Strength50Recipe()
        => new SystemWorkoutRecipe(
            "OTF_STRENGTH50_2026_10_06",
            "OTF Strength 50 Lower - 2026-10-06",
            "RESISTANCE_CIRCUIT",
            "October 6 Strength 50 Lower Body template. Published 9:30, 22:30, and 3:00 blocks are preserved. Community correction is reflected: the first block uses a lateral shift rather than a lateral lunge, the second block uses the lateral step-over variation, and the final bridge interval is noted as 90 seconds.",
            [
                Slot("OTF_S50_1006_HIGH_PLANK_KNEE_DRIVE", "High Plank Knee Drive", 3, 12, "9:30 block; repeat circuit until time.", false, "Block 1 - 9:30", "Circuit", 570, null, null, 3, true, "12 reps x ~3 rounds"),
                Slot("OTF_S50_1006_SIDE_PLANK_LEG_LIFT", "Side Plank With Leg Lift", 3, 12, "9:30 block; 12 total/side as coached; repeat circuit until time.", false, "Block 1 - 9:30", "Circuit", 570, null, null, 3, true, "12 reps x ~3 rounds"),
                Slot("OTF_S50_1006_DB_BRIDGE_BENCH", "Dumbbell Bridge With Back On Bench", 3, 12, "9:30 block; repeat circuit until time.", false, "Block 1 - 9:30", "Circuit", 570, null, null, 3, true, "12 reps x ~3 rounds"),
                Slot("OTF_S50_1006_LATERAL_SHIFT", "Single Dumbbell Alternating Lateral Shift", 3, 12, "9:30 block. Community correction: lateral shift, not lateral lunge.", false, "Block 1 - 9:30", "Circuit", 570, null, null, 3, true, "12 total x ~3 rounds"),

                Slot("OTF_S50_1006_REV_LUNGE_ROTATION", "Goblet Alternating Reverse Lunge With Torso Rotation", 3, 12, "22:30 block; repeat long strength circuit until time.", false, "Block 2 - 22:30", "Strength Circuit", 1350, null, null, 3, true, "12 total x ~3 rounds"),
                Slot("OTF_S50_1006_STEP_UP", "Dumbbell Bench Step-Up", 3, 12, "22:30 block; 12 total.", false, "Block 2 - 22:30", "Strength Circuit", 1350, null, null, 3, true, "12 total x ~3 rounds"),
                Slot("OTF_S50_1006_REV_LUNGE_STEP_OVER", "Goblet Reverse Lunge With Torso Rotation to Lateral Step-Over", 3, 12, "22:30 block; 6 each side. Community follow-up described the lateral movement as a step-over variation.", false, "Block 2 - 22:30", "Strength Circuit", 1350, null, null, 3, true, "6/side x ~3 rounds"),
                Slot("OTF_S50_1006_SUMO_SQUAT", "Sumo Squat", 3, 5, "22:30 block.", false, "Block 2 - 22:30", "Strength Circuit", 1350, null, null, 3, true, "5 reps x ~3 rounds"),
                Slot("OTF_S50_1006_SUMO_DEADLIFT", "Sumo Deadlift", 3, 5, "22:30 block.", false, "Block 2 - 22:30", "Strength Circuit", 1350, null, null, 3, true, "5 reps x ~3 rounds"),
                Slot("OTF_S50_1006_SUMO_COMBO", "Sumo Squat to Deadlift Combo", 3, 5, "22:30 block.", false, "Block 2 - 22:30", "Strength Circuit", 1350, null, null, 3, true, "5 reps x ~3 rounds"),

                Slot("OTF_S50_1006_FINISH_PLANK_1", "High Plank Cross Knee Drive", 1, 1, "Final 3-minute block, first interval.", false, "Finisher - 3:00", "Finisher", 60, null, null, null, false, "1 min"),
                Slot("OTF_S50_1006_FINISH_BRIDGE", "Dumbbell Bridge With Alternating Knee Lift", 1, 1, "Final block. Community correction reports this bridge interval as 90 seconds.", false, "Finisher - 3:00", "Finisher", 90, null, null, null, false, "90 sec"),
                Slot("OTF_S50_1006_FINISH_PLANK_2", "High Plank Cross Knee Drive", 1, 1, "Final plank interval; preserve coach timing if studio demo differs.", false, "Finisher - 3:00", "Finisher", 30, null, null, null, false, "30 sec to complete 3:00 block")
            ],
            GroupName: "Orangetheory",
            PlannedMinutes: 50m);

    // Source: https://burnboard.app/otf-intel.html — S50, October 7, 2026.
    // Original daily thread: https://www.reddit.com/r/orangetheory/comments/1wzbv1b/
    internal static SystemWorkoutRecipe Create20261007Strength50Recipe()
        => new SystemWorkoutRecipe(
            "OTF_STRENGTH50_2026_10_07",
            "OTF Strength 50 Total - 2026-10-07",
            "RESISTANCE_CIRCUIT",
            "October 7 Strength 50 Total Body: three 12-minute blocks targeting the same primary muscle group back-to-back, a 30-second knee-tuck buy-out after Block 2, and a 3-minute core finisher. Published reps and timed work are preserved; three rounds per superset movement are editable estimates, not prescribed sets. Source: Burn Board S50 community intel for 2026-10-07.",
            [
                Slot("OTF_S50_1007_HIGH_ROW", "High Row", 3, 10,
                    "12-minute block; back-to-back with TRX Bridge Row, then rest. Three rounds are an editable estimate for phone-free logging; the source does not prescribe a fixed set count.", false, "Block 1", "Superset", 720, null, null, 3, true, "10 total x ~3 rounds"),
                Slot("OTF_S50_1007_TRX_BRIDGE_ROW", "TRX Bridge Row", 3, 10,
                    "Back-to-back with High Row, then rest. Three rounds are an editable estimate for phone-free logging; the source does not prescribe a fixed set count.", false, "Block 1", "Superset", 720, null, null, 3, true, "10 total x ~3 rounds"),
                Slot("OTF_S50_1007_GOBLET_SQUAT", "Goblet Squats", 3, 10,
                    "Back-to-back with TRX Reverse Lunges, then rest. Three rounds are an editable estimate for phone-free logging; the source does not prescribe a fixed set count.", false, "Block 1", "Superset", 720, null, null, 3, true, "10 total x ~3 rounds"),
                Slot("OTF_S50_1007_TRX_REVERSE_LUNGE", "TRX Reverse Lunges", 3, 10,
                    "Back-to-back with Goblet Squats, then rest. Three rounds are an editable estimate for phone-free logging; the source does not prescribe a fixed set count.", false, "Block 1", "Superset", 720, null, null, 3, true, "10 total x ~3 rounds"),
                Slot("OTF_S50_1007_CHEST_FLY", "Chest Fly", 3, 10,
                    "12-minute block; back-to-back with Side to Side Push-Ups, then rest. Three rounds are an editable estimate for phone-free logging; the source does not prescribe a fixed set count.", false, "Block 2", "Superset", 720, null, null, 3, true, "10 total x ~3 rounds"),
                Slot("OTF_S50_1007_SIDE_PUSHUP", "Side to Side Push-Ups", 3, 10,
                    "Back-to-back with Chest Fly, then rest. Three rounds are an editable estimate for phone-free logging; the source does not prescribe a fixed set count.", false, "Block 2", "Superset", 720, null, null, 3, true, "10 total x ~3 rounds"),
                Slot("OTF_S50_1007_SPLIT_SQUAT_HINGE", "Split Squat w/Hinge", 3, 10,
                    "10 each side; back-to-back with Alternating Single-Leg Airplane, then rest. Three rounds are an editable estimate for phone-free logging; the source does not prescribe a fixed set count.", false, "Block 2", "Superset", 720, null, null, 3, true, "10 each x ~3 rounds"),
                Slot("OTF_S50_1007_AIRPLANE", "Alternating Single-Leg Airplane", 3, 10,
                    "Back-to-back with Split Squat w/Hinge, then rest. Three rounds are an editable estimate for phone-free logging; the source does not prescribe a fixed set count.", false, "Block 2", "Superset", 720, null, null, 3, true, "10 total x ~3 rounds"),
                Slot("OTF_S50_1007_KNEE_TUCK", "Knee Tucks", 1, 1,
                    "Published timed buy-out: 30 seconds; reps are not specified.", false, "Block 2 Buy-Out", "Finisher", 30, null, null, null, false, "30 sec"),
                Slot("OTF_S50_1007_LATERAL_RAISE", "Lateral Raise", 3, 10,
                    "12-minute block; back-to-back with TRX Y-Raise, then rest. Three rounds are an editable estimate for phone-free logging; the source does not prescribe a fixed set count.", false, "Block 3", "Superset", 720, null, null, 3, true, "10 total x ~3 rounds"),
                Slot("OTF_S50_1007_TRX_Y", "TRX Y-Raise", 3, 10,
                    "Back-to-back with Lateral Raise, then rest. Three rounds are an editable estimate for phone-free logging; the source does not prescribe a fixed set count.", false, "Block 3", "Superset", 720, null, null, 3, true, "10 total x ~3 rounds"),
                Slot("OTF_S50_1007_CROSS_LUNGE", "Alt Cross Back Lunge", 3, 10,
                    "Back-to-back with Alt Step Out Squat Body Weight, then rest. Three rounds are an editable estimate for phone-free logging; the source does not prescribe a fixed set count.", false, "Block 3", "Superset", 720, null, null, 3, true, "10 total x ~3 rounds"),
                Slot("OTF_S50_1007_STEP_SQUAT", "Alt Step Out Squat Body Weight", 3, 10,
                    "Back-to-back with Alt Cross Back Lunge, then rest. Three rounds are an editable estimate for phone-free logging; the source does not prescribe a fixed set count.", false, "Block 3", "Superset", 720, null, null, 3, true, "10 total x ~3 rounds"),
                Slot("OTF_S50_1007_SITUP_1", "Sit-Up to Knee Tuck", 1, 1,
                    "First 30 seconds of the published 3-minute finisher; reps are not specified.", false, "Finisher", "Finisher", 30, null, null, null, false, "30 sec"),
                Slot("OTF_S50_1007_PLANK_L", "High Side Plank with Kickstand - Left", 1, 1,
                    "Left side; reps are not specified.", false, "Finisher", "Finisher", 60, null, null, null, false, "1 min left"),
                Slot("OTF_S50_1007_PLANK_R", "High Side Plank with Kickstand - Right", 1, 1,
                    "Right side; reps are not specified.", false, "Finisher", "Finisher", 60, null, null, null, false, "1 min right"),
                Slot("OTF_S50_1007_SITUP_2", "Sit-Up to Knee Tuck", 1, 1,
                    "Last 30 seconds of the published 3-minute finisher; reps are not specified.", false, "Finisher", "Finisher", 30, null, null, null, false, "30 sec")
            ],
            GroupName: "Orangetheory",
            PlannedMinutes: 50m);


    internal static SystemWorkoutRecipe Create20261016Recipe()
        => new SystemWorkoutRecipe(
            "OTF_2G_2026_10_16",
            "OTF 2G - 2026-10-16",
            "ORANGETHEORY",
            "Orangetheory 2G revenge template for October 16, 2026. Preserves the published hill-build and reverse-hill treadmill blocks, 3.5-minute tread finisher, timed floor circuits, 100 m all-out row repeats, and burpee finisher. Three rounds are estimates only for repeat-until-time floor work; the row circuit may reach 3-4 rounds.",
            [
                Slot("OTF_1016_T1_BASE_4", "Treadmill Base at 4%", 1, 1, "Maintain base pace; power walkers 6%+.", true, "Tread Block 1", "Tread", 120, null, 4m, null, false, "2 min base @ 4% (PW 6%+)"),
                Slot("OTF_1016_T1_BASE_REC_1", "Treadmill Base", 1, 1, "45 sec base between hill efforts.", true, "Tread Block 1", "Tread", 45, null, null, null, false, "45 sec base"),
                Slot("OTF_1016_T1_BASE_5", "Treadmill Base at 5%", 1, 1, "Maintain base pace; power walkers 7%+.", true, "Tread Block 1", "Tread", 90, null, 5m, null, false, "90 sec base @ 5% (PW 7%+)"),
                Slot("OTF_1016_T1_BASE_REC_2", "Treadmill Base", 1, 1, "45 sec base between hill efforts.", true, "Tread Block 1", "Tread", 45, null, null, null, false, "45 sec base"),
                Slot("OTF_1016_T1_BASE_6", "Treadmill Base at 6%", 1, 1, "Maintain base pace; power walkers 8%+.", true, "Tread Block 1", "Tread", 60, null, 6m, null, false, "1 min base @ 6% (PW 8%+)"),
                Slot("OTF_1016_T1_BASE_REC_3", "Treadmill Base", 1, 1, "45 sec base between hill efforts.", true, "Tread Block 1", "Tread", 45, null, null, null, false, "45 sec base"),
                Slot("OTF_1016_T1_BASE_7", "Treadmill Base at 7%", 1, 1, "Maintain base pace; power walkers 9%+.", true, "Tread Block 1", "Tread", 30, null, 7m, null, false, "30 sec base @ 7% (PW 9%+)"),
                Slot("OTF_1016_T1_BASE_REC_4", "Treadmill Base", 1, 1, "45 sec base before all-out.", true, "Tread Block 1", "Tread", 45, null, null, null, false, "45 sec base"),
                Slot("OTF_1016_T1_AO", "Treadmill All Out", 1, 1, "Power walkers 10%+.", true, "Tread Block 1", "Tread", 30, null, null, null, false, "30 sec AO (PW 10%+)"),
                Slot("OTF_1016_T1_WR", "Walking Recovery", 1, 1, "Published 90 sec walking recovery; get to base when ready.", true, "Tread Recovery 1", "Recovery", 90, null, null, null, false, "90 sec WR; base when ready"),
                Slot("OTF_1016_T2_BASE_7", "Treadmill Base at 7%", 1, 1, "Reverse hill sequence; maintain base pace; power walkers 9%+.", true, "Tread Block 2", "Tread", 30, null, 7m, null, false, "30 sec base @ 7% (PW 9%+)"),
                Slot("OTF_1016_T2_BASE_REC_1", "Treadmill Base", 1, 1, "45 sec base between hill efforts.", true, "Tread Block 2", "Tread", 45, null, null, null, false, "45 sec base"),
                Slot("OTF_1016_T2_BASE_6", "Treadmill Base at 6%", 1, 1, "Reverse hill sequence; power walkers 8%+.", true, "Tread Block 2", "Tread", 60, null, 6m, null, false, "1 min base @ 6% (PW 8%+)"),
                Slot("OTF_1016_T2_BASE_REC_2", "Treadmill Base", 1, 1, "45 sec base between hill efforts.", true, "Tread Block 2", "Tread", 45, null, null, null, false, "45 sec base"),
                Slot("OTF_1016_T2_BASE_5", "Treadmill Base at 5%", 1, 1, "Reverse hill sequence; power walkers 7%+.", true, "Tread Block 2", "Tread", 90, null, 5m, null, false, "90 sec base @ 5% (PW 7%+)"),
                Slot("OTF_1016_T2_BASE_REC_3", "Treadmill Base", 1, 1, "45 sec base between hill efforts.", true, "Tread Block 2", "Tread", 45, null, null, null, false, "45 sec base"),
                Slot("OTF_1016_T2_BASE_4", "Treadmill Base at 4%", 1, 1, "Reverse hill sequence; power walkers 6%+.", true, "Tread Block 2", "Tread", 120, null, 4m, null, false, "2 min base @ 4% (PW 6%+)"),
                Slot("OTF_1016_T2_BASE_REC_4", "Treadmill Base", 1, 1, "45 sec base before all-out.", true, "Tread Block 2", "Tread", 45, null, null, null, false, "45 sec base"),
                Slot("OTF_1016_T2_AO", "Treadmill All Out", 1, 1, "Power walkers 10%+.", true, "Tread Block 2", "Tread", 30, null, null, null, false, "30 sec AO (PW 10%+)"),
                Slot("OTF_1016_T2_WR", "Walking Recovery", 1, 1, "Published 90 sec walking recovery; get to base when ready.", true, "Tread Recovery 2", "Recovery", 90, null, null, null, false, "90 sec WR; base when ready"),
                Slot("OTF_1016_T3_PUSH", "Treadmill Push", 1, 1, "30 sec push; power walkers 8%+.", true, "Tread Block 3", "Tread", 30, null, null, null, false, "30 sec push (PW 8%+)"),
                Slot("OTF_1016_T3_PUSH_AO", "Treadmill Push to All Out", 1, 1, "30 sec push to all out; power walkers 8-10%.", true, "Tread Block 3", "Tread", 30, null, null, null, false, "30 sec push to AO (PW 8-10%)"),
                Slot("OTF_1016_T3_AO", "Treadmill All Out", 1, 1, "30 sec all out; power walkers 10%+.", true, "Tread Block 3", "Tread", 30, null, null, null, false, "30 sec AO (PW 10%+)"),
                Slot("OTF_1016_T3_WR", "Walking Recovery", 1, 1, "30 sec walking recovery.", true, "Tread Block 3", "Recovery", 30, null, null, null, false, "30 sec WR"),
                Slot("OTF_1016_T3_PUSH_AO_90", "Treadmill Push to All Out", 1, 1, "90 sec push to all out; coach may cue all out if able; power walkers 8-10%.", true, "Tread Block 3", "Tread", 90, null, null, null, false, "90 sec push to AO (PW 8-10%)"),
                Slot("OTF_1016_SITUP_STAND", "Dumbbell Bench Sit-Up to Stand", 3, 12, "Floor Block 1 - 12.25 minutes. Rest after each movement; repeat the sequence until time is called. Three rounds are an estimate for logging.", false, "Floor Block 1", "Circuit", 735, null, null, 3, true, "12 reps x ~3 rounds"),
                Slot("OTF_1016_ALT_ROW_DEADLIFT", "Alternating Low Row to Deadlift", 3, 12, "Floor Block 1 - 12.25 minutes. Rest after each movement; repeat the sequence until time is called. Three rounds are an estimate for logging.", false, "Floor Block 1", "Circuit", 735, null, null, 3, true, "12 reps x ~3 rounds"),
                Slot("OTF_1016_SPLIT_SQUAT_CURL", "Split Squat Hold with Bicep Curl", 3, 6, "6 each side; rear foot on bench. Floor Block 1 - 12.25 minutes; rest and repeat until time is called. Three rounds are an estimate for logging.", false, "Floor Block 1", "Circuit", 735, null, null, 3, true, "6/side x ~3 rounds"),
                Slot("OTF_1016_CLEAN_FRONT_SQUAT", "Dumbbell Single Arm Clean to Front Squat", 4, 6, "4-8 reps each side; repeat back-to-back with 100 m all-out row, then rest until time is called. 3-4 rounds were reported as typical; four rounds is an upper-end estimate.", false, "Rower Block 1", "Circuit", 330, null, null, 4, true, "4-8 each x ~3-4 rounds"),
                Slot("OTF_1016_ROW_100_AO", "Row", 4, 1, "100 m all-out row; check and remember average watts; rest. Repeat with clean-to-front-squat until time is called. 3-4 rounds were reported as typical.", true, "Rower Block 1", "Row", 330, 100m, null, 4, true, "100 m AO row x ~3-4 rounds"),
                Slot("OTF_1016_BURPEE", "Bodyweight Burpee", 1, 1, "Finisher construct: 30 sec bodyweight burpees, then 30 sec push-ups, then 30 sec high plank jack to plank pop; rest 30 sec; then 90 sec bodyweight full burpees with high plank. Finish with member-choice collapse.", false, "Floor Block 2", "Finisher", 210, null, null, null, false, "30 + 30 + 30 sec; rest 30 sec; 90 sec full burpees"),
                Slot("OTF_1016_PUSHUP", "Push-Up", 1, 1, "First 30 sec of back-to-back finisher construct.", false, "Floor Block 2", "Finisher", 30, null, null, null, false, "30 sec"),
                Slot("OTF_1016_PLANK_JACK_POP", "High Plank Jack to Plank Pop", 1, 1, "30 sec of high plank jack to plank pop in back-to-back finisher construct.", false, "Floor Block 2", "Finisher", 30, null, null, null, false, "30 sec"),
                Slot("OTF_1016_FULL_BURPEE", "Bodyweight Full Burpee with High Plank", 1, 1, "90 sec after 30 sec rest; member-choice collapse at end.", false, "Floor Block 2", "Finisher", 90, null, null, null, false, "90 sec")
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
