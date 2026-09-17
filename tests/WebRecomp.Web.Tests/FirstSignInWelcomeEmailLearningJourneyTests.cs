using WebRecomp.Web.Services;

namespace WebRecomp.Web.Tests;

public sealed class FirstSignInWelcomeEmailLearningJourneyTests
{
    [Fact]
    public void Email_FramesProgramAsLearningTool()
    {
        var content =
            FirstSignInWelcomeEmailService.BuildContent(
                "Client");

        Assert.Contains(
            "learning program, not just a tracking app",
            content.Body,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "consistency, not perfection",
            content.Body,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "12-WEEK COMMITMENT",
            content.Body);
    }

    [Fact]
    public void Email_MakesMovementAndResistanceTrainingCentral()
    {
        var content =
            FirstSignInWelcomeEmailService.BuildContent(
                "Client");

        Assert.Contains(
            "Activity is the driver of the program",
            content.Body);

        Assert.Contains(
            "Resistance training is especially important",
            content.Body);

        Assert.Contains(
            "Do something purposeful with your body every day",
            content.Body);
    }

    [Fact]
    public void Email_ExplainsActivityDrivenFuelPrescription()
    {
        var content =
            FirstSignInWelcomeEmailService.BuildContent(
                "Client");

        Assert.Contains(
            "The harder the activity you have planned, the more fuel you may need",
            content.Body);

        Assert.Contains(
            "Use the fuel the program gives you",
            content.Body);

        Assert.Contains(
            "daily calorie and macronutrient prescription",
            content.Body);
    }

    [Fact]
    public void Email_ExplainsWeeklyPlanDailyEntryScaleAndNutrition()
    {
        var content =
            FirstSignInWelcomeEmailService.BuildContent(
                "Client");

        Assert.Contains(
            "Go to Weekly Plan",
            content.Body);

        Assert.Contains(
            "EVERY MORNING: MEASURE",
            content.Body);

        Assert.Contains(
            "Go to Daily Entry",
            content.Body);

        Assert.Contains(
            "nutrition app of your choice",
            content.Body);
    }

    [Fact]
    public void Email_AllowsIncompleteDataButEncouragesBestEffort()
    {
        var content =
            FirstSignInWelcomeEmailService.BuildContent(
                "Client");

        Assert.Contains(
            "YOU DO NOT HAVE TO ENTER EVERYTHING",
            content.Body);

        Assert.Contains(
            "best prescription and analysis possible from the information that is available",
            content.Body);

        Assert.Contains(
            "Honest imperfect data",
            content.Body);
    }

    [Fact]
    public void Email_ExplainsGraduationOrContinuationWithoutGuaranteeingOutcome()
    {
        var content =
            FirstSignInWelcomeEmailService.BuildContent(
                "Client");

        Assert.Contains(
            "continue with 12 Week Recomp for another term",
            content.Body);

        Assert.Contains(
            "Or you can let it go",
            content.Body);

        Assert.Contains(
            "Individual results vary",
            content.Body);
    }
}