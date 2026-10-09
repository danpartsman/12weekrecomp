using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.ViewModels;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine30;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin/users")]
public sealed class AdminUsersController(
    RecompDbContext db, Engine30DashboardProjectionService dashboardProjection) : Controller
{
    [HttpGet("{userId:guid}/calorie-inputs")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CalorieInputs(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.UserAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (user is null) return NotFound();

        var clientIds = db.UserClientAccess.Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .Select(x => x.ClientId);
        var clients = await db.Clients.AsNoTracking().Where(x => clientIds.Contains(x.ClientId))
            .ToListAsync(cancellationToken);
        var profiles = new List<object>();
        var activityProfiles = new Engine30ClientActivityProfileService(db);
        foreach (var client in clients)
        {
            var today = AppUserContextService.GetClientToday(client);
            var measurement = await db.Measurements.AsNoTracking()
                .Where(x => x.ClientId == client.ClientId && !x.IsDeleted && x.WeightLb > 0m
                    && x.LocalMeasurementDate <= today)
                .OrderByDescending(x => x.LocalMeasurementDate).ThenByDescending(x => x.MeasurementAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            var programs = await db.Programs.AsNoTracking()
                .Where(x => x.ClientId == client.ClientId && x.ProgramStatusCode == "ACTIVE")
                .Select(x => new { x.ProgramId, x.GoalCode, x.SelectedPlanCode, x.AvailableTrainingDaysWeek })
                .ToListAsync(cancellationToken);
            var roles = await activityProfiles.BuildRuntimeRolesAsync(client.ClientId, today, cancellationToken);
            Engine30BaseLivingResult? resting = null;
            Engine30HabitualActivityResult? habitual = null;
            if (client.DateOfBirth is DateOnly birth && birth <= today.AddYears(-19)
                && client.HeightIn > 0m && measurement?.WeightLb is decimal weight
                && client.SexCode is "MALE" or "FEMALE")
            {
                resting = new Engine30BaseLivingCalculator().Calculate(
                    new Engine30BaseLivingInput(today, birth, client.SexCode, client.HeightIn, weight));
                habitual = new Engine30HabitualActivityCalculator().Calculate(
                    resting.BaseLivingEnergyKcal, roles,
                    Engine30ActivityProfileCatalog.ResolveLifestyle(client.LifestyleActivityCode).Code,
                    habitualWindowHoursPerWeek: Engine30HabitualActivityWindowPolicy.Resolve(resting.AgeYears).HoursPerWeek);
            }
            var recommendations = await db.Recommendations.AsNoTracking()
                .Where(x => x.ClientId == client.ClientId && x.ActionDate >= today.AddDays(-7) && x.ActionDate <= today)
                .OrderByDescending(x => x.CreatedAtUtc).Take(14)
                .Select(x => new { x.ProgramId, x.ActionDate, x.BaseCalories, x.RecommendedCalories,
                    x.RecommendedWorkoutTypeCode, x.CreatedAtUtc }).ToListAsync(cancellationToken);
            Engine30DashboardTodayTomorrowProjection? currentPrescription = null;
            string? prescriptionIssue = null;
            if (resting is not null && programs.Count > 0)
            {
                var activeProgram = await db.Programs.AsNoTracking()
                    .Where(x => x.ClientId == client.ClientId && x.ProgramStatusCode == "ACTIVE")
                    .OrderByDescending(x => x.StartDate).FirstAsync(cancellationToken);
                try
                {
                    currentPrescription = await dashboardProjection.BuildTodayTomorrowAsync(
                        new AppClientContext(user, client, activeProgram), today, cancellationToken);
                }
                catch (InvalidOperationException exception)
                {
                    prescriptionIssue = exception.Message;
                }
            }
            profiles.Add(new
            {
                client.ClientName, client.ClientId, ActionDate = today,
                AgeYears = resting?.AgeYears, client.SexCode, client.HeightIn,
                WeightLb = measurement?.WeightLb, MeasurementDate = measurement?.LocalMeasurementDate,
                client.LifestyleActivityCode, LifestyleWasCollected = !string.IsNullOrWhiteSpace(client.LifestyleActivityCode),
                OccupationalRoles = roles,
                RestingEnergyKcal = resting?.BaseLivingEnergyKcal,
                HabitualActivityKcalPerDay = habitual?.AverageDailyHabitualActivityEnergyKcal,
                NonWorkoutMaintenanceKcalPerDay = habitual?.BasePlusHabitualEnergyKcal,
                Programs = programs, RecentSavedRecommendations = recommendations,
                CurrentDashboardPrescription = currentPrescription,
                PrescriptionIssue = prescriptionIssue
            });
        }
        var inspection = new
        {
            UserName = user.PreferredDisplayName ?? user.DisplayName,
            Note = "Read-only saved inputs. Non-workout maintenance excludes formal exercise. Recent saved recommendations are calorie targets, not maintenance, and may predate profile changes. No work rows alone cannot distinguish retired from skipped onboarding.",
            Profiles = profiles
        };
        var json = System.Text.Json.JsonSerializer.Serialize(inspection,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var encoded = System.Net.WebUtility.HtmlEncode(json);
        return Content("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Calorie input inspection</title></head>"
            + "<body><main><a href=\"/admin/users\">Back to User Management</a><h1>Calorie input inspection</h1>"
            + "<pre style=\"white-space:pre-wrap;overflow-wrap:anywhere\">" + encoded + "</pre></main></body></html>",
            "text/html; charset=utf-8");
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        var term = (search ?? string.Empty).Trim();
        var usersQuery = db.UserAccounts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(term))
        {
            usersQuery = usersQuery.Where(x =>
                (x.EmailNormalized != null && x.EmailNormalized.Contains(term)) ||
                (x.DisplayName != null && x.DisplayName.Contains(term)) ||
                (x.PreferredDisplayName != null && x.PreferredDisplayName.Contains(term)));
        }

        var users = await usersQuery
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ThenBy(x => x.EmailNormalized)
            .Take(1000)
            .ToListAsync(cancellationToken);

        var credentials = await ReadCredentialsAsync(cancellationToken);
        var currentUserId = CurrentUserId();

        return View(new AdminUsersIndexViewModel
        {
            Search = term,
            Users = users.Select(user =>
            {
                var hasCredential = credentials.TryGetValue(user.UserId, out var credential);
                return new AdminUserRowViewModel
                {
                    UserId = user.UserId,
                    DisplayName = user.PreferredDisplayName ?? user.DisplayName ?? "—",
                    Email = user.EmailNormalized ?? user.ContactEmail ?? "—",
                    AccountType = hasCredential ? "12WR account" : "Microsoft account",
                    StatusCode = user.AccountStatusCode,
                    RoleCode = hasCredential ? credential!.RoleCode : "Microsoft / external",
                    IsLocalAccount = hasCredential,
                    IsCurrentUser = currentUserId == user.UserId,
                    CreatedAtUtc = user.CreatedAtUtc,
                    UpdatedAtUtc = user.UpdatedAtUtc,
                    LastSignInAtUtc = hasCredential ? credential!.LastSignInAtUtc : null
                };
            }).ToArray()
        });
    }

    [HttpPost("{userId:guid}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(Guid userId, string status, string? search, CancellationToken cancellationToken)
    {
        var normalized = (status ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized is not ("ACTIVE" or "SUSPENDED"))
            return BadRequest("Unsupported account status.");

        if (CurrentUserId() == userId && normalized != "ACTIVE")
        {
            TempData["StatusMessage"] = "You cannot suspend the administrator account you are currently using.";
            return RedirectToAction(nameof(Index), new { search });
        }

        var user = await db.UserAccounts.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (user is null)
            return NotFound();

        user.AccountStatusCode = normalized;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = $"{user.EmailNormalized ?? user.DisplayName ?? "User"} is now {normalized}.";
        return RedirectToAction(nameof(Index), new { search });
    }

    [HttpPost("{userId:guid}/role")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(Guid userId, string role, string? search, CancellationToken cancellationToken)
    {
        var normalized = (role ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized is not ("CLIENT" or "ADMINISTRATOR"))
            return BadRequest("Unsupported role.");

        if (CurrentUserId() == userId && normalized != "ADMINISTRATOR")
        {
            TempData["StatusMessage"] = "You cannot remove Administrator from the account you are currently using.";
            return RedirectToAction(nameof(Index), new { search });
        }

        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE app.LocalAuthCredentials
            SET GlobalRoleCode = {normalized},
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE UserId = {userId};
            """,
            cancellationToken);

        TempData["StatusMessage"] = affected == 0
            ? "That is a legacy Microsoft account. Its Microsoft identity remains listed here, but its role is still managed by the Microsoft compatibility sign-in configuration."
            : $"Role updated to {normalized}.";

        return RedirectToAction(nameof(Index), new { search });
    }

    [HttpPost("{userId:guid}/update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateUser(
        Guid userId,
        string? displayName,
        string? email,
        string? search,
        CancellationToken cancellationToken)
    {
        var user = await db.UserAccounts.SingleOrDefaultAsync(
            x => x.UserId == userId,
            cancellationToken);

        if (user is null)
            return NotFound();

        var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            TempData["StatusMessage"] = "Email cannot be blank.";
            return RedirectToAction(nameof(Index), new { search });
        }

        var conflict = await db.UserAccounts.AnyAsync(
            x => x.UserId != userId && x.EmailNormalized == normalizedEmail,
            cancellationToken);

        if (conflict)
        {
            TempData["StatusMessage"] =
                "That email is already used by another account. Change or delete the duplicate first.";
            return RedirectToAction(nameof(Index), new { search });
        }

        var name = (displayName ?? string.Empty).Trim();
        user.EmailNormalized = normalizedEmail;
        user.ContactEmail = normalizedEmail;
        user.DisplayName = string.IsNullOrWhiteSpace(name) ? null : name;
        user.PreferredDisplayName = string.IsNullOrWhiteSpace(name) ? null : name;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "User account updated.";
        return RedirectToAction(nameof(Index), new { search });
    }

    [HttpPost("{userId:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(
        Guid userId,
        string? search,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId() == userId)
        {
            TempData["StatusMessage"] =
                "You cannot delete the administrator account you are currently using.";
            return RedirectToAction(nameof(Index), new { search });
        }

        var user = await db.UserAccounts.SingleOrDefaultAsync(
            x => x.UserId == userId,
            cancellationToken);

        if (user is null)
            return RedirectToAction(nameof(Index), new { search });

        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction =
                    await db.Database.BeginTransactionAsync(cancellationToken);

                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM app.LocalAuthCredentials
                    WHERE UserId = {userId};
                    """,
                    cancellationToken);

                db.UserAccounts.Remove(user);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });

            TempData["StatusMessage"] =
                "User account deleted.";
        }
        catch (DbException)
        {
            TempData["StatusMessage"] =
                "That account still has 12WR data attached to it, so it was not deleted. Suspend it or move/clean up its data first.";
        }

        return RedirectToAction(nameof(Index), new { search });
    }

    private Guid? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }

    private async Task<Dictionary<Guid, CredentialInfo>> ReadCredentialsAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, CredentialInfo>();
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;

        if (closeWhenDone)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT UserId, GlobalRoleCode, LastSignInAtUtc
                FROM app.LocalAuthCredentials;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result[reader.GetGuid(0)] = new CredentialInfo(
                    reader.IsDBNull(1) ? "CLIENT" : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetDateTime(2));
            }
        }
        catch (DbException)
        {
            // TEST can still contain pre-native-auth databases. In that case
            // every existing account is displayed as a Microsoft account.
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }

        return result;
    }

    private sealed record CredentialInfo(string RoleCode, DateTime? LastSignInAtUtc);
}

