using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.ExternalConnections;
namespace WebRecomp.Web.Controllers;
[Authorize]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ExternalConnectionsController(AppUserContextService users,DeviceConnectionStore connections,DeviceIntegrationService service) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return NotFound();
        var context=await users.GetAsync(HttpContext,ct);if(context.Client==null||context.Program==null)return RedirectToAction("Index","Intake");
        if(!await service.OwnsAsync(context.User.UserId,context.Client.ClientId,ct))return Forbid();
        return View(new ExternalConnectionsPage(await service.ProviderAsync(ct),await service.ConnectionAsync(context.User.UserId,context.Client.ClientId,ct)));
    }
    [HttpPost,ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("fitdays-preview")]
    public async Task<IActionResult> SyncOnOpen(CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return NotFound();
        var setting=await service.ProviderAsync(ct);
        if(!setting.Enabled||!setting.AutomaticSyncEnabled)return Json(new {updated=false});
        var context=await users.GetAsync(HttpContext,ct);
        if(context.Client==null||context.Program==null||!await service.OwnsAsync(context.User.UserId,context.Client.ClientId,ct))return Json(new {updated=false});
        var c=await service.ConnectionAsync(context.User.UserId,context.Client.ClientId,ct);
        if(!DeviceSyncPolicy.ShouldCheckOnOpen(setting,c,DateTime.UtcNow))return Json(new {updated=false});
        await service.SyncAsync(c!.DeviceConnectionId,ct);
        return Json(new {updated=service.LastImportedCount>0});
    }
    [HttpPost,ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("fitdays-preview")]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return NotFound();
        var context=await users.GetAsync(HttpContext,ct);if(context.Client==null)return RedirectToAction("Index","Intake");
        if(!await service.OwnsAsync(context.User.UserId,context.Client.ClientId,ct))return Forbid();
        var connection=await service.ConnectionAsync(context.User.UserId,context.Client.ClientId,ct);
        if(connection!=null)TempData["DeviceMessage"]=await service.SyncAsync(connection.DeviceConnectionId,ct);
        return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("fitdays-preview")]
    public async Task<IActionResult> PullReadings(CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return NotFound();
        var context=await users.GetAsync(HttpContext,ct);
        if(context.Client==null||!await service.OwnsAsync(context.User.UserId,context.Client.ClientId,ct))return Forbid();
        var c=await service.ConnectionAsync(context.User.UserId,context.Client.ClientId,ct);
        if(c?.ProtectedCredential==null)return Json(new {updated=false,message="Connect your Fitdays scale once in Profile → Device integrations, then save your selected profile."});
        var message=await service.SyncAsync(c.DeviceConnectionId,ct);
        return Json(new {updated=service.LastImportedCount>0,message});
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(bool automaticSync,CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return NotFound();
        var context=await users.GetAsync(HttpContext,ct);if(context.Client==null)return RedirectToAction("Index","Intake");
        if(!await service.OwnsAsync(context.User.UserId,context.Client.ClientId,ct))return Forbid();
        await connections.PreferenceAsync(context.User.UserId,context.Client.ClientId,automaticSync,ct);
        TempData["DeviceMessage"]="Device integration preference saved.";return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return NotFound();
        var context=await users.GetAsync(HttpContext,ct);if(context.Client==null)return RedirectToAction("Index","Intake");
        if(!await service.OwnsAsync(context.User.UserId,context.Client.ClientId,ct))return Forbid();
        var changed=await connections.DisconnectAsync(context.User.UserId,context.Client.ClientId,ct);
        TempData["DeviceMessage"]=changed?"Fitdays disconnected. Your imported measurements were retained.":"A sync is in progress. Wait a moment before disconnecting.";
        return RedirectToAction(nameof(Index));
    }
}
public sealed record ExternalConnectionsPage(DeviceProviderSetting Provider,DeviceConnection? Connection);
