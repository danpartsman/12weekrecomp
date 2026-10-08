namespace WebRecomp.Web.Services.Coaching;

public sealed record CoachWorkoutGuide(
    string CoachCode,
    string CoachDisplayName,
    string WorkoutTypeCode,
    string Title,
    string AssetPath,
    string AvatarPath,
    string AltText,
    string NutritionGuideCode,
    string NutritionSummary,
    string PresentationVariant);

/// <summary>
/// Workout-specific educational content.
///
/// Engine 3.0 remains authoritative for client-specific calories, glycogen,
/// readiness, recovery, and macro prescriptions. These guides provide the
/// teaching/presentation layer around those outputs.
///
/// Current resolver behavior:
/// 1. Prefer the selected coach when that coach has content for the workout.
/// 2. Otherwise use the available workout specialist.
/// 3. Never invent a guide for an unsupported workout.
///
/// The old MERISOL code is accepted only as a backwards-compatibility alias.
/// MARISOL is the canonical spelling.
/// </summary>
public static class CoachWorkoutGuideCatalog
{
    public const string DefaultCoachCode = "MARISOL";

    private static readonly CoachWorkoutGuide[] Guides =
    [
        new(
            CoachCode: "MARISOL",
            CoachDisplayName: "Marisol",
            WorkoutTypeCode: "MAJOR_LEGS",
            Title: "Major Legs - Train Hard, Fuel Smart",
            AssetPath: "~/images/coach/marisol-leg-day-coach-detail.webp",
            AvatarPath: "~/images/coach/marisol-coach-avatar.webp",
            AltText:
                "Coach Marisol Major Legs guide covering effort, heavy leg training, glycogen use, and carbohydrate reload.",
            NutritionGuideCode: "HIGH_CARB_MAJOR_STRENGTH",
            NutritionSummary:
                "Protein stays protected. Use meaningful carbohydrate before and after training to support leg-day performance and glycogen restoration.",
            PresentationVariant: "left"),

        new(
            CoachCode: "MARISOL",
            CoachDisplayName: "Marisol",
            WorkoutTypeCode: "ORANGETHEORY",
            Title: "Orangetheory - Strength and Cardio",
            AssetPath: "~/images/coach/marisol-orangetheory-guide.webp",
            AvatarPath: "~/images/coach/marisol-coach-avatar.webp",
            AltText:
                "Coach Marisol Orangetheory guide covering treadmill, rower, floor work, perceived effort, fueling, and recovery.",
            NutritionGuideCode: "HIGH_CARB_MIXED_CONDITIONING",
            NutritionSummary:
                "Use carbohydrate around the session because mixed conditioning can create substantial glycogen demand. Keep protein adequate and rehydrate.",
            PresentationVariant: "right"),

        new(
            CoachCode: "MARISOL",
            CoachDisplayName: "Marisol",
            WorkoutTypeCode: "MAJOR_PULL",
            Title: "Major Pull - Back, Biceps, Rear Delts",
            AssetPath: "~/images/coach/marisol-major-pull-guide.webp",
            AvatarPath: "~/images/coach/marisol-coach-avatar.webp",
            AltText:
                "Coach Marisol Major Pull guide covering back, biceps, rear delts, perceived effort, progression, glycogen, and recovery.",
            NutritionGuideCode: "MODERATE_HIGH_CARB_MAJOR_STRENGTH",
            NutritionSummary:
                "Protect protein and use enough carbohydrate to support hard pulling volume. Reload after training according to the day's Engine 3.0 prescription.",
            PresentationVariant: "center"),

        new(
            CoachCode: "MARISOL",
            CoachDisplayName: "Marisol",
            WorkoutTypeCode: "MAJOR_PUSH",
            Title: "Major Push - Chest, Shoulders, Triceps",
            AssetPath: "~/images/coach/marisol-major-push-guide.webp",
            AvatarPath: "~/images/coach/marisol-coach-avatar.webp",
            AltText:
                "Coach Marisol Major Push guide covering pressing technique, perceived effort, glycogen use, and workout nutrition.",
            NutritionGuideCode: "MODERATE_HIGH_CARB_MAJOR_STRENGTH",
            NutritionSummary:
                "Use a protein-and-carbohydrate meal before hard pressing when practical. Refill with protein plus meaningful carbohydrate after training.",
            PresentationVariant: "right"),

        new(
            CoachCode: "MARISOL",
            CoachDisplayName: "Marisol",
            WorkoutTypeCode: "FULL_BODY_STRENGTH",
            Title: "Full-Body Strength - Big Lifts, Smart Fuel",
            AssetPath: "~/images/coach/marisol-full-body-strength-guide.webp",
            AvatarPath: "~/images/coach/marisol-coach-avatar.webp",
            AltText:
                "Coach Marisol Full-Body Strength guide covering compound lifts, perceived effort, glycogen use, and nutrition.",
            NutritionGuideCode: "HIGH_CARB_FULL_BODY_STRENGTH",
            NutritionSummary:
                "Full-body strength recruits more muscle. Use adequate carbohydrate plus protein before and after training, with hydration throughout.",
            PresentationVariant: "left"),

        new(
            CoachCode: "CASSIE",
            CoachDisplayName: "Cassie",
            WorkoutTypeCode: "ZONE2_CARDIO",
            Title: "Zone 2 Cardio - Build the Aerobic Base",
            AssetPath: "~/images/coach/cassie-zone2-cardio-v1015c.png",
            AvatarPath: "~/images/coach/cassie-avatar-v1015c.png",
            AltText:
                "Coach Cassie walking on a treadmill for Zone 2 cardio.",
            NutritionGuideCode: "LOW_MODERATE_CARB_CARDIO",
            NutritionSummary:
                "Zone 2 generally needs less carbohydrate than hard intervals. Use a light carb snack only when useful and eat a normal protein-centered meal afterward.",
            PresentationVariant: "left"),

        new(
            CoachCode: "CASSIE",
            CoachDisplayName: "Cassie",
            WorkoutTypeCode: "GENERAL_CARDIO",
            Title: "General Cardio - Consistency and Capacity",
            AssetPath: "~/images/coach/cassie-general-cardio-v1015c.png",
            AvatarPath: "~/images/coach/cassie-avatar-v1015c.png",
            AltText:
                "Coach Cassie beside a stationary exercise bike.",
            NutritionGuideCode: "LOW_MODERATE_CARB_CARDIO",
            NutritionSummary:
                "Keep fueling simple. A small carbohydrate snack is optional before training; normal protein and moderate carbohydrate afterward usually fit the day.",
            PresentationVariant: "right"),

        new(
            CoachCode: "CASSIE",
            CoachDisplayName: "Cassie",
            WorkoutTypeCode: "RUNNING_INTERVALS",
            Title: "Running Intervals - Work Hard, Recover on Purpose",
            AssetPath: "~/images/coach/cassie-running-intervals-v1015c.png",
            AvatarPath: "~/images/coach/cassie-avatar-v1015c.png",
            AltText:
                "Coach Cassie coaching running intervals beside a treadmill.",
            NutritionGuideCode: "HIGH_CARB_INTERVALS",
            NutritionSummary:
                "Intervals are carbohydrate-demanding. Use carbohydrates plus protein before when needed and restore glycogen with carbohydrate plus protein afterward.",
            PresentationVariant: "center"),

        new(
            CoachCode: "CASSIE",
            CoachDisplayName: "Cassie",
            WorkoutTypeCode: "TRUE_HIIT",
            Title: "True HIIT - Short, Intense, Controlled",
            AssetPath: "~/images/coach/cassie-true-hiit-v1015c.png",
            AvatarPath: "~/images/coach/cassie-avatar-v1015c.png",
            AltText:
                "Coach Cassie beside an air bike between high-intensity efforts.",
            NutritionGuideCode: "HIGH_CARB_HIIT",
            NutritionSummary:
                "True HIIT is short but glycolytic. Arrive fueled, hydrate, and use protein plus carbohydrate afterward to support recovery and glycogen restoration.",
            PresentationVariant: "right"),

        new(
            CoachCode: "CASSIE",
            CoachDisplayName: "Cassie",
            WorkoutTypeCode: "MIXED_CONDITIONING",
            Title: "Mixed Conditioning - Strong, Organized, Repeatable",
            AssetPath: "~/images/coach/cassie-mixed-conditioning-v1015c.png",
            AvatarPath: "~/images/coach/cassie-avatar-v1015c.png",
            AltText:
                "Coach Cassie holding a kettlebell in a functional training area.",
            NutritionGuideCode: "MODERATE_HIGH_CARB_MIXED_CONDITIONING",
            NutritionSummary:
                "Mixed conditioning combines strength and cardio demand. Use protein plus carbohydrate around meaningful sessions and replenish after training.",
            PresentationVariant: "left")
    ];

    public static CoachWorkoutGuide? Resolve(
        string? coachCode,
        string? workoutTypeCode)
    {
        if (string.IsNullOrWhiteSpace(workoutTypeCode))
            return null;

        var requestedCoach =
            NormalizeCoachCode(coachCode);

        var workout =
            workoutTypeCode.Trim().ToUpperInvariant();

        var preferred =
            Guides.FirstOrDefault(x =>
                x.CoachCode == requestedCoach
                && x.WorkoutTypeCode == workout);

        if (preferred is not null)
            return preferred;

        return Guides.FirstOrDefault(x =>
            x.WorkoutTypeCode == workout);
    }

    public static IReadOnlyList<CoachWorkoutGuide> ForWorkout(
        string? workoutTypeCode)
    {
        if (string.IsNullOrWhiteSpace(workoutTypeCode))
            return Array.Empty<CoachWorkoutGuide>();

        var workout =
            workoutTypeCode.Trim().ToUpperInvariant();

        return Guides
            .Where(x => x.WorkoutTypeCode == workout)
            .ToArray();
    }

    private static string NormalizeCoachCode(
        string? coachCode)
    {
        if (string.IsNullOrWhiteSpace(coachCode))
            return DefaultCoachCode;

        var normalized =
            coachCode.Trim().ToUpperInvariant();

        return normalized == "MERISOL"
            ? "MARISOL"
            : normalized;
    }
}
