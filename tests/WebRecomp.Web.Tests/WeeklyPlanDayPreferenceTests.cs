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
    [Theory]
    [InlineData("PLAN_1")]
    [InlineData("PLAN_2")]
    [InlineData("PLAN_3")]
    [InlineData("PLAN_4")]
    [InlineData("PLAN_5")]
    public void StartingWeeksSeparateHardDaysWhenPossible(string plan)
    {
        var service = new WeeklyPlanService(null!, null!);
        for (byte days = 1; days <= 6; days++)
        {
            var template = service.BuildRecommendedTemplate(plan, days, "INTERMEDIATE");
            Assert.Equal(7, template.Count);
            Assert.DoesNotContain(Enumerable.Range(0, 7),
                day => Hard(template[day]) && Hard(template[(day + 1) % 7]));
        }
    }

    [Fact]
    public void DayPreferencesKeepRecoveryBetweenHardDaysWhenFeasible()
    {
        string[] template = ["MAJOR_LEGS", "REST", "MAJOR_PUSH", "ZONE2_CARDIO", "MAJOR_PULL", "REST", "REST"];
        var actual = WeeklyPlanService.ApplyDayPreferences(template, [1, 4], [2]);

        Assert.Equal("REST", actual[2]);
        Assert.NotEqual("REST", actual[1]);
        Assert.NotEqual("REST", actual[4]);
        Assert.Equal(template.Order(), actual.Order());
        Assert.DoesNotContain(Enumerable.Range(0, 7),
            day => Hard(actual[day]) && Hard(actual[(day + 1) % 7]));
    }

    private static bool Hard(string code) => code is
        "MAJOR_LEGS" or "MAJOR_PUSH" or "MAJOR_PULL" or
        "FULL_BODY_STRENGTH" or "TRUE_HIIT" or "ORANGETHEORY";

}
