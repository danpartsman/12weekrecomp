using WebRecomp.Web.Services.Coaching;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class CoachWorkoutGuideCatalogTests
{
    [Theory]
    [InlineData("MARISOL", "MAJOR_LEGS", "MARISOL")]
    [InlineData("MARISOL", "ORANGETHEORY", "MARISOL")]
    [InlineData("MARISOL", "MAJOR_PULL", "MARISOL")]
    [InlineData("MARISOL", "MAJOR_PUSH", "MARISOL")]
    [InlineData("MARISOL", "FULL_BODY_STRENGTH", "MARISOL")]
    [InlineData("CASSIE", "ZONE2_CARDIO", "CASSIE")]
    [InlineData("CASSIE", "GENERAL_CARDIO", "CASSIE")]
    [InlineData("CASSIE", "RUNNING_INTERVALS", "CASSIE")]
    [InlineData("CASSIE", "TRUE_HIIT", "CASSIE")]
    [InlineData("CASSIE", "MIXED_CONDITIONING", "CASSIE")]
    public void ExpectedCoachWorkoutGuideResolves(
        string requestedCoach,
        string workout,
        string expectedCoach)
    {
        var guide =
            CoachWorkoutGuideCatalog.Resolve(
                requestedCoach,
                workout);

        Assert.NotNull(guide);
        Assert.Equal(
            expectedCoach,
            guide!.CoachCode);

        Assert.False(
            string.IsNullOrWhiteSpace(
                guide.NutritionGuideCode));

        Assert.False(
            string.IsNullOrWhiteSpace(
                guide.NutritionSummary));

        Assert.Contains(
            guide.PresentationVariant,
            new[] { "left", "right", "center" });
    }

    [Fact]
    public void OldMerisolSpelling_IsBackwardsCompatibilityAlias()
    {
        var guide =
            CoachWorkoutGuideCatalog.Resolve(
                "MERISOL",
                "MAJOR_PUSH");

        Assert.NotNull(guide);
        Assert.Equal(
            "MARISOL",
            guide!.CoachCode);

        Assert.Equal(
            "Marisol",
            guide.CoachDisplayName);
    }

    [Fact]
    public void PreferredCoachFallsBackToWorkoutSpecialist()
    {
        var guide =
            CoachWorkoutGuideCatalog.Resolve(
                "MARISOL",
                "TRUE_HIIT");

        Assert.NotNull(guide);
        Assert.Equal(
            "CASSIE",
            guide!.CoachCode);
    }

    [Fact]
    public void UnsupportedWorkoutDoesNotInventGuide()
    {
        Assert.Null(
            CoachWorkoutGuideCatalog.Resolve(
                "MARISOL",
                "NOT_A_REAL_WORKOUT"));
    }
}
