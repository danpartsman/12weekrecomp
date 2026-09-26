using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class WeeklyPlanDayPreferenceTests
{
    [Fact]
    public void ExplicitTrainingAndRestDaysMoveSessionsWithoutChangingWorkoutMix()
    {
        string[] template = ["MAJOR_LEGS", "REST", "MAJOR_PUSH", "TRUE_HIIT", "MAJOR_PULL", "REST", "REST"];
        var result = WeeklyPlanService.ApplyDayPreferences(template, [5], [3]);

        Assert.Equal("REST", result[3]); // Thursday is an explicit rest day.
        Assert.NotEqual("REST", result[5]); // Saturday is an explicit training day.
        Assert.Equal(template.Where(code => code != "REST").Order(),
            result.Where(code => code != "REST").Order());
        Assert.Equal(4, result.Count(code => code != "REST"));
    }

    [Fact]
    public void NoPreferencesPreserveOriginalWeek()
    {
        string[] template = ["MAJOR_LEGS", "REST", "MAJOR_PUSH", "TRUE_HIIT", "MAJOR_PULL", "REST", "REST"];
        Assert.Equal(template, WeeklyPlanService.ApplyDayPreferences(template, [], []));
    }

    [Fact]
    public void ConflictingPreferencesAreRejected()
    {
        string[] template = ["MAJOR_LEGS", "REST", "MAJOR_PUSH", "TRUE_HIIT", "MAJOR_PULL", "REST", "REST"];
        Assert.Throws<ArgumentException>(() => WeeklyPlanService.ApplyDayPreferences(template, [1], [1]));
    }
}
