using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Controllers;
[Authorize(Roles="Coach,Administrator")]
[Route("coach/clients/{clientId:guid}/workout")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CoachWorkoutController(RecompDbContext db,AppUserContextService users,CoachCenterService coaches,CoachWorkoutPlanService workouts) : Controller
{
    private async Task<AppClientContext?> OwnedAsync(Guid id,CancellationToken ct)
    {
        var user=(await users.GetAsync(HttpContext,ct)).User;
        if(!await coaches.CanAccessAsync(user.UserId,id,ct)) return null;
        var client=await db.Clients.AsNoTracking().SingleOrDefaultAsync(x=>x.ClientId==id && x.ClientStatusCode=="ACTIVE",ct);
        var program=await db.Programs.AsNoTracking().Where(x=>x.ClientId==id && x.ProgramStatusCode=="ACTIVE").OrderByDescending(x=>x.StartDate).FirstOrDefaultAsync(ct);
        return client is null || program is null?null:new(user,client,program);
    }
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid clientId,DateOnly date,string? choice,CancellationToken ct)
    {
        var context=await OwnedAsync(clientId,ct);if(context is null)return NotFound();
        if(!ModelState.IsValid) return BadRequest("Choose a valid date.");
        if(!CoachWorkoutPlanService.Editable(date,AppUserContextService.GetClientToday(context.Client!),context.Program!.StartDate,
            await db.WorkoutSessions.AnyAsync(x=>x.ClientId==clientId && x.ProgramId==context.Program.ProgramId && x.WorkoutDate==date,ct))) return BadRequest("This date is locked. Choose a future date without a started workout.");
        try {return View(await workouts.PageAsync(context,date,choice,null,ct));}
        catch(ArgumentException ex) {return BadRequest(ex.Message);}
    }
    [HttpPost("preview"),ValidateAntiForgeryToken]
    public Task<IActionResult> Preview(Guid clientId,CoachWorkoutInput input,CancellationToken ct) => EvaluateAsync(clientId,input,false,ct);
    [HttpPost("save"),ValidateAntiForgeryToken]
    public Task<IActionResult> Save(Guid clientId,CoachWorkoutInput input,CancellationToken ct) => EvaluateAsync(clientId,input,true,ct);
    private async Task<IActionResult> EvaluateAsync(Guid clientId,CoachWorkoutInput input,bool save,CancellationToken ct)
    {
        var context=await OwnedAsync(clientId,ct);if(context is null)return NotFound();
        CoachWorkoutPage page;
        try
        {
            page=await workouts.PageAsync(context,input.Date,input.Choice,input,ct);
            if(!ModelState.IsValid)return View("Index",page);
            var preview=await workouts.PreviewAsync(context,input,ct);page=preview.Page;
            if(save)
            {
                if(input.PreviewToken!=page.PreviewToken) ModelState.AddModelError("","The plan or inputs changed. Review this preview before saving.");
                else if(!await workouts.SaveAsync(context,input,preview.Candidate,preview.Fingerprint,page,ct)) ModelState.AddModelError("","The client connection or weekly plan changed, or this workout started. Reload and review before saving.");
                else {TempData["CoachStatus"]="Workout and schedule updated. Nutrition now follows the adjusted training plan.";return RedirectToAction("Client","Coach",new {clientId,week=WebRecomp.Web.Services.Engine30.Engine30DashboardProjectionService.WeekStart(input.Date).ToString("yyyy-MM-dd")});}
            }
            return View("Index",page);
        }
        catch(InvalidOperationException)
        {
            ModelState.AddModelError("","Complete the client's profile, active program and scale setup before previewing calculated targets.");
            page=await workouts.PageAsync(context,input.Date,input.Choice,input,ct);return View("Index",page);
        }
        catch(ArgumentException ex)
        {
            ModelState.AddModelError("",ex.Message);
            try {page=await workouts.PageAsync(context,input.Date,input.Choice,input,ct);return View("Index",page);}
            catch(ArgumentException){return BadRequest("Choose an available workout for this client.");}
        }
    }
}
