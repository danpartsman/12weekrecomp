namespace WebRecomp.Web.ViewModels;

public sealed class AdminWorkoutTypesIndexViewModel
{
    public required string ConfigVersionCode { get; set; }
    public required string StatusCode { get; set; }
    public List<AdminWorkoutTypeRowViewModel> Workouts { get; set; } = [];
}

public sealed class AdminWorkoutTypeRowViewModel
{
    public Guid WorkoutTypeDefinitionId { get; set; }
    public required string WorkoutTypeCode { get; set; }
    public required string DisplayName { get; set; }
    public required string WorkoutCategoryCode { get; set; }
    public required string WorkoutCategoryName { get; set; }

    public decimal FuelScore { get; set; }

    // Standardized Engine 3.0 admin comparison:
    // modeled glycogen depletion for 60 minutes, 200-lb male, 20% body fat.
    public decimal? Engine30FuelGlycogen60MinuteG { get; set; }
    public decimal DefaultMinutes { get; set; }

    public decimal PushLoad { get; set; }
    public decimal PullLoad { get; set; }
    public decimal LegLoad { get; set; }
    public decimal CardioLoad { get; set; }

    public bool IsActive { get; set; }
    public bool IsSelectable { get; set; }
    public short DisplayOrder { get; set; }

    // Engine 3.0 shadow profile summary.
    public string? Engine30FamilyCode { get; set; }
    public string? Engine30ModalityCode { get; set; }
    public decimal? Engine30DefaultRpe { get; set; }
    public string? Engine30VersionCode { get; set; }
    public bool HasEngine30Profile { get; set; }
}

public sealed class AdminWorkoutTypeEditViewModel
{
    public Guid WorkoutTypeDefinitionId { get; set; }

    public required string WorkoutTypeCode { get; set; }
    public required string DisplayName { get; set; }
    public required string WorkoutCategoryCode { get; set; }

    public string? Description { get; set; }
    public string? Instructions { get; set; }
    public string? OverviewVideoUrl { get; set; }

    // Legacy Engine 2 compatibility fields.
    public decimal DefaultMinutes { get; set; }
    public decimal FuelScore { get; set; }

    // Standardized Engine 3.0 admin comparison:
    // modeled glycogen depletion for 60 minutes, 200-lb male, 20% body fat.
    public decimal? Engine30FuelGlycogen60MinuteG { get; set; }

    public decimal PushLoad { get; set; }
    public decimal PullLoad { get; set; }
    public decimal LegLoad { get; set; }
    public decimal CardioLoad { get; set; }

    public string WeeklyRoleCode { get; set; } = "";
    public string? RegionCode { get; set; }

    public bool CountsDedicatedHeavy { get; set; }
    public bool CountsLightStrength { get; set; }
    public bool CountsPrimaryConditioning { get; set; }
    public bool CountsRecovery { get; set; }

    public string FuelPriorityCode { get; set; } = "";
    public string SchedulePrincipleCode { get; set; } = "";

    public string? StrategyNote { get; set; }
    public string? MacroClassCode { get; set; }
    public string? PrimaryFocusCode { get; set; }

    public decimal RawGlycogenGMin { get; set; }
    public decimal DefaultIntensityFactor { get; set; }
    public short DefaultRestSec { get; set; }
    public decimal ReferenceWorkoutCalPerMin { get; set; }
    public decimal GlycogenCarbShare { get; set; }

    public decimal StrengthStress { get; set; }
    public decimal PlannedRestorationPct { get; set; }
    public decimal ProteinAdjustmentG { get; set; }
    public decimal TargetFatG { get; set; }
    public decimal TypicalRecoveryHours { get; set; }

    public string? PlanningNote { get; set; }

    public bool IsSelectable { get; set; }
    public short DisplayOrder { get; set; }

    public required string ConfigVersionCode { get; set; }
    public required string StatusCode { get; set; }

    // New Engine 3.0 workout-definition/calibration editor.
    public AdminEngine30WorkoutProfileViewModel Engine30 { get; set; } =
        AdminEngine30WorkoutProfileViewModel.CreateDefault();

    public string? Engine30ChangeNotes { get; set; }

    public string? Engine30ActiveVersionCode { get; set; }
    public string? Engine30BaselineVersionCode { get; set; }
    public List<AdminEngine30WorkoutVersionViewModel> Engine30History { get; set; } = [];

    public decimal Engine30ImpliedMetAt210Lb { get; set; }
}

public sealed class AdminEngine30WorkoutProfileViewModel
{
    public string WorkoutFamilyCode { get; set; } = "CARDIO";
    public string DefaultModalityCode { get; set; } = "GENERAL_CARDIO";
    public string TrainingPatternCode { get; set; } = "STEADY";
    public string SessionStructureCode { get; set; } = "CONTINUOUS";

    public decimal DefaultDurationMinutes { get; set; } = 45m;
    public decimal DefaultRpe { get; set; } = 4m;

    public bool SupportsObjectiveWorkload { get; set; } = true;
    public bool SupportsResistanceDetails { get; set; }
    public bool SupportsIntervalDetails { get; set; }
    public bool SupportsSegments { get; set; }

    public decimal ReferenceWorkoutCaloriesPerMinute { get; set; } = 5.5m;
    public decimal ReferenceRpe { get; set; } = 4m;
    public decimal ReferenceWeightLb { get; set; } = 180m;
    public string ScalingBasisCode { get; set; } = "BODY_WEIGHT";
    public decimal? ReferenceFatFreeMassLb { get; set; }
    public decimal? DefaultRestSeconds { get; set; }

    public decimal ResistanceReferenceDurationMinutes { get; set; } = 60m;
    public decimal ResistanceReferenceHardSets { get; set; } = 16m;
    public decimal ResistanceHardSetDensitySensitivity { get; set; } = 0.50m;
    public decimal ResistanceRestFallbackSensitivity { get; set; } = 0.25m;
    public decimal ResistanceDensityMinimumFactor { get; set; } = 0.85m;
    public decimal ResistanceDensityMaximumFactor { get; set; } = 1.15m;

    public string GlycogenModelCode { get; set; } = "CONTINUOUS";
    public decimal GlycogenReferenceDurationMinutes { get; set; } = 45m;
    public decimal GlycogenReferenceDepletionFraction { get; set; } = 0.14m;
    public decimal GlycogenIntensitySensitivity { get; set; } = 0.50m;
    public decimal GlycogenMinimumDepletionFraction { get; set; } = 0.01m;
    public decimal GlycogenMaximumDepletionFraction { get; set; } = 0.70m;
    public decimal TargetRestorationFraction { get; set; } = 1.00m;
    public decimal CarbohydratePerGlycogenG { get; set; } = 1.00m;

    public decimal IntervalWorkSeconds { get; set; } = 60m;
    public decimal IntervalRecoverySeconds { get; set; } = 60m;
    public int IntervalRepetitions { get; set; } = 15;
    public decimal IntervalWorkRpe { get; set; } = 8m;
    public decimal IntervalRecoveryRpe { get; set; } = 3m;
    public decimal IntervalRecoveryContributionWeight { get; set; } = 0.25m;
    public decimal IntervalWorkIntensitySensitivity { get; set; } = 0.50m;
    public decimal IntervalRecoveryIntensitySensitivity { get; set; } = 0.25m;

    public List<AdminEngine30MuscleRegionViewModel> MuscleRegions { get; set; } = [];
    public List<AdminEngine30SegmentViewModel> Segments { get; set; } = [];

    public static AdminEngine30WorkoutProfileViewModel CreateDefault()
        => new()
        {
            MuscleRegions =
            [
                new()
                {
                    MuscleRegionCode = "LOWER_BODY",
                    WholeBodyMuscleFraction = 0.50m
                },
                new()
                {
                    MuscleRegionCode = "UPPER_BODY",
                    WholeBodyMuscleFraction = 0.20m
                },
                new(),
                new()
            ],
            Segments =
                Enumerable.Range(1, 6)
                    .Select(i => new AdminEngine30SegmentViewModel
                    {
                        DisplayOrder = i * 10
                    })
                    .ToList()
        };
}

public sealed class AdminEngine30MuscleRegionViewModel
{
    public string? MuscleRegionCode { get; set; }
    public decimal WholeBodyMuscleFraction { get; set; }
    public decimal RecruitmentFraction { get; set; }
    public decimal HardSets { get; set; }
}

public sealed class AdminEngine30SegmentViewModel
{
    public bool Enabled { get; set; }
    public int DisplayOrder { get; set; }

    public string? SegmentCode { get; set; }
    public string? DisplayName { get; set; }

    public string ModalityCode { get; set; } = "GENERAL_CARDIO";
    public string TrainingPatternCode { get; set; } = "STEADY";
    public string SessionStructureCode { get; set; } = "CONTINUOUS";

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

public sealed class AdminEngine30WorkoutVersionViewModel
{
    public Guid Engine30WorkoutProfileVersionId { get; set; }
    public int VersionNumber { get; set; }
    public required string VersionCode { get; set; }
    public bool IsBaseline { get; set; }
    public bool IsActive { get; set; }
    public Guid? RestoredFromProfileVersionId { get; set; }
    public string? ChangeNotes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

