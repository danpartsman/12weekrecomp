using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Controllers;

[Authorize]
[Route("my-coach")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MyCoachController(RecompDbContext db, AppUserContextService users, CoachCenterService coaches) : Controller
{
    private async Task<Guid?> OwnedClientAsync(CancellationToken ct)
    {
        var context = await users.GetAsync(HttpContext, ct);
        if (context.Client is null) return null;
        return await db.UserClientAccess.AnyAsync(x => x.UserId == context.User.UserId && x.ClientId == context.Client.ClientId && x.AccessRoleCode == "OWNER" && x.RevokedAtUtc == null, ct) ? context.Client.ClientId : null;
    }
    private async Task<MyCoachesViewModel> BuildAsync(Guid clientId, CoachConnectInput input, CancellationToken ct)
    {
        var connections = await (from link in db.CoachClientRelationships.AsNoTracking()
            join user in db.UserAccounts on link.CoachUserId equals user.UserId
            join profile in db.CoachProfiles on user.UserId equals profile.UserId into profiles
            from profile in profiles.DefaultIfEmpty()
            where link.ClientId == clientId && link.StatusCode == "ACTIVE" && link.RevokedAtUtc == null
            select new CoachConnection(link.CoachClientRelationshipId, profile == null ? (user.PreferredDisplayName ?? user.DisplayName ?? "Coach") : profile.DisplayName,
                profile == null ? null : profile.Bio, profile == null ? null : profile.Specialties)).ToListAsync(ct);
        return new(connections, input) { ClientId = clientId };
    }
    [HttpGet("pending-snaps")]
    public async Task<IActionResult> PendingSnaps([FromServices] CoachAccountabilityStore store,[FromServices] IConfiguration configuration,[FromServices] SimpleCoachingService simple,CancellationToken ct)
    {
        if(!AccountabilityPreview.Enabled(configuration))return NotFound();
        var id=await OwnedClientAsync(ct);
        if(!id.HasValue)return Json(new{clientId=(Guid?)null,workouts=Array.Empty<object>()});
        var user=(await users.GetAsync(HttpContext,ct)).User;
        try
        {
            var row=SimpleCoaching.Enabled(configuration)?await simple.ReadAsync(user.UserId,id.Value,false,ct):await store.ReadSnapAsync(user.UserId,id.Value,false,ct);
            var workouts=row is null?[]:AccountabilityDocument.Parse(row.StateJson).SnapWorkouts;
            var state=row is null?null:AccountabilityDocument.Parse(row.StateJson);
            return Json(new{clientId=id.Value,workouts=workouts.Where(w=>(!SimpleCoaching.Enabled(configuration)||w.Simple)&&!(SimpleCoaching.Enabled(configuration)?state!.SnapResults.GetValueOrDefault(w.Id)?.Finalized==true:SnapWorkoutLibrary.Completed(w,state!.SnapResults.GetValueOrDefault(w.Id)))).OrderBy(w=>w.Date).Select(w=>new{w.Id,w.Name,w.Date})});
        }
        catch(UnauthorizedAccessException){return Json(new{clientId=id.Value,workouts=Array.Empty<object>()});}
    }
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var id = await OwnedClientAsync(ct);
        return id.HasValue ? View(await BuildAsync(id.Value, new(), ct)) : RedirectToAction("Index", "Intake");
    }
    [HttpPost("connect"), ValidateAntiForgeryToken, EnableRateLimiting("access-code")]
    public async Task<IActionResult> Connect(CoachConnectInput input, CancellationToken ct)
    {
        var id = await OwnedClientAsync(ct);
        if (!id.HasValue) return Forbid();
        if (ModelState.IsValid && await coaches.AcceptAsync(id.Value, input.Code, ct)) return RedirectToAction(nameof(Index));
        ModelState.AddModelError("Code", "This code is invalid, expired, already used, or this coach is already connected.");
        return View("Index", await BuildAsync(id.Value, input, ct));
    }
    [HttpPost("disconnect/{relationshipId:guid}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect(Guid relationshipId, CancellationToken ct)
    {
        var id = await OwnedClientAsync(ct);
        if (!id.HasValue) return Forbid();
        var row = await db.CoachClientRelationships.SingleOrDefaultAsync(x => x.CoachClientRelationshipId == relationshipId && x.ClientId == id && x.StatusCode == "ACTIVE", ct);
        if (row is null) return NotFound();
        row.StatusCode = "REVOKED"; row.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }
}
