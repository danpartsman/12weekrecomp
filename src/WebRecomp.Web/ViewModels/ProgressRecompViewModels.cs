namespace WebRecomp.Web.ViewModels;

public sealed class RecompProgressViewModel
{
    public string ClientName { get; set; } = string.Empty;
    public string MuscleMetricLabel { get; set; } = "Skeletal Muscle";
    public bool UsingSkeletalMuscle { get; set; }

    public decimal? CurrentFatMassLb { get; set; }
    public decimal? FatMassChangeLb { get; set; }

    public decimal? CurrentMuscleLb { get; set; }
    public decimal? MuscleChangeLb { get; set; }

    public decimal? CurrentWeightLb { get; set; }
    public decimal? WeightChangeLb { get; set; }

    public decimal? CurrentBodyFatPercent { get; set; }
    public decimal? BodyFatPercentChange { get; set; }

    public decimal? CurrentSubcutaneousFatPercent { get; set; }
    public decimal? SubcutaneousFatPercentChange { get; set; }

    public decimal? CurrentVisceralFatRating { get; set; }
    public decimal? VisceralFatChange { get; set; }

    public decimal? CurrentBodyWaterLb { get; set; }
    public decimal? CurrentWaistIn { get; set; }

    public IReadOnlyList<RecompProgressPoint> Points { get; set; } =
        Array.Empty<RecompProgressPoint>();
}

public sealed record RecompProgressPoint(
    DateOnly Date,
    decimal? WeightLb,
    decimal? BodyFatPercent,
    decimal? FatMassLb,
    decimal? MuscleLb,
    decimal? SubcutaneousFatPercent,
    decimal? VisceralFatRating,
    decimal? BodyWaterLb,
    decimal? WaistIn);
