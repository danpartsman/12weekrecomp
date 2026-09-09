using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30AdminFuelComparisonServiceTests
{
    private readonly Engine30AdminFuelComparisonService _service = new();

    [Fact]
    public void RecoveryProfile_ReturnsZeroGlycogenDemand()
    {
        var profile = new Engine30WorkoutAdminProfile
        {
            WorkoutFamilyCode = Engine30WorkoutFamilyCodes.Recovery,
            GlycogenModelCode = "ZERO",
            DefaultDurationMinutes = 20m
        };

        Assert.Equal(0m, _service.Calculate60MinuteGlycogen(profile));
    }

    [Fact]
    public void ContinuousCardio_HigherDepletionCoefficientRaisesFuelComparison()
    {
        var low = CardioProfile(0.08m);
        var high = CardioProfile(0.16m);

        var lowValue = _service.Calculate60MinuteGlycogen(low);
        var highValue = _service.Calculate60MinuteGlycogen(high);

        Assert.NotNull(lowValue);
        Assert.NotNull(highValue);
        Assert.True(highValue > lowValue);
    }

    [Fact]
    public void Resistance_HigherRecruitmentRaisesFuelComparison()
    {
        var low = ResistanceProfile(0.40m);
        var high = ResistanceProfile(0.85m);

        var lowValue = _service.Calculate60MinuteGlycogen(low);
        var highValue = _service.Calculate60MinuteGlycogen(high);

        Assert.NotNull(lowValue);
        Assert.NotNull(highValue);
        Assert.True(highValue > lowValue);
    }

    [Fact]
    public void CompositeProfile_ProducesSequentialGlycogenComparison()
    {
        var profile = new Engine30WorkoutAdminProfile
        {
            WorkoutFamilyCode = Engine30WorkoutFamilyCodes.Composite,
            GlycogenModelCode = "SEQUENTIAL_COMPOSITE",
            DefaultDurationMinutes = 60m,
            DefaultRpe = 7m,
            GlycogenMinimumDepletionFraction = 0.01m,
            GlycogenMaximumDepletionFraction = 0.70m,
            GlycogenIntensitySensitivity = 0.50m,
            IntervalWorkSeconds = 60m,
            IntervalRecoverySeconds = 60m,
            IntervalWorkRpe = 8m,
            IntervalRecoveryRpe = 3m,
            IntervalWorkIntensitySensitivity = 0.50m,
            IntervalRecoveryIntensitySensitivity = 0.25m,
            IntervalRecoveryContributionWeight = 0.25m,
            Segments =
            [
                new Engine30WorkoutAdminSegment
                {
                    Enabled = true,
                    DisplayOrder = 10,
                    SegmentCode = "TREAD",
                    ModalityCode = Engine30WorkoutModalityCodes.Treadmill,
                    GlycogenModelCode = "CONTINUOUS",
                    DurationMinutes = 30m,
                    DefaultRpe = 6m,
                    GlycogenReferenceDepletionFraction = 0.12m,
                    LowerBodyMuscleFraction = 0.50m,
                    LowerBodyRecruitmentFraction = 0.70m
                },
                new Engine30WorkoutAdminSegment
                {
                    Enabled = true,
                    DisplayOrder = 20,
                    SegmentCode = "FLOOR",
                    ModalityCode = Engine30WorkoutModalityCodes.Resistance,
                    GlycogenModelCode = "RESISTANCE",
                    DurationMinutes = 30m,
                    DefaultRpe = 7m,
                    GlycogenReferenceDepletionFraction = 0.22m,
                    LowerBodyMuscleFraction = 0.50m,
                    LowerBodyRecruitmentFraction = 0.60m,
                    UpperBodyMuscleFraction = 0.20m,
                    UpperBodyRecruitmentFraction = 0.65m,
                    HardSetsPerRegion = 4m
                }
            ]
        };

        var value = _service.Calculate60MinuteGlycogen(profile);

        Assert.NotNull(value);
        Assert.True(value > 0m);
    }

    private static Engine30WorkoutAdminProfile CardioProfile(decimal depletion)
        => new()
        {
            WorkoutFamilyCode = Engine30WorkoutFamilyCodes.Cardio,
            DefaultModalityCode = Engine30WorkoutModalityCodes.Cycling,
            DefaultDurationMinutes = 45m,
            DefaultRpe = 3m,
            GlycogenModelCode = "CONTINUOUS",
            GlycogenReferenceDurationMinutes = 45m,
            GlycogenReferenceDepletionFraction = depletion,
            GlycogenIntensitySensitivity = 0.50m,
            GlycogenMinimumDepletionFraction = 0.01m,
            GlycogenMaximumDepletionFraction = 0.70m,
            MuscleRegions =
            [
                new Engine30WorkoutAdminMuscleRegion
                {
                    MuscleRegionCode = "LOWER_BODY",
                    WholeBodyMuscleFraction = 0.50m,
                    RecruitmentFraction = 0.70m
                }
            ]
        };

    private static Engine30WorkoutAdminProfile ResistanceProfile(decimal recruitment)
        => new()
        {
            WorkoutFamilyCode = Engine30WorkoutFamilyCodes.Resistance,
            DefaultModalityCode = Engine30WorkoutModalityCodes.Resistance,
            DefaultDurationMinutes = 60m,
            DefaultRpe = 8m,
            GlycogenModelCode = "RESISTANCE",
            GlycogenReferenceDurationMinutes = 60m,
            GlycogenReferenceDepletionFraction = 0.28m,
            GlycogenMinimumDepletionFraction = 0.05m,
            GlycogenMaximumDepletionFraction = 0.65m,
            MuscleRegions =
            [
                new Engine30WorkoutAdminMuscleRegion
                {
                    MuscleRegionCode = "LOWER_BODY",
                    WholeBodyMuscleFraction = 0.50m,
                    RecruitmentFraction = recruitment,
                    HardSets = 8m
                }
            ]
        };
}
