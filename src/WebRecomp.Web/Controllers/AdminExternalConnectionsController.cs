using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.ExternalConnections;
namespace WebRecomp.Web.Controllers;
[Authorize(Roles="Administrator")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class AdminExternalConnectionsController(RecompDbContext db,DeviceConnectionStore connections) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return NotFound();
        var p=await connections.ProviderAsync(ct);
        var model=new ProviderSettingsModel {Enabled=p.Enabled,AutomaticSyncEnabled=p.AutomaticSyncEnabled,SyncIntervalMinutes=p.SyncIntervalMinutes,HistoryDays=p.HistoryDays,AppVersion=p.AppVersion,BodyCompositionEnabled=p.BodyCompositionEnabled,SkeletalMuscleUnit=p.SkeletalMuscleUnit,BodyWaterUnit=p.BodyWaterUnit};
        await Populate(model,ct);return View(model);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ProviderSettingsModel model,CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return NotFound();
        if(!ModelState.IsValid){await Populate(model,ct);return View(model);}
        await connections.UpdateProviderAsync(new DeviceProviderSetting {Enabled=model.Enabled,AutomaticSyncEnabled=model.AutomaticSyncEnabled,SyncIntervalMinutes=model.SyncIntervalMinutes,HistoryDays=model.HistoryDays,AppVersion=model.AppVersion,BodyCompositionEnabled=model.BodyCompositionEnabled,SkeletalMuscleUnit=model.SkeletalMuscleUnit,BodyWaterUnit=model.BodyWaterUnit,UpdatedAtUtc=DateTime.UtcNow},ct);
        TempData["DeviceMessage"]="External connection settings saved.";return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Pause(Guid connectionId,CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return NotFound();
        await connections.PauseAsync(connectionId,ct);
        TempData["DeviceMessage"]="Automatic sync paused for this connection.";return RedirectToAction(nameof(Index));
    }
    private async Task Populate(ProviderSettingsModel model,CancellationToken ct)
    {
        var rows=(await connections.ListAsync(ct)).Take(200).ToList();
        var ids=rows.Select(c=>c.UserId).Distinct().ToList();
        var users=await db.UserAccounts.AsNoTracking().Where(u=>ids.Contains(u.UserId)).ToDictionaryAsync(u=>u.UserId,ct);
        model.Connections=rows.Select(c=>new DeviceAdminRow(c.DeviceConnectionId,users.TryGetValue(c.UserId,out var u)?u.PreferredDisplayName??u.DisplayName??"Recomp user":"Recomp user",c.PersonName,c.StatusCode,c.AutomaticSync,c.LastSuccessAtUtc,c.LastMessage)).ToList();
    }
}
public sealed class ProviderSettingsModel
{
    public bool Enabled {get;set;}
    public bool AutomaticSyncEnabled {get;set;}
    [Range(15,1440)]public int SyncIntervalMinutes {get;set;}=15;
    [Range(1,90)]public int HistoryDays {get;set;}=30;
    [Required,StringLength(32),RegularExpression(@"^\d+(\.\d+){1,3}$")]public string AppVersion {get;set;}="1.21.0";
    public bool BodyCompositionEnabled {get;set;}=true;
    [Required,RegularExpression("^(PERCENT|LB|KG)$")]public string SkeletalMuscleUnit {get;set;}="PERCENT";
    [Required,RegularExpression("^(PERCENT|LB|KG)$")]public string BodyWaterUnit {get;set;}="PERCENT";
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]public List<DeviceAdminRow> Connections {get;set;}=[];
}
public sealed record DeviceAdminRow(Guid Id,string UserName,string PersonName,string Status,bool Automatic,DateTime? LastSuccess,string? Message);
