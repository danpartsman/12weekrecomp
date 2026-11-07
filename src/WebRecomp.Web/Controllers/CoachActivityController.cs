using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.Notifications;
namespace WebRecomp.Web.Controllers;
[Authorize(Roles="Coach,Administrator"), Route("coach/activity-notices")]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
public sealed class CoachActivityController(RecompDbContext db, AppUserContextService users, NotificationFiles files) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => Json(new { notices=await CoachActivityNotices.ReadAsync(db,files,(await users.GetAsync(HttpContext,ct)).User.UserId,ct) });
    [HttpPost("dismiss"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss([FromForm]string[] ids,CancellationToken ct)
    {
        if(ids.Length>50 || ids.Any(x=>x.Length>200))return BadRequest();
        var coach=(await users.GetAsync(HttpContext,ct)).User.UserId;
        var valid=(await CoachActivityNotices.ReadAsync(db,files,coach,ct)).Select(x=>x.Id).ToHashSet();
        var key=CoachActivityNotices.ReceiptKey(coach);
        using(files.Lease(key))
        {
            var seen=files.Read<List<string>>(key)??[];
            files.Write(key,seen.Concat(ids.Where(valid.Contains)).Distinct().TakeLast(2000).ToList());
        }
        return Ok();
    }
}
