using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin/demo-sessions")]
public sealed class AdminDemoSessionsController(
    RecompDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var data = await (
            from session in db.DemoSessions.AsNoTracking()

            join workspace in db.DemoWorkspaces.AsNoTracking()
                on session.DemoWorkspaceId
                equals workspace.DemoWorkspaceId

            join invite in db.DemoInvites.AsNoTracking()
                on workspace.DemoInviteId
                equals invite.DemoInviteId

            orderby session.CreatedAtUtc descending

            select new
            {
                session.DemoSessionId,
                session.DemoWorkspaceId,

                invite.InviteCode,
                workspace.DemoTypeCode,
                workspace.WorkspaceStatusCode,

                session.CreatedAtUtc,
                session.LastSeenAtUtc,
                session.ExpiresAtUtc,
                session.RevokedAtUtc,

                workspace.LastActivityAtUtc,
                WorkspaceExpiresAtUtc = workspace.ExpiresAtUtc,
                workspace.InactivityTimeoutDays,

                workspace.UtmSource,
                workspace.UtmMedium,
                workspace.UtmCampaign,
                workspace.UtmContent,
                workspace.ReferrerHost,
                workspace.ReferrerPath,
                workspace.LandingPath,
                workspace.CountryCode,
                workspace.RegionName,
                workspace.CityName,

                session.DeviceTypeCode,
                session.OperatingSystem,
                session.BrowserName,
                session.PageViewCount,
                session.LastPagePath
            })
            .Take(250)
            .ToListAsync(cancellationToken);

        var rows = data
            .Select(x =>
            {
                var inactivityExpiresAtUtc =
                    x.LastActivityAtUtc.AddDays(
                        x.InactivityTimeoutDays);

                var sessionStatus =
                    x.RevokedAtUtc.HasValue
                        ? "REVOKED"
                        : x.ExpiresAtUtc <= now
                            ? "EXPIRED"
                            : x.ExpiresAtUtc > now
                              && x.ExpiresAtUtc <= now.AddHours(24)
                                ? "EXPIRING"
                                : "ACTIVE";

                var workspaceStatus =
                    !string.Equals(
                        x.WorkspaceStatusCode,
                        "ACTIVE",
                        StringComparison.OrdinalIgnoreCase)
                        ? x.WorkspaceStatusCode
                        : x.ExpiresAtUtc <= now
                          || inactivityExpiresAtUtc <= now
                            ? "EXPIRED"
                            : "ACTIVE";

                if (workspaceStatus != "ACTIVE"
                    && sessionStatus == "ACTIVE")
                {
                    sessionStatus = "EXPIRED";
                }

                return new AdminDemoSessionRow
                {
                    DemoSessionId = x.DemoSessionId,
                    DemoWorkspaceId = x.DemoWorkspaceId,

                    InviteCode = x.InviteCode,
                    DemoTypeCode = x.DemoTypeCode,

                    SessionStatus = sessionStatus,
                    WorkspaceStatus = workspaceStatus,

                    CreatedAtUtc = x.CreatedAtUtc,
                    LastSeenAtUtc = x.LastSeenAtUtc,
                    LastActivityAtUtc = x.LastActivityAtUtc,

                    SessionExpiresAtUtc = x.ExpiresAtUtc,
                    WorkspaceExpiresAtUtc = x.WorkspaceExpiresAtUtc,
                    InactivityExpiresAtUtc =
                        inactivityExpiresAtUtc,

                    RevokedAtUtc = x.RevokedAtUtc,

                    UtmSource = x.UtmSource,
                    UtmMedium = x.UtmMedium,
                    UtmCampaign = x.UtmCampaign,
                    UtmContent = x.UtmContent,

                    ReferrerHost = x.ReferrerHost,
                    ReferrerPath = x.ReferrerPath,
                    LandingPath = x.LandingPath,

                    CountryCode = x.CountryCode,
                    RegionName = x.RegionName,
                    CityName = x.CityName,

                    DeviceTypeCode = x.DeviceTypeCode,
                    OperatingSystem = x.OperatingSystem,
                    BrowserName = x.BrowserName,

                    PageViewCount = x.PageViewCount,
                    LastPagePath = x.LastPagePath
                };
            })
            .ToList();

        return View(new AdminDemoSessionsViewModel
        {
            Sessions = rows,
            ActiveCount =
                rows.Count(x => x.SessionStatus == "ACTIVE"
                             || x.SessionStatus == "EXPIRING")
        });
    }


    [HttpPost("{id:guid}/terminate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Terminate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var session = await db.DemoSessions
            .SingleOrDefaultAsync(
                x => x.DemoSessionId == id,
                cancellationToken);

        if (session is null)
            return NotFound();

        var now = DateTime.UtcNow;

        if (session.RevokedAtUtc.HasValue)
        {
            TempData["DemoSessionError"] =
                "That demo session has already been terminated.";

            return RedirectToAction(nameof(Index));
        }

        if (session.ExpiresAtUtc <= now)
        {
            TempData["DemoSessionError"] =
                "That demo session has already expired.";

            return RedirectToAction(nameof(Index));
        }

        session.RevokedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);

        TempData["DemoSessionSuccess"] =
            "Demo session terminated.";

        return RedirectToAction(nameof(Index));
    }
}
