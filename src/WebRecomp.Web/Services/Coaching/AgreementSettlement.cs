using WebRecomp.Web.Controllers;
using WebRecomp.Web.Models;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Services.Coaching;

public sealed record CoachFinancialReview(bool Excused, int? CheatFoods);

internal sealed record AgreementDay(
    NutritionDay? Nutrition, CoachSummaryPrescription? Target, bool NutritionComplete, bool HasScaleOrRecovery,
    bool WorkoutPlanned, bool WorkoutCompleted, bool Excused, int? ReviewedCheatFoods);

internal sealed record AgreementPeriod(
    CoachFinancialAgreement Agreement, int RuleIndex, DateOnly Start, DateOnly End);

internal static class AgreementSettlement
{
    public static IReadOnlyList<CoachFinancialAgreement> AcceptedVersions(CoachFinancialAgreement agreement) =>
        (agreement.AcceptedHistory ?? []).Concat(agreement.Status == "accepted"
            ? [agreement with { AcceptedHistory = null, Reviews = null }]
            : []).ToArray();

    public static IReadOnlyList<AgreementPeriod> DuePeriods(
        IEnumerable<CoachFinancialAgreement> versions, Guid owner, TimeZoneInfo zone,
        DateOnly firstEligible, DateOnly earliestClose, DateOnly closedThrough)
    {
        DateOnly EffectiveDay(CoachFinancialAgreement version) =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(version.AcceptedUtc!.Value, DateTimeKind.Utc), zone)).AddDays(1);

        var accepted = versions.Where(v => v.Status == "accepted" && v.AcceptedBy == owner
            && v.AcceptedUtc.HasValue && v.Rules.Count == 10)
            .OrderBy(EffectiveDay).ThenBy(v => v.Revision).ToArray();
        var periods = new List<AgreementPeriod>();
        for (var date = earliestClose; date <= closedThrough; date = date.AddDays(1))
        {
            var version = accepted.LastOrDefault(v => EffectiveDay(v) <= date);
            if (version is null) continue;
            for (var i = 0; i < version.Rules.Count; i++)
            {
                var rule = version.Rules[i];
                if (!rule.Enabled || rule.Amount <= 0 || rule.Frequency is not ("daily" or "weekly" or "monthly")) continue;
                var (start, end) = AccountabilityAssessment.Period(date, rule.Frequency);
                if (end == date && start >= firstEligible && start >= EffectiveDay(version))
                    periods.Add(new(version, i, start, end));
            }
        }
        return periods;
    }

    public static AccountabilityCharge? Assess(Guid coach, AgreementPeriod period, IReadOnlyList<AgreementDay> evidence)
    {
        var days = evidence.Where(day => !day.Excused).ToArray();
        var rule = period.Agreement.Rules[period.RuleIndex];
        int? occurrences = days.Length == 0 ? 0 : period.RuleIndex switch
        {
            < 4 => AnyViolation(days.Select(day => Outside(day, rule, period.RuleIndex))),
            4 => days.Any(day => day.WorkoutPlanned && !day.WorkoutCompleted) ? 1 : 0,
            5 => days.Any(day => !day.NutritionComplete) ? 1 : 0,
            6 => days.Any(day => !day.HasScaleOrRecovery) ? 1 : 0,
            7 => days.Sum(day => day.ReviewedCheatFoods ?? 0),
            8 => NutritionCompliance(period.Agreement, days),
            9 => days.Any(day => day.WorkoutPlanned)
                && days.All(day => !day.WorkoutPlanned || day.WorkoutCompleted) ? 1 : 0,
            _ => null
        };
        if (occurrences is null) return null;
        var raw = AccountabilityAssessment.Cents(rule.Amount * occurrences.Value);
        var capped = rule.Maximum > 0 ? Math.Min(raw, rule.Maximum) : raw;
        var amount = capped >= rule.Minimum ? AccountabilityAssessment.Cents(capped) : 0;
        var key = $"agreement:{coach:N}:{period.RuleIndex}:{period.Start:yyyy-MM-dd}:{period.End:yyyy-MM-dd}";
        var dates = period.Start == period.End ? $"{period.End:yyyy-MM-dd}" : $"{period.Start:yyyy-MM-dd} to {period.End:yyyy-MM-dd}";
        return new(key, period.End, period.RuleIndex >= 8 ? "incentive-client" : "penalty",
            $"{rule.Name} · {dates} · agreement revision {period.Agreement.Revision}", amount, raw);
    }

    private static int? AnyViolation(IEnumerable<bool?> values)
    {
        var results = values.ToArray();
        return results.Contains(true) ? 1 : results.Contains(null) ? null : 0;
    }

    private static int? NutritionCompliance(CoachFinancialAgreement agreement, AgreementDay[] days)
    {
        if (days.Any(day => !day.NutritionComplete)) return 0;
        var limits = Enumerable.Range(0, 4).Where(i => agreement.Rules[i].Enabled).ToArray();
        if (limits.Length == 0) return 0;
        var violations = AnyViolation(days.SelectMany(day => limits.Select(i => Outside(day, agreement.Rules[i], i))));
        return violations is null ? null : violations == 0 ? 1 : 0;
    }

    private static bool? Outside(AgreementDay day, CoachFinancialRule rule, int index)
    {
        if (!day.NutritionComplete) return null;
        var nutrition = day.Nutrition!;
        var actual = index switch
        {
            0 => nutrition.ActualProteinG!.Value,
            1 => nutrition.ActualCarbsG!.Value,
            2 => nutrition.ActualFatG!.Value,
            _ => nutrition.ActualCalories!.Value
        };
        if (rule.LimitMode == "actual" && rule.LowerLimit is decimal lower && rule.UpperLimit is decimal upper)
            return actual < lower || actual > upper;
        if (rule.LimitMode != "system-percent" || day.Target is not { } target) return null;
        var baseline = index switch { 0 => target.ProteinG, 1 => target.CarbsG, 2 => target.FatG, _ => target.Calories };
        if (baseline <= 0 || rule.LowerLimit is not decimal under || rule.UpperLimit is not decimal over) return null;
        return actual < baseline * (1 - under / 100) || actual > baseline * (1 + over / 100);
    }
}
