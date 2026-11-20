using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;

namespace WebRecomp.Web.Controllers;

public sealed class CoachTaskInput
{
    public Guid RequestId { get; set; }
    [Required, StringLength(120)] public string? Title { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    [Required] public string Billing { get; set; } = "fixed";
    [StringLength(40)] public string? Unit { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    [Required] public decimal? Rate { get; set; } = 0;
    [Required] public decimal? LateFee { get; set; } = 0;
    [Required] public DateTime? DueLocal { get; set; }
}

public sealed record CoachTasksPage(CoachTaskParty Party, bool IsCoach, CoachTaskInput Input,
    IReadOnlyList<CoachTaskAssignment> Tasks, DateTimeOffset Now);

[Authorize, Route("coach-workspace/{clientId:guid}/{coachId:guid}/tasks")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CoachTasksController(AppUserContextService users, CoachTaskService tasks, IConfiguration configuration) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid clientId, Guid coachId, CancellationToken ct)
    {
        var access = await AccessAsync(clientId, coachId, ct);
        if (access is null) return NotFound();
        return View(await PageAsync(access.Value.Party, access.Value.IsCoach, new() { RequestId = Guid.NewGuid() }, ct));
    }

    [HttpPost("assign"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(Guid clientId, Guid coachId, [Bind(Prefix = "Input")] CoachTaskInput input,
        CancellationToken ct)
    {
        var access = await AccessAsync(clientId, coachId, ct);
        if (access is null) return NotFound();
        if (!access.Value.IsCoach) return Forbid();
        if (ModelState.IsValid && input.DueLocal.HasValue && input.Rate.HasValue && input.LateFee.HasValue)
        {
            try
            {
                await tasks.AssignAsync(access.Value.Party, input.RequestId, input.Title, input.Notes, input.Billing,
                    input.Unit, input.Minimum, input.Maximum, input.Rate.Value, input.LateFee.Value, input.DueLocal.Value, ct);
                TempData["TaskStatus"] = "Task assigned.";
                return RedirectToAction(nameof(Index), new { clientId, coachId });
            }
            catch (ArgumentException error) { ModelState.AddModelError("", error.Message); }
            catch (AccountabilityConflictException) { ModelState.AddModelError("", "The account changed. Submit the task again."); }
        }
        return View("Index", await PageAsync(access.Value.Party, true, input, ct));
    }

    [HttpPost("{id:guid}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid clientId, Guid coachId, Guid id, int version, string command,
        decimal? quantity, CancellationToken ct)
    {
        var access = await AccessAsync(clientId, coachId, ct);
        if (access is null) return NotFound();
        if (access.Value.IsCoach) return Forbid();
        try
        {
            if (!ModelState.IsValid) throw new ArgumentException("Enter a valid task quantity.");
            if (!await tasks.UpdateAsync(access.Value.Party, id, version, command, quantity, ct)) return NotFound();
            TempData["TaskStatus"] = command switch
            {
                "start" => "Task clock started.",
                "stop" => "Task clock stopped.",
                _ => "Task completed."
            };
        }
        catch (ArgumentException error)
        {
            TempData["TaskError"] = error.Message;
            return RedirectToAction(nameof(Index), new { clientId, coachId });
        }
        catch (AccountabilityConflictException)
        {
            TempData["TaskError"] = "The task changed. Review its latest status and try again.";
            return RedirectToAction(nameof(Index), new { clientId, coachId });
        }
        return RedirectToAction(nameof(Index), null, new { clientId, coachId }, $"task-{id:N}");
    }

    private async Task<(CoachTaskParty Party, bool IsCoach)?> AccessAsync(Guid clientId, Guid coachId, CancellationToken ct)
    {
        if (!SimpleCoaching.Enabled(configuration)) return null;
        var user = (await users.GetAsync(HttpContext, ct)).User;
        var party = await tasks.PartyAsync(clientId, coachId, ct);
        if (party is null || (user.UserId != party.OwnerId && user.UserId != party.CoachId)) return null;
        return (party, user.UserId == party.CoachId);
    }

    private async Task<CoachTasksPage> PageAsync(CoachTaskParty party, bool isCoach, CoachTaskInput input, CancellationToken ct) =>
        new(party, isCoach, input, await tasks.ReadAsync(party, ct), DateTimeOffset.UtcNow);
}
