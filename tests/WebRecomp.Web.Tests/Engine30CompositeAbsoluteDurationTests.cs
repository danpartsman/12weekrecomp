using System.Reflection;
using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30CompositeAbsoluteDurationTests
{
    [Fact]
    public void AdminCompositeMinutes_AreAbsolute_NotStretchedToPlannedSessionTotal()
    {
        var normalized =
            Normalize(
                CompositeProfile(14m),
                plannedMinutes: 60m);

        Assert.Equal(
            42m,
            normalized.PlannedDurationMinutes);

        Assert.Equal(
            new[] { 14m, 14m, 14m },
            normalized.Segments
                .Select(x => x.PlannedDurationMinutes)
                .ToArray());
    }

    [Fact]
    public void ShorterAdminCompositeMinutes_LowerModeledEnergy()
    {
        var sixty =
            Normalize(
                CompositeProfile(20m),
                plannedMinutes: 60m);

        var fortyTwo =
            Normalize(
                CompositeProfile(14m),
                plannedMinutes: 60m);

        var calculator =
            new Engine30WorkoutEnergyCalculator();

        var sixtyNet =
            sixty.Segments.Sum(
                x => NetEnergy(
                    calculator,
                    x.PlannedDurationMinutes));

        var fortyTwoNet =
            fortyTwo.Segments.Sum(
                x => NetEnergy(
                    calculator,
                    x.PlannedDurationMinutes));

        Assert.Equal(
            60m,
            sixty.PlannedDurationMinutes);

        Assert.Equal(
            42m,
            fortyTwo.PlannedDurationMinutes);

        Assert.True(
            fortyTwoNet < sixtyNet,
            $"Expected 42-minute composite energy ({fortyTwoNet}) to be below 60-minute energy ({sixtyNet}).");
    }

    [Fact]
    public void ActualWholeSessionDuration_RemainsObservedTruth()
    {
        var normalized =
            Normalize(
                CompositeProfile(14m),
                plannedMinutes: 60m,
                actualMinutes: 60m);

        Assert.Equal(
            42m,
            normalized.PlannedDurationMinutes);

        Assert.Equal(
            new[] { 14m, 14m, 14m },
            normalized.Segments
                .Select(x => x.PlannedDurationMinutes)
                .ToArray());

        Assert.Equal(
            new decimal?[] { 20m, 20m, 20m },
            normalized.Segments
                .Select(x => x.ActualDurationMinutes)
                .ToArray());
    }

    private static decimal NetEnergy(
        Engine30WorkoutEnergyCalculator calculator,
        decimal durationMinutes)
        => calculator.Calculate(
                new Engine30WorkoutEnergyInput(
                    WorkoutTypeCode: "ORANGETHEORY",
                    BaseLivingEnergyKcal: 1900m,
                    WeightLb: 200m,
                    DurationMinutes: durationMinutes,
                    ReferenceWorkoutCaloriesPerMinute: 6m,
                    ReferenceWeightLb: 180m,
                    SessionRpe: 7m))
            .NetWorkoutEnergyKcal;

    private static Engine30WorkoutSessionInput Normalize(
        Engine30WorkoutAdminProfile profile,
        decimal plannedMinutes,
        decimal? actualMinutes = null)
    {
        var method =
            typeof(Engine30WorkoutRuntimeService)
                .GetMethod(
                    "BuildRuntimeSessionInput",
                    BindingFlags.NonPublic
                    | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "BuildRuntimeSessionInput was not found.");

        var source =
            new Engine30WorkoutSessionInput(
                WorkoutTypeCode: "ORANGETHEORY",
                PlannedDurationMinutes: plannedMinutes,
                ActualDurationMinutes: actualMinutes);

        return
            (Engine30WorkoutSessionInput)(
                method.Invoke(
                    null,
                    new object[] { source, profile })
                ?? throw new InvalidOperationException(
                    "BuildRuntimeSessionInput returned null."));
    }

    private static Engine30WorkoutAdminProfile CompositeProfile(
        decimal segmentMinutes)
        => new()
        {
            WorkoutFamilyCode =
                Engine30WorkoutFamilyCodes.Composite,

            DefaultModalityCode =
                Engine30WorkoutModalityCodes.Mixed,

            TrainingPatternCode =
                Engine30TrainingPatternCodes.Composite,

            SessionStructureCode =
                Engine30WorkoutSessionCodes.StructureComposite,

            DefaultDurationMinutes = 60m,
            DefaultRpe = 7m,

            IntervalWorkSeconds = 60m,
            IntervalRecoverySeconds = 60m,
            IntervalWorkRpe = 8m,
            IntervalRecoveryRpe = 3m,

            Segments =
            [
                Segment(
                    10,
                    "TREAD",
                    Engine30WorkoutModalityCodes.Treadmill,
                    "CONTINUOUS",
                    segmentMinutes),

                Segment(
                    20,
                    "ROW",
                    Engine30WorkoutModalityCodes.Rowing,
                    "INTERVAL",
                    segmentMinutes),

                Segment(
                    30,
                    "FLOOR",
                    Engine30WorkoutModalityCodes.Resistance,
                    "RESISTANCE",
                    segmentMinutes)
            ]
        };

    private static Engine30WorkoutAdminSegment Segment(
        int order,
        string code,
        string modality,
        string glycogenModel,
        decimal duration)
        => new()
        {
            Enabled = true,
            DisplayOrder = order,
            SegmentCode = code,
            DisplayName = code,
            ModalityCode = modality,
            TrainingPatternCode =
                glycogenModel == "INTERVAL"
                    ? Engine30TrainingPatternCodes.Intervals
                    : glycogenModel == "RESISTANCE"
                        ? Engine30TrainingPatternCodes.ResistanceCircuit
                        : Engine30TrainingPatternCodes.Steady,
            SessionStructureCode =
                glycogenModel == "INTERVAL"
                    ? Engine30WorkoutSessionCodes.StructureIntervals
                    : Engine30WorkoutSessionCodes.StructureContinuous,
            DurationMinutes = duration,
            DefaultRpe = 7m,
            GlycogenModelCode = glycogenModel
        };
}