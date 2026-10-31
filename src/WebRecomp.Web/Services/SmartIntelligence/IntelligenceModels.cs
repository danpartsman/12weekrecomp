namespace WebRecomp.Web.Services.SmartIntelligence;

public sealed record IntelligenceDay
{
    public DateOnly Date { get; init; }
    public bool CompletedDay { get; init; }
    public double? Weight { get; init; }
    public double? Fat { get; init; }
    public double? Muscle { get; init; }
    public double? Water { get; init; }
    public double? Waist { get; init; }
    public double? SubcutaneousFat { get; init; }
    public double? VisceralFat { get; init; }
    public double? Calories { get; init; }
    public double? Protein { get; init; }
    public double? Carbs { get; init; }
    public double? DietaryFat { get; init; }
    public bool PossiblePartialFoodDay { get; init; }
    public double? TargetCalories { get; init; }
    public double? TargetProtein { get; init; }
    public double? TargetCarbs { get; init; }
    public double? TargetFat { get; init; }
    public string? TargetSource { get; init; }
    public double? TrainingMinutes { get; init; }
    public double? TrainingLoad { get; init; }
    public double? EstimatedBurn { get; init; }
    public double? EstimatedGlycogen { get; init; }
    public double? Sleep { get; init; }
    public double? Energy { get; init; }
    public string? RecoveryNote { get; init; }
    public string? FoodNote { get; init; }
    public IReadOnlyList<IntelligenceWorkout> Workouts { get; init; } = [];
}
public sealed record IntelligenceWorkout(string Name, string Type, double Minutes, double? Rpe,
    string Source, IReadOnlyList<IntelligenceExercise> Exercises);
public sealed record IntelligenceExercise(string Name, double? Weight, string Unit, int Sets,
    IReadOnlyList<int?> Reps, string? Note, bool TemplateEstimate);
public sealed record StatePoint(DateOnly Date, double? Raw, double? Estimate, double? Lower, double? Upper,
    double? WeeklySlope, double? Residual, bool Downweighted, int DaysSinceReading, int Readings,
    double? Average3, double? Average7, double? Change3, double? Change7);
public sealed record StateSeries(string Key, string Label, string Units, double ObservationNoise,
    double ProcessNoise, IReadOnlyList<StatePoint> Points);
public sealed record CorrelationResult(string Input, string Outcome, int LagDays, string Transform,
    int Pairs, double? R, string Status);
public sealed record IntelligenceFinding(string Title, string Evidence, string Interpretation);
public sealed record IntelligenceReport(string Version, string Fingerprint, DateOnly Start, DateOnly End,
    string Sensitivity, string MuscleMetric, IReadOnlyList<IntelligenceDay> Days,
    IReadOnlyList<StateSeries> States, IReadOnlyList<CorrelationResult> Correlations,
    IReadOnlyList<IntelligenceFinding> Findings, IReadOnlyList<IntelligenceFinding> WeeklySummaries);
public sealed record IntelligencePage(IntelligenceReport Report, bool OpenAiReady, string OpenAiModel, bool IsAdmin);
public sealed record AiFinding(string Title, string Evidence, string Hypothesis, string NextStep);
public sealed record IntelligenceAiResult(string Summary, string Confidence, IReadOnlyList<AiFinding> Findings,
    IReadOnlyList<string> Limitations, string Provider, string Model, string Fingerprint, DateTime GeneratedAtUtc);
