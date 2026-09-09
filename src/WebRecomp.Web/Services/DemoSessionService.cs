using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record DemoRequestMetadata(
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string? UtmContent,
    string? ReferrerHost,
    string? ReferrerPath,
    string? LandingPath,
    string? IpAddress,
    string? CountryCode,
    string? DeviceTypeCode,
    string? OperatingSystem,
    string? BrowserName,
    string? UserAgent);
public sealed record DemoRedemptionResult(
    bool Success,
    string? ErrorCode,
    string? Message,
    DemoWorkspace? Workspace,
    string? RawSessionToken,
    DateTime? SessionExpiresAtUtc);

public sealed class DemoSessionService(RecompDbContext db)
{
    public const string CookieName = "recomp_demo_session";

    private static readonly TimeSpan ActivityWriteInterval =
        TimeSpan.FromMinutes(5);

    public async Task<DemoRedemptionResult> RedeemAsync(
        string inviteCode,
        DemoRequestMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        inviteCode = inviteCode?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(inviteCode))
        {
            return Failure(
                "MISSING_CODE",
                "The demo invitation code is missing.");
        }

        DemoRedemptionResult? result = null;

        // The DbContext is configured with retry-on-failure. User-created
        // transactions must therefore execute through EF's execution strategy.
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            var now = DateTime.UtcNow;

            var invite = await db.DemoInvites
                .SingleOrDefaultAsync(
                    x => x.InviteCode == inviteCode,
                    cancellationToken);

            if (invite is null)
            {
                result = Failure(
                    "INVALID_CODE",
                    "This demo invitation is not valid.");
                return;
            }

            if (!string.Equals(
                    invite.StatusCode,
                    "ACTIVE",
                    StringComparison.OrdinalIgnoreCase))
            {
                result = Failure(
                    "INVITE_NOT_ACTIVE",
                    "This demo invitation is no longer active.");
                return;
            }

            if (invite.ExpiresAtUtc is not null
                && invite.ExpiresAtUtc.Value <= now)
            {
                result = Failure(
                    "INVITE_EXPIRED",
                    "This demo invitation has expired.");
                return;
            }

            if (invite.MaxRedemptions is not null
                && invite.RedemptionCount >= invite.MaxRedemptions.Value)
            {
                result = Failure(
                    "INVITE_LIMIT_REACHED",
                    "This demo invitation has reached its usage limit.");
                return;
            }

            var workspaceId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            var workspaceExpiresAtUtc =
                now.AddDays(invite.WorkspaceLifetimeDays);

            var sessionExpiresAtUtc =
                now.AddDays(invite.SessionLifetimeDays);

            if (sessionExpiresAtUtc > workspaceExpiresAtUtc)
                sessionExpiresAtUtc = workspaceExpiresAtUtc;

            var rawToken = GenerateOpaqueToken();
            var tokenHash = HashToken(rawToken);

            var user = new UserAccount
            {
                UserId = userId,
                ExternalSubject = $"demo:{workspaceId:N}",
                DisplayName = "12 Week Recomp Demo",
                PreferredDisplayName = "Demo User",
                ThemeCode = "SYSTEM",
                AccountStatusCode = "ACTIVE",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            var workspace = new DemoWorkspace
            {
                DemoWorkspaceId = workspaceId,
                DemoInviteId = invite.DemoInviteId,
                UserId = userId,
                DemoTypeCode = invite.InviteTypeCode,
                WorkspaceStatusCode = "ACTIVE",
                ExpiresAtUtc = workspaceExpiresAtUtc,
                InactivityTimeoutDays = invite.InactivityTimeoutDays,
                SessionLifetimeDays = invite.SessionLifetimeDays,
                LastActivityAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,

                UtmSource = Clean(metadata?.UtmSource, 100),
                UtmMedium = Clean(metadata?.UtmMedium, 100),
                UtmCampaign = Clean(metadata?.UtmCampaign, 150),
                UtmContent = Clean(metadata?.UtmContent, 150),

                ReferrerHost = Clean(metadata?.ReferrerHost, 255),
                ReferrerPath = Clean(metadata?.ReferrerPath, 500),
                LandingPath = Clean(metadata?.LandingPath, 500),

                CountryCode = Clean(metadata?.CountryCode, 2)
            };

            var session = new DemoSession
            {
                DemoSessionId = Guid.NewGuid(),
                DemoWorkspaceId = workspaceId,
                SessionTokenHash = tokenHash,
                CreatedAtUtc = now,
                LastSeenAtUtc = now,
                ExpiresAtUtc = sessionExpiresAtUtc,

                IpAddress = Clean(metadata?.IpAddress, 64),
                DeviceTypeCode = Clean(metadata?.DeviceTypeCode, 16),
                OperatingSystem = Clean(metadata?.OperatingSystem, 64),
                BrowserName = Clean(metadata?.BrowserName, 64),
                UserAgent = Clean(metadata?.UserAgent, 1000),

                PageViewCount = 0,
                LastPagePath = null
            };

            invite.RedemptionCount++;
            invite.UpdatedAtUtc = now;

            db.UserAccounts.Add(user);
            db.DemoWorkspaces.Add(workspace);
            db.DemoSessions.Add(session);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            result = new DemoRedemptionResult(
                true,
                null,
                null,
                workspace,
                rawToken,
                sessionExpiresAtUtc);
        });

        return result
            ?? Failure(
                "UNKNOWN",
                "The demo workspace could not be created.");
    }

    public async Task<ClaimsPrincipal?> TryRestorePrincipalAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (!httpContext.Request.Cookies.TryGetValue(
                CookieName,
                out var rawToken)
            || string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var tokenHash = HashToken(rawToken);
        var now = DateTime.UtcNow;

        var record = await (
            from demoSession in db.DemoSessions
            join demoWorkspace in db.DemoWorkspaces
                on demoSession.DemoWorkspaceId equals demoWorkspace.DemoWorkspaceId
            join demoUser in db.UserAccounts
                on demoWorkspace.UserId equals demoUser.UserId
            where demoSession.SessionTokenHash == tokenHash
            select new
            {
                Session = demoSession,
                Workspace = demoWorkspace,
                User = demoUser
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (record is null)
        {
            DeleteBrowserCookie(httpContext.Response);
            return null;
        }

        var session = record.Session;
        var workspace = record.Workspace;
        var user = record.User;

        var inactiveAtUtc =
            workspace.LastActivityAtUtc.AddDays(
                workspace.InactivityTimeoutDays);

        var valid =
            session.RevokedAtUtc is null
            && session.ExpiresAtUtc > now
            && workspace.ExpiresAtUtc > now
            && inactiveAtUtc > now
            && string.Equals(
                workspace.WorkspaceStatusCode,
                "ACTIVE",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                user.AccountStatusCode,
                "ACTIVE",
                StringComparison.OrdinalIgnoreCase);

        if (!valid)
        {
            if (workspace.WorkspaceStatusCode == "ACTIVE"
                && (workspace.ExpiresAtUtc <= now
                    || inactiveAtUtc <= now))
            {
                workspace.WorkspaceStatusCode = "EXPIRED";
                workspace.UpdatedAtUtc = now;
            }

            if (session.RevokedAtUtc is null)
                session.RevokedAtUtc = now;

            await db.SaveChangesAsync(cancellationToken);

            DeleteBrowserCookie(httpContext.Response);
            return null;
        }

        if (now - session.LastSeenAtUtc >= ActivityWriteInterval)
        {
            session.LastSeenAtUtc = now;
            workspace.LastActivityAtUtc = now;
            workspace.UpdatedAtUtc = now;

            await db.SaveChangesAsync(cancellationToken);
        }

        var claims = new List<Claim>
        {
            new(
                ClaimTypes.NameIdentifier,
                user.UserId.ToString()),

            // AppUserContextService already uses oid as the durable subject.
            // Supplying the temporary demo subject here lets the existing
            // ownership/isolation code work unchanged.
            new("oid", user.ExternalSubject),

            new(
                "name",
                user.DisplayName ?? "12 Week Recomp Demo"),

            new(
                "recomp_demo",
                "true"),

            new(
                "recomp_demo_workspace_id",
                workspace.DemoWorkspaceId.ToString()),

            new(
                "recomp_demo_type",
                workspace.DemoTypeCode)
        };

        var identity = new ClaimsIdentity(
            claims,
            authenticationType: "RecompDemo",
            nameType: "name",
            roleType: ClaimTypes.Role);

        return new ClaimsPrincipal(identity);
    }

    public void WriteBrowserCookie(
        HttpResponse response,
        string rawToken,
        DateTime expiresAtUtc)
    {
        var utcExpiration = expiresAtUtc.Kind == DateTimeKind.Utc
            ? expiresAtUtc
            : DateTime.SpecifyKind(
                expiresAtUtc,
                DateTimeKind.Utc);

        response.Cookies.Append(
            CookieName,
            rawToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = "/",
                Expires = new DateTimeOffset(utcExpiration)
            });
    }

    public async Task RevokeCurrentSessionAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (!httpContext.Request.Cookies.TryGetValue(
                CookieName,
                out var rawToken)
            || string.IsNullOrWhiteSpace(rawToken))
        {
            DeleteBrowserCookie(httpContext.Response);
            return;
        }

        var tokenHash = HashToken(rawToken);
        var now = DateTime.UtcNow;

        var session = await db.DemoSessions
            .SingleOrDefaultAsync(
                x => x.SessionTokenHash == tokenHash,
                cancellationToken);

        if (session is not null && session.RevokedAtUtc is null)
        {
            session.RevokedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        DeleteBrowserCookie(httpContext.Response);
    }

    public async Task TrackPageViewAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (!IsDemoPrincipal(httpContext.User))
            return;

        if (!HttpMethods.IsGet(httpContext.Request.Method))
            return;

        if (httpContext.Response.StatusCode != StatusCodes.Status200OK)
            return;

        var contentType = httpContext.Response.ContentType;

        if (string.IsNullOrWhiteSpace(contentType)
            || !contentType.StartsWith(
                "text/html",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!httpContext.Request.Cookies.TryGetValue(
                CookieName,
                out var rawToken)
            || string.IsNullOrWhiteSpace(rawToken))
        {
            return;
        }

        var tokenHash = HashToken(rawToken);
        var now = DateTime.UtcNow;

        var session = await db.DemoSessions
            .SingleOrDefaultAsync(
                x => x.SessionTokenHash == tokenHash,
                cancellationToken);

        if (session is null
            || session.RevokedAtUtc.HasValue
            || session.ExpiresAtUtc <= now)
        {
            return;
        }

        var workspace = await db.DemoWorkspaces
            .SingleOrDefaultAsync(
                x => x.DemoWorkspaceId == session.DemoWorkspaceId,
                cancellationToken);

        if (workspace is null
            || !string.Equals(
                workspace.WorkspaceStatusCode,
                "ACTIVE",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        session.PageViewCount++;
        session.LastPagePath =
            Clean(httpContext.Request.Path.Value, 500);

        session.LastSeenAtUtc = now;

        workspace.LastActivityAtUtc = now;
        workspace.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);
    }

    public static bool IsDemoPrincipal(ClaimsPrincipal principal) =>
        principal.HasClaim(
            c => string.Equals(
                     c.Type,
                     "recomp_demo",
                     StringComparison.OrdinalIgnoreCase)
                 && string.Equals(
                     c.Value,
                     "true",
                     StringComparison.OrdinalIgnoreCase));

    public static DemoRequestMetadata CaptureRequestMetadata(
        HttpContext context)
    {
        var request = context.Request;

        var userAgent =
            request.Headers.UserAgent.ToString();

        string? referrerHost = null;
        string? referrerPath = null;

        var forwardedReferrerHost =
            request.Query["wr_referrer_host"].ToString();

        var forwardedReferrerPath =
            request.Query["wr_referrer_path"].ToString();

        if (!string.IsNullOrWhiteSpace(forwardedReferrerHost))
        {
            referrerHost = forwardedReferrerHost;
            referrerPath = forwardedReferrerPath;
        }
        else if (Uri.TryCreate(
                     request.Headers.Referer.ToString(),
                     UriKind.Absolute,
                     out var referrer))
        {
            referrerHost = referrer.Host;
            referrerPath = referrer.AbsolutePath;
        }

        var ipAddress =
            FirstValue(request.Headers["CF-Connecting-IP"].ToString())
            ?? FirstValue(request.Headers["X-Forwarded-For"].ToString())
            ?? context.Connection.RemoteIpAddress?.ToString();

        var countryCode =
            FirstValue(request.Headers["CF-IPCountry"].ToString());

        return new DemoRequestMetadata(
            Clean(request.Query["utm_source"].ToString(), 100),
            Clean(request.Query["utm_medium"].ToString(), 100),
            Clean(request.Query["utm_campaign"].ToString(), 150),
            Clean(request.Query["utm_content"].ToString(), 150),
            Clean(referrerHost, 255),
            Clean(referrerPath, 500),
            Clean(request.Path + request.QueryString, 500),
            Clean(ipAddress, 64),
            Clean(countryCode, 2),
            DetectDeviceType(userAgent),
            DetectOperatingSystem(userAgent),
            DetectBrowser(userAgent),
            Clean(userAgent, 1000));
    }

    private static string DetectDeviceType(string userAgent)
    {
        if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase)
            || (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase)
                && !userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase)))
        {
            return "TABLET";
        }

        if (userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            return "MOBILE";
        }

        return "DESKTOP";
    }

    private static string DetectOperatingSystem(string userAgent)
    {
        if (userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            return "Windows";

        if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase))
            return "iOS";

        if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
            return "Android";

        if (userAgent.Contains("Mac OS", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase))
            return "macOS";

        if (userAgent.Contains("CrOS", StringComparison.OrdinalIgnoreCase))
            return "ChromeOS";

        if (userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase))
            return "Linux";

        return "Other";
    }

    private static string DetectBrowser(string userAgent)
    {
        if (userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
            return "Edge";

        if (userAgent.Contains("OPR/", StringComparison.OrdinalIgnoreCase))
            return "Opera";

        if (userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
            return "Firefox";

        if (userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase))
            return "Chrome";

        if (userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase))
            return "Safari";

        return "Other";
    }

    private static string? FirstValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim();
    }

    private static string? Clean(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
    private static DemoRedemptionResult Failure(
        string errorCode,
        string message) =>
        new(
            false,
            errorCode,
            message,
            null,
            null,
            null);

    private static string GenerateOpaqueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(rawToken));

        return Convert.ToHexString(bytes);
    }

    private static void DeleteBrowserCookie(HttpResponse response)
    {
        response.Cookies.Delete(
            CookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = "/"
            });
    }
}

