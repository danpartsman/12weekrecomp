using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;

namespace WebRecomp.Web.Controllers;

[Authorize, Route("clients/{clientId:guid}/snap/live/{workoutId}"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SnapLiveController(AppUserContextService users, SnapLiveService live, IConfiguration configuration) : Controller
{
    private async Task<IActionResult> Run(Func<Guid, Task<SnapLiveView>> action, CancellationToken ct)
    {
        if (!SimpleCoaching.Enabled(configuration)) return NotFound();
        try { return Json(await action((await users.GetAsync(HttpContext, ct)).User.UserId)); }
        catch (UnauthorizedAccessException) { return NotFound(); }
        catch (AccountabilityConflictException) { return Conflict(new { message = "Progress changed in another tab. Your entries remain here. Reload to compare the saved version before continuing." }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (IOException) { Response.Headers.RetryAfter = "2"; return StatusCode(503, new { message = "Another update is saving. Your entries remain here; try again shortly." }); }
    }
    [HttpGet("")]
    public Task<IActionResult> Snapshot(Guid clientId, string workoutId, CancellationToken ct, long? before = null) =>
        Run(async actor => live.View(await live.AllowedAsync(actor, clientId, workoutId, ct), before), ct);
    [HttpPost("start"), ValidateAntiForgeryToken, EnableRateLimiting(SnapRequestRateLimits.Live)]
    public Task<IActionResult> Start(Guid clientId, string workoutId, CancellationToken ct) => Run(actor => live.StartAsync(actor, clientId, workoutId, ct), ct);
    [HttpPost("progress"), ValidateAntiForgeryToken, EnableRateLimiting(SnapRequestRateLimits.Live), RequestSizeLimit(65536)]
    public Task<IActionResult> Progress(Guid clientId, string workoutId, SimpleSnapController.ResultInput input, CancellationToken ct) =>
        Run(actor => !ModelState.IsValid ? throw new ArgumentException("Check the reps, weights and RPE before saving.")
            : live.SaveAsync(actor, clientId, workoutId, input.AssignmentToken, input.ProgressRevision, ToSets(input), ct), ct);
    public static Dictionary<string, List<AccountabilitySet>> ToSets(SimpleSnapController.ResultInput input)
    {
        if (input.Exercises.Count is < 1 or > 20 || input.Exercises.Select(e => e.Id).Distinct().Count() != input.Exercises.Count
            || input.Exercises.Any(e => e.Sets.Count > 20)) throw new ArgumentException("Reload the assigned exercise rows before saving.");
        return input.Exercises.ToDictionary(e => e.Id, e => e.Sets.Select(s => new AccountabilitySet { Reps = s.Reps, Weight = s.Weight, Rpe = e.Rpe }).ToList());
    }
    [HttpPost("message"), ValidateAntiForgeryToken, EnableRateLimiting(SnapRequestRateLimits.Live), RequestSizeLimit(32768)]
    public Task<IActionResult> Message(Guid clientId, string workoutId, MessageInput input, CancellationToken ct) =>
        Run(actor => !ModelState.IsValid ? throw new ArgumentException("Enter a message of 1–2,000 characters.")
            : live.MessageAsync(actor, clientId, workoutId, input.RequestId, input.Body, ct), ct);
    public sealed class MessageInput
    {
        public Guid RequestId { get; set; }
        [Required, StringLength(2000)] public string Body { get; set; } = "";
    }
}
