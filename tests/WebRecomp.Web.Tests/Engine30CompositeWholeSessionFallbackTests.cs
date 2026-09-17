using System.Reflection;
using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30CompositeWholeSessionFallbackTests
{
    [Fact]
    public void WholeSessionActualRpe_FallsBackIntoAllCompositeSegments()
    {
        var normalized =
            Normalize(
                new Engine30WorkoutSessionInput(
                    WorkoutTypeCode: "ORANGETHEORY",
                    PlannedDurationMinutes: 60m,
                    ActualRpe: 9m));

        Assert.All(
            normalized.Segments,
            segment => Assert.Equal(9m, segment.ActualRpe));
    }

    [Fact]
    public void WholeSessionPlannedRpeOverride_FallsBackIntoAllCompositeSegments()
    {
        var normalized =
            Normalize(
                new Engine30WorkoutSessionInput(
                    WorkoutTypeCode: "ORANGETHEORY",
                    PlannedDurationMinutes: 60m,
                    PlannedRpeOverride: 8m));

        Assert.All(
            normalized.Segments,
            segment => Assert.Equal(8m, segment.PlannedRpeOverride));
    }

    [Fact]
    public void SegmentActualRpe_TakesPriorityOverWholeSessionActualRpe()
    {
        var normalized =
            Normalize(
                new Engine30WorkoutSessionInput(
                    WorkoutTypeCode: "ORANGETHEORY",
                    PlannedDurationMinutes: 60m,
                    ActualRpe: 9m,
                    Segments:
                    [
                        new Engine30WorkoutSegmentInput(
                            SegmentCode: "TREAD",
                            ModalityCode: Engine30WorkoutModalityCodes.Treadmill,
                            PlannedDurationMinutes: 14m,
                            DefaultRpe: 7m,
                            ActualRpe: 8m)
                    ]));

        var tread =
            normalized.Segments.Single(
                x => x.SegmentCode.Equals(
                    "TREAD",
                    StringComparison.OrdinalIgnoreCase));

        Assert.Equal(8m, tread.ActualRpe);

        Assert.All(
            normalized.Segments.Where(
                x => !x.SegmentCode.Equals(
                    "TREAD",
                    StringComparison.OrdinalIgnoreCase)),
            segment => Assert.Equal(9m, segment.ActualRpe));
    }

    [Fact]
    public void WholeSessionActualDuration_ResolvesObservedCompositeSegmentDurations()
    {
        var normalized =
            Normalize(
                new Engine30WorkoutSessionInput(
                    WorkoutTypeCode: "ORANGETHEORY",
                    PlannedDurationMinutes: 60m,
                    ActualDurationMinutes: 60m));

        Assert.Equal(
            42m,
            normalized.PlannedDurationMinutes);

        Assert.Equal(
            new decimal?[] { 20m, 20m, 20m },
            normalized.Segments
                .Select(x => x.ActualDurationMinutes)
                .ToArray());
    }

    private static Engine30WorkoutSessionInput Normalize(
        Engine30WorkoutSessionInput source)
    {
        var method =
            typeof(Engine30WorkoutRuntimeService)
                .GetMethod(
                    "BuildRuntimeSessionInput",
                    BindingFlags.NonPublic
                    | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "BuildRuntimeSessionInput was not found.");

        return
            (Engine30WorkoutSessionInput)(
                method.Invoke(
                    null,
                    new object[]
                    {
                        source,
                        CompositeProfile()
                    })
                ?? throw new InvalidOperationException(
                    "BuildRuntimeSessionInput returned null."));
    }

    private static Engine30WorkoutAdminProfile CompositeProfile()
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

            Segments =
            [
                Segment(
                    10,
                    "TREAD",
                    Engine30WorkoutModalityCodes.Treadmill),

                Segment(
                    20,
                    "ROW",
                    Engine30WorkoutModalityCodes.Rowing),

                Segment(
                    30,
                    "FLOOR",
                    Engine30WorkoutModalityCodes.Resistance)
            ]
        };

    private static Engine30WorkoutAdminSegment Segment(
        int order,
        string code,
        string modality)
        => new()
        {
            Enabled = true,
            DisplayOrder = order,
            SegmentCode = code,
            DisplayName = code,
            ModalityCode = modality,
            TrainingPatternCode =
                modality == Engine30WorkoutModalityCodes.Resistance
                    ? Engine30TrainingPatternCodes.ResistanceCircuit
                    : Engine30TrainingPatternCodes.Steady,
            SessionStructureCode =
                Engine30WorkoutSessionCodes.StructureContinuous,
            DurationMinutes = 14m,
            DefaultRpe = 7m,
            GlycogenModelCode =
                modality == Engine30WorkoutModalityCodes.Resistance
                    ? "RESISTANCE"
                    : "CONTINUOUS"
        };
}