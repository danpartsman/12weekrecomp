using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

public sealed class NutritionController(
    RecompDbContext db,
    AppUserContextService userContext) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? date, CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var targetDate = date ?? AppUserContextService.GetClientToday(context.Client);

        var row = await db.NutritionDays
            .SingleOrDefaultAsync(x => x.ProgramId == context.Program.ProgramId && x.NutritionDate == targetDate, cancellationToken);

        return View(new NutritionViewModel
        {
            Date = targetDate,
            ActualCalories = row?.ActualCalories,
            ActualProteinG = row?.ActualProteinG,
            ActualCarbsG = row?.ActualCarbsG,
            ActualFatG = row?.ActualFatG,
            FiberG = row?.FiberG,
            WaterOz = row?.WaterOz,
            Notes = row?.Notes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(NutritionViewModel model, CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!ModelState.IsValid)
        {
            if (IsAjaxRequest())
                return BadRequest(new { success = false, message = FirstModelError("Check the nutrition values.") });

            return View(model);
        }

        var row = await db.NutritionDays
            .SingleOrDefaultAsync(x => x.ProgramId == context.Program.ProgramId && x.NutritionDate == model.Date, cancellationToken);

        if (row is null)
        {
            row = new NutritionDay
            {
                NutritionDayId = Guid.NewGuid(),
                ClientId = context.Client.ClientId,
                ProgramId = context.Program.ProgramId,
                NutritionDate = model.Date,
                EntrySourceCode = "MANUAL",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.NutritionDays.Add(row);
        }

        row.ActualCalories = model.ActualCalories;
        row.ActualProteinG = model.ActualProteinG;
        row.ActualCarbsG = model.ActualCarbsG;
        row.ActualFatG = model.ActualFatG;
        row.FiberG = model.FiberG;
        row.WaterOz = model.WaterOz;
        row.Notes = model.Notes;
        row.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var requiredComplete =
            model.ActualCalories.HasValue &&
            model.ActualProteinG.HasValue &&
            model.ActualCarbsG.HasValue &&
            model.ActualFatG.HasValue;

        if (IsAjaxRequest())
        {
            return Json(new
            {
                success = true,
                requiredComplete,
                message = requiredComplete
                    ? "Saved automatically."
                    : "Draft saved — complete the * fields."
            });
        }

        TempData["StatusMessage"] = requiredComplete
            ? "Nutrition saved."
            : "Nutrition draft saved. Complete the required fields when totals are available.";

        return RedirectToAction(nameof(Index), new { date = model.Date.ToString("yyyy-MM-dd") });
    }

    private bool IsAjaxRequest()
        => string.Equals(
            Request.Headers["X-Requested-With"].ToString(),
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);

    private string FirstModelError(string fallback)
        => ModelState.Values
               .SelectMany(x => x.Errors)
               .Select(x => x.ErrorMessage)
               .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
           ?? fallback;
}
