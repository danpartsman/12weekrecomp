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
        : DirectionPersists ? "Same direction on two weigh-in days" : "Direction awaiting a second weigh-in day";
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
        var daily = readings.Where(x => !x.IsDeleted && x.LocalMeasurementDate <= date)
            .GroupBy(x => x.LocalMeasurementDate)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.MeasurementAtUtc).ThenByDescending(x => x.CreatedAtUtc)
                .ThenByDescending(x => x.MeasurementId).First());
        var readingDays = daily.Keys.OrderDescending().ToArray();
        decimal? Fat(Measurement x) => x.WeightLb is > 0m && x.BodyFatPct is > 0m and < 1m
            ? x.WeightLb.Value * x.BodyFatPct.Value : null;
        bool Comparable(Func<Measurement, decimal?> value) =>
            daily.Values.Any(x => x.LocalMeasurementDate >= latestStart && value(x) is > 0m)
            && daily.Values.Any(x => x.LocalMeasurementDate >= previousStart && x.LocalMeasurementDate <= previousEnd && value(x) is > 0m);
        // Never subtract generic muscle mass from skeletal muscle mass. Use one
        // compatible field for both periods; new Fitdays/manual data is skeletal.
        var skeletal = Comparable(x => x.SkeletalMuscleMassLb)
            || (!Comparable(x => x.MuscleMassLb) && daily.Values.Any(x => x.SkeletalMuscleMassLb is > 0m
                && (x.LocalMeasurementDate >= latestStart
                    || (x.LocalMeasurementDate >= previousStart && x.LocalMeasurementDate <= previousEnd))));
        decimal? Muscle(Measurement x) => skeletal ? x.SkeletalMuscleMassLb : x.MuscleMassLb;
        decimal[] weights = comparisonDays == 7 ? [.20m, .18m, .16m, .14m, .12m, .11m, .09m]
            : [1m, 1m, 1m, 1m, 1m, 1m, 1m];
        RecompCompositionWindow Window(DateOnly from, DateOnly to, Func<Measurement, decimal?> value)
        {
            var count = 0;
            var sum = 0m;
            var totalWeight = 0m;
            for (var offset = 0; offset < averagingDays; offset++)
            {
                if (!daily.TryGetValue(to.AddDays(-offset), out var reading)
                    || value(reading) is not decimal amount || amount <= 0m) continue;
                count++;
                sum += amount * weights[offset];
                totalWeight += weights[offset];
            }
            return new(from, to, count, totalWeight == 0m ? null : sum / totalWeight);
        }
        RecompCompositionChange Compare(string key, string label, Func<Measurement, decimal?> value, bool gainIsFavorable)
        {
            var current = new RecompCompositionChange(key, label, Window(latestStart, date, value),
                Window(previousStart, previousEnd, value), gainIsFavorable);
            if (current.Sparse || current.DisplayChangeLb is not decimal change || change == 0m) return current;
            // Only nonzero comparisons on weigh-in days count toward a streak.
            var matches = 0;
            foreach (var day in readingDays)
            {
                if (value(daily[day]) is not > 0m) continue;
                var offset = day.DayNumber - date.DayNumber;
                var observed = day == date ? current : current with
                {
                    Latest = Window(latestStart.AddDays(offset), day, value),
                    Previous = Window(previousStart.AddDays(offset), previousEnd.AddDays(offset), value)
                };
                if (observed.Sparse || observed.DisplayChangeLb is not decimal direction || direction == 0m) continue;
                if (Math.Sign(direction) != Math.Sign(change)) break;
                if (++matches == 2) return current with { DirectionPersists = true };
            }
            return current;
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
