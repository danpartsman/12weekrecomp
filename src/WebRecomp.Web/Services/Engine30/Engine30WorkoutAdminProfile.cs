namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Persisted, administrator-editable Engine 3.0 workout profile.
///
/// This is SHADOW configuration. It does not alter frozen Engine 2.0.
/// The profile is serialized into the version-history table so every saved
/// revision can be restored without deleting history.
/// </summary>
public sealed class Engine30WorkoutAdminProfile
{
    public int SchemaVersion { get; set; } = 1;

    public string WorkoutFamilyCode { get; set; } = Engine30WorkoutFamilyCodes.Cardio;
    public string DefaultModalityCode { get; set; } = Engine30WorkoutModalityCodes.GeneralCardio;
    public string TrainingPatternCode { get; set; } = Engine30TrainingPatternCodes.Steady;
    public string SessionStructureCode { get; set; } = Engine30WorkoutSessionCodes.StructureContinuous;

    public decimal DefaultDurationMinutes { get; set; } = 45m;
    public decimal DefaultRpe { get; set; } = 4m;

    public bool SupportsObjectiveWorkload { get; set; } = true;
    public bool SupportsResistanceDetails { get; set; }
    public bool SupportsIntervalDetails { get; set; }
    public bool SupportsSegments { get; set; }

    // Bucket 3 energy calibration.
    public decimal ReferenceWorkoutCaloriesPerMinute { get; set; } = 5.5m;
    public decimal ReferenceRpe { get; set; } = 4m;
    public decimal ReferenceWeightLb { get; set; } = 180m;
    public string ScalingBasisCode { get; set; } = "BODY_WEIGHT";
    public decimal? ReferenceFatFreeMassLb { get; set; }
    public decimal? DefaultRestSeconds { get; set; }

    // Resistance energy workload calibration.
    public decimal ResistanceReferenceDurationMinutes { get; set; } = 60m;
    public decimal ResistanceReferenceHardSets { get; set; } = 16m;
    public decimal ResistanceHardSetDensitySensitivity { get; set; } = 0.50m;
    public decimal ResistanceRestFallbackSensitivity { get; set; } = 0.25m;
    public decimal ResistanceDensityMinimumFactor { get; set; } = 0.85m;
    public decimal ResistanceDensityMaximumFactor { get; set; } = 1.15m;

    // Glycogen workload calibration.
    public string GlycogenModelCode { get; set; } = "CONTINUOUS";
    public decimal GlycogenReferenceDurationMinutes { get; set; } = 45m;
    public decimal GlycogenReferenceDepletionFraction { get; set; } = 0.14m;
    public decimal GlycogenIntensitySensitivity { get; set; } = 0.50m;
    public decimal GlycogenMinimumDepletionFraction { get; set; } = 0.01m;
    public decimal GlycogenMaximumDepletionFraction { get; set; } = 0.70m;
    public decimal TargetRestorationFraction { get; set; } = 1.00m;
    public decimal CarbohydratePerGlycogenG { get; set; } = 1.00m;

    // Standard interval defaults.
    public decimal IntervalWorkSeconds { get; set; } = 60m;
    public decimal IntervalRecoverySeconds { get; set; } = 60m;
    public int IntervalRepetitions { get; set; } = 15;
    public decimal IntervalWorkRpe { get; set; } = 8m;
    public decimal IntervalRecoveryRpe { get; set; } = 3m;
    public decimal IntervalRecoveryContributionWeight { get; set; } = 0.25m;
    public decimal IntervalWorkIntensitySensitivity { get; set; } = 0.50m;
    public decimal IntervalRecoveryIntensitySensitivity { get; set; } = 0.25m;

    public List<Engine30WorkoutAdminMuscleRegion> MuscleRegions { get; set; } = [];
    public List<Engine30WorkoutAdminSegment> Segments { get; set; } = [];
}

public sealed class Engine30WorkoutAdminMuscleRegion
{
    public string MuscleRegionCode { get; set; } = "";
    public decimal WholeBodyMuscleFraction { get; set; }
    public decimal RecruitmentFraction { get; set; }
    public decimal HardSets { get; set; }
}

public sealed class Engine30WorkoutAdminSegment
{
    public bool Enabled { get; set; }
    public int DisplayOrder { get; set; }

    public string SegmentCode { get; set; } = "";
    public string DisplayName { get; set; } = "";

    public string ModalityCode { get; set; } = Engine30WorkoutModalityCodes.GeneralCardio;
    public string TrainingPatternCode { get; set; } = Engine30TrainingPatternCodes.Steady;
    public string SessionStructureCode { get; set; } = Engine30WorkoutSessionCodes.StructureContinuous;

    public decimal DurationMinutes { get; set; }
    public decimal DefaultRpe { get; set; }

    public decimal ReferenceWorkoutCaloriesPerMinute { get; set; }
    public decimal ReferenceRpe { get; set; }
    public string ScalingBasisCode { get; set; } = "BODY_WEIGHT";
    public decimal? ReferenceFatFreeMassLb { get; set; }
    public decimal? DefaultRestSeconds { get; set; }

    public string GlycogenModelCode { get; set; } = "CONTINUOUS";
    public decimal GlycogenReferenceDepletionFraction { get; set; }

    public decimal LowerBodyMuscleFraction { get; set; } = 0.50m;
    public decimal LowerBodyRecruitmentFraction { get; set; }
    public decimal UpperBodyMuscleFraction { get; set; } = 0.20m;
    public decimal UpperBodyRecruitmentFraction { get; set; }
    public decimal HardSetsPerRegion { get; set; }
}
