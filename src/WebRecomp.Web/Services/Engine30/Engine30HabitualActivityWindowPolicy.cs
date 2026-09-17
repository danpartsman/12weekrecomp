namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Draft Engine 3.0 policy for the weekly background habitual-activity window.
///
/// This is a background Bucket #2 modeling window, not a cap on a person's
/// actual activity.
///
/// TEST v1009 calibration rule:
/// - age 50 or younger: 60 h/week;
/// - after age 50: subtract 1 h/week per year;
/// - age 80 and older: floor at 30 h/week.
///
/// Actual occupational hours are always counted in full. The window only
/// determines inferred home-equivalent hours:
///
///     max(0, AgeAdjustedWindowHours - ActualWorkHours)
///
/// Separately reported purposeful activity remains separate evidence/activity
/// and is not silently absorbed into this background allowance.
/// </summary>
public static class Engine30HabitualActivityWindowPolicy
{
    public const int FullWindowThroughAgeYears = 50;
    public const int MinimumWindowAtAgeYears = 80;

    public const decimal FullWindowHoursPerWeek = 60m;
    public const decimal ReductionHoursPerYearAfter50 = 1m;
    public const decimal MinimumWindowHoursPerWeek = 30m;

    public const string ModelCode =
        "AGE_ADJUSTED_60_TO_30H_WORK_HOME_PLUS_LIFESTYLE_PREMIUM";

    public static Engine30HabitualActivityWindow Resolve(
        int ageYears)
    {
        if (ageYears < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ageYears));
        }

        decimal hours;

        if (ageYears <= FullWindowThroughAgeYears)
        {
            hours = FullWindowHoursPerWeek;
        }
        else
        {
            var yearsAfter50 =
                ageYears - FullWindowThroughAgeYears;

            hours =
                FullWindowHoursPerWeek
                - (yearsAfter50 * ReductionHoursPerYearAfter50);

            hours =
                Math.Max(
                    MinimumWindowHoursPerWeek,
                    hours);
        }

        return new Engine30HabitualActivityWindow(
            AgeYears: ageYears,
            HoursPerWeek: Round(hours),
            ModelCode: ModelCode);
    }

    private static decimal Round(decimal value)
        => Math.Round(
            value,
            2,
            MidpointRounding.AwayFromZero);
}

public sealed record Engine30HabitualActivityWindow(
    int AgeYears,
    decimal HoursPerWeek,
    string ModelCode);