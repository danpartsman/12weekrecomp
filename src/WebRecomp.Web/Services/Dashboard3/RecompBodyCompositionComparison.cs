using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.Dashboard3;

public sealed record RecompCompositionWindow(DateOnly Start, DateOnly End, int Count, decimal? AverageLb)
{
    public int AveragingDays => End.DayNumber - Start.DayNumber + 1;
}

public sealed record RecompCompositionChange(string Key, string Label, RecompCompositionWindow Latest,
    RecompCompositionWindow Previous, bool GainIsFavorable)
{
    public decimal? ChangeLb => Latest.AverageLb.HasValue && Previous.AverageLb.HasValue
        ? Latest.AverageLb.Value - Previous.AverageLb.Value : null;
    public decimal? DisplayChangeLb => ChangeLb.HasValue
        ? decimal.Round(ChangeLb.Value, 1, MidpointRounding.AwayFromZero) : null;
    public bool DirectionPersists { get; init; }
    public bool Sparse => Latest.Count < 4 || Previous.Count < 4;
    public string Band => DisplayChangeLb is null or 0m || Sparse ? "gray"
        : !DirectionPersists ? "yellow"
        : (DisplayChangeLb > 0m) == GainIsFavorable ? "green" : "red";
    public string Status => DisplayChangeLb is not decimal change ? "Comparison unavailable"
        : Sparse ? "Sparse evidence"
        : change == 0m ? "No change"
        : DirectionPersists ? "Same direction on two consecutive days" : "Direction awaiting a second day";
    private string PeriodLabel => "seven-day";
    public string? MissingNotice => Latest.Count == 0 && Previous.Count == 0
        ? $"No {Label.ToLowerInvariant()} readings in either {PeriodLabel} period."
        : Latest.Count == 0 ? $"No {Label.ToLowerInvariant()} readings in the latest {PeriodLabel} period."
        : Previous.Count == 0 ? $"No {Label.ToLowerInvariant()} readings in the earlier {PeriodLabel} period." : null;
}

public sealed record RecompBodyCompositionComparison(RecompCompositionChange Fat, RecompCompositionChange Muscle,
    int ComparisonDays = 7)
{
    public int AveragingDays => Fat.Latest.AveragingDays;
    public RecompCompositionWindow? LatestWeight { get; init; }
    public RecompCompositionWindow? PreviousWeight { get; init; }
    public decimal? WeightChangeLb => LatestWeight?.AverageLb - PreviousWeight?.AverageLb;
    public decimal MinimumScaleLb { get; init; } = 1m;
    public decimal ScaleLb => Math.Max(MinimumScaleLb, Math.Ceiling(Math.Max(Math.Abs(Fat.ChangeLb ?? 0m),
        Math.Abs(Muscle.ChangeLb ?? 0m)) * 2m) / 2m);
    public decimal Position(decimal change) => 50m + change / ScaleLb * 45m;

    public static RecompBodyCompositionComparison Build(DateOnly date, IEnumerable<Measurement> readings,
        int comparisonDays = 7)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(comparisonDays, 3);
        var averagingDays = 7;
        // Only completed local days are compared.
        date = date.AddDays(-1);
        var latestStart = date.AddDays(1 - averagingDays);
        var previousEnd = date.AddDays(-comparisonDays);
        var previousStart = previousEnd.AddDays(1 - averagingDays);
        // One canonical reading per local date, like Daily Entry / Progress. Repeated
        // imports or extra weigh-ins must not give one day disproportionate weight.
        var daily = readings.Where(x => !x.IsDeleted && x.LocalMeasurementDate <= date
                && (x.LocalMeasurementDate >= latestStart.AddDays(-1)
                    || (x.LocalMeasurementDate >= previousStart.AddDays(-1) && x.LocalMeasurementDate <= previousEnd)))
            .GroupBy(x => x.LocalMeasurementDate)
            .Select(g => g.OrderByDescending(x => x.MeasurementAtUtc).ThenByDescending(x => x.CreatedAtUtc)
                .ThenByDescending(x => x.MeasurementId).First()).ToArray();
        decimal? Fat(Measurement x) => x.WeightLb is > 0m && x.BodyFatPct is > 0m and < 1m
            ? x.WeightLb.Value * x.BodyFatPct.Value : null;
        bool Comparable(Func<Measurement, decimal?> value) =>
            daily.Any(x => x.LocalMeasurementDate >= latestStart && value(x) is > 0m)
            && daily.Any(x => x.LocalMeasurementDate >= previousStart && x.LocalMeasurementDate <= previousEnd && value(x) is > 0m);
        // Never subtract generic muscle mass from skeletal muscle mass. Use one
        // compatible field for both periods; new Fitdays/manual data is skeletal.
        var skeletal = Comparable(x => x.SkeletalMuscleMassLb)
            || (!Comparable(x => x.MuscleMassLb) && daily.Any(x => x.SkeletalMuscleMassLb is > 0m));
        decimal? Muscle(Measurement x) => skeletal ? x.SkeletalMuscleMassLb : x.MuscleMassLb;
        RecompCompositionWindow Window(DateOnly from, DateOnly to, Func<Measurement, decimal?> value)
        {
            var values = daily.Where(x => x.LocalMeasurementDate >= from && x.LocalMeasurementDate <= to)
                .Select(x => (Value: value(x), Offset: to.DayNumber - x.LocalMeasurementDate.DayNumber))
                .Where(x => x.Value is > 0m).ToArray();
            decimal[] weights = [.20m, .18m, .16m, .14m, .12m, .11m, .09m];
            var sum = values.Sum(x => comparisonDays == 7 ? weights[x.Offset] : 1m);
            var mean = sum == 0m ? (decimal?)null : values.Sum(x => x.Value!.Value * (comparisonDays == 7 ? weights[x.Offset] : 1m)) / sum;
            return new(from, to, values.Length, mean);
        }
        RecompCompositionChange Compare(string key, string label, Func<Measurement, decimal?> value, bool gainIsFavorable)
        {
            var current = new RecompCompositionChange(key, label, Window(latestStart, date, value),
                Window(previousStart, previousEnd, value), gainIsFavorable);
            // A carried-forward average without fresh readings cannot confirm direction.
            var yesterday = date.AddDays(-1);
            if (current.Sparse || current.DisplayChangeLb is not decimal change || change == 0m
                || !daily.Any(x => x.LocalMeasurementDate == date && value(x) is > 0m)
                || !daily.Any(x => x.LocalMeasurementDate == yesterday && value(x) is > 0m))
                return current;
            var prior = current with
            {
                Latest = Window(latestStart.AddDays(-1), yesterday, value),
                Previous = Window(previousStart.AddDays(-1), previousEnd.AddDays(-1), value)
            };
            return current with { DirectionPersists = !prior.Sparse && prior.DisplayChangeLb is decimal priorChange
                && Math.Sign(change) == Math.Sign(priorChange) };
        }
        return new(Compare("fat", "Body fat", Fat, false),
            Compare("muscle", skeletal ? "Skeletal muscle mass" : "Muscle mass", Muscle, true), comparisonDays)
        { LatestWeight = Window(latestStart, date, x => x.WeightLb), PreviousWeight = Window(previousStart, previousEnd, x => x.WeightLb) };
    }
}

public sealed record RecompBodyCompositionTrends(RecompBodyCompositionComparison SevenDay,
    RecompBodyCompositionComparison ThirtyDay)
{
    public static RecompBodyCompositionTrends Build(DateOnly date, IEnumerable<Measurement> readings)
    {
        var rows = readings.ToArray();
        var sevenDay = RecompBodyCompositionComparison.Build(date, rows);
        var thirtyDay = RecompBodyCompositionComparison.Build(date, rows, 30);
        // Equal changes must have equal lengths when comparing the two horizons.
        var scale = Math.Max(sevenDay.ScaleLb, thirtyDay.ScaleLb);
        return new(sevenDay with { MinimumScaleLb = scale }, thirtyDay with { MinimumScaleLb = scale });
    }
}
