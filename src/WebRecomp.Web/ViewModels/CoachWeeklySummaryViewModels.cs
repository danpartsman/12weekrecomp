using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
namespace WebRecomp.Web.ViewModels;

public sealed record CoachSummaryWorkout(WorkoutSession Session, IReadOnlyList<DetailedWorkoutExerciseRecord> Exercises);
public sealed record CoachSummaryPrescription(DateOnly Date, decimal ProteinG, decimal CarbsG, decimal FatG,
    decimal Calories, string? WorkoutName, decimal? Minutes, decimal? Rpe, string Source);
public sealed record CoachSummaryDay(DateOnly Date, Measurement? Scale, int ScaleCount,
    DailyCheckin? Checkin, NutritionDay? Nutrition, string? PlannedWorkout,
    IReadOnlyList<CoachSummaryWorkout> Workouts)
{
    public CoachSummaryPrescription? Prescription { get; init; }
}
public sealed record CoachSummaryGauge(string Label, string Unit, decimal? Actual, decimal? Target, int LoggedDays, int TargetDays)
{
    public decimal? Percent => Actual.HasValue && Target is > 0m ? Actual.Value / Target.Value * 100m : null;
    public decimal Fill => Math.Clamp(Percent ?? 0m, 0m, 100m);
    public bool Complete => LoggedDays == 7 && TargetDays == 7;
}
public sealed record CoachSummaryBodyChange(string Label, decimal? Earlier, decimal? Recent, int EarlierCount, int RecentCount)
{
    public decimal? Change => Earlier.HasValue && Recent.HasValue ? Recent.Value - Earlier.Value : null;
}
public sealed class CoachWeeklySummaryViewModel
{
    public required Guid ClientId { get; init; }
    public required string ClientName { get; init; }
    public required DateOnly Today { get; init; }
    // ReferenceDate defaults to the client's local today. Its preceding seven days are complete.
    public required DateOnly ReferenceDate { get; init; }
    public DateOnly WeekStart => ReferenceDate.AddDays(-7);
    public DateOnly WeekEnd => ReferenceDate.AddDays(-1);
    public required IReadOnlyList<CoachSummaryDay> Days { get; init; }
    public string? PrescriptionNotice { get; init; }
    public IEnumerable<CoachSummaryDay> CompletedDays => Days.Where(x => x.Date >= WeekStart && x.Date <= WeekEnd);
    public int NutritionDays => CompletedDays.Count(x => x.Nutrition is { } n &&
        (n.ActualProteinG.HasValue || n.ActualCarbsG.HasValue || n.ActualFatG.HasValue || n.ActualCalories.HasValue));
    public int CompletedWorkouts => CompletedDays.Sum(x => x.Workouts.Count);
    public decimal TrainingMinutes => CompletedDays.Sum(x => x.Workouts.Sum(w => w.Session.DurationMin));
    public IReadOnlyList<CoachSummaryGauge> Gauges =>
    [
        Gauge("Calories", "kcal", x => x.ActualCalories, x => x.Calories),
        Gauge("Protein", "g", x => x.ActualProteinG, x => x.ProteinG),
        Gauge("Carbs", "g", x => x.ActualCarbsG, x => x.CarbsG),
        Gauge("Fat", "g", x => x.ActualFatG, x => x.FatG)
    ];
    private CoachSummaryGauge Gauge(string label, string unit, Func<NutritionDay, decimal?> actual, Func<CoachSummaryPrescription, decimal> target)
    {
        var days = CompletedDays.ToArray();
        var values = days.Select(x => x.Nutrition is null ? null : actual(x.Nutrition)).Where(x => x.HasValue).ToArray();
        var targets = days.Where(x => x.Prescription is not null).Select(x => target(x.Prescription!)).ToArray();
        return new(label, unit, values.Length == 0 ? null : values.Sum(x => x!.Value),
            targets.Length == 7 ? targets.Sum() : null, values.Length, targets.Length);
    }
    private CoachSummaryBodyChange BodyChange(string label, Func<Measurement, decimal?> value)
    {
        var early = CompletedDays.Where(x => x.Date <= WeekStart.AddDays(2)).Select(x => x.Scale is null ? null : value(x.Scale))
            .Where(x => x is > 0m).Select(x => x!.Value).ToArray();
        var late = CompletedDays.Where(x => x.Date >= WeekEnd.AddDays(-2)).Select(x => x.Scale is null ? null : value(x.Scale))
            .Where(x => x is > 0m).Select(x => x!.Value).ToArray();
        return new(label, early.Length == 0 ? null : early.Average(), late.Length == 0 ? null : late.Average(), early.Length, late.Length);
    }
    public CoachSummaryBodyChange FatChange => BodyChange("Fat mass", x => x.WeightLb is > 0m
        ? x.WeightLb * Engine30DashboardProjectionService.NormalizeBodyFatFraction(x.BodyFatPct) : null);
    public CoachSummaryBodyChange MuscleChange
    {
        get
        {
            var skeletal = BodyChange("Skeletal muscle", x => x.SkeletalMuscleMassLb);
            var total = BodyChange("Muscle mass", x => x.MuscleMassLb);
            return skeletal.Change.HasValue || !total.Change.HasValue ? skeletal : total;
        }
    }
    public decimal BodyScale => Math.Max(1m, Math.Ceiling(Math.Max(Math.Abs(FatChange.Change ?? 0m), Math.Abs(MuscleChange.Change ?? 0m)) * 2m) / 2m);
}
