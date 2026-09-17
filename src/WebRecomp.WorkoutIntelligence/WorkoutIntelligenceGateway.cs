namespace WebRecomp.WorkoutIntelligence;

/// <summary>
/// First implementation of the Workout Intelligence internal API.
///
/// This is deliberately independent from Engine 3.0. It converts detailed
/// exercise/set data into a normalized handoff object. Engine 3.0 integration
/// will be implemented through an adapter in a later phase.
/// </summary>
public sealed class WorkoutIntelligenceGateway : IWorkoutIntelligenceGateway
{
    public WorkoutIntelligenceHandoff Analyze(WorkoutIntelligenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var exercises = request.Exercises ?? Array.Empty<WorkoutExerciseInput>();
        var allSets = exercises.SelectMany(x => x.Sets ?? Array.Empty<WorkoutSetInput>()).ToArray();
        var hardSets = allSets.Where(IsHardSet).ToArray();

        var volumeLoad = hardSets
            .Where(x => x.Reps.HasValue && x.Load.HasValue)
            .Sum(x => x.Reps!.Value * x.Load!.Value);

        var rpeValues = hardSets
            .Where(x => x.Rpe.HasValue)
            .Select(x => x.Rpe!.Value)
            .ToArray();

        var rirValues = hardSets
            .Where(x => x.Rir.HasValue)
            .Select(x => x.Rir!.Value)
            .ToArray();

        var muscleDemand = BuildMuscleDemand(exercises);
        var safeMinutes = Math.Max(1, request.DurationMinutes);

        return new WorkoutIntelligenceHandoff(
            Schema: WorkoutIntelligenceContract.Schema,
            SessionKey: request.SessionKey,
            DurationMinutes: Math.Max(0, request.DurationMinutes),
            ExerciseCount: exercises.Count,
            TotalSets: allSets.Length,
            HardSets: hardSets.Length,
            VolumeLoad: decimal.Round(volumeLoad, 2),
            AverageRpe: AverageOrNull(rpeValues),
            AverageRir: AverageOrNull(rirValues),
            HardSetsPerMinute: decimal.Round((decimal)hardSets.Length / safeMinutes, 4),
            MuscleDemand: muscleDemand,
            SourceMode: "detailed-workout");
    }

    private static bool IsHardSet(WorkoutSetInput set)
    {
        if (set.IsWarmup)
        {
            return false;
        }

        return (set.Reps ?? 0) > 0 || (set.DurationSeconds ?? 0) > 0;
    }

    private static decimal? AverageOrNull(decimal[] values)
    {
        return values.Length == 0
            ? null
            : decimal.Round(values.Average(), 2);
    }

    private static IReadOnlyList<MuscleDemandHandoff> BuildMuscleDemand(
        IReadOnlyList<WorkoutExerciseInput> exercises)
    {
        var demand = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var exercise in exercises)
        {
            var hardSetCount = (exercise.Sets ?? Array.Empty<WorkoutSetInput>()).Count(IsHardSet);
            if (hardSetCount == 0)
            {
                continue;
            }

            foreach (var target in exercise.MuscleTargets ?? Array.Empty<MuscleTargetInput>())
            {
                if (string.IsNullOrWhiteSpace(target.MuscleSlug))
                {
                    continue;
                }

                var weightedSets = hardSetCount * RoleWeight(target.Role);
                demand[target.MuscleSlug] = demand.GetValueOrDefault(target.MuscleSlug) + weightedSets;
            }
        }

        return demand
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new MuscleDemandHandoff(x.Key, decimal.Round(x.Value, 2)))
            .ToArray();
    }

    private static decimal RoleWeight(string? role)
    {
        return role?.Trim().ToLowerInvariant() switch
        {
            "primary" => 1.00m,
            "secondary" => 0.50m,
            "tertiary" => 0.25m,
            _ => 0.25m
        };
    }
}
