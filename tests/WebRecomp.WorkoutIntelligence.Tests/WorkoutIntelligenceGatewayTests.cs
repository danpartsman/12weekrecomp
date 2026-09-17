using WebRecomp.WorkoutIntelligence;

namespace WebRecomp.WorkoutIntelligence.Tests;

public sealed class WorkoutIntelligenceGatewayTests
{
    [Fact]
    public void Analyze_ReturnsVersionedNormalizedHandoff()
    {
        var gateway = new WorkoutIntelligenceGateway();

        var request = new WorkoutIntelligenceRequest(
            SessionKey: "session-1",
            DurationMinutes: 60,
            Exercises:
            [
                new WorkoutExerciseInput(
                    ExerciseId: Guid.NewGuid(),
                    ExerciseName: "Hack Squat",
                    Mechanics: "compound",
                    MuscleTargets:
                    [
                        new("quadriceps", "primary"),
                        new("glutes", "secondary")
                    ],
                    Sets:
                    [
                        new(10, 100m, 5m, 5m, 90, null, true),
                        new(10, 200m, 8m, 2m, 120, null),
                        new(8, 220m, 9m, 1m, 150, null)
                    ])
            ]);

        var result = gateway.Analyze(request);

        Assert.Equal("workout-intelligence/v1", result.Schema);
        Assert.Equal(3, result.TotalSets);
        Assert.Equal(2, result.HardSets);
        Assert.Equal(3760m, result.VolumeLoad);
        Assert.Equal(8.5m, result.AverageRpe);
        Assert.Equal(1.5m, result.AverageRir);
        Assert.Contains(result.MuscleDemand, x => x.MuscleSlug == "quadriceps" && x.WeightedSetEquivalents == 2m);
        Assert.Contains(result.MuscleDemand, x => x.MuscleSlug == "glutes" && x.WeightedSetEquivalents == 1m);
    }

    [Fact]
    public void Analyze_DoesNotCountWarmupAsHardSet()
    {
        var gateway = new WorkoutIntelligenceGateway();

        var result = gateway.Analyze(new WorkoutIntelligenceRequest(
            "session-2",
            30,
            [
                new WorkoutExerciseInput(
                    Guid.NewGuid(),
                    "Dumbbell Curl",
                    "isolation",
                    [new("biceps", "primary")],
                    [
                        new(15, 10m, null, null, null, null, true),
                        new(12, 20m, 8m, 2m, 90, null, false)
                    ])
            ]));

        Assert.Equal(2, result.TotalSets);
        Assert.Equal(1, result.HardSets);
        Assert.Equal(240m, result.VolumeLoad);
    }
}
