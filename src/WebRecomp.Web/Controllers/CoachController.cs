using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
public sealed class CoachController(RecompDbContext db, AppUserContextService users, CoachCenterService coaches) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await coaches.BuildAsync((await users.GetAsync(HttpContext, ct)).User, ct));

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

    [HttpPost("invite"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(CancellationToken ct)
    {
        TempData["CoachInviteCode"] = await coaches.InviteAsync((await users.GetAsync(HttpContext, ct)).User.UserId, ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("clients/{clientId:guid}")]
    public async Task<IActionResult> Client(Guid clientId, CancellationToken ct)
    {
        var user = (await users.GetAsync(HttpContext, ct)).User;
        if (!await coaches.CanAccessAsync(user.UserId, clientId, ct)) return NotFound();
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == clientId && x.ClientStatusCode == "ACTIVE", ct);
        if (client is null) return NotFound();
        var program = await db.Programs.AsNoTracking().Where(x => x.ClientId == clientId && x.ProgramStatusCode == "ACTIVE").OrderByDescending(x => x.StartDate).FirstOrDefaultAsync(ct);
        var measurement = await db.Measurements.AsNoTracking().Where(x => x.ClientId == clientId && !x.IsDeleted).OrderByDescending(x => x.MeasurementAtUtc).FirstOrDefaultAsync(ct);
        var workouts = await db.WorkoutSessions.AsNoTracking().Where(x => x.ClientId == clientId && x.CompletedFlag).OrderByDescending(x => x.WorkoutDate).Take(7).ToListAsync(ct);
        return View(new CoachClientDetailViewModel(client, program, measurement, workouts));
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
