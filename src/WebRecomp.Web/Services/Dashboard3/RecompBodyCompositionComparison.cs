using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.Dashboard3;

public sealed record RecompCompositionWindow(DateOnly Start, DateOnly End, int Count, decimal? AverageLb);

public sealed record RecompCompositionChange(string Key, string Label, RecompCompositionWindow Latest,
    RecompCompositionWindow Previous, bool GainIsFavorable)
{
    public decimal? ChangeLb => Latest.AverageLb.HasValue && Previous.AverageLb.HasValue
        ? Latest.AverageLb.Value - Previous.AverageLb.Value : null;
    public decimal? DisplayChangeLb => ChangeLb.HasValue
        ? decimal.Round(ChangeLb.Value, 1, MidpointRounding.AwayFromZero) : null;
    // Product display tolerance, not a claim of measurement precision.
    public string Band => DisplayChangeLb is not decimal change ? "gray" : Math.Abs(change) <= .2m ? "yellow"
        : (change > 0m) == GainIsFavorable ? "green" : "red";
    public string Status => DisplayChangeLb is not decimal change ? "Comparison unavailable"
        : Math.Abs(change) <= .2m ? "Little change" : change > 0m ? "Gain" : "Loss";
    public string? MissingNotice => Latest.Count == 0 && Previous.Count == 0
        ? $"No {Label.ToLowerInvariant()} readings in either three-day period."
        : Latest.Count == 0 ? $"No {Label.ToLowerInvariant()} readings in the latest three-day period."
        : Previous.Count == 0 ? $"No {Label.ToLowerInvariant()} readings in the earlier three-day period." : null;
}

public sealed record RecompBodyCompositionComparison(RecompCompositionChange Fat, RecompCompositionChange Muscle)
{
    public decimal ScaleLb => Math.Max(1m, Math.Ceiling(Math.Max(Math.Abs(Fat.ChangeLb ?? 0m),
        Math.Abs(Muscle.ChangeLb ?? 0m)) * 2m) / 2m);
    public decimal Position(decimal change) => 50m + change / ScaleLb * 45m;

    public static RecompBodyCompositionComparison Build(DateOnly date, IEnumerable<Measurement> readings)
    {
        var latestStart = date.AddDays(-2);
        var previousEnd = date.AddDays(-7);
        var previousStart = date.AddDays(-9);
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
                .Select(value).Where(x => x is > 0m).Select(x => x!.Value).ToArray();
            return new(from, to, values.Length, values.Length == 0 ? null : values.Average());
        }
        return new(new("fat", "Body fat", Window(latestStart, date, Fat), Window(previousStart, previousEnd, Fat), false),
            new("muscle", skeletal ? "Skeletal muscle mass" : "Muscle mass",
                Window(latestStart, date, Muscle), Window(previousStart, previousEnd, Muscle), true));
    }
}
