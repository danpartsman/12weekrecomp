using WebRecomp.Web.Services.Engine30;

namespace WebRecomp.Web.Tests;

public sealed class Engine30LocalMuscleConstraintServiceTests
{
    private readonly Engine30LocalMuscleConstraintService _service = new();

    [Fact]
    public void SoreLegs_OnAPartialLegWorkout_KeepsTheOtherRegions()
    {
        var result =
            _service.Evaluate(
                ["PUSH", "LEGS"],
                [
                    new Engine30LocalMuscleConstraint(
                        Engine30MuscleRegionCodes.Legs,
                        Engine30LocalConstraintKindCodes.Soreness,
                        Score: 7)
                ]);

        Assert.Equal(
            Engine30LocalWorkoutDecisionCodes.PartialWithoutLimitedRegions,
            result.DecisionCode);
        Assert.Equal(["PUSH"], result.KeptRegionCodes);
        Assert.Equal(["LEGS"], result.ExcludedRegionCodes);
        Assert.Contains("LOCAL_SORENESS_LEGS", result.ReasonCodes);
        Assert.Contains("PARTIAL_WORKOUT_STILL_AVAILABLE", result.ReasonCodes);
    }

    [Fact]
    public void LocalPain_ExcludesOnlyThatRegion()
    {
        var result =
            _service.Evaluate(
                ["PULL", "LEGS", "CORE"],
                [
                    new Engine30LocalMuscleConstraint(
                        "legs",
                        Engine30LocalConstraintKindCodes.Pain)
                ]);

        Assert.Equal(
            Engine30LocalWorkoutDecisionCodes.PartialWithoutLimitedRegions,
            result.DecisionCode);
        Assert.Equal(["PULL", "CORE"], result.KeptRegionCodes);
        Assert.Equal(["LEGS"], result.ExcludedRegionCodes);
        Assert.Contains("LOCAL_PAIN_LEGS", result.ReasonCodes);
    }

    [Fact]
    public void MildSoreness_DoesNotRemoveTheRegion()
    {
        var result =
            _service.Evaluate(
                ["LEGS"],
                [
                    new Engine30LocalMuscleConstraint(
                        Engine30MuscleRegionCodes.Legs,
                        Engine30LocalConstraintKindCodes.Soreness,
                        Score: 5)
                ]);

        Assert.Equal(
            Engine30LocalWorkoutDecisionCodes.KeepPlannedMuscles,
            result.DecisionCode);
        Assert.Equal(["LEGS"], result.KeptRegionCodes);
        Assert.Empty(result.ExcludedRegionCodes);
    }

    [Fact]
    public void EveryPlannedRegionLimited_HasNoSafeWorkout()
    {
        var result =
            _service.Evaluate(
                ["LEGS"],
                [
                    new Engine30LocalMuscleConstraint(
                        Engine30MuscleRegionCodes.Legs,
                        Engine30LocalConstraintKindCodes.Pain)
                ]);

        Assert.Equal(
            Engine30LocalWorkoutDecisionCodes.NoSafeRegion,
            result.DecisionCode);
        Assert.Empty(result.KeptRegionCodes);
        Assert.Contains("NO_PLANNED_REGION_REMAINS", result.ReasonCodes);
    }

    [Fact]
    public void UnknownRegion_IsIgnored_AndDoesNotCancelTheWorkout()
    {
        var result =
            _service.Evaluate(
                ["PUSH"],
                [
                    new Engine30LocalMuscleConstraint(
                        "NECK",
                        Engine30LocalConstraintKindCodes.Pain)
                ]);

        Assert.Equal(
            Engine30LocalWorkoutDecisionCodes.KeepPlannedMuscles,
            result.DecisionCode);
        Assert.Equal(["PUSH"], result.KeptRegionCodes);
        Assert.Contains("IGNORED_UNKNOWN_REGION", result.ReasonCodes);
    }
}
