using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Controllers;
[Authorize(Roles="Coach,Administrator")]
[Route("coach/clients/{clientId:guid}/week")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CoachWeekController(RecompDbContext db,AppUserContextService users,CoachCenterService coaches,CoachWeekService weeks) : Controller
{
    private async Task<AppClientContext?> OwnedAsync(Guid clientId,CancellationToken ct)
    {
        var user=(await users.GetAsync(HttpContext,ct)).User;
        if(!await coaches.CanAccessAsync(user.UserId,clientId,ct)) return null;
        var client=await db.Clients.AsNoTracking().SingleOrDefaultAsync(x=>x.ClientId==clientId && x.ClientStatusCode=="ACTIVE",ct);
        var program=await db.Programs.AsNoTracking().Where(x=>x.ClientId==clientId && x.ProgramStatusCode=="ACTIVE").OrderByDescending(x=>x.StartDate).FirstOrDefaultAsync(ct);
        return client is null || program is null?null:new(user,client,program);
    }
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid clientId,DateOnly? week,CancellationToken ct)
    {
        var context=await OwnedAsync(clientId,ct);if(context is null) return NotFound();
        var earliest=AppUserContextService.GetClientToday(context.Client!);
        if(earliest<context.Program!.StartDate) earliest=context.Program.StartDate;
        var monday=Engine30DashboardProjectionService.WeekStart(earliest);
        if(monday<earliest) monday=monday.AddDays(7);
        try {return View(await weeks.BuildAsync(context,week ?? monday,null,ct));}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException) {TempData["CoachStatus"]=ex.Message;return RedirectToAction("Client","Coach",new {clientId});}
    }
    [HttpPost("preview"),ValidateAntiForgeryToken]
    public Task<IActionResult> Preview(Guid clientId,CoachWeekInput input,CancellationToken ct) => EvaluateAsync(clientId,input,false,ct);
    [HttpPost("save"),ValidateAntiForgeryToken]
    public Task<IActionResult> Save(Guid clientId,CoachWeekInput input,CancellationToken ct) => EvaluateAsync(clientId,input,true,ct);
    private async Task<IActionResult> EvaluateAsync(Guid clientId,CoachWeekInput input,bool save,CancellationToken ct)
    {
        var context=await OwnedAsync(clientId,ct);if(context is null) return NotFound();
        CoachWeekPage page;
        try {page=await weeks.BuildAsync(context,input.Rules.EffectiveWeek,input,ct);}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException) {TempData["CoachStatus"]=ex.Message;return RedirectToAction("Client","Coach",new {clientId});}
        if(!ModelState.IsValid) return View("Index",page);
        try {await weeks.PreviewAsync(context,page,ct);}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException) {ModelState.AddModelError("",ex.Message);return View("Index",page);}
        foreach(var error in page.Errors) ModelState.AddModelError("",error);
        if(!save || !ModelState.IsValid) return View("Index",page);
        if(!await weeks.SaveAsync(context,page,ct)) {ModelState.AddModelError("","The plan, prescription or access changed. Review this new preview before saving.");return View("Index",page);}
        TempData["CoachStatus"]="Weekly workouts and macro goals saved. The client's assigned targets use this prescription from the selected week.";
        return RedirectToAction("Client","Coach",new {clientId,week=input.Rules.EffectiveWeek.ToString("yyyy-MM-dd")});
    }
}
