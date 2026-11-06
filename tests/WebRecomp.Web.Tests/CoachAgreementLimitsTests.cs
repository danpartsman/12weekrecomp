using System.Text.Json;
using WebRecomp.Web.Controllers;
using WebRecomp.Web.Services.Coaching;
using Xunit;

namespace WebRecomp.Web.Tests;

public class CoachAgreementLimitsTests
{
    [Theory]
    [InlineData("actual", 150, 200, 0, true)]
    [InlineData("actual", 200, 150, 0, false)]
    [InlineData("actual", 1800, 2300, 3, true)]
    [InlineData("actual", 0, 30001, 3, false)]
    [InlineData("system-percent", 5, 10, 0, true)]
    [InlineData("system-percent", 20, 5, 0, true)]
    [InlineData("system-percent", 101, 10, 0, false)]
    [InlineData("system-percent", -1, 10, 0, false)]
    [InlineData("system-percent", 0, 1001, 0, false)]
    [InlineData("dollars", 5, 10, 0, false)]
    public void ValidatesNutritionCriteria(string mode, int lower, int upper, int index, bool valid)
    {
        Assert.Equal(valid, CoachAgreementLimits.Validate(mode, lower, upper, index) is null);
    }

    [Fact]
    public void RequiresBothNutritionBoundsAndKeepsCompletionCriteriaSeparate()
    {
        Assert.NotNull(CoachAgreementLimits.Validate("actual", null, 200, 0));
        Assert.Null(CoachAgreementLimits.Validate("event", null, null, 4));
        Assert.NotNull(CoachAgreementLimits.Validate("actual", 0, 2, 4));
    }

    [Fact]
    public void OldAcceptedAgreementRetainsDollarCapsAndLegacyCriterion()
    {
        const string json = """{"Name":"Protein","Enabled":true,"Frequency":"daily","Amount":5,"Minimum":2,"Maximum":20,"Criterion":"Existing criterion"}""";
        var rule = JsonSerializer.Deserialize<CoachFinancialRule>(json)!;
        Assert.Equal(2m, rule.Minimum);
        Assert.Equal(20m, rule.Maximum);
        Assert.Equal("prescription", rule.LimitMode);
        Assert.Contains("Coach nutrition minimum/maximum", CoachAgreementLimits.Describe(rule, 0));
    }

    [Fact]
    public void SavedCriteriaShowUnitsOrPercentagesInsteadOfDollars()
    {
        var rule = new CoachFinancialRule("Protein", true, "daily", 5, 0, 0, "Outside range", "actual", 150, 200);
        Assert.Equal("150–200 g per day", CoachAgreementLimits.Describe(rule, 0));
        Assert.Equal("150–200 kcal per day", CoachAgreementLimits.Describe(rule, 3));
        var percent = rule with { LimitMode = "system-percent", LowerLimit = 5, UpperLimit = 10 };
        var roundTrip = JsonSerializer.Deserialize<CoachFinancialRule>(JsonSerializer.Serialize(percent))!;
        Assert.Equal("5% under to 10% over the system's daily prescription", CoachAgreementLimits.Describe(roundTrip, 0));
        Assert.Equal(5m, roundTrip.Amount);
    }
}
