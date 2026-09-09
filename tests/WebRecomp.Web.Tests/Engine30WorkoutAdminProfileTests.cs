using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30WorkoutAdminProfileTests
{
    [Fact]
    public void BodyWeightEnergyAnchor_PrintsSameImpliedMetDiagnostic()
    {
        var profile =
            new Engine30WorkoutAdminProfile
            {
                ReferenceWorkoutCaloriesPerMinute = 5.5m,
                ReferenceRpe = 4m,
                ReferenceWeightLb = 180m,
                ScalingBasisCode = "BODY_WEIGHT"
            };

        var met =
            Engine30WorkoutProfileService
                .ImpliedMetAt210Lb(profile);

        Assert.Equal(
            4.04m,
            decimal.Round(
                met,
                2,
                MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Validation_RejectsOutOfRangeGlycogenDepletion()
    {
        var profile =
            new Engine30WorkoutAdminProfile
            {
                GlycogenReferenceDepletionFraction = 1.25m
            };

        Assert.Throws<InvalidOperationException>(
            () =>
                Engine30WorkoutProfileService
                    .Validate(profile));
    }
}
