using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Coach,Administrator")]
[Route("coach")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CoachController(RecompDbContext db, AppUserContextService users, CoachCenterService coaches, CoachClientPlanService? clientPlans = null) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await coaches.BuildAsync((await users.GetAsync(HttpContext, ct)).User, ct));

    [HttpGet("snap-status")]
    public async Task<IActionResult> SnapStatus(CancellationToken ct)
    {
        var user=(await users.GetAsync(HttpContext,ct)).User;
        return Json((await coaches.SnapStatusesAsync(user.UserId,ct)).Select(x=>new{clientId=x.Key,snap=x.Value}));
    }

    [HttpPost("profile"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(CoachProfileInput input, CancellationToken ct)
    {
        var user = (await users.GetAsync(HttpContext, ct)).User;
        if (!ModelState.IsValid)
        {
            var model = await coaches.BuildAsync(user, ct);
            return View("Index", model with { Profile = input });
        }
        var profile = await db.CoachProfiles.SingleOrDefaultAsync(x => x.UserId == user.UserId, ct);
        if (profile is null) { profile = new() { UserId = user.UserId }; db.CoachProfiles.Add(profile); }
        profile.DisplayName = input.DisplayName.Trim(); profile.Bio = input.Bio?.Trim(); profile.Specialties = input.Specialties?.Trim(); profile.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        TempData["CoachStatus"] = "Coach profile saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("invite"), ValidateAntiForgeryToken, EnableRateLimiting("access-code")]
    public async Task<IActionResult> Invite(CancellationToken ct)
    {
        TempData["CoachInviteCode"] = await coaches.InviteAsync((await users.GetAsync(HttpContext, ct)).User.UserId, ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("clients/{clientId:guid}")]
    public async Task<IActionResult> Client(Guid clientId, CancellationToken ct, DateOnly? week = null)
    {
        var user = (await users.GetAsync(HttpContext, ct)).User;
        if (!await coaches.CanAccessAsync(user.UserId, clientId, ct)) return NotFound();
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == clientId && x.ClientStatusCode == "ACTIVE", ct);
        if (client is null) return NotFound();
        var program = await db.Programs.AsNoTracking().Where(x => x.ClientId == clientId && x.ProgramStatusCode == "ACTIVE").OrderByDescending(x => x.StartDate).FirstOrDefaultAsync(ct);
        var measurement = await db.Measurements.AsNoTracking().Where(x => x.ClientId == clientId && !x.IsDeleted).OrderByDescending(x => x.MeasurementAtUtc).FirstOrDefaultAsync(ct);
        var workouts = await db.WorkoutSessions.AsNoTracking().Where(x => x.ClientId == clientId && x.CompletedFlag).OrderByDescending(x => x.WorkoutDate).Take(7).ToListAsync(ct);
        if (!ModelState.IsValid) return BadRequest("Choose a valid week.");
        var today=AppUserContextService.GetClientToday(client);
        if(week.HasValue && (week<today.AddDays(-84) || week>today.AddDays(84))) return BadRequest("Choose a week within 12 weeks of today.");
        CoachClientPlan? plan=null;
        if(program is not null)
        {
            try { plan=await clientPlans!.BuildAsync(new(user,client,program),week,ct); }
            catch(InvalidOperationException) { plan=new() {Today=today,WeekStart=WebRecomp.Web.Services.Engine30.Engine30DashboardProjectionService.WeekStart(week ?? today),Notice="Complete the client's profile, active program and scale setup before reviewing their calculated plan."}; }
        }
        return View(new CoachClientDetailViewModel(client, program, measurement, workouts) {Plan=plan});
    }

    [HttpGet("clients/{clientId:guid}/accountability")]
    public async Task<IActionResult> Accountability(Guid clientId, CancellationToken ct)
    {
        if (!AccountabilityPreview.Enabled(HttpContext.RequestServices.GetRequiredService<IConfiguration>())) return NotFound();
        var user = (await users.GetAsync(HttpContext, ct)).User;
        if (!await coaches.CanAccessAsync(user.UserId, clientId, ct)) return NotFound();
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == clientId && x.ClientStatusCode == "ACTIVE", ct);
        return client is null ? NotFound() : View(new AccountabilityPreviewViewModel(clientId, client.ClientName, true));
    }

    [HttpPost("clients/{clientId:guid}/disconnect"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect(Guid clientId, CancellationToken ct)
    {
        var user = (await users.GetAsync(HttpContext, ct)).User;
        var rows = await db.CoachClientRelationships.Where(x => x.CoachUserId == user.UserId && x.ClientId == clientId && x.StatusCode == "ACTIVE").ToListAsync(ct);
        foreach (var row in rows) { row.StatusCode = "REVOKED"; row.RevokedAtUtc = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }
}
