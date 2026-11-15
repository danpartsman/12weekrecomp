using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.Dashboard3;

public sealed record RecompCompositionWindow(DateOnly Start, DateOnly End, int Count, decimal? AverageLb)
{
    public decimal? DailyVariationLb { get; init; }
    public int AveragingDays => End.DayNumber - Start.DayNumber + 1;
}

public sealed record RecompCompositionChange(string Key, string Label, RecompCompositionWindow Latest,
    RecompCompositionWindow Previous, bool GainIsFavorable)
{
    public decimal? ChangeLb => Latest.AverageLb.HasValue && Previous.AverageLb.HasValue
        ? Latest.AverageLb.Value - Previous.AverageLb.Value : null;
    public decimal? DisplayChangeLb => ChangeLb.HasValue
        ? decimal.Round(ChangeLb.Value, 1, MidpointRounding.AwayFromZero) : null;
    // Direction is descriptive. Without device-specific validation, no change is
    // presented as confirmed tissue gain/loss or a measured 95% confidence result.
    public bool Sparse => Latest.Count < 4 || Previous.Count < 4;
    public decimal ObservedVariationLb => Math.Max(Latest.DailyVariationLb ?? 0m, Previous.DailyVariationLb ?? 0m);
    public string Band => ChangeLb is null || Sparse ? "gray" : "yellow";
    public string Status => DisplayChangeLb is not decimal change ? "Comparison unavailable"
        : Sparse ? "Sparse evidence"
        : Math.Abs(change) <= Math.Max(.2m, ObservedVariationLb) ? "Within observed variation"
        : change > 0m ? "Estimated increase · unconfirmed" : "Estimated decrease · unconfirmed";
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
    public string Interpretation => Fat.Sparse || Muscle.Sparse
        ? "Sparse body-composition evidence. Missing days are excluded; they are not failed weigh-ins."
        : "These are scale estimates, not confirmed tissue changes. Stable weight cannot distinguish a fat/lean exchange from fluid-related estimation changes. Compare waist and strength trends over several weeks. Water, fat and muscle estimates from one scale are not independent confirmations.";
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
                && (x.LocalMeasurementDate >= latestStart
                    || (x.LocalMeasurementDate >= previousStart && x.LocalMeasurementDate <= previousEnd)))
            .GroupBy(x => x.LocalMeasurementDate)
            .Select(g => g.OrderByDescending(x => x.MeasurementAtUtc).ThenByDescending(x => x.CreatedAtUtc)
                .ThenByDescending(x => x.MeasurementId).First()).ToArray();
        decimal? Fat(Measurement x) => x.WeightLb is > 0m && x.BodyFatPct is > 0m and < 1m
            ? x.WeightLb.Value * x.BodyFatPct.Value : null;
        bool Comparable(Func<Measurement, decimal?> value) =>
            daily.Any(x => x.LocalMeasurementDate >= latestStart && value(x) is > 0m)
            && daily.Any(x => x.LocalMeasurementDate <= previousEnd && value(x) is > 0m);
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
            var variation = values.Length < 2 ? (decimal?)null : (decimal)Math.Sqrt((double)(values.Sum(x => (x.Value!.Value - mean!.Value) * (x.Value.Value - mean.Value)) / (values.Length - 1)));
            return new(from, to, values.Length, mean) { DailyVariationLb = variation };
        }
        return new(new("fat", "Body fat", Window(latestStart, date, Fat), Window(previousStart, previousEnd, Fat), false),
            new("muscle", skeletal ? "Skeletal muscle mass" : "Muscle mass",
                Window(latestStart, date, Muscle), Window(previousStart, previousEnd, Muscle), true), comparisonDays)
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
