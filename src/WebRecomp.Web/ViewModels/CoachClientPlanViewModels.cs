using System.ComponentModel.DataAnnotations;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.Dashboard3;
namespace WebRecomp.Web.ViewModels;
public sealed record CoachPlanExercise(Guid ExerciseId, string Name, int Sets, string Reps, decimal? WeightLb);
public sealed record CoachActualWorkout(WorkoutSession Session, IReadOnlyList<DetailedWorkoutExerciseRecord> Exercises);
public sealed record CoachPlanDay(DateOnly Date, Engine30DashboardDayProjection Engine, Engine30DashboardDayProjection Assigned,
    NutritionDay? Intake, PlannedWorkout? Plan, string Name, IReadOnlyList<CoachPlanExercise> Exercises,
    IReadOnlyList<CoachActualWorkout> Actual, bool CanEdit);
public sealed class CoachClientPlan
{
    public Engine30ClientHabitualActivityProfile? Activity { get; init; }
    public DateOnly Today { get; init; }
    public DateOnly WeekStart { get; init; }
    public IReadOnlyList<CoachPlanDay> Days { get; init; } = [];
    public RecompCockpit? Progress { get; init; }
    public IReadOnlyList<CoachWorkoutPlanRevision> History { get; init; } = [];
    public IReadOnlyDictionary<Guid,string> Authors { get; init; } = new Dictionary<Guid,string>();
    public string? Notice { get; init; }
    public string RecommendedWorkout { get; init; } = "";
    public string RecommendationReason { get; init; } = "";
}
public sealed class CoachWorkoutExerciseInput
{
    public Guid ExerciseId { get; set; }
    public string Name { get; set; } = "";
    [Range(1,20)] public int Sets { get; set; } = 3;
    [Required,StringLength(150)] public string Reps { get; set; } = "10";
    [Range(0,2000)] public decimal? WeightLb { get; set; }
}
public sealed class CoachWorkoutInput
{
    public DateOnly Date { get; set; }
    [Required,StringLength(100)] public string Choice { get; set; } = "";
    [Required,StringLength(150)] public string Name { get; set; } = "";
    [Range(0,360)] public decimal Minutes { get; set; }
    [Range(1,10)] public decimal Rpe { get; set; } = 5;
    public bool Customize { get; set; }
    public List<CoachWorkoutExerciseInput> Exercises { get; set; } = [];
    [Required,StringLength(1000)] public string Reason { get; set; } = "";
    public string? PreviewToken { get; set; }
}
public sealed record CoachWorkoutOption(string Value, string Name);
public sealed class CoachWorkoutPage
{
    public Guid ClientId { get; init; }
    public string ClientName { get; init; } = "";
    public CoachWorkoutInput Input { get; init; } = new();
    public IReadOnlyList<CoachWorkoutOption> Options { get; init; } = [];
    public IReadOnlyList<CoachWorkoutOption> Catalog { get; init; } = [];
    public bool CanCustomize { get; init; }
    public string SourceName { get; init; } = "";
    public IReadOnlyDictionary<DateOnly,Engine30DashboardDayProjection>? Before { get; set; }
    public IReadOnlyDictionary<DateOnly,Engine30DashboardDayProjection>? After { get; set; }
    public string? PreviewToken { get; set; }
    public string? Notice { get; set; }
}
