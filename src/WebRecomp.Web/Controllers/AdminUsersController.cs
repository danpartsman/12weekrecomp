using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin/users")]
public sealed class AdminUsersController(RecompDbContext db) : Controller
{
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
