using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record AppClientContext(
    UserAccount User,
    Client? Client,
    ProgramRecord? Program);

public sealed class AppUserContextService(
    RecompDbContext db,
    IConfiguration configuration)
{
    public async Task<AppClientContext> GetAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var principal = httpContext.User;

        string? externalSubject = null;
        string? principalName = null;

        if (principal.Identity?.IsAuthenticated == true)
        {
            // External ID's object ID is our durable Recomp identity key.
            // Check both the short OIDC claim name and Microsoft's mapped form.
            externalSubject =
                GetClaim(principal, "oid")
                ?? GetClaim(
                    principal,
                    "http://schemas.microsoft.com/identity/claims/objectidentifier");

            // Fallback only if oid is unexpectedly unavailable.
            externalSubject ??= GetClaim(principal, "sub");

            principalName =
                GetClaim(principal, "preferred_username")
                ?? GetClaim(principal, "email")
                ?? GetClaim(principal, ClaimTypes.Email)
                ?? GetClaim(principal, "name")
                ?? principal.Identity.Name;
        }

        if (string.IsNullOrWhiteSpace(externalSubject))
        {
            var allowDevelopmentUser =
                configuration.GetValue<bool>("WebRecomp:AllowDevelopmentUser");

            if (!allowDevelopmentUser)
            {
                throw new UnauthorizedAccessException(
                    "No authenticated Recomp customer principal was found.");
            }

            externalSubject = "development-user";
            principalName ??= "development@example.local";
        }

        externalSubject = externalSubject.Trim();

        var normalizedEmail = string.IsNullOrWhiteSpace(principalName)
            ? null
            : principalName.Trim().ToLowerInvariant();

        var user = await db.UserAccounts
            .SingleOrDefaultAsync(
                x => x.ExternalSubject == externalSubject,
                cancellationToken);

        if (user is null)
        {
            user = new UserAccount
            {
                UserId = Guid.NewGuid(),
                ExternalSubject = externalSubject,
                EmailNormalized = normalizedEmail,
                DisplayName = principalName,
                PreferredDisplayName = principalName,
                ContactEmail = normalizedEmail,
                ThemeCode = "SYSTEM",
                AccountStatusCode = "ACTIVE",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            db.UserAccounts.Add(user);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (!string.Equals(
                    user.AccountStatusCode,
                    "ACTIVE",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    "This Recomp account is not active.");
            }

            var changed = false;

            if (normalizedEmail is not null
                && user.EmailNormalized != normalizedEmail)
            {
                user.EmailNormalized = normalizedEmail;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(principalName)
                && user.DisplayName != principalName)
            {
                user.DisplayName = principalName;
                changed = true;
            }

            if (changed)
            {
                user.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var activeAccess = await db.UserClientAccess
            .Where(x =>
                x.UserId == user.UserId
                && x.RevokedAtUtc == null)
            .OrderBy(x => x.GrantedAtUtc)
            .ToListAsync(cancellationToken);

        if (activeAccess.Count == 0)
            return new AppClientContext(user, null, null);

        // Prefer the user's own client profile.
        var ownerAccess = activeAccess
            .Where(x =>
                string.Equals(
                    x.AccessRoleCode,
                    "OWNER",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        UserClientAccess access;

        if (ownerAccess.Count == 1)
        {
            access = ownerAccess[0];
        }
        else if (ownerAccess.Count > 1)
        {
            throw new InvalidOperationException(
                "This account has more than one active OWNER client profile. " +
                "Resolve the ownership data before continuing.");
        }
        else if (activeAccess.Count == 1)
        {
            access = activeAccess[0];
        }
        else
        {
            // Multiple non-owner assignments require an explicit
            // coach/client selector rather than choosing arbitrarily.
            return new AppClientContext(user, null, null);
        }

        var client = await db.Clients
            .SingleOrDefaultAsync(
                x =>
                    x.ClientId == access.ClientId
                    && x.ClientStatusCode == "ACTIVE",
                cancellationToken);

        if (client is null)
            return new AppClientContext(user, null, null);

        var program = await db.Programs
            .Where(x =>
                x.ClientId == client.ClientId
                && x.ProgramStatusCode == "ACTIVE")
            .OrderByDescending(x => x.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        return new AppClientContext(user, client, program);
    }

    public static DateOnly GetClientToday(Client client)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(client.TimeZone);
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
            return DateOnly.FromDateTime(local);
        }
        catch
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }
    }

    private static string? GetClaim(
        ClaimsPrincipal principal,
        string claimType)
    {
        return principal.Claims
            .FirstOrDefault(c =>
                string.Equals(
                    c.Type,
                    claimType,
                    StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }
}