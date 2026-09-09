using System.Reflection;
using WebRecomp.Web.ViewModels;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class AdminWorkoutTypesSegmentBindingTests
{
    [Fact]
    public void UnusedSegmentTextFields_AreNullable()
    {
        var nullability =
            new NullabilityInfoContext();

        var segmentCode =
            typeof(AdminEngine30SegmentViewModel)
                .GetProperty(
                    nameof(AdminEngine30SegmentViewModel.SegmentCode))
            ?? throw new InvalidOperationException(
                "SegmentCode property was not found.");

        var displayName =
            typeof(AdminEngine30SegmentViewModel)
                .GetProperty(
                    nameof(AdminEngine30SegmentViewModel.DisplayName))
            ?? throw new InvalidOperationException(
                "DisplayName property was not found.");

        Assert.Equal(
            NullabilityState.Nullable,
            nullability.Create(segmentCode).WriteState);

        Assert.Equal(
            NullabilityState.Nullable,
            nullability.Create(displayName).WriteState);
    }

    [Fact]
    public void UnusedMuscleRegionCode_IsNullable()
    {
        var nullability =
            new NullabilityInfoContext();

        var property =
            typeof(AdminEngine30MuscleRegionViewModel)
                .GetProperty(
                    nameof(AdminEngine30MuscleRegionViewModel.MuscleRegionCode))
            ?? throw new InvalidOperationException(
                "MuscleRegionCode property was not found.");

        Assert.Equal(
            NullabilityState.Nullable,
            nullability.Create(property).WriteState);
    }

    [Fact]
    public void DefaultCompositeRows_CanRemainUnused()
    {
        var profile =
            AdminEngine30WorkoutProfileViewModel.CreateDefault();

        Assert.Equal(
            6,
            profile.Segments.Count);

        Assert.All(
            profile.Segments,
            segment =>
            {
                Assert.False(segment.Enabled);
                Assert.True(
                    string.IsNullOrWhiteSpace(
                        segment.SegmentCode));
                Assert.True(
                    string.IsNullOrWhiteSpace(
                        segment.DisplayName));
            });
    }
}