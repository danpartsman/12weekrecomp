using System.Reflection;
using System.Runtime.CompilerServices;

namespace WebRecomp.Web.Services.WorkoutIntelligence;

/// <summary>
/// Registers the current dated Orangetheory template with the existing System
/// Workouts catalog. The source template is also retained as
/// wwwroot/workout-templates/otf/2026-09-27-2g.csv.
///
/// SystemWorkoutRecipeCatalog currently exposes a fixed presentation array, so
/// the dated OTF recipe replaces the generic Orangetheory presentation card
/// while retaining the generic ORANGETHEORY engine physiology. The dated recipe
/// is also added to the catalog lookup so selecting the card resolves the same
/// recipe that was displayed.
/// </summary>
internal static class OrangetheoryDailyRecipeRegistration
{
    private const string DatedSystemCode = "SYS_OTF_2G_2026_09_27";

    [ModuleInitializer]
    internal static void Register()
    {
        var recipe = new SystemWorkoutRecipe(
            DatedSystemCode,
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

        // PresentationRecipes is backed by the catalog's fixed array. Replace
        // only the generic OTF card; all other System Workout cards are untouched.
        if (SystemWorkoutRecipeCatalog.PresentationRecipes is SystemWorkoutRecipe[] presentation)
        {
            var genericIndex = Array.FindIndex(
                presentation,
                candidate => candidate.SystemCode.Equals(
                    "ORANGETHEORY",
                    StringComparison.OrdinalIgnoreCase));

            if (genericIndex >= 0)
                presentation[genericIndex] = recipe;
        }

        // TryGet() resolves selections through the catalog's private dictionary.
        // Add the dated key without changing or removing the generic engine entry.
        var byCodeField = typeof(SystemWorkoutRecipeCatalog).GetField(
            "ByCode",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (byCodeField?.GetValue(null) is IDictionary<string, SystemWorkoutRecipe> byCode)
            byCode[DatedSystemCode] = recipe;
    }

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
