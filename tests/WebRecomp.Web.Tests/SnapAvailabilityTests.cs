using WebRecomp.Web.Services.Coaching;

namespace WebRecomp.Web.Tests;

public sealed class SnapAvailabilityTests
{
    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.Zero);
    private static SnapTiming Clock(DateTimeOffset start, int minutes, DateTimeOffset now, params AvailabilityWindow[] blocks) =>
        SnapAvailabilityClock.Calculate(start, minutes, now, blocks, At(30, 0));

    [Fact]
    public void TwoHoursAssignedDuringWorkStartsAfterWorkThenPausesForSleep()
    {
        var timing = Clock(At(12, 17), 120, At(12, 18),
            new AvailabilityWindow(At(12, 16), At(12, 20), "Work"), new AvailabilityWindow(At(12, 21), At(13, 6), "Sleep"));
        Assert.Equal(At(12, 20), timing.AvailableFromUtc);
        Assert.Equal(At(12, 20), timing.PausedUntilUtc);
        Assert.Equal(At(13, 7), timing.DeadlineUtc);
        Assert.Equal(7200, timing.RemainingSeconds);
    }

    [Fact]
    public void OverlappingAndAdjacentBlocksDoNotDoubleCountProtectedTime()
    {
        var timing = Clock(At(12, 10), 120, At(12, 11),
            new AvailabilityWindow(At(12, 10, 30), At(12, 12), "Work"),
            new AvailabilityWindow(At(12, 11), At(12, 13), "Workout"), new AvailabilityWindow(At(12, 13), At(12, 14), "Work"));
        Assert.Equal(At(12, 15, 30), timing.DeadlineUtc);
        Assert.Equal(5400, timing.RemainingSeconds);
        Assert.Equal(At(12, 14), timing.PausedUntilUtc);
    }

    [Fact]
    public void IllnessProtectsWholeDaysAndDoesNotEraseEarlierLateness()
    {
        var illness = new AvailabilityWindow(At(12, 0), At(14, 0), "Illness");
        Assert.Equal(At(14, 2), Clock(At(12, 10), 120, At(13, 12), illness).DeadlineUtc);
        Assert.Equal(At(11, 12), Clock(At(11, 10), 120, At(13, 12), illness).DeadlineUtc);
    }

    [Fact]
    public void ADeadlineAtTheStartOfBlockedTimeMovesToAvailableTime()
    {
        var timing = Clock(At(12, 10), 60, At(12, 11), new AvailabilityWindow(At(12, 11), At(12, 13), "Work"));
        Assert.Equal(At(12, 13), timing.DeadlineUtc);
        Assert.Equal(At(12, 13), timing.PausedUntilUtc);
    }

    [Fact]
    public void NoAvailableTimeDefersDeadlineWithoutALatePenalty()
    {
        var timing = Clock(At(12, 10), 120, At(12, 11), new AvailabilityWindow(At(12, 0), At(30, 0), "Unavailable"));
        Assert.Null(timing.DeadlineUtc);
        Assert.Null(timing.AvailableFromUtc);
        Assert.Equal(7200, timing.RemainingSeconds);
    }

    [Fact]
    public void OvernightRecurrenceUsesTheDayTheShiftStartsAndPreservesPastTimeWhenRemoved()
    {
        var date = new DateOnly(2026, 11, 9); // Monday
        var block = new AvailabilityBlock { Kind = "work", StartDate = date, Days = [DayOfWeek.Monday],
            StartTime = new(22, 0), EndTime = new(6, 0), CreatedUtc = At(9, 12), CancelledUtc = At(10, 2) };
        var window = Assert.Single(SnapAvailabilityClock.Expand([block], TimeZoneInfo.Utc, date.AddDays(1), date.AddDays(7)));
        Assert.Equal(At(9, 22), window.Start);
        Assert.Equal(At(10, 2), window.End);
    }

    [Fact]
    public void IllnessRangeIncludesAllSelectedDates()
    {
        var date = new DateOnly(2026, 11, 12);
        var block = new AvailabilityBlock { Kind = "illness", AllDay = true, StartDate = date,
            EndDate = date.AddDays(2), CreatedUtc = At(12, 20) };
        var windows = SnapAvailabilityClock.Expand([block], TimeZoneInfo.Utc, date, date.AddDays(3));
        var combined = Assert.Single(SnapAvailabilityClock.Merge(windows));
        Assert.Equal(At(12, 0), combined.Start);
        Assert.Equal(At(15, 0), combined.End);
    }

    [Fact]
    public void FallBackSleepProtectsBothRepeatedHoursAndSpringForwardSkipsMissingHour()
    {
        var zone = SnapAvailabilityClock.Zone("America/Los_Angeles");
        var fall = new AvailabilityBlock { Kind = "sleep", StartDate = new(2026, 11, 1), StartTime = new(1, 0), EndTime = new(2, 0) };
        var fallWindow = Assert.Single(SnapAvailabilityClock.Expand([fall], zone, fall.StartDate, fall.StartDate));
        Assert.Equal(2, (fallWindow.End - fallWindow.Start).TotalHours);
        var spring = new AvailabilityBlock { Kind = "sleep", StartDate = new(2026, 3, 8), StartTime = new(1, 0), EndTime = new(4, 0) };
        var springWindow = Assert.Single(SnapAvailabilityClock.Expand([spring], zone, spring.StartDate, spring.StartDate));
        Assert.Equal(2, (springWindow.End - springWindow.Start).TotalHours);
    }
}
