using WebRecomp.Web.Services.WorkoutTargeting;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Tests;

public sealed class WorkoutSelectorRecommendationTests
{
    [Theory]
    [InlineData(null, false, "start")]
    [InlineData("", false, "start")]
    [InlineData("invalid", false, "start")]
    [InlineData(null, true, "live")]
    [InlineData("saved", false, "saved")]
    [InlineData("new", false, "new")]
    [InlineData("system", false, "system")]
    public void EntryBypassesLandingButPreservesExplicitDestinations(string? requested, bool session, string expected)
    {
        var method = typeof(WebRecomp.Web.Controllers.DetailedWorkoutController).GetMethod(
            "NormalizeWorkspaceMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Equal(expected, method.Invoke(null, [requested, session]));
    }

    [Fact]
    public void SameTemplateDoesNotOpenDuplicateRecommendation()
    {
        var id = Guid.NewGuid();
        var model = Model(new WorkoutTargetChoice { TemplateId = id }, new WorkoutTargetChoice { TemplateId = Guid.NewGuid() });
        model.PlannedWorkoutTemplateId = id;
        Assert.Null(model.DifferentRecommendedWorkout);
    }

    [Fact]
    public void SameSystemCodeIsCaseInsensitive()
    {
        var model = Model(new WorkoutTargetChoice { SystemCode = "MAJOR_PUSH" });
        model.PlannedSystemWorkoutCode = "major_push";
        Assert.Null(model.DifferentRecommendedWorkout);
    }

    [Fact]
    public void AdaptedPlanOpensSeparately()
    {
        var id = Guid.NewGuid();
        var choice = new WorkoutTargetChoice { TemplateId = id, Adapted = true };
        var model = Model(choice);
        model.PlannedWorkoutTemplateId = id;
        Assert.Same(choice, model.DifferentRecommendedWorkout);
    }

    [Fact]
    public void DifferentTemplateOfSameWorkoutTypeStillOpens()
    {
        var choice = new WorkoutTargetChoice { TemplateId = Guid.NewGuid(), TypeCode = "MAJOR_PUSH" };
        var model = Model(choice);
        model.PlannedWorkoutTemplateId = Guid.NewGuid();
        Assert.Same(choice, model.DifferentRecommendedWorkout);
    }

    [Fact]
    public void NoPlanShowsTopRecommendation()
    {
        var choice = new WorkoutTargetChoice { SystemCode = "MAJOR_PULL" };
        Assert.Same(choice, Model(choice).DifferentRecommendedWorkout);
    }

    [Fact]
    public void MissingRecommendationsKeepPlanAvailable()
    {
        var model = new DetailedWorkoutEditorViewModel { PlannedWorkoutTemplateId = Guid.NewGuid() };
        Assert.Null(model.DifferentRecommendedWorkout);
    }

    private static DetailedWorkoutEditorViewModel Model(params WorkoutTargetChoice[] choices)
        => new() { StartRecommendations = new() { Choices = choices.ToList() } };
}
