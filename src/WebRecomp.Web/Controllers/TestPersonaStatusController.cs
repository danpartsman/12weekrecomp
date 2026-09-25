using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

[AllowAnonymous]
[Route("test-persona/status")]
public sealed class TestPersonaStatusController(
    RecompDbContext db,
    TestPersonaSessionService sessions,
    INutritionFoodLogStore nutritionStore) : ControllerBase
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
                rows.Add(Empty(spec.Email));
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

            var program = clientId.HasValue
                ? await db.Programs
                    .AsNoTracking()
                    .Where(x =>
                        x.ClientId == clientId.Value
                        && x.ProgramStatusCode == "ACTIVE")
                    .OrderByDescending(x => x.StartDate)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;

            var measurements = clientId.HasValue
                ? await db.Measurements.CountAsync(
                    x => x.ClientId == clientId.Value && !x.IsDeleted,
                    cancellationToken)
                : 0;

            var dailyCheckins = program is not null
                ? await db.DailyCheckins.CountAsync(
                    x => x.ProgramId == program.ProgramId,
                    cancellationToken)
                : 0;

            var nutritionDays = program is not null
                ? await db.NutritionDays.CountAsync(
                    x => x.ProgramId == program.ProgramId,
                    cancellationToken)
                : 0;

            var completedWorkouts = program is not null
                ? await db.WorkoutSessions.CountAsync(
                    x => x.ProgramId == program.ProgramId && x.CompletedFlag,
                    cancellationToken)
                : 0;

            var plannedWorkouts = program is not null
                ? await db.PlannedWorkouts.CountAsync(
                    x => x.ProgramId == program.ProgramId,
                    cancellationToken)
                : 0;

            var foodLogDays = 0;
            if (clientId.HasValue && program is not null)
            {
                for (var day = 0; day < 28; day++)
                {
                    var log = await nutritionStore.GetDailyLogAsync(
                        clientId.Value,
                        program.StartDate.AddDays(day),
                        cancellationToken);

                    if (log.Items.Count >= 4)
                        foodLogDays++;
                }
            }

            var active = string.Equals(
                user.AccountStatusCode,
                "ACTIVE",
                StringComparison.OrdinalIgnoreCase);

            // A persona is ready for immediate analysis AND UI testing when
            // core Engine history and all 28 item-level food-log days exist.
            var ready =
                active
                && clientId.HasValue
                && program is not null
                && measurements >= 29
                && dailyCheckins >= 29
                && nutritionDays >= 28
                && foodLogDays >= 28
                && completedWorkouts > 0
                && plannedWorkouts >= 28;

            rows.Add(new TestPersonaStatusRow(
                spec.Email,
                Exists: true,
                Active: active,
                Client: clientId.HasValue,
                Program: program is not null,
                Measurements: measurements,
                DailyCheckins: dailyCheckins,
                NutritionDays: nutritionDays,
                FoodLogDays: foodLogDays,
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

    // Temporary TEST-only auth diagnostic. It is deliberately limited to the
    // synthetic test.com namespace and never returns password/hash material.
    [HttpGet("local-auth-check")]
    public async Task<IActionResult> LocalAuthCheck(
        string email,
        CancellationToken cancellationToken)
    {
        if (!sessions.IsEnabled)
            return NotFound();

        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (!normalizedEmail.EndsWith("@test.com", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "TEST-domain accounts only." });

        var matching = await db.UserAccounts
            .AsNoTracking()
            .Where(x => x.EmailNormalized == normalizedEmail)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matching.Count != 1)
        {
            return Ok(new
            {
                environment = "TEST",
                email = normalizedEmail,
                userCount = matching.Count,
                credentialExists = false
            });
        }

        var user = matching[0];
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;

        if (closeWhenDone)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT PasswordHash,
                       GlobalRoleCode,
                       FailedSignInCount,
                       LockoutEndUtc,
                       LastSignInAtUtc
                FROM app.LocalAuthCredentials
                WHERE UserId = @userId;
                """;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@userId";
            parameter.Value = user.UserId;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return Ok(new
                {
                    environment = "TEST",
                    email = normalizedEmail,
                    userCount = 1,
                    active = string.Equals(user.AccountStatusCode, "ACTIVE", StringComparison.OrdinalIgnoreCase),
                    externalSubjectKind = user.ExternalSubject.StartsWith("local:", StringComparison.OrdinalIgnoreCase) ? "local" : "external",
                    credentialExists = false
                });
            }

            var passwordHash = reader.GetString(0);
            var roleCode = reader.GetString(1);
            var failedSignInCount = reader.GetInt32(2);
            var lockoutEndUtc = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3);
            var lastSignInAtUtc = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4);

            int? passwordHashFormatMarker = null;
            try
            {
                var decoded = Convert.FromBase64String(passwordHash);
                if (decoded.Length > 0)
                    passwordHashFormatMarker = decoded[0];
            }
            catch (FormatException)
            {
                // Structural signal only; no credential material is returned.
            }

            return Ok(new
            {
                environment = "TEST",
                email = normalizedEmail,
                userCount = 1,
                active = string.Equals(user.AccountStatusCode, "ACTIVE", StringComparison.OrdinalIgnoreCase),
                externalSubjectKind = user.ExternalSubject.StartsWith("local:", StringComparison.OrdinalIgnoreCase) ? "local" : "external",
                credentialExists = true,
                passwordHashLength = passwordHash.Length,
                passwordHashIsBase64 = passwordHashFormatMarker.HasValue,
                passwordHashFormatMarker,
                roleCode,
                failedSignInCount,
                lockoutEndUtc,
                lastSignInAtUtc
            });
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }
    }

    private static TestPersonaStatusRow Empty(string email)
        => new(
            email,
            Exists: false,
            Active: false,
            Client: false,
            Program: false,
            Measurements: 0,
            DailyCheckins: 0,
            NutritionDays: 0,
            FoodLogDays: 0,
            CompletedWorkouts: 0,
            PlannedWorkouts: 0,
            Ready: false);

    private sealed record TestPersonaStatusRow(
        string Email,
        bool Exists,
        bool Active,
        bool Client,
        bool Program,
        int Measurements,
        int DailyCheckins,
        int NutritionDays,
        int FoodLogDays,
        int CompletedWorkouts,
        int PlannedWorkouts,
        bool Ready);
}
