using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Notifications;

namespace WebRecomp.Web.Services.Coaching;

public sealed class AvailabilityCalendar
{
    public int Revision { get; set; }
    public List<AvailabilityBlock> Blocks { get; set; } = [];
}

public sealed class AvailabilityBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "work";
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool AllDay { get; set; }
    public List<DayOfWeek> Days { get; set; } = [];
    public Guid? PlannedWorkoutId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? CancelledUtc { get; set; }
}

public sealed record AvailabilityWindow(DateTimeOffset Start, DateTimeOffset End, string Reason);
public sealed record SnapTiming(DateTimeOffset? DeadlineUtc, DateTimeOffset? AvailableFromUtc,
    DateTimeOffset? PausedUntilUtc, double RemainingSeconds, string? Reason);

public static class SnapAvailabilityClock
{
    public static TimeZoneInfo Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    // A repeated fall-back hour is fully protected; nonexistent spring times move forward.
    public static DateTimeOffset Utc(DateOnly date, TimeOnly time, TimeZoneInfo zone, bool end = false)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local)
            ? (end ? zone.GetAmbiguousTimeOffsets(local).Min() : zone.GetAmbiguousTimeOffsets(local).Max())
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public static List<AvailabilityWindow> Expand(IEnumerable<AvailabilityBlock> blocks, TimeZoneInfo zone,
        DateOnly from, DateOnly through)
    {
        var output = new List<AvailabilityWindow>();
        foreach (var block in blocks)
        for (var date = from.AddDays(-1); date <= through; date = date.AddDays(1))
        {
            if (date < block.StartDate || (block.EndDate.HasValue && date > block.EndDate)) continue;
            if (block.AllDay ? date > (block.EndDate ?? block.StartDate)
                : block.Days.Count == 0 ? date != block.StartDate : !block.Days.Contains(date.DayOfWeek)) continue;
            var start = Utc(date, block.AllDay ? TimeOnly.MinValue : block.StartTime, zone);
            var end = Utc(block.AllDay || block.EndTime <= block.StartTime ? date.AddDays(1) : date,
                block.AllDay ? TimeOnly.MinValue : block.EndTime, zone, end: true);
            if (block.Kind != "illness" && start < block.CreatedUtc) start = block.CreatedUtc;
            if (block.CancelledUtc is { } cancelled && end > cancelled) end = cancelled;
            if (end > start) output.Add(new(start, end, Label(block.Kind)));
        }
        return output;
    }

    public static string Label(string kind) => kind switch
    { "sleep" => "Sleep", "work" => "Work", "workout" => "Planned workout", "illness" => "Illness", _ => "Unavailable" };

    public static List<AvailabilityWindow> Merge(IEnumerable<AvailabilityWindow> windows)
    {
        var output = new List<AvailabilityWindow>();
        foreach (var window in windows.Where(w => w.End > w.Start).OrderBy(w => w.Start))
        {
            if (output.Count == 0 || output[^1].End < window.Start) output.Add(window);
            else
            {
                var old = output[^1];
                output[^1] = old with { End = old.End > window.End ? old.End : window.End,
                    Reason = old.Reason == window.Reason ? old.Reason : "Unavailable" };
            }
        }
        return output;
    }

    public static SnapTiming Calculate(DateTimeOffset start, int minutes, DateTimeOffset now,
        IEnumerable<AvailabilityWindow> windows, DateTimeOffset horizon)
    {
        var merged = Merge(windows);
        DateTimeOffset? Next(DateTimeOffset point)
        {
            foreach (var window in merged)
                if (window.Start <= point && point < window.End) point = window.End;
            return point < horizon ? point : null;
        }
        var cursor = start;
        var remaining = TimeSpan.FromMinutes(minutes);
        DateTimeOffset? deadline = null;
        foreach (var window in merged.Where(w => w.End > start))
        {
            var free = window.Start > cursor ? window.Start - cursor : TimeSpan.Zero;
            if (free >= remaining) { deadline = Next(cursor + remaining); break; }
            remaining -= free;
            if (window.End > cursor) cursor = window.End;
        }
        if (!deadline.HasValue && cursor + remaining < horizon) deadline = Next(cursor + remaining);
        var until = now > start ? now : start;
        var elapsed = until - start;
        foreach (var window in merged)
        {
            var left = window.Start > start ? window.Start : start;
            var right = window.End < until ? window.End : until;
            if (right > left) elapsed -= right - left;
        }
        var seconds = Math.Max(0, minutes * 60 - elapsed.TotalSeconds);
        var blocked = merged.FirstOrDefault(w => w.Start <= now && now < w.End);
        // A deadline moved off a block boundary remains paused even with zero minutes left.
        var paused = (deadline is null || deadline > now) && blocked is not null;
        var available = Next(start);
        return new(deadline, available, paused ? blocked!.End : null, seconds,
            now < start ? "Scheduled" : paused ? blocked!.Reason : deadline is null ? "No available time in the next year" : null);
    }
}

public sealed class SnapAvailabilityService(RecompDbContext db, NotificationFiles files, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private static string Key(Guid client) => $"users/availability-{client:N}";
    public AvailabilityCalendar Read(Guid client) => files.Read<AvailabilityCalendar>(Key(client)) ?? new();

    public void Add(Guid client, int revision, AvailabilityBlock block, Guid? replaceId = null)
    {
        if (block.Kind is not ("sleep" or "work" or "workout" or "illness")
            || block.StartDate.Year is < 2020 or > 2100 || block.EndDate < block.StartDate
            || block.Days.Any(day => !Enum.IsDefined(day)) || block.Days.Distinct().Count() != block.Days.Count
            || (block.AllDay && block.Kind != "illness") || (!block.AllDay && block.StartTime == block.EndTime)
            || (block.Kind == "illness" && !block.AllDay))
            throw new ArgumentException("Check the dates, times and days of the week.");
        using var lease = files.Lease(Key(client));
        var calendar = Read(client);
        if (calendar.Revision != revision) throw new AccountabilityConflictException();
        if (calendar.Blocks.Count >= 1000) throw new ArgumentException("The calendar has reached its block limit.");
        if (replaceId.HasValue)
        {
            var previous = calendar.Blocks.SingleOrDefault(b => b.Id == replaceId && b.CancelledUtc == null)
                ?? throw new ArgumentException("This schedule changed. Reload before editing.");
            previous.CancelledUtc = time.GetUtcNow();
        }
        block.CreatedUtc = time.GetUtcNow();
        calendar.Blocks.Add(block); calendar.Revision++;
        files.Write(Key(client), calendar);
    }

    public void Remove(Guid client, int revision, Guid id)
    {
        using var lease = files.Lease(Key(client));
        var calendar = Read(client);
        if (calendar.Revision != revision) throw new AccountabilityConflictException();
        var block = calendar.Blocks.SingleOrDefault(b => b.Id == id && b.CancelledUtc == null)
            ?? throw new ArgumentException("This block has already been removed.");
        // Preserve elapsed protected time when a recurring schedule changes.
        block.CancelledUtc = time.GetUtcNow(); calendar.Revision++;
        files.Write(Key(client), calendar);
    }

    public async Task<List<AvailabilityWindow>> WindowsAsync(Client client, DateOnly from, DateOnly through, CancellationToken ct)
    {
        var calendar = Read(client.ClientId);
        var plannedIds = calendar.Blocks.Where(b => b.PlannedWorkoutId.HasValue).Select(b => b.PlannedWorkoutId!.Value).ToArray();
        var activePlans = (await db.PlannedWorkouts.AsNoTracking().Where(p => p.ClientId == client.ClientId
            && plannedIds.Contains(p.PlannedWorkoutId) && p.WorkoutTypeCode != "REST")
            .Select(p => new { p.PlannedWorkoutId, p.PlannedDate }).ToListAsync(ct))
            .ToDictionary(p => p.PlannedWorkoutId, p => p.PlannedDate);
        var blocks = calendar.Blocks.Where(b => !b.PlannedWorkoutId.HasValue
            || activePlans.GetValueOrDefault(b.PlannedWorkoutId.Value) == b.StartDate);
        var zone = SnapAvailabilityClock.Zone(client.TimeZone);
        var windows = SnapAvailabilityClock.Expand(blocks, zone, from, through);
        var illness = await db.DailyCheckins.AsNoTracking().Where(d => d.ClientId == client.ClientId
            && d.CheckinDate >= from.AddDays(-1) && d.CheckinDate <= through && d.IllnessFlag)
            .Select(d => d.CheckinDate).Distinct().ToListAsync(ct);
        windows.AddRange(illness.Select(date => new AvailabilityWindow(
            SnapAvailabilityClock.Utc(date, TimeOnly.MinValue, zone),
            SnapAvailabilityClock.Utc(date.AddDays(1), TimeOnly.MinValue, zone, true), "Illness")));
        return windows;
    }

    public async Task ApplyAsync(Client client, AccountabilityState state, CancellationToken ct)
    {
        var workouts = state.SnapWorkouts.Where(w => w.Simple && w.CreatedAtUtc.HasValue).ToArray();
        if (workouts.Length == 0) return;
        var now = time.GetUtcNow();
        var zone = SnapAvailabilityClock.Zone(client.TimeZone);
        var from = workouts.Min(w => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(w.CreatedAtUtc!.Value, zone).DateTime));
        var through = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime).AddDays(366);
        var windows = await WindowsAsync(client, from, through, ct);
        var horizon = SnapAvailabilityClock.Utc(through.AddDays(1), TimeOnly.MinValue, zone);
        foreach (var workout in workouts)
        {
            // Retain a stable anchor so repeated refreshes never add blocked time twice.
            var minutes = workout.TimeLimitMinutes ?? (workout.DeadlineUtc.HasValue
                ? Math.Clamp((int)(workout.DeadlineUtc.Value - workout.CreatedAtUtc!.Value).TotalMinutes, 1, 10080) : 120);
            workout.TimingStartUtc ??= workout.DeadlineUtc?.AddMinutes(-minutes) ?? workout.CreatedAtUtc;
            workout.TimeLimitMinutes = minutes;
            var effectiveNow = state.SnapResults.GetValueOrDefault(workout.Id) is { Finalized: true, CompletedAtUtc: { } completed }
                ? completed : now;
            workout.Timing = SnapAvailabilityClock.Calculate(workout.TimingStartUtc!.Value, minutes, effectiveNow, windows, horizon);
            workout.DeadlineUtc = workout.Timing.DeadlineUtc;
        }
    }
}
