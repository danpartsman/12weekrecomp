using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

[AllowAnonymous]
[Route("test-persona/status")]
public sealed class TestPersonaStatusController(
    RecompDbContext db,
    TestPersonaSessionService sessions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        if (!sessions.IsEnabled)
            return NotFound();

        var rows = new List<TestPersonaStatusRow>();

        foreach (var spec in TestPersonaCatalog.All)
        {
            var subject = TestPersonaCatalog.ExternalSubject(spec.Email);
            var user = await db.UserAccounts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.ExternalSubject == subject,
                    cancellationToken);

            if (user is null)
            {
                rows.Add(new TestPersonaStatusRow(
                    spec.Email,
                    Exists: false,
                    Active: false,
                    Client: false,
                    Program: false,
                    Measurements: 0,
                    DailyCheckins: 0,
                    NutritionDays: 0,
                    CompletedWorkouts: 0,
                    PlannedWorkouts: 0,
                    Ready: false));
                continue;
            }

            var clientId = await db.UserClientAccess
                .AsNoTracking()
                .Where(x =>
                    x.UserId == user.UserId
                    && x.RevokedAtUtc == null
                    && x.AccessRoleCode == "OWNER")
                .Select(x => (Guid?)x.ClientId)
                .FirstOrDefaultAsync(cancellationToken);

            var programId = clientId.HasValue
                ? await db.Programs
                    .AsNoTracking()
                    .Where(x =>
                        x.ClientId == clientId.Value
                        && x.ProgramStatusCode == "ACTIVE")
                    .OrderByDescending(x => x.StartDate)
                    .Select(x => (Guid?)x.ProgramId)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;

            var measurements = clientId.HasValue
                ? await db.Measurements.CountAsync(
                    x => x.ClientId == clientId.Value && !x.IsDeleted,
                    cancellationToken)
                : 0;

            var dailyCheckins = programId.HasValue
                ? await db.DailyCheckins.CountAsync(
                    x => x.ProgramId == programId.Value,
                    cancellationToken)
                : 0;

            var nutritionDays = programId.HasValue
                ? await db.NutritionDays.CountAsync(
                    x => x.ProgramId == programId.Value,
                    cancellationToken)
                : 0;

            var completedWorkouts = programId.HasValue
                ? await db.WorkoutSessions.CountAsync(
                    x => x.ProgramId == programId.Value && x.CompletedFlag,
                    cancellationToken)
                : 0;

            var plannedWorkouts = programId.HasValue
                ? await db.PlannedWorkouts.CountAsync(
                    x => x.ProgramId == programId.Value,
                    cancellationToken)
                : 0;

            var active = string.Equals(
                user.AccountStatusCode,
                "ACTIVE",
                StringComparison.OrdinalIgnoreCase);

            // A persona is ready for immediate analysis when it has:
            // - the account/client/program chain,
            // - 28 historical daily records + today's morning measurement/check-in,
            // - historical nutrition,
            // - completed actual training,
            // - enough Weekly Plan rows to cover at least the historical window.
            var ready =
                active
                && clientId.HasValue
                && programId.HasValue
                && measurements >= 29
                && dailyCheckins >= 29
                && nutritionDays >= 28
                && completedWorkouts > 0
                && plannedWorkouts >= 28;

            rows.Add(new TestPersonaStatusRow(
                spec.Email,
                Exists: true,
                Active: active,
                Client: clientId.HasValue,
                Program: programId.HasValue,
                Measurements: measurements,
                DailyCheckins: dailyCheckins,
                NutritionDays: nutritionDays,
                CompletedWorkouts: completedWorkouts,
                PlannedWorkouts: plannedWorkouts,
                Ready: ready));
        }

        return Ok(new
        {
            environment = "TEST",
            personaCount = rows.Count,
            ready = rows.Count == TestPersonaCatalog.All.Count && rows.All(x => x.Ready),
            personas = rows
        });
    }

    private sealed record TestPersonaStatusRow(
        string Email,
        bool Exists,
        bool Active,
        bool Client,
        bool Program,
        int Measurements,
        int DailyCheckins,
        int NutritionDays,
        int CompletedWorkouts,
        int PlannedWorkouts,
        bool Ready);
}
