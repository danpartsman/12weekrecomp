using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

public sealed class Engine30SequentialCompositeGlycogenCalculatorTests
{
    private readonly Engine30SequentialCompositeGlycogenCalculator _calculator = new();
    private readonly ITestOutputHelper _output;

    public Engine30SequentialCompositeGlycogenCalculatorTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void OverlappingSegments_DepleteFromRemainingPool_NotFreshPool()
    {
        /*
            DRAFT composite anchors only.

            TREAD and ROW both use the same LOWER_BODY region.

            The second segment must start from the glycogen remaining after
            the first segment rather than receiving a fresh regional pool.
        */

        var result =
            Calculate(
                Segment(
                    "TREAD",
                    lowerRecruitment:
                        0.70m,
                    lowerDepletion:
                        0.12m),

                Segment(
                    "ROW",
                    lowerRecruitment:
                        0.80m,
                    lowerDepletion:
                        0.20m));

        Assert.Equal(
            2,
            result.Exposures.Count);

        var tread =
            result.Exposures[0];

        var row =
            result.Exposures[1];

        Assert.Equal(
            "TREAD",
            tread.SegmentCode);

        Assert.Equal(
            "ROW",
            row.SegmentCode);

        Assert.Equal(
            tread.RegionRemainingAfterG,
            row.RegionRemainingBeforeG);

        /*
            Naive double counting would apply both exposures to the original
            fresh regional glycogen pool.

            Sequential modeling must produce less total depletion because
            ROW acts on what remains after TREAD.
        */
        var freshPoolNaiveTotal =
            tread.RegionBaselineGlycogenG
            * tread.RecruitmentFraction
            * tread.DepletionFraction
            +
            row.RegionBaselineGlycogenG
            * row.RecruitmentFraction
            * row.DepletionFraction;

        Assert.True(
            result.EstimatedGlycogenDepletedG
            < freshPoolNaiveTotal);

        Assert.Equal(
            result.EstimatedGlycogenDepletedG,
            result.CarbohydrateReloadRequirementG);

        Assert.InRange(
            Math.Abs(
                result.CarbohydrateReloadEnergyKcal
                - result.CarbohydrateReloadRequirementG * 4m),
            0m,
            0.03m);

        _output.WriteLine("");
        _output.WriteLine(
            "ENGINE 3.0 - SEQUENTIAL COMPOSITE GLYCOGEN");

        _output.WriteLine(
            "DRAFT values; validates overlapping-pool carry-forward only.");

        _output.WriteLine("");
        _output.WriteLine(
            new string('-', 132));

        _output.WriteLine(
            $"{"Segment",-12} " +
            $"{"Region",-14} " +
            $"{"BeforeG",10} " +
            $"{"RecruitFx",10} " +
            $"{"DepleteFx",10} " +
            $"{"SegmentG",10} " +
            $"{"AfterG",10}");

        _output.WriteLine(
            new string('-', 132));

        foreach (var exposure in result.Exposures)
        {
            _output.WriteLine(
                $"{exposure.SegmentCode,-12} " +
                $"{exposure.MuscleGroupCode,-14} " +
                $"{exposure.RegionRemainingBeforeG,10:0.00} " +
                $"{exposure.RecruitmentFraction,10:0.000} " +
                $"{exposure.DepletionFraction,10:0.0000} " +
                $"{exposure.SegmentGlycogenDepletedG,10:0.00} " +
                $"{exposure.RegionRemainingAfterG,10:0.00}");
        }

        _output.WriteLine(
            new string('-', 132));

        _output.WriteLine(
            $"Sequential total depleted = {result.EstimatedGlycogenDepletedG:0.00} g");

        _output.WriteLine(
            $"Naive fresh-pool total     = {freshPoolNaiveTotal:0.00} g");
    }

    [Fact]
    public void ReverseOrder_ChangesSegmentAttribution_ButNotFinalTotal_UnderCurrentProportionalModel()
    {
        /*
            Important architectural fact:

            Under the CURRENT proportional-within-region model, each exposure
            multiplies the remaining region by (1 - recruitment*depletion).

            Multiplication is commutative. Therefore reversing the same set of
            exposures changes which segment is credited with how many depleted
            grams, but it does NOT change the final regional total.

            A future sub-region / fiber-pool model could make order affect the
            final total. The current model does not.
        */

        var treadThenRow =
            Calculate(
                Segment(
                    "TREAD",
                    0.70m,
                    0.12m),

                Segment(
                    "ROW",
                    0.80m,
                    0.20m));

        var rowThenTread =
            Calculate(
                Segment(
                    "ROW",
                    0.80m,
                    0.20m),

                Segment(
                    "TREAD",
                    0.70m,
                    0.12m));

        Assert.Equal(
            treadThenRow.EstimatedGlycogenDepletedG,
            rowThenTread.EstimatedGlycogenDepletedG);

        Assert.NotEqual(
            treadThenRow.Exposures[0].SegmentGlycogenDepletedG,
            rowThenTread.Exposures[1].SegmentGlycogenDepletedG);

        Assert.NotEqual(
            treadThenRow.Exposures[1].SegmentGlycogenDepletedG,
            rowThenTread.Exposures[0].SegmentGlycogenDepletedG);
    }

    [Fact]
    public void NonOverlappingRegions_AccumulateIndependently()
    {
        var result =
            _calculator.Calculate(
                BaseInput(
                    new[]
                    {
                        new Engine30CompositeGlycogenSegmentInput(
                            SegmentCode:
                                "LOWER",

                            MuscleGroups:
                                new[]
                                {
                                    new Engine30MuscleGlycogenInput(
                                        MuscleGroupCode:
                                            "LOWER_BODY",

                                        WholeBodySkeletalMuscleFraction:
                                            0.50m,

                                        RecruitmentFraction:
                                            0.70m,

                                        DepletionFraction:
                                            0.12m)
                                }),

                        new Engine30CompositeGlycogenSegmentInput(
                            SegmentCode:
                                "UPPER",

                            MuscleGroups:
                                new[]
                                {
                                    new Engine30MuscleGlycogenInput(
                                        MuscleGroupCode:
                                            "UPPER_BODY",

                                        WholeBodySkeletalMuscleFraction:
                                            0.20m,

                                        RecruitmentFraction:
                                            0.60m,

                                        DepletionFraction:
                                            0.18m)
                                })
                    }));

        Assert.Equal(
            2,
            result.Regions.Count);

        /*
            Exposure rows are individually rounded to 0.01 g for diagnostics,
            while the calculator totals unrounded internal state and rounds
            only the final result. A one-centigram-scale summation difference
            is therefore expected and is not a physiological/model error.
        */
        var roundedExposureTotal =
            result.Exposures.Sum(
                x => x.SegmentGlycogenDepletedG);

        Assert.InRange(
            Math.Abs(
                roundedExposureTotal
                - result.EstimatedGlycogenDepletedG),
            0m,
            0.02m);
    }

    [Fact]
    public void SameRegion_WithInconsistentAnatomicalFraction_IsRejected()
    {
        var input =
            BaseInput(
                new[]
                {
                    new Engine30CompositeGlycogenSegmentInput(
                        SegmentCode:
                            "A",

                        MuscleGroups:
                            new[]
                            {
                                new Engine30MuscleGlycogenInput(
                                    MuscleGroupCode:
                                        "LOWER_BODY",

                                    WholeBodySkeletalMuscleFraction:
                                        0.50m,

                                    RecruitmentFraction:
                                        0.70m,

                                    DepletionFraction:
                                        0.12m)
                            }),

                    new Engine30CompositeGlycogenSegmentInput(
                        SegmentCode:
                            "B",

                        MuscleGroups:
                            new[]
                            {
                                new Engine30MuscleGlycogenInput(
                                    MuscleGroupCode:
                                        "LOWER_BODY",

                                    WholeBodySkeletalMuscleFraction:
                                        0.55m,

                                    RecruitmentFraction:
                                        0.80m,

                                    DepletionFraction:
                                        0.20m)
                            })
                });

        Assert.Throws<ArgumentException>(
            () =>
                _calculator.Calculate(
                    input));
    }

    private Engine30SequentialCompositeGlycogenResult Calculate(
        params Engine30CompositeGlycogenSegmentInput[] segments)
        => _calculator.Calculate(
            BaseInput(
                segments));

    private static Engine30SequentialCompositeGlycogenInput BaseInput(
        IReadOnlyCollection<Engine30CompositeGlycogenSegmentInput> segments)
        => new(
            FatFreeMassLb:
                144m,

            SkeletalMuscleFractionOfFfm:
                0.528m,

            BaselineGlycogenGPerKgMuscle:
                23m,

            TargetRestorationFraction:
                1.00m,

            CarbohydratePerGlycogenG:
                1.00m,

            Segments:
                segments);

    private static Engine30CompositeGlycogenSegmentInput Segment(
        string code,
        decimal lowerRecruitment,
        decimal lowerDepletion)
        => new(
            SegmentCode:
                code,

            MuscleGroups:
                new[]
                {
                    new Engine30MuscleGlycogenInput(
                        MuscleGroupCode:
                            "LOWER_BODY",

                        WholeBodySkeletalMuscleFraction:
                            0.50m,

                        RecruitmentFraction:
                            lowerRecruitment,

                        DepletionFraction:
                            lowerDepletion)
                });
}