namespace WebRecomp.Web.Services;

/// <summary>
/// Calendar helper for Monday-based planning weeks.
/// Kept separate from recommendation/recovery services so planning code does not
/// depend on a physiology engine.
/// </summary>
public static class PlanningWeek
{
    public static DateOnly Start(DateOnly date)
    {
        var diff =
            ((int)date.DayOfWeek + 6) % 7; // Monday = 0

        return date.AddDays(-diff);
    }
}
