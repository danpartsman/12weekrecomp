using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record AppClientContext(UserAccount User, Client? Client, ProgramRecord? Program);

public sealed class AppUserContextService(
    RecompDbContext db,
    IConfiguration configuration)
{
    public async Task<AppClientContext> GetAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var identityProvider = httpContext.Request.Headers["X-MS-CLIENT-PRINCIPAL-IDP"].FirstOrDefault();
        var principal = ReadPrincipal(httpContext);

        var externalSubject = principal?.ObjectId
            ?? httpContext.Request.Headers["X-MS-CLIENT-PRINCIPAL-ID"].FirstOrDefault();

        var principalName = principal?.PreferredName
            ?? httpContext.Request.Headers["X-MS-CLIENT-PRINCIPAL-NAME"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(externalSubject)
            && !string.IsNullOrWhiteSpace(identityProvider)
            && !string.Equals(identityProvider, "aad", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Unsupported App Service identity provider '{identityProvider}'.");
        }

        if (string.IsNullOrWhiteSpace(externalSubject))
        {
            var allowDevelopmentUser = configuration.GetValue<bool>("WebRecomp:AllowDevelopmentUser");
            if (!allowDevelopmentUser)
                throw new UnauthorizedAccessException("No App Service authenticated principal was found.");

            externalSubject = "development-user";
            principalName ??= "development@example.local";
        }

        externalSubject = externalSubject.Trim();

        var normalizedEmail = string.IsNullOrWhiteSpace(principalName)
            ? null
            : principalName.Trim().ToLowerInvariant();

        var user = await db.UserAccounts
            .SingleOrDefaultAsync(x => x.ExternalSubject == externalSubject, cancellationToken);

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
            if (!string.Equals(user.AccountStatusCode, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("This Recomp account is not active.");

            var changed = false;

            if (normalizedEmail is not null && user.EmailNormalized != normalizedEmail)
            {
                user.EmailNormalized = normalizedEmail;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(principalName) && user.DisplayName != principalName)
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
            .Where(x => x.UserId == user.UserId && x.RevokedAtUtc == null)
            .OrderBy(x => x.GrantedAtUtc)
            .ToListAsync(cancellationToken);

        if (activeAccess.Count == 0)
            return new AppClientContext(user, null, null);

        // v1003 member behavior:
        // Prefer the user's own client profile (OWNER). This allows the same user
        // to also hold COACH access to many other clients without confusing the
        // normal member Dashboard/Workout/Nutrition flows.
        //
        // Future coach UI will explicitly select among COACH-access clients rather
        // than silently choosing one here.
        var ownerAccess = activeAccess
            .Where(x => string.Equals(x.AccessRoleCode, "OWNER", StringComparison.OrdinalIgnoreCase))
            .ToList();

        UserClientAccess access;

        if (ownerAccess.Count == 1)
        {
            access = ownerAccess[0];
        }
        else if (ownerAccess.Count > 1)
        {
            throw new InvalidOperationException(
                "This account has more than one active OWNER client profile. Resolve the ownership data before continuing.");
        }
        else if (activeAccess.Count == 1)
        {
            // Allows a coach/test account with access to exactly one client to
            // continue using the current client-oriented pages.
            access = activeAccess[0];
        }
        else
        {
            // Multiple non-owner client assignments are valid for a coach, but
            // require an explicit coach/client selector that will be added in the
            // coach workflow rather than choosing an arbitrary client.
            return new AppClientContext(user, null, null);
        }

        var client = await db.Clients
            .SingleOrDefaultAsync(
                x => x.ClientId == access.ClientId && x.ClientStatusCode == "ACTIVE",
                cancellationToken);

        if (client is null)
            return new AppClientContext(user, null, null);

        var program = await db.Programs
            .Where(x => x.ClientId == client.ClientId && x.ProgramStatusCode == "ACTIVE")
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

    private static AppServicePrincipal? ReadPrincipal(HttpContext httpContext)
    {
        var encoded = httpContext.Request.Headers["X-MS-CLIENT-PRINCIPAL"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(encoded))
            return null;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("claims", out var claims)
                || claims.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? objectId = null;
            string? preferredName = null;
            string? displayName = null;

            foreach (var claim in claims.EnumerateArray())
            {
                if (!claim.TryGetProperty("typ", out var typeElement)
                    || !claim.TryGetProperty("val", out var valueElement))
                {
                    continue;
                }

                var type = typeElement.GetString();
                var value = valueElement.GetString();

                if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(value))
                    continue;

                if (string.Equals(type, "oid", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        type,
                        "http://schemas.microsoft.com/identity/claims/objectidentifier",
                        StringComparison.OrdinalIgnoreCase))
                {
                    objectId ??= value;
                }
                else if (string.Equals(type, "preferred_username", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(type, "email", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(
                             type,
                             "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress",
                             StringComparison.OrdinalIgnoreCase))
                {
                    preferredName ??= value;
                }
                else if (string.Equals(type, "name", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(
                             type,
                             "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
                             StringComparison.OrdinalIgnoreCase))
                {
                    displayName ??= value;
                }
            }

            return new AppServicePrincipal(
                ObjectId: objectId,
                PreferredName: preferredName ?? displayName);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record AppServicePrincipal(string? ObjectId, string? PreferredName);
}
