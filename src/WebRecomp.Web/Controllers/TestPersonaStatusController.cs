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

        var rows = new List<object>();

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
                rows.Add(new
                {
                    email = spec.Email,
                    exists = false
                });
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

            rows.Add(new
            {
                email = spec.Email,
                exists = true,
                active = user.AccountStatusCode == "ACTIVE",
                client = clientId.HasValue,
                program = programId.HasValue,
                measurements = clientId.HasValue
                    ? await db.Measurements.CountAsync(
                        x => x.ClientId == clientId.Value && !x.IsDeleted,
                        cancellationToken)
                    : 0,
                dailyCheckins = programId.HasValue
                    ? await db.DailyCheckins.CountAsync(
                        x => x.ProgramId == programId.Value,
                        cancellationToken)
                    : 0,
                nutritionDays = programId.HasValue
                    ? await db.NutritionDays.CountAsync(
                        x => x.ProgramId == programId.Value,
                        cancellationToken)
                    : 0,
                completedWorkouts = programId.HasValue
                    ? await db.WorkoutSessions.CountAsync(
                        x => x.ProgramId == programId.Value && x.CompletedFlag,
                        cancellationToken)
                    : 0,
                plannedWorkouts = programId.HasValue
                    ? await db.PlannedWorkouts.CountAsync(
                        x => x.ProgramId == programId.Value,
                        cancellationToken)
                    : 0
            });
        }

        return Ok(new
        {
            environment = "TEST",
            personaCount = rows.Count,
            personas = rows
        });
    }
}
