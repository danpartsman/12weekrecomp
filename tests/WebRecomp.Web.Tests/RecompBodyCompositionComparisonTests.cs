using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Dashboard3;

namespace WebRecomp.Web.Tests;

public sealed class RecompBodyCompositionComparisonTests
{
    private static readonly DateOnly Today = new(2026, 10, 11);
    private static Measurement Reading(int offset, decimal? weight = 200m, decimal? fat = .25m,
        decimal? muscle = 80m) => new()
    {
        MeasurementId = Guid.NewGuid(), LocalMeasurementDate = Today.AddDays(offset),
        MeasurementAtUtc = Today.AddDays(offset).ToDateTime(new TimeOnly(8, 0)),
        WeightLb = weight, BodyFatPct = fat, SkeletalMuscleMassLb = muscle
    };

    [Fact]
    public void FullWindowsUsePerReadingFatMassAndExactlySevenDayOffset()
    {
        var result = RecompBodyCompositionComparison.Build(Today,
            [Reading(-9), Reading(-8), Reading(-7), Reading(-2, 180, .3m, 81),
                Reading(-1, 200, .2m, 82), Reading(0, 220, .1m, 83), Reading(-6, 400), Reading(1, 400)]);
        Assert.Equal(3, result.AveragingDays);
        Assert.Equal(new DateOnly(2026, 10, 9), result.Fat.Latest.Start);
        Assert.Equal(new DateOnly(2026, 10, 2), result.Fat.Previous.Start);
        Assert.Equal(new DateOnly(2026, 10, 4), result.Fat.Previous.End);
        Assert.Equal(3, result.Fat.Latest.Count);
        Assert.Equal((54m + 40m + 22m) / 3m - 50m, result.Fat.ChangeLb);
        Assert.Equal(2m, result.Muscle.ChangeLb);
        Assert.Equal("green", result.Fat.Band); Assert.Equal("green", result.Muscle.Band);
    }

    [Theory]
    [InlineData(1, 1)] [InlineData(1, 2)] [InlineData(2, 1)] [InlineData(2, 2)] [InlineData(3, 1)]
    public void OneOrTwoAvailableReadingsAreAveragedWithoutZeroFill(int latest, int earlier)
    {
        var rows = Enumerable.Range(0, latest).Select(i => Reading(-i, 196, .25m, 81))
            .Concat(Enumerable.Range(0, earlier).Select(i => Reading(-7-i)));
        var result = RecompBodyCompositionComparison.Build(Today, rows);
        Assert.Equal(latest, result.Fat.Latest.Count); Assert.Equal(earlier, result.Muscle.Previous.Count);
        Assert.Equal(-1m, result.Fat.ChangeLb); Assert.Equal(1m, result.Muscle.ChangeLb);
    }

    [Fact]
    public void MissingMuscleDoesNotSuppressFatComparison()
    {
        var result = RecompBodyCompositionComparison.Build(Today, [Reading(-7), Reading(0, 196, muscle: null)]);
        Assert.Equal(-1m, result.Fat.ChangeLb); Assert.Null(result.Muscle.ChangeLb);
        Assert.Contains("latest three-day period", result.Muscle.MissingNotice);
        Assert.Equal("gray", result.Muscle.Band);
    }

    [Fact]
    public void MissingFatRequiresWeightAndNamesTheMissingPeriod()
    {
        var result = RecompBodyCompositionComparison.Build(Today, [Reading(-7, weight: null), Reading(0)]);
        Assert.Null(result.Fat.ChangeLb); Assert.Equal(0, result.Fat.Previous.Count);
        Assert.Contains("earlier three-day period", result.Fat.MissingNotice);
        Assert.Equal(0m, result.Muscle.ChangeLb);
    }

    [Fact]
    public void NoReadingsInEitherWindowAreExplicitRatherThanZeroChange()
    {
        var result = RecompBodyCompositionComparison.Build(Today, []);
        Assert.Null(result.Fat.ChangeLb); Assert.Null(result.Muscle.ChangeLb);
        Assert.Contains("either three-day period", result.Fat.MissingNotice);
        Assert.Contains("either three-day period", result.Muscle.MissingNotice);
    }

    [Theory]
    [InlineData("0", "yellow", "yellow")]
    [InlineData("0.1", "yellow", "yellow")]
    [InlineData("-0.2", "yellow", "yellow")]
    [InlineData("0.2", "yellow", "yellow")]
    [InlineData("0.21", "yellow", "yellow")]
    [InlineData("0.25", "red", "green")]
    [InlineData("-0.21", "yellow", "yellow")]
    [InlineData("-0.25", "green", "red")]
    public void ColorsInvertForMuscleAndToleranceIncludesBothBoundaries(string input, string fat, string muscle)
    {
        var change = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        var previous = new RecompCompositionWindow(Today.AddDays(-9), Today.AddDays(-7), 1, 50m);
        var latest = new RecompCompositionWindow(Today.AddDays(-2), Today, 1, 50m + change);
        Assert.Equal(fat, new RecompCompositionChange("fat", "Body fat", latest, previous, false).Band);
        Assert.Equal(muscle, new RecompCompositionChange("muscle", "Muscle mass", latest, previous, true).Band);
    }

    [Fact]
    public void DeletedAndSupersededReadingsDoNotAddWeightToAnAverage()
    {
        var older = Reading(0, 240); var latest = Reading(0, 196);
        latest.MeasurementAtUtc = latest.MeasurementAtUtc.AddHours(1);
        var deleted = Reading(-1, 400); deleted.IsDeleted = true;
        var result = RecompBodyCompositionComparison.Build(Today, [Reading(-7), older, latest, deleted]);
        Assert.Equal(1, result.Fat.Latest.Count); Assert.Equal(-1m, result.Fat.ChangeLb);
    }

    [Fact]
    public void SkeletalAndGenericMuscleAreNeverSubtractedFromEachOther()
    {
        var previous = Reading(-7, muscle: null); previous.MuscleMassLb = 140;
        var latest = Reading(0);
        var result = RecompBodyCompositionComparison.Build(Today, [previous, latest]);
        Assert.Null(result.Muscle.ChangeLb); Assert.Contains("earlier", result.Muscle.MissingNotice);
    }

    [Fact]
    public void ComparableLegacyMuscleCanStillBeUsed()
    {
        var previous = Reading(-7, muscle: null); previous.MuscleMassLb = 140;
        var latest = Reading(0); latest.MuscleMassLb = 141;
        var result = RecompBodyCompositionComparison.Build(Today, [previous, latest]);
        Assert.Equal("Muscle mass", result.Muscle.Label); Assert.Equal(1m, result.Muscle.ChangeLb);
        Assert.Equal("green", result.Muscle.Band);
    }

    [Fact]
    public void LinesShareASymmetricScaleWithLossLeftAndGainRight()
    {
        var result = RecompBodyCompositionComparison.Build(Today, [Reading(-7), Reading(0, 180, muscle: 81)]);
        Assert.Equal(5m, result.ScaleLb);
        Assert.True(result.Position(result.Fat.ChangeLb!.Value) < 50m);
        Assert.True(result.Position(result.Muscle.ChangeLb!.Value) > 50m);
        Assert.Equal(50m, result.Position(0m));
    }

    [Fact]
    public void ThirtyDayComparisonUsesOnlyItsTwoSevenDayWindows()
    {
        var readings = Enumerable.Range(0, 7).Select(i => Reading(-36 + i, 200 + 4 * i, muscle: 80 + i))
            .Concat(Enumerable.Range(0, 7).Select(i => Reading(-6 + i, 196 + 4 * i, muscle: 83 + i)))
            .Concat([Reading(-37, 400), Reading(-29, 400), Reading(-7, 400), Reading(1, 400)]);
        var result = RecompBodyCompositionComparison.Build(Today, readings, 30);
        Assert.Equal(30, result.ComparisonDays);
        Assert.Equal(7, result.AveragingDays);
        Assert.Equal(Today.AddDays(-36), result.Fat.Previous.Start);
        Assert.Equal(Today.AddDays(-30), result.Fat.Previous.End);
        Assert.Equal(Today.AddDays(-6), result.Fat.Latest.Start);
        Assert.Equal(Today, result.Fat.Latest.End);
        Assert.Equal(7, result.Fat.Previous.Count);
        Assert.Equal(7, result.Fat.Latest.Count);
        Assert.Equal(53m, result.Fat.Previous.AverageLb);
        Assert.Equal(52m, result.Fat.Latest.AverageLb);
        Assert.Equal(-1m, result.Fat.ChangeLb);
        Assert.Equal(3m, result.Muscle.ChangeLb);
    }

    [Theory]
    [InlineData(1, 1)] [InlineData(1, 2)] [InlineData(2, 1)] [InlineData(2, 2)]
    public void ThirtyDayComparisonAcceptsSparseWindows(int latest, int earlier)
    {
        var rows = Enumerable.Range(0, latest).Select(i => Reading(-i, 196, muscle: 81))
            .Concat(Enumerable.Range(0, earlier).Select(i => Reading(-30-i)));
        var result = RecompBodyCompositionComparison.Build(Today, rows, 30);
        Assert.Equal(latest, result.Muscle.Latest.Count);
        Assert.Equal(earlier, result.Fat.Previous.Count);
        Assert.Equal(-1m, result.Fat.ChangeLb);
        Assert.Equal(1m, result.Muscle.ChangeLb);
    }

    [Fact]
    public void MissingThirtyDayBaselineDoesNotSuppressSevenDayOrInventZero()
    {
        var result = RecompBodyCompositionTrends.Build(Today,
            [Reading(-7), Reading(0, 196, muscle: 81)]);
        Assert.Equal(-1m, result.SevenDay.Fat.ChangeLb);
        Assert.Equal(1m, result.SevenDay.Muscle.ChangeLb);
        Assert.Null(result.ThirtyDay.Fat.ChangeLb);
        Assert.Null(result.ThirtyDay.Muscle.ChangeLb);
        Assert.Contains("earlier seven-day period", result.ThirtyDay.Fat.MissingNotice);
        Assert.Equal(1, result.ThirtyDay.Fat.Latest.Count);
    }

    [Fact]
    public void BothHorizonsShareScaleWithoutMixingTheirBaselines()
    {
        var result = RecompBodyCompositionTrends.Build(Today,
            [Reading(-30, 220, muscle: 78), Reading(-7, 204, muscle: 80), Reading(0, 200, muscle: 81)]);
        Assert.Equal(-1m, result.SevenDay.Fat.ChangeLb);
        Assert.Equal(-5m, result.ThirtyDay.Fat.ChangeLb);
        Assert.Equal(1m, result.SevenDay.Muscle.ChangeLb);
        Assert.Equal(3m, result.ThirtyDay.Muscle.ChangeLb);
        Assert.Equal(5m, result.SevenDay.ScaleLb);
        Assert.Equal(result.SevenDay.ScaleLb, result.ThirtyDay.ScaleLb);
        Assert.Equal(result.SevenDay.Position(1m), result.ThirtyDay.Position(1m));
    }
}
