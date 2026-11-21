using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Dashboard3;
namespace WebRecomp.Web.Tests;
public sealed class RecompBodyCompositionComparisonTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static Measurement R(int offset, decimal weight = 200, decimal? fat = .25m, decimal? muscle = 80) => new()
    { MeasurementId = Guid.NewGuid(), LocalMeasurementDate = Today.AddDays(offset), MeasurementAtUtc = Today.AddDays(offset).ToDateTime(new TimeOnly(8,0)), WeightLb = weight, BodyFatPct = fat, SkeletalMuscleMassLb = muscle };
    [Fact] public void WeeklyWindowsAreNonoverlappingAndExcludeToday()
    {
        var rows = Enumerable.Range(1,14).Select(i => R(-i)).Append(R(0,400));
        var c = RecompBodyCompositionComparison.Build(Today,rows);
        Assert.Equal(Today.AddDays(-7),c.Fat.Latest.Start); Assert.Equal(Today.AddDays(-1),c.Fat.Latest.End);
        Assert.Equal(Today.AddDays(-14),c.Fat.Previous.Start); Assert.Equal(Today.AddDays(-8),c.Fat.Previous.End);
        Assert.Equal(7,c.Fat.Latest.Count); Assert.Equal(0,c.Fat.ChangeLb);
    }
    [Fact] public void SameWeightsAreAppliedToBothWeeks()
    {
        var rows = Enumerable.Range(1,14).Select(i => R(-i, i == 1 || i == 8 ? 220 : 200));
        var c = RecompBodyCompositionComparison.Build(Today,rows);
        Assert.Equal(51,c.Fat.Latest.AverageLb); Assert.Equal(51,c.Fat.Previous.AverageLb);
    }
    [Fact] public void MissingValuesRenormalizeWeightsPerMetricWithoutZeroFill()
    {
        var c = RecompBodyCompositionComparison.Build(Today,[R(-1,200,muscle:null),R(-2,220,muscle:90),R(-8)]);
        Assert.Equal((50m*.2m+55m*.18m)/.38m,c.Fat.Latest.AverageLb);
        Assert.Equal(90,c.Muscle.Latest.AverageLb); Assert.Equal(1,c.Muscle.Latest.Count);
        Assert.True(c.Muscle.Sparse); Assert.Equal("gray",c.Muscle.Band);
    }
    [Fact] public void MonthlyWindowsUseStraightAveragesThirtyDaysApart()
    {
        var rows = Enumerable.Range(1,7).Select(i=>R(-i, i==1?228:200))
            .Concat(Enumerable.Range(31,7).Select(i=>R(-i))).Append(R(-30,400)).Append(R(0,400));
        var c=RecompBodyCompositionComparison.Build(Today,rows,30);
        Assert.Equal(Today.AddDays(-37),c.Fat.Previous.Start); Assert.Equal(Today.AddDays(-31),c.Fat.Previous.End);
        Assert.Equal(51,c.Fat.Latest.AverageLb); Assert.Equal(1,c.Fat.ChangeLb);
    }
    [Fact] public void WeightOnlyVacationDaysDoNotCreateCompositionReadings()
    {
        var c=RecompBodyCompositionComparison.Build(Today,[R(-1),R(-8),R(-9,fat:null,muscle:null)]);
        Assert.Equal(1,c.Fat.Previous.Count); Assert.Equal(2,c.PreviousWeight!.Count);
        Assert.Equal("Sparse evidence",c.Fat.Status);
    }
    [Fact] public void StableWeightDoesNotEraseAnEstimatedFatChange()
    {
        var rows=Enumerable.Range(1,14).Select(i=>R(-i,200,i<=7?.27m:.25m,i<=7?78:80));
        var c=RecompBodyCompositionComparison.Build(Today,rows);
        Assert.Equal(0,c.WeightChangeLb); Assert.Equal(4,c.Fat.ChangeLb); Assert.Equal(-2,c.Muscle.ChangeLb);
        Assert.Equal("red",c.Fat.Band); Assert.True(c.Fat.DirectionPersists);
    }
    [Theory]
    [InlineData(7, true)]
    [InlineData(7, false)]
    [InlineData(30, true)]
    [InlineData(30, false)]
    public void DirectionUsesYellowOnTheFirstDayAndGreenOrRedOnTheSecond(int days, bool favorable)
    {
        var baseline = Enumerable.Range(3, days + 6).Select(i => R(-i)).ToArray();
        var first = R(-2, fat: favorable ? .246m : .254m, muscle: favorable ? 80.8m : 79.2m);
        var second = R(-1, fat: first.BodyFatPct, muscle: first.SkeletalMuscleMassLb);
        var firstDay = RecompBodyCompositionComparison.Build(Today.AddDays(-1), baseline.Append(first), days);
        Assert.Equal("yellow", firstDay.Fat.Band);
        Assert.Equal("yellow", firstDay.Muscle.Band);
        var secondDay = RecompBodyCompositionComparison.Build(Today, baseline.Append(first).Append(second), days);
        Assert.Equal(favorable ? "green" : "red", secondDay.Fat.Band);
        Assert.Equal(favorable ? "green" : "red", secondDay.Muscle.Band);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    public void ReversalResetsOnlyTheMetricThatChangedDirection(int days)
    {
        var baseline = Enumerable.Range(4, days + 5).Select(i => R(-i));
        var rows = baseline.Concat([R(-3, fat: .26m, muscle: 81), R(-2, fat: .26m, muscle: null),
            R(-1, fat: .20m, muscle: 81)]);
        var chart = RecompBodyCompositionComparison.Build(Today, rows, days);
        Assert.True(chart.Fat.DisplayChangeLb < 0);
        Assert.True(chart.Muscle.DisplayChangeLb > 0);
        Assert.Equal("yellow", chart.Fat.Band);
        Assert.Equal("green", chart.Muscle.Band);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    public void ZeroAndMissingDaysPauseTheStreak(int days)
    {
        var baseline = Enumerable.Range(5, days + 10).Select(i => R(-i));
        var rows = baseline.Concat([R(-4, fat: .246m), R(-3, fat: days == 7 ? .2536m : .254m),
            R(-1, fat: .24m)]).ToArray();
        var first = RecompBodyCompositionComparison.Build(Today.AddDays(-3), rows, days).Fat;
        Assert.Equal("yellow", first.Band);
        var zero = RecompBodyCompositionComparison.Build(Today.AddDays(-2), rows, days).Fat;
        Assert.Equal(0m, zero.DisplayChangeLb);
        Assert.Equal("gray", zero.Band);
        var resumed = RecompBodyCompositionComparison.Build(Today, rows, days).Fat;
        Assert.Equal("green", resumed.Band);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    public void MissingDayCarriesColorWithoutAddingAWeighIn(int days)
    {
        var baseline = Enumerable.Range(4, days + 8).Select(i => R(-i));
        var first = baseline.Append(R(-3, fat: .24m));
        Assert.Equal("yellow", RecompBodyCompositionComparison.Build(Today, first, days).Fat.Band);
        var confirmed = first.Append(R(-2, fat: .24m));
        Assert.Equal("green", RecompBodyCompositionComparison.Build(Today, confirmed, days).Fat.Band);
    }
    [Fact] public void DeletedAndSupersededReadingsDoNotGetExtraWeight()
    {
        var old=R(-1,400); var latest=R(-1); latest.MeasurementAtUtc=old.MeasurementAtUtc.AddHours(1);
        var deleted=R(-2,400); deleted.IsDeleted=true;
        var c=RecompBodyCompositionComparison.Build(Today,[old,latest,deleted,R(-8)]);
        Assert.Equal(1,c.Fat.Latest.Count); Assert.Equal(0,c.Fat.ChangeLb);
    }
    [Fact] public void MuscleTypesAreNeverMixed()
    {
        var old=R(-8,muscle:null); old.MuscleMassLb=140;
        var c=RecompBodyCompositionComparison.Build(Today,[old,R(-1)]);
        Assert.Null(c.Muscle.ChangeLb); Assert.NotNull(c.Muscle.MissingNotice);
    }
    [Fact] public void EmptyPeriodsStayUnknownAndShareASymmetricScale()
    {
        var c=RecompBodyCompositionTrends.Build(Today,[]);
        Assert.Null(c.SevenDay.Fat.ChangeLb); Assert.Equal("gray",c.SevenDay.Fat.Band);
        Assert.Equal(c.SevenDay.ScaleLb,c.ThirtyDay.ScaleLb); Assert.Equal(50,c.SevenDay.Position(0));
    }
}
