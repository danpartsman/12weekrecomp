using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.Dashboard3;

public sealed record RecompWeeklyTrendPoint(
    DateOnly Date, DateOnly WeekStart, DateOnly WeekEnd,
    decimal? FatChangeLb, decimal? MuscleChangeLb, decimal? WeightLb, string MuscleLabel,
    string Status, string Band, int FatLatestReadings, int FatEarlierReadings,
    int MuscleLatestReadings, int MuscleEarlierReadings,
    int NutritionDays, decimal? Calories, decimal? Protein, decimal? Carbs, decimal? Fat,
    int PrescriptionDays, decimal? TargetCalories, decimal? TargetProtein, decimal? TargetCarbs, decimal? TargetFat,
    int WorkoutDays, int PlannedWorkoutDays, decimal WorkoutMinutes, decimal? WorkoutRpe,
    int? TreatItems, int TreatKnownDays);

public sealed record RecompWeeklyTrend(IReadOnlyList<RecompWeeklyTrendPoint> Points)
{
    private static (string Status, string Band) Outcome(RecompBodyCompositionComparison composition)
    {
        var (fat, muscle, _) = composition;
        if (fat.Sparse || muscle.Sparse) return ("Partial evidence", "gray");
        if (fat.DisplayChangeLb * muscle.DisplayChangeLb > 0m) return ("Mixed", "yellow");
        if (fat.Band == "yellow" || muscle.Band == "yellow") return ("New direction", "yellow");
        if (fat.Band == "green" || muscle.Band == "green") return ("Favorable", "green");
        if (fat.Band == "red" || muscle.Band == "red") return ("Needs review", "red");
        return ("Stable", "gray");
    }

    public static RecompWeeklyTrend Build(DateOnly date, DateOnly programStart, IEnumerable<Measurement> readings,
        IEnumerable<NutritionDay> nutrition, IEnumerable<WorkoutSession> workouts, IEnumerable<PlannedWorkout> plans,
        IReadOnlyDictionary<DateOnly, RecompDayEvidence> prescriptions,
        IReadOnlyDictionary<DateOnly, int?> treats)
    {
        var first = date.AddDays(-41);
        if (first < programStart.AddDays(7)) first = programStart.AddDays(7);
        var measurements = readings.ToArray(); var foods = nutrition.ToArray();
        var sessions = workouts.Where(x => x.CompletedFlag && x.DurationMin > 0 && x.WorkoutTypeCode != "REST").ToArray();
        var schedule = plans.Where(x => x.WorkoutTypeCode != "REST").ToArray();
        var points = new List<RecompWeeklyTrendPoint>();
        for (var end = first; end <= date; end = end.AddDays(1))
        {
            // Morning scale closes the preceding seven completed behavior days.
            var start = end.AddDays(-7); var last = end.AddDays(-1);
            var composition = RecompBodyCompositionComparison.Build(end, measurements);
            var known = foods.Where(x => x.NutritionDate >= start && x.NutritionDate <= last
                && x.ActualCalories.HasValue && x.ActualProteinG.HasValue && x.ActualCarbsG.HasValue && x.ActualFatG.HasValue).ToArray();
            var targets = prescriptions.Where(x => x.Key >= start && x.Key <= last).Select(x => x.Value).ToArray();
            var completed = sessions.Where(x => x.WorkoutDate >= start && x.WorkoutDate <= last).ToArray();
            var scheduled = schedule.Where(x => x.PlannedDate >= start && x.PlannedDate <= last).ToArray();
            var effort = completed.Where(x => x.SessionRpe.HasValue).ToArray();
            var flex = treats.Where(x => x.Key >= start && x.Key <= last && x.Value.HasValue).ToArray();
            var (status, band) = Outcome(composition);
            points.Add(new(end, start, last, composition.Fat.DisplayChangeLb, composition.Muscle.DisplayChangeLb, WeightTrend(end, measurements),
                composition.Muscle.Label, status, band,
                composition.Fat.Latest.Count, composition.Fat.Previous.Count, composition.Muscle.Latest.Count, composition.Muscle.Previous.Count,
                known.Length, Mean(known, x => x.ActualCalories!.Value), Mean(known, x => x.ActualProteinG!.Value),
                Mean(known, x => x.ActualCarbsG!.Value), Mean(known, x => x.ActualFatG!.Value),
                targets.Length, Mean(targets, x => x.EnergyTarget), Mean(targets, x => x.ProteinTarget),
                Mean(targets, x => x.CarbsTarget), Mean(targets, x => x.FatTarget),
                completed.Select(x => x.WorkoutDate).Distinct().Count(), scheduled.Select(x => x.PlannedDate).Distinct().Count(),
                completed.Sum(x => x.DurationMin), effort.Length == 0 ? null : effort.Sum(x => x.DurationMin * x.SessionRpe!.Value) / effort.Sum(x => x.DurationMin),
                flex.Length == 0 ? null : flex.Sum(x => x.Value!.Value), flex.Length));
        }
        return new(points);
    }
    private static decimal? WeightTrend(DateOnly date, Measurement[] readings)
    {
        // Seven-day average of recorded scale weight, shown in pounds.
        var start = date.AddDays(-7);
        var daily = readings.Where(x => !x.IsDeleted && x.WeightLb is > 0m
                && x.LocalMeasurementDate >= start && x.LocalMeasurementDate < date)
            .GroupBy(x => x.LocalMeasurementDate)
            .Select(g => g.OrderByDescending(x => x.MeasurementAtUtc).ThenByDescending(x => x.CreatedAtUtc)
                .ThenByDescending(x => x.MeasurementId).First())
            .Select(x => x.WeightLb!.Value).ToArray();
        return daily.Length == 0 ? null : daily.Average();
    }

    private static decimal? Mean<T>(T[] rows, Func<T, decimal> value) => rows.Length == 0 ? null : rows.Average(value);
}
