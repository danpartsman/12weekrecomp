using WebRecomp.Web.Services;

namespace WebRecomp.Web.Tests;

public sealed class FirstSignInWelcomeEmailServiceTests
{
    [Fact]
    public void WelcomeContent_TeachesTheActual12WRLoop()
    {
        var content =
            FirstSignInWelcomeEmailService.BuildContent(
                "Client");

        Assert.Contains(
            "Plan -> Do -> Log -> Measure -> Learn -> Adapt",
            content.Body);

        Assert.Contains(
            "WEEKLY PLAN",
            content.Body);

        Assert.Contains(
            "LOG NUTRITION AS ACCURATELY AS PRACTICAL",
            content.Body);

        Assert.Contains(
            "ENTER YOUR MORNING SCALE READINGS",
            content.Body);

        Assert.Contains(
            "WATCH THE TREND",
            content.Body);
    }

    [Fact]
    public void WelcomeContent_Frames12WRAsLearning_NotCrashDiet()
    {
        var content =
            FirstSignInWelcomeEmailService.BuildContent(
                "Client");

        Assert.Contains(
            "learning process",
            content.Body,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "habits",
            content.Body,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "for life",
            content.Body,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WelcomeContent_UsesEvidenceLanguageWithoutGuaranteeingResults()
    {
        var content =
            FirstSignInWelcomeEmailService.BuildContent(
                "Client");

        Assert.Contains(
            "evidence-based",
            content.Body,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "strong track record",
            content.Body,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Individual results still vary",
            content.Body,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentEmailTransportContract_CanBeAdapted()
    {
        var compatible =
            FirstSignInWelcomeEmailService.CanAdaptCurrentContract(
                out var reason);

        Assert.True(
            compatible,
            reason);
    }
}