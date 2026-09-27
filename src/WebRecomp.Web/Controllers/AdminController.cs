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

        ViewData["FreeUseOverlayEnabled"] =
            await db.GlobalSettings.AsNoTracking()
                .Where(x => x.SettingCode == "PUBLIC_FREE_OVERLAY_ENABLED")
                .Select(x => x.DecimalValue)
                .FirstOrDefaultAsync(cancellationToken) != 0m;
        return View();
    }

    [HttpPost("free-use-overlay")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetFreeUseOverlay(
        bool enabled,
        CancellationToken cancellationToken)
    {
        var setting = await db.GlobalSettings
            .SingleOrDefaultAsync(
                x => x.SettingCode == "PUBLIC_FREE_OVERLAY_ENABLED",
                cancellationToken);
        if (setting is null)
        {
            var configId = await db.ConfigVersions.AsNoTracking()
                .Where(x => x.VersionCode == "CFG-3000.0")
                .Select(x => (Guid?)x.ConfigVersionId)
                .FirstOrDefaultAsync(cancellationToken);
            if (!configId.HasValue)
                return Problem("Engine configuration is unavailable.");

            setting = new WebRecomp.Web.Models.GlobalSetting
            {
                GlobalSettingId = Guid.NewGuid(),
                ConfigVersionId = configId.Value,
                SettingCode = "PUBLIC_FREE_OVERLAY_ENABLED",
                Description = "Show the free-use announcement on public pages."
            };
            db.GlobalSettings.Add(setting);
        }
        setting.DecimalValue = enabled ? 1m : 0m;
        await db.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] =
            enabled ? "Free-use announcement enabled." : "Free-use announcement disabled.";
        return RedirectToAction(nameof(Index));
    }
}