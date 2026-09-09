namespace WebRecomp.Web.ViewModels;

public sealed class AdminEngineViewModel
{
    public required string VersionCode { get; init; }
    public required string StatusCode { get; init; }
    public string? Description { get; init; }

    public DateTime? PublishedAtUtc { get; init; }
    public DateTime? EffectiveFromUtc { get; init; }

    public bool IsValid { get; init; }

    public IReadOnlyList<string> ValidationIssues { get; init; }
        = Array.Empty<string>();

    public IReadOnlyList<AdminEngineCalculationGroup> CalculationGroups { get; init; }
        = Array.Empty<AdminEngineCalculationGroup>();

    public IReadOnlyList<AdminTrainingPrescriptionPreviewRow> TrainingPreviews { get; init; }
        = Array.Empty<AdminTrainingPrescriptionPreviewRow>();

    public IReadOnlyList<AdminEngineParameterRow> Parameters { get; init; }
        = Array.Empty<AdminEngineParameterRow>();
}

public sealed class AdminEngineCalculationGroup
{
    public required string Category { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }

    public IReadOnlyList<string> FormulaLines { get; init; }
        = Array.Empty<string>();

    public IReadOnlyList<string> DependencyLines { get; init; }
        = Array.Empty<string>();

    public required string IntegrationStatusCode { get; init; }
    public required string IntegrationStatusLabel { get; init; }
    public required string IntegrationStatusDescription { get; init; }
}

public sealed class AdminEngineParameterRow
{
    public required string Key { get; init; }
    public required string Category { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }

    public decimal CurrentValue { get; init; }
    public decimal DefaultValue { get; init; }

    public required string Unit { get; init; }

    public decimal? MinimumValue { get; init; }
    public decimal? MaximumValue { get; init; }

    public required string BasisType { get; init; }
    public required string Rationale { get; init; }

    public required string DefaultVersion { get; init; }
    public required string IntroducedEngineVersion { get; init; }

    public string? EffectOnCalculation { get; init; }

    public bool Uses12WrDefault { get; init; }
    public bool IsModifiedFromDefault { get; init; }
}

public sealed class AdminTrainingPrescriptionPreviewRow
{
    public required string WorkoutTypeCode { get; init; }
    public required string WorkoutName { get; init; }
    public required string PrescriptionModeCode { get; init; }

    public required string TrainingPurpose { get; init; }
    public required string TargetArea { get; init; }

    public decimal RecommendedMinutes { get; init; }
    public int? ApproximateWorkingSets { get; init; }

    public decimal? TargetRpeMinimum { get; init; }
    public decimal? TargetRpeMaximum { get; init; }

    public required string RpeScopeCode { get; init; }

    public required string IntensityGuidance { get; init; }
    public required string PhysiologicalPurpose { get; init; }
    public required string ExpectedTrainingCost { get; init; }
    public required string VolumeBasis { get; init; }
}
