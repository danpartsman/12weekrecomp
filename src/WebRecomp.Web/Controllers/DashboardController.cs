using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

public sealed class DashboardController(
    RecompDbContext db,
    AppUserContextService userContext,
    DashboardService dashboard) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);

        // The Dashboard is an Engine 3.0 calculation surface, not an onboarding
        // form. Never let a new or interrupted account reach the engine until
        // the minimum real physiology + plan inputs exist.
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Onboarding");

        var client = context.Client;
        var program = context.Program;
        var today = AppUserContextService.GetClientToday(client);

        var profileReady =
            client.DateOfBirth.HasValue
            && !string.IsNullOrWhiteSpace(client.SexCode)
            && client.HeightIn >= 48m
            && client.HeightIn <= 90m
            && !string.IsNullOrWhiteSpace(program.GoalCode)
            && program.AvailableTrainingDaysWeek is >= 1 and <= 7;

        var hasUsableWeight = await db.Measurements
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.ClientId == client.ClientId
                    && x.ProgramId == program.ProgramId
                    && !x.IsDeleted
                    && x.WeightLb.HasValue
                    && x.WeightLb.Value > 0m
                    && x.LocalMeasurementDate <= today,
                cancellationToken);

        if (!profileReady || !hasUsableWeight)
        {
            TempData["StatusMessage"] =
                "Finish the short setup first. Recomp needs your real weight, age, calculation sex, height and plan direction before Engine 3.0 can safely calculate a Dashboard prescription.";

            return RedirectToAction("Index", "Onboarding");
        }

        // A short orientation reminder for newly created accounts. Do not
        // substitute plan data for actual entries or prompt on rest days.
        if (context.User.CreatedAtUtc > DateTime.UtcNow.AddDays(-8))
        {
            var createdUtc = DateTime.SpecifyKind(
                context.User.CreatedAtUtc,
                DateTimeKind.Utc);
            DateOnly createdDate;
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(client.TimeZone);
                createdDate = DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTimeFromUtc(createdUtc, zone));
            }
            catch (TimeZoneNotFoundException)
            {
                createdDate = DateOnly.FromDateTime(createdUtc);
            }

            if (today >= createdDate && today < createdDate.AddDays(7))
            {
                var hasTodayWeight = await db.Measurements.AsNoTracking().AnyAsync(
                    x => x.ClientId == client.ClientId
                        && x.ProgramId == program.ProgramId
                        && x.LocalMeasurementDate == today
                        && !x.IsDeleted
                        && x.WeightLb.HasValue
                        && x.WeightLb.Value > 0m,
                    cancellationToken);
                var hasWorkout = await db.WorkoutSessions.AsNoTracking().AnyAsync(
                    x => x.ClientId == client.ClientId
                        && x.ProgramId == program.ProgramId
                        && x.WorkoutDate == today
                        && x.CompletedFlag,
                    cancellationToken);
                var activityRecorded = await db.DailyCheckins.AsNoTracking().AnyAsync(
                    x => x.ProgramId == program.ProgramId
                        && x.CheckinDate == today
                        && x.DailyActivityHours.HasValue,
                    cancellationToken);
                var isRestDay = await db.PlannedWorkouts.AsNoTracking().AnyAsync(
                    x => x.ProgramId == program.ProgramId
                        && x.PlannedDate == today
                        && x.WorkoutTypeCode == "REST",
                    cancellationToken);
                var hasNutrition = await db.NutritionDays.AsNoTracking().AnyAsync(
                    x => x.ProgramId == program.ProgramId
                        && x.NutritionDate == today
                        && x.ActualCalories.HasValue
                        && x.ActualProteinG.HasValue
                        && x.ActualCarbsG.HasValue
                        && x.ActualFatG.HasValue,
                    cancellationToken);

                ViewData["ReminderWeight"] = !hasTodayWeight;
                ViewData["ReminderWorkout"] = !hasWorkout && !activityRecorded && !isRestDay;
                ViewData["ReminderNutrition"] = !hasNutrition;
                ViewData["ReminderKey"] =
                    $"recomp-entry-guide:{context.User.UserId}:{today:yyyy-MM-dd}";
            }
        }

        var vm = await dashboard.BuildAsync(context, today, cancellationToken);
        return View(vm);
    }
    [HttpGet("/Dashboard2")]
    [HttpGet("/Dashboard/Dashboard2")]
    public async Task<IActionResult> Dashboard2(
        [FromServices] WebRecomp.NutritionIntelligence.Persistence.INutritionFoodLogStore foodLog,
        [FromServices] ILogger<DashboardController> logger,
        int? occasions,
        CancellationToken cancellationToken)
    {
        var result = await Index(cancellationToken);
        if (result is not ViewResult view || view.Model is not WebRecomp.Web.ViewModels.DashboardViewModel day)
            return result;
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        WebRecomp.NutritionIntelligence.Persistence.NutritionDailyLogReadModel? log = null;
        var row = await db.NutritionDays.AsNoTracking().SingleOrDefaultAsync(
            x => x.ProgramId == context.Program!.ProgramId && x.NutritionDate == day.ActionDate,
            cancellationToken);
        var source = row?.EntrySourceCode;
        var useFood = source is null || source == "FOOD_LOG" || source == "MANUAL";
        if (context.Program!.NutritionFoodLogEnabled)
        {
            try { log = await foodLog.GetDailyLogAsync(context.Client!.ClientId, day.ActionDate, cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Dashboard2 meal log unavailable; using daily totals");
                ViewData["MealLogNotice"] = "Meal details are temporarily unavailable. Daily totals are shown; meal timing has not been inferred.";
            }
        }
        var hour = DateTime.UtcNow.Hour;
        try { hour = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(context.Client!.TimeZone)).Hour; }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }
        ViewData["MacroDayPlan"] = WebRecomp.Web.Services.Dashboard2.MacroDayPlanner.Build(day, log, useFood, hour, occasions);
        view.ViewName = "Dashboard2";
        return view;
    }

    [HttpGet("/Dashboard3")]
    [HttpGet("/Dashboard/Dashboard3")]
    public async Task<IActionResult> Dashboard3(
        [FromServices] WebRecomp.NutritionIntelligence.Persistence.INutritionFoodLogStore foodLog,
        [FromServices] ILogger<DashboardController> logger,
        [FromServices] WebRecomp.Web.Services.Dashboard3.RecompCockpitService cockpit,
        int? occasions, CancellationToken cancellationToken)
    {
        var result = await Dashboard2(foodLog, logger, occasions, cancellationToken);
        if (result is not ViewResult view || view.Model is not WebRecomp.Web.ViewModels.DashboardViewModel day)
            return result;
        if (!day.NeedsCurrentWeight)
        {
            var context = await userContext.GetAsync(HttpContext, cancellationToken);
            ViewData["RecompCockpit"] = await cockpit.BuildAsync(context, day,
                (WebRecomp.Web.Services.Dashboard2.MacroDayPlan)ViewData["MacroDayPlan"]!, cancellationToken);
        }
        view.ViewName = "Dashboard3";
        return view;
    }

    [HttpPost("/Dashboard3/FlexibleItems")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FlexibleItems(
        [FromServices] WebRecomp.Web.Services.Dashboard3.RecompCockpitService cockpit,
        DateOnly date, int? count, CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null) return RedirectToAction("Index", "Onboarding");
        if (!ModelState.IsValid || !count.HasValue) return BadRequest("Enter the number of flexible items for the selected day.");
        try { await cockpit.SaveFlexibleItemsAsync(context, date, count.Value, cancellationToken); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        TempData["StatusMessage"] = "Flexible item count saved. Your Recomp Score has been refreshed.";
        return Redirect("/Dashboard3");
    }

}

