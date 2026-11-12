using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;

namespace WebRecomp.Web.Controllers;

public sealed record AvailabilityPage(Guid ClientId, string ClientName, string TimeZone, bool CanEdit,
    DateOnly Week, AvailabilityCalendar Calendar, IReadOnlyList<AvailabilityWindow> Windows,
    IReadOnlyList<PlannedWorkout> Plans)
{
    public AvailabilityBlock? EditBlock { get; init; }
    public IReadOnlyDictionary<DateOnly,string> PlanNames { get; init; } = new Dictionary<DateOnly,string>();
}

[Authorize, Route("availability"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AvailabilityController(AppUserContextService users, RecompDbContext db,
    CoachAccountabilityPreviewService access, SnapAvailabilityService availability,
    SimpleCoachingService simple, WeeklyPlanService? weeklyPlans = null) : Controller
{
    private async Task<(Client Client, Guid Actor, bool Owner)?> Resolve(Guid? clientId, CancellationToken ct)
    {
        var context = await users.GetAsync(HttpContext, ct);
        var id = clientId ?? context.Client?.ClientId;
        if (!id.HasValue) return null;
        var owner = await access.IsOwnerAsync(context.User.UserId, id.Value, ct);
        if (!owner && !await access.CanAssignSnapAsync(context.User.UserId, id.Value, ct)) return null;
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(c => c.ClientId == id && c.ClientStatusCode == "ACTIVE", ct);
        return client is null ? null : (client, context.User.UserId, owner);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(Guid? clientId, DateOnly? week, CancellationToken ct, Guid? editId = null)
    {
        var resolved = await Resolve(clientId, ct);
        if (resolved is not { } auth) return NotFound();
        var today = AppUserContextService.GetClientToday(auth.Client);
        var date = week ?? today;
        if (date < today.AddDays(-84) || date > today.AddDays(366)) return BadRequest("Choose a week within the next year or the past 12 weeks.");
        var monday = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        var through = monday.AddDays(6);
        var calendar = availability.Read(auth.Client.ClientId);
        var edit = editId.HasValue ? calendar.Blocks.SingleOrDefault(b => b.Id == editId && b.CancelledUtc == null) : null;
        if (editId.HasValue && (!auth.Owner || edit is null)) return NotFound();
        var program = await db.Programs.AsNoTracking().Where(p => p.ClientId == auth.Client.ClientId && p.ProgramStatusCode == "ACTIVE")
            .OrderByDescending(p => p.StartDate).FirstOrDefaultAsync(ct);
        if (program is not null && weeklyPlans is not null && monday == PlanningWeek.Start(today))
            await weeklyPlans.EnsureCurrentWeekAsync(auth.Client, program, today, ct);
        var programId = program?.ProgramId;
        var plans = await db.PlannedWorkouts.AsNoTracking().Where(p => p.ClientId == auth.Client.ClientId && p.ProgramId == programId
            && p.PlannedDate >= monday && p.PlannedDate <= through)
            .OrderBy(p => p.PlannedDate).ToListAsync(ct);
        return View(new AvailabilityPage(auth.Client.ClientId, auth.Client.ClientName, auth.Client.TimeZone, auth.Owner,
            monday, calendar,
            await availability.WindowsAsync(auth.Client, monday, through, ct), plans)
            { EditBlock = edit, PlanNames = await PlannedWorkoutNames.LoadAsync(db, auth.Client.ClientId, plans, ct) });
    }

    [HttpPost("block"), ValidateAntiForgeryToken, RequestSizeLimit(16384)]
    public async Task<IActionResult> Add(Guid clientId, BlockInput input, CancellationToken ct)
    {
        var resolved = await Resolve(clientId, ct);
        if (resolved is not { } auth) return NotFound();
        if (!auth.Owner) return Forbid();
        if (!ModelState.IsValid) return BadRequest("Enter valid dates and times for this block.");
        try
        {
            var today = AppUserContextService.GetClientToday(auth.Client);
            var original = input.Id.HasValue ? availability.Read(clientId).Blocks.SingleOrDefault(b => b.Id == input.Id && b.CancelledUtc == null) : null;
            if (input.Id.HasValue && original is null) throw new ArgumentException("This schedule changed. Reload before editing.");
            var keepingStart = original is not null && input.StartDate == original.StartDate;
            if ((!keepingStart && input.StartDate < today.AddDays(input.Kind == "illness" ? -14 : 0))
                || input.StartDate > today.AddDays(366) || input.EndDate > today.AddDays(366))
                throw new ArgumentException("Use a date within the next year. Illness can also be recorded for the past 14 days.");
            if (input.Kind == "illness" && input.Repeat) throw new ArgumentException("Use a date range for illness.");
            if (input.Repeat && input.Days.Count == 0) throw new ArgumentException("Choose at least one repeat day.");
            if (input.PlannedWorkoutId.HasValue)
            {
                var plan = await db.PlannedWorkouts.AsNoTracking().SingleOrDefaultAsync(p => p.ClientId == clientId
                    && p.PlannedWorkoutId == input.PlannedWorkoutId && p.WorkoutTypeCode != "REST", ct);
                if (plan is null || plan.PlannedDate != input.StartDate || input.Kind != "workout" || input.Repeat)
                    throw new ArgumentException("Choose a workout from this client's plan.");
            }
            availability.Add(clientId, input.Revision, new()
            {
                Kind = input.Kind, StartDate = input.StartDate, EndDate = input.Kind == "illness" || input.Repeat ? input.EndDate : null,
                StartTime = input.StartTime, EndTime = input.EndTime, AllDay = input.Kind == "illness",
                Days = input.Repeat ? input.Days : [], PlannedWorkoutId = input.PlannedWorkoutId
            }, input.Id);
            // Reconcile any late fees already posted before an illness/check-in correction.
            if (await access.CanViewAsync(auth.Actor, clientId, false, ct))
                await simple.RefreshAsync(auth.Actor, clientId, false, ct);
            TempData["AvailabilityStatus"] = input.Id.HasValue ? "Schedule updated." : "Protected time saved. Snap completion windows now follow your calendar.";
        }
        catch (Exception e) when (e is ArgumentException or AccountabilityConflictException)
        { TempData["AvailabilityStatus"] = e.Message; }
        return RedirectToAction(nameof(Index), new { clientId, week = input.Id.HasValue ? null : input.StartDate.ToString("yyyy-MM-dd") });
    }

    [HttpPost("remove"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(Guid clientId, int revision, Guid id, CancellationToken ct)
    {
        var resolved = await Resolve(clientId, ct);
        if (resolved is not { } auth) return NotFound();
        if (!auth.Owner) return Forbid();
        try
        {
            availability.Remove(clientId, revision, id);
            if (await access.CanViewAsync(auth.Actor, clientId, false, ct))
                await simple.RefreshAsync(auth.Actor, clientId, false, ct);
            TempData["AvailabilityStatus"] = "Schedule deleted. Earlier protected time is retained.";
        }
        catch (Exception e) when (e is ArgumentException or AccountabilityConflictException)
        { TempData["AvailabilityStatus"] = e.Message; }
        return RedirectToAction(nameof(Index), new { clientId });
    }

    public sealed class BlockInput
    {
        public Guid? Id { get; set; }
        public int Revision { get; set; }
        [Required] public string Kind { get; set; } = "work";
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public bool Repeat { get; set; }
        public List<DayOfWeek> Days { get; set; } = [];
        public Guid? PlannedWorkoutId { get; set; }
    }
}
