using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Coach,Administrator")]
[Route("coach/clients/{clientId:guid}/summary")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CoachSummaryController(AppUserContextService users, CoachWeeklySummaryService summaries) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid clientId, DateOnly? week, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest("Choose a valid week.");
        var user = (await users.GetAsync(HttpContext, ct)).User;
        try
        {
            var summary = await summaries.BuildAsync(user.UserId, clientId, week, ct);
            return summary is null ? NotFound() : View(summary);
        }
        catch (ArgumentOutOfRangeException) { return BadRequest("Choose a valid week, no more than 12 weeks ahead."); }
    }
}
