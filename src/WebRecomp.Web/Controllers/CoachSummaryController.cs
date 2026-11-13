using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.ViewModels;
using WebRecomp.NutritionIntelligence.Persistence;
namespace WebRecomp.Web.Controllers;

public sealed record CoachDailyReviewPage(CoachWeeklySummaryViewModel Summary, CoachSummaryDay Day,
    NutritionDailyLogReadModel? Food, string? FoodNotice);

[Authorize(Roles = "Coach,Administrator")]
[Route("coach/clients/{clientId:guid}/summary")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CoachSummaryController(AppUserContextService users, CoachWeeklySummaryService summaries,
    INutritionFoodLogStore? foods = null, ILogger<CoachSummaryController>? logger = null) : Controller
{
    [HttpGet("day")]
    public async Task<IActionResult> Day(Guid clientId, DateOnly? date, CancellationToken ct)
    {
        if(!ModelState.IsValid)return BadRequest("Choose a valid date.");
        var actor=(await users.GetAsync(HttpContext,ct)).User.UserId;
        try
        {
            var summary=await summaries.BuildAsync(actor,clientId,date,ct);
            if(summary is null)return NotFound();
            var day=summary.Days.Single(d=>d.Date==summary.ReferenceDate);
            NutritionDailyLogReadModel? food=null;string? notice=null;
            if(foods is not null)
            {
                try{food=await foods.GetDailyLogAsync(clientId,day.Date,ct);}
                catch(Exception e)when(e is not OperationCanceledException)
                {logger?.LogWarning(e,"Coach daily food review unavailable for client {ClientId}",clientId);notice="Food item details are temporarily unavailable. Saved daily totals are shown below.";}
            }
            return View("Day",new CoachDailyReviewPage(summary,day,food,notice));
        }
        catch(ArgumentOutOfRangeException){return BadRequest("Choose today or an earlier date.");}
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(Guid clientId, DateOnly? date, CancellationToken ct, DateOnly? week = null)
    {
        if (!ModelState.IsValid) return BadRequest("Choose a valid week.");
        var user = (await users.GetAsync(HttpContext, ct)).User;
        try
        {
            var summary = await summaries.BuildAsync(user.UserId, clientId, date ?? week, ct);
            return summary is null ? NotFound() : View(summary);
        }
        catch (ArgumentOutOfRangeException) { return BadRequest("Choose a valid reference date through today."); }
    }
}
