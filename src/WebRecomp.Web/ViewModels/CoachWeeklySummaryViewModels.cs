using WebRecomp.Web.Models;
namespace WebRecomp.Web.ViewModels;

public sealed record CoachSummaryWorkout(WorkoutSession Session, IReadOnlyList<DetailedWorkoutExerciseRecord> Exercises);
public sealed record CoachSummaryDay(DateOnly Date, Measurement? Scale, int ScaleCount,
    DailyCheckin? Checkin, NutritionDay? Nutrition, string? PlannedWorkout,
    IReadOnlyList<CoachSummaryWorkout> Workouts);
public sealed class CoachWeeklySummaryViewModel
{
    public required Guid ClientId { get; init; }
    public required string ClientName { get; init; }
    public required DateOnly Today { get; init; }
    public required DateOnly WeekStart { get; init; }
    public required IReadOnlyList<CoachSummaryDay> Days { get; init; }
    public int NutritionDays => Days.Count(x => x.Nutrition is { } n &&
        (n.ActualProteinG.HasValue || n.ActualCarbsG.HasValue || n.ActualFatG.HasValue || n.ActualCalories.HasValue));
    public int CompletedWorkouts => Days.Sum(x => x.Workouts.Count);
    public decimal TrainingMinutes => Days.Sum(x => x.Workouts.Sum(w => w.Session.DurationMin));
}
