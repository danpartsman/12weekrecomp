using System.Globalization;
using WebRecomp.Web.Controllers;

namespace WebRecomp.Web.Services.Coaching;

public static class CoachAgreementLimits
{
    public static string Unit(int ruleIndex) => ruleIndex == 3 ? "kcal" : "g";

    public static string? Validate(string mode, decimal? lower, decimal? upper, int ruleIndex)
    {
        if (ruleIndex >= 4)
            return mode == "event" && lower is null && upper is null ? null : "This rule uses the stated completion criterion.";
        if (mode is not ("actual" or "system-percent"))
            return "Choose actual values or percentages under/over the system prescription.";
        if (lower is null || upper is null || lower < 0 || upper < 0)
            return "Enter nonnegative lower and upper limits.";
        if (mode == "actual")
        {
            var maximum = ruleIndex == 3 ? 30000m : 5000m;
            return lower > upper || upper > maximum ? "Actual minimum must not exceed maximum, and values must be within range." : null;
        }
        return lower > 100 || upper > 1000 ? "Percent under must be 0–100; percent over must be 0–1000." : null;
    }

    public static string Describe(CoachFinancialRule rule, int ruleIndex)
    {
        static string Number(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—";
        if (ruleIndex >= 4) return rule.Criterion;
        return rule.LimitMode switch
        {
            "actual" => $"{Number(rule.LowerLimit)}–{Number(rule.UpperLimit)} {Unit(ruleIndex)} per day",
            "system-percent" => $"{Number(rule.LowerLimit)}% under to {Number(rule.UpperLimit)}% over the system's daily prescription",
            _ => "Coach nutrition minimum/maximum; missing bounds use 90%–110% of the system's daily prescription"
        };
    }
}
