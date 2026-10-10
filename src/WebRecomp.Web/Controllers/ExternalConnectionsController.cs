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
public sealed class ExternalConnectionsController(AppUserContextService users,RecompDbContext db,DeviceIntegrationService service) : Controller
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
    public async Task<IActionResult> Settings(bool automaticSync,CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return NotFound();
        var context=await users.GetAsync(HttpContext,ct);if(context.Client==null)return RedirectToAction("Index","Intake");
        if(!await service.OwnsAsync(context.User.UserId,context.Client.ClientId,ct))return Forbid();
        await db.DeviceConnections.Where(c=>c.UserId==context.User.UserId&&c.ClientId==context.Client.ClientId&&c.ProviderCode=="FITDAYS")
            .ExecuteUpdateAsync(s=>s.SetProperty(c=>c.AutomaticSync,automaticSync).SetProperty(c=>c.UpdatedAtUtc,DateTime.UtcNow),ct);
        TempData["DeviceMessage"]="Device integration preference saved.";return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return NotFound();
        var context=await users.GetAsync(HttpContext,ct);if(context.Client==null)return RedirectToAction("Index","Intake");
        if(!await service.OwnsAsync(context.User.UserId,context.Client.ClientId,ct))return Forbid();
        var changed=await db.DeviceConnections.Where(c=>c.UserId==context.User.UserId&&c.ClientId==context.Client.ClientId&&c.ProviderCode=="FITDAYS"&&(c.SyncLeaseUntilUtc==null||c.SyncLeaseUntilUtc<DateTime.UtcNow))
            .ExecuteUpdateAsync(s=>s.SetProperty(c=>c.ProtectedCredential,(string?)null).SetProperty(c=>c.AutomaticSync,false).SetProperty(c=>c.StatusCode,"DISCONNECTED").SetProperty(c=>c.UpdatedAtUtc,DateTime.UtcNow).SetProperty(c=>c.LastMessage,"Disconnected. Imported measurements retained."),ct);
        TempData["DeviceMessage"]=changed>0?"Fitdays disconnected. Your imported measurements were retained.":"A sync is in progress. Wait a moment before disconnecting.";
        return RedirectToAction(nameof(Index));
    }
}
public sealed record ExternalConnectionsPage(DeviceProviderSetting Provider,DeviceConnection? Connection);
