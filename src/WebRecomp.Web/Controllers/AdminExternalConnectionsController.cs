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
public sealed class AdminExternalConnectionsController(RecompDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return NotFound();
        var p=await db.DeviceProviderSettings.AsNoTracking().SingleAsync(p=>p.ProviderCode=="FITDAYS",ct);
        var model=new ProviderSettingsModel {Enabled=p.Enabled,AutomaticSyncEnabled=p.AutomaticSyncEnabled,SyncIntervalMinutes=p.SyncIntervalMinutes,HistoryDays=p.HistoryDays,AppVersion=p.AppVersion,BodyCompositionEnabled=p.BodyCompositionEnabled,SkeletalMuscleUnit=p.SkeletalMuscleUnit,BodyWaterUnit=p.BodyWaterUnit};
        await Populate(model,ct);return View(model);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ProviderSettingsModel model,CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return NotFound();
        if(!ModelState.IsValid){await Populate(model,ct);return View(model);}
        await db.DeviceProviderSettings.Where(p=>p.ProviderCode=="FITDAYS").ExecuteUpdateAsync(s=>s.SetProperty(p=>p.Enabled,model.Enabled).SetProperty(p=>p.AutomaticSyncEnabled,model.AutomaticSyncEnabled).SetProperty(p=>p.SyncIntervalMinutes,model.SyncIntervalMinutes).SetProperty(p=>p.HistoryDays,model.HistoryDays).SetProperty(p=>p.AppVersion,model.AppVersion).SetProperty(p=>p.BodyCompositionEnabled,model.BodyCompositionEnabled).SetProperty(p=>p.SkeletalMuscleUnit,model.SkeletalMuscleUnit).SetProperty(p=>p.BodyWaterUnit,model.BodyWaterUnit).SetProperty(p=>p.UpdatedAtUtc,DateTime.UtcNow),ct);
        TempData["DeviceMessage"]="External connection settings saved.";return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Pause(Guid connectionId,CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return NotFound();
        await db.DeviceConnections.Where(c=>c.DeviceConnectionId==connectionId).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.AutomaticSync,false).SetProperty(c=>c.UpdatedAtUtc,DateTime.UtcNow),ct);
        TempData["DeviceMessage"]="Automatic sync paused for this connection.";return RedirectToAction(nameof(Index));
    }
    private async Task Populate(ProviderSettingsModel model,CancellationToken ct)
    {
        model.Connections=await (from c in db.DeviceConnections.AsNoTracking() join u in db.UserAccounts.AsNoTracking() on c.UserId equals u.UserId select new DeviceAdminRow(c.DeviceConnectionId,u.PreferredDisplayName??u.DisplayName??"Recomp user",c.PersonName,c.StatusCode,c.AutomaticSync,c.LastSuccessAtUtc,c.LastMessage)).Take(200).ToListAsync(ct);
    }
}
public sealed class ProviderSettingsModel
{
    public bool Enabled {get;set;}
    public bool AutomaticSyncEnabled {get;set;}
    [Range(15,1440)]public int SyncIntervalMinutes {get;set;}=60;
    [Range(1,90)]public int HistoryDays {get;set;}=30;
    [Required,StringLength(32),RegularExpression(@"^\d+(\.\d+){1,3}$")]public string AppVersion {get;set;}="1.21.0";
    public bool BodyCompositionEnabled {get;set;}=true;
    [Required,RegularExpression("^(PERCENT|LB|KG)$")]public string SkeletalMuscleUnit {get;set;}="PERCENT";
    [Required,RegularExpression("^(PERCENT|LB|KG)$")]public string BodyWaterUnit {get;set;}="PERCENT";
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]public List<DeviceAdminRow> Connections {get;set;}=[];
}
public sealed record DeviceAdminRow(Guid Id,string UserName,string PersonName,string Status,bool Automatic,DateTime? LastSuccess,string? Message);
