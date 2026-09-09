using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin")]
public sealed class AdminController(
    RecompDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        ViewData["UnreadContactCount"] =
            await db.ContactMessages
                .AsNoTracking()
                .CountAsync(
                    x => x.StatusCode == "NEW",
                    cancellationToken);

        var now = DateTime.UtcNow;

        ViewData["ActiveDemoSessionCount"] =
            await (
                from session in db.DemoSessions.AsNoTracking()
                join workspace in db.DemoWorkspaces.AsNoTracking()
                    on session.DemoWorkspaceId
                    equals workspace.DemoWorkspaceId
                where session.RevokedAtUtc == null
                      && session.ExpiresAtUtc > now
                      && workspace.WorkspaceStatusCode == "ACTIVE"
                      && workspace.ExpiresAtUtc > now
                      && workspace.LastActivityAtUtc
                          .AddDays(workspace.InactivityTimeoutDays) > now
                select session.DemoSessionId
            )
            .CountAsync(cancellationToken);

        return View();
    }
}