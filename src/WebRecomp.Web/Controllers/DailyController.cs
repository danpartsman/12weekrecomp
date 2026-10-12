using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

public sealed class DailyController(
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

        var checkin = await db.DailyCheckins
            .SingleOrDefaultAsync(x => x.ProgramId == context.Program.ProgramId && x.CheckinDate == targetDate, cancellationToken);

        var measurement = await db.Measurements
            .Where(x => x.ProgramId == context.Program.ProgramId && x.LocalMeasurementDate == targetDate && !x.IsDeleted)
            .OrderByDescending(x => x.MeasurementAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var vm = new DailyViewModel
        {
            Date = targetDate,
            WeightLb = measurement?.WeightLb,
            BodyFatPercent = measurement?.BodyFatPct * 100m,
            MuscleMassLb = measurement?.MuscleMassLb,
            BodyWaterLb = measurement?.BodyWaterLb,
            VisceralFatRating = measurement?.VisceralFatRating,
            WaistIn = measurement?.WaistIn,
            HipsIn = measurement?.HipsIn,
            SleepHours = checkin?.SleepHours,
            RestingHr = checkin?.RestingHr,
            Steps = checkin?.Steps,
            EnergyScore = checkin?.EnergyScore,
            SorenessScore = checkin?.SorenessScore,
            PainFlag = checkin?.PainFlag ?? false,
            IllnessFlag = checkin?.IllnessFlag ?? false,
            Notes = WebRecomp.Web.Services.WorkoutTargeting.BodyCheckinCodec.Read(checkin?.Notes).Note
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(DailyViewModel model, CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!ModelState.IsValid)
        {
            if (IsAjaxRequest())
                return BadRequest(new { success = false, message = FirstModelError("Check the highlighted daily values.") });

            return View(model);
        }

        var checkin = await db.DailyCheckins
            .SingleOrDefaultAsync(x => x.ProgramId == context.Program.ProgramId && x.CheckinDate == model.Date, cancellationToken);

        if (checkin is null)
        {
            checkin = new DailyCheckin
            {
                CheckinId = Guid.NewGuid(),
                ClientId = context.Client.ClientId,
                ProgramId = context.Program.ProgramId,
                CheckinDate = model.Date,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.DailyCheckins.Add(checkin);
        }

        checkin.SleepHours = model.SleepHours;
        checkin.RestingHr = model.RestingHr;
        checkin.Steps = model.Steps;
        checkin.EnergyScore = model.EnergyScore;
        checkin.SorenessScore = model.SorenessScore;
        checkin.PainFlag = model.PainFlag;
        checkin.IllnessFlag = model.IllnessFlag;
        checkin.IntendedWorkoutTypeCode = null;
        checkin.Notes = WebRecomp.Web.Services.WorkoutTargeting.BodyCheckinCodec.UpdateNote(checkin.Notes, model.Notes);
        checkin.UpdatedAtUtc = DateTime.UtcNow;

        var hasMeasurementValues =
            model.WeightLb.HasValue ||
            model.BodyFatPercent.HasValue ||
            model.MuscleMassLb.HasValue ||
            model.BodyWaterLb.HasValue ||
            model.VisceralFatRating.HasValue ||
            model.WaistIn.HasValue ||
            model.HipsIn.HasValue;

        var dailySourceRecordId = $"DAILY:{model.Date:yyyy-MM-dd}";

        var measurement = await db.Measurements
            .SingleOrDefaultAsync(
                x => x.ClientId == context.Client.ClientId
                     && x.ProgramId == context.Program.ProgramId
                     && x.MeasurementSourceCode == "DAILY"
                     && x.SourceRecordId == dailySourceRecordId
                     && !x.IsDeleted,
                cancellationToken);

        if (hasMeasurementValues || measurement is not null)
        {
            if (measurement is null)
            {
                measurement = new Measurement
                {
                    MeasurementId = Guid.NewGuid(),
                    ClientId = context.Client.ClientId,
                    ProgramId = context.Program.ProgramId,
                    LocalMeasurementDate = model.Date,
                    MeasurementSourceCode = "DAILY",
                    SourceRecordId = dailySourceRecordId,
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Measurements.Add(measurement);
            }

            measurement.MeasurementAtUtc = DateTime.UtcNow;
            measurement.WeightLb = model.WeightLb;
            measurement.BodyFatPct = model.BodyFatPercent.HasValue ? model.BodyFatPercent.Value / 100m : null;
            measurement.MuscleMassLb = model.MuscleMassLb;
            measurement.BodyWaterLb = model.BodyWaterLb;
            measurement.VisceralFatRating = model.VisceralFatRating;
            measurement.WaistIn = model.WaistIn;
            measurement.HipsIn = model.HipsIn;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (IsAjaxRequest())
            return Json(new { success = true, message = "Saved automatically." });

        TempData["StatusMessage"] = "Weigh In Stats saved.";
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
