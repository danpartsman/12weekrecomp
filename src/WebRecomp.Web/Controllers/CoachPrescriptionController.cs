using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Controllers;

[Authorize(Roles="Coach,Administrator")]
[Route("coach/clients/{clientId:guid}/prescriptions")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CoachPrescriptionController(RecompDbContext db, AppUserContextService users, CoachCenterService coaches,
    Engine30DashboardProjectionService engine, ProgramPlanService plans) : Controller
{
    private static DateOnly NextWeek(DateOnly today) => Engine30DashboardProjectionService.WeekStart(today).AddDays(7);
    private async Task<(AppClientContext? Context, UserAccount User)> OwnedAsync(Guid clientId,CancellationToken ct)
    {
        var user=(await users.GetAsync(HttpContext,ct)).User;
        if(!await coaches.CanAccessAsync(user.UserId,clientId,ct)) return (null,user);
        var client=await db.Clients.AsNoTracking().SingleOrDefaultAsync(x=>x.ClientId==clientId && x.ClientStatusCode=="ACTIVE",ct);
        var program=await db.Programs.AsNoTracking().Where(x=>x.ClientId==clientId && x.ProgramStatusCode=="ACTIVE").OrderByDescending(x=>x.StartDate).FirstOrDefaultAsync(ct);
        return client is null || program is null ? (null,user) : (new(user,client,program),user);
    }
    private async Task<CoachPrescriptionPage> PageAsync(AppClientContext context,CoachPrescriptionRules? input,CancellationToken ct)
    {
        var history=await db.CoachPrescriptionRuleVersions.AsNoTracking().Where(x=>x.ClientId==context.Client!.ClientId && x.ProgramId==context.Program!.ProgramId)
            .OrderByDescending(x=>x.CreatedAtUtc).ThenByDescending(x=>x.CoachPrescriptionRuleVersionId).Take(20).ToListAsync(ct);
        if(input is null)
        {
            input=history.Count>0 ? JsonSerializer.Deserialize<CoachPrescriptionRules>(history[0].RulesJson)! : new();
            input.EffectiveWeek=NextWeek(AppUserContextService.GetClientToday(context.Client!));
            input.ExpectedVersionId=history.FirstOrDefault()?.CoachPrescriptionRuleVersionId;
            input.Reason=""; input.PreviewToken=null;
        }
        var ids=history.Select(x=>x.CoachUserId).Distinct().ToArray();
        var authors=await db.UserAccounts.Where(x=>ids.Contains(x.UserId)).ToDictionaryAsync(x=>x.UserId,x=>x.PreferredDisplayName ?? x.DisplayName ?? "Trainer",ct);
        return new() {ClientId=context.Client!.ClientId,ClientName=context.Client.ClientName,Rules=input,History=history,Authors=authors};
    }
    private static string Token(CoachPrescriptionPage page,Guid programId)
    {
        var copy=JsonSerializer.Deserialize<CoachPrescriptionRules>(JsonSerializer.Serialize(page.Rules))!;
        copy.PreviewToken=null;
        var rules=JsonSerializer.Serialize(copy);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(page.ClientId+"|"+programId+"|"+rules+"|"+JsonSerializer.Serialize(page.EngineDays))));
    }
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid clientId,CancellationToken ct)
    {
        var owned=await OwnedAsync(clientId,ct);
        return owned.Context is null ? NotFound() : View(await PageAsync(owned.Context,null,ct));
    }
    [HttpPost("preview"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(Guid clientId,CoachPrescriptionRules input,CancellationToken ct) => await EvaluateAsync(clientId,input,false,ct);
    [HttpPost("save"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Guid clientId,CoachPrescriptionRules input,CancellationToken ct) => await EvaluateAsync(clientId,input,true,ct);
    private async Task<IActionResult> EvaluateAsync(Guid clientId,CoachPrescriptionRules input,bool save,CancellationToken ct)
    {
        var owned=await OwnedAsync(clientId,ct);
        if(owned.Context is null) return NotFound();
        var context=owned.Context;
        var today=AppUserContextService.GetClientToday(context.Client!);
        if(input.EffectiveWeek<today || Engine30DashboardProjectionService.WeekStart(input.EffectiveWeek)!=input.EffectiveWeek)
            ModelState.AddModelError("EffectiveWeek","Choose an upcoming Monday; past prescriptions are preserved.");
        if(input.EffectiveWeek>today.AddDays(84)) ModelState.AddModelError("EffectiveWeek","Choose a week within the next 12 weeks.");
        foreach(var error in CoachPrescriptionAllocator.Validate(input)) ModelState.AddModelError("",error);
        var page=await PageAsync(context,input,ct);
        if(!ModelState.IsValid) return View("Index",page);
        IReadOnlyDictionary<DateOnly,Engine30DashboardDayProjection> baseline;
        try
        {
            var plan=await plans.GetPlanCodeForDateAsync(context.Program!,input.EffectiveWeek,ct);
            baseline=await engine.BuildPlanningWeekAsync(context,input.EffectiveWeek,plan,ct,includeCoachRules:false);
        }
        catch(Exception ex) when(ex is InvalidOperationException or ArgumentException)
        { ModelState.AddModelError("","Complete the client's profile, program and scale setup before previewing a prescription."); return View("Index",page); }
        var preview=CoachPrescriptionAllocator.Evaluate(baseline.Values.ToArray(),input);
        page=new() {ClientId=clientId,ClientName=page.ClientName,Rules=input,History=page.History,Authors=page.Authors,EngineDays=baseline.Values.OrderBy(x=>x.Date).ToArray(),Preview=preview};
        page.PreviewToken=Token(page,context.Program!.ProgramId);
        foreach(var error in preview.Errors) ModelState.AddModelError("",error);
        if(!save || !ModelState.IsValid) return View("Index",page);
        if(input.PreviewToken!=page.PreviewToken) {ModelState.AddModelError("","The rules or engine prescription changed. Review this updated preview before activating.");return View("Index",page);}
        await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        if(!await coaches.CanAccessAsync(owned.User.UserId,clientId,ct)) return NotFound();
        var current=await db.CoachPrescriptionRuleVersions.Where(x=>x.ClientId==clientId && x.ProgramId==context.Program.ProgramId)
            .OrderByDescending(x=>x.CreatedAtUtc).ThenByDescending(x=>x.CoachPrescriptionRuleVersionId).Select(x=>(Guid?)x.CoachPrescriptionRuleVersionId).FirstOrDefaultAsync(ct);
        if(current!=input.ExpectedVersionId) {ModelState.AddModelError("","Another trainer changed this client's rules. Reload and review the latest version.");return View("Index",page);}
        db.CoachPrescriptionRuleVersions.Add(new() {CoachPrescriptionRuleVersionId=Guid.NewGuid(),ClientId=clientId,ProgramId=context.Program.ProgramId,
            CoachUserId=owned.User.UserId,EffectiveWeek=input.EffectiveWeek,IsEnabled=input.Enabled,RulesJson=JsonSerializer.Serialize(input),Reason=input.Reason.Trim(),
            PreviewJson=JsonSerializer.Serialize(new { Engine=page.EngineDays, Adjusted=preview.Days, preview.WeeklyCalories }),CreatedAtUtc=DateTime.UtcNow});
        await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);
        TempData["CoachStatus"]="Prescription rules saved for the selected week.";
        return RedirectToAction(nameof(Index),new {clientId});
    }
}
