namespace WebRecomp.Web.ViewModels;

/// <summary>A direct display projection of the saved Weekly Plan, not an Engine recommendation.</summary>
public sealed record SavedWeeklyWorkoutDay(
    DateOnly Date,
    string? Name,
    string? WorkoutTypeCode,
    decimal? DurationMinutes,
    decimal? Rpe);
