using WebRecomp.Web.Services.Learning;

namespace WebRecomp.Web.Tests;

public sealed class LuciaPresentationTests
{
    [Fact]
    public void ConsecutiveLessonsHaveDifferentOpeningsAndKeepTheirContent()
    {
        var lucia = CoachCatalog.All.Single(x => x.Code == "LUCIA");
        const string lesson = "A useful lesson with its original evidence intact.";
        var messages = Enumerable.Range(0, 8).Select(i => CoachVoice.Speak(lucia, lesson, i)).ToArray();
        Assert.Equal(8, messages.Distinct().Count());
        Assert.All(messages, message => Assert.Contains(lesson, message));
        Assert.All(messages, message => Assert.DoesNotContain("let's put this to work", message));
        Assert.Equal(messages[0], CoachVoice.Speak(lucia, lesson, 8));
    }

    [Fact]
    public void LuciaUsesIndividualPhotosInsteadOfStretchedAtlasPanels()
    {
        var lucia = CoachCatalog.All.Single(x => x.Code == "LUCIA");
        Assert.Equal(6, lucia.GalleryImages.Distinct().Count());
        Assert.All(lucia.GalleryImages, image =>
        {
            Assert.NotNull(image);
            Assert.Contains("lucia-strength-v1015e-", image);
            Assert.DoesNotContain("#pose=", image);
        });
    }
}
