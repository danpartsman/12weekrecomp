using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.Notifications;

namespace WebRecomp.Web.Controllers;

[Authorize, Route("fee-notices")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AutomaticFeeNoticesController(RecompDbContext db, AppUserContextService users, NotificationFiles files) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var context = await users.GetAsync(HttpContext, ct);
        return Json(new { notices = await AutomaticFeeNotices.ReadAsync(db, files, context.User.UserId,
            context.Client?.ClientId, User.IsInRole("Coach") || User.IsInRole("Administrator"), ct) });
    }

    [HttpPost("dismiss"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss([FromForm] string[] ids, CancellationToken ct)
    {
        if (ids.Length > 50 || ids.Any(id => !Guid.TryParseExact(id, "N", out _))) return BadRequest();
        var context = await users.GetAsync(HttpContext, ct);
        var user = context.User.UserId;
        var valid = (await AutomaticFeeNotices.ReadAsync(db, files, user, context.Client?.ClientId,
            User.IsInRole("Coach") || User.IsInRole("Administrator"), ct)).Select(n => n.Id).ToHashSet();
        var key = AutomaticFeeNotices.ReceiptKey(user);
        using (files.Lease(key))
        {
            var seen = files.Read<List<string>>(key) ?? [];
            files.Write(key, seen.Concat(ids.Where(valid.Contains)).Distinct().TakeLast(2000).ToList());
        }
        return Ok();
    }
}
