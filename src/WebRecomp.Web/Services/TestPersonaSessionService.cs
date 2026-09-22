using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.Services;

public sealed class TestPersonaSessionService(
    RecompDbContext db,
    IDataProtectionProvider dataProtection,
    IConfiguration configuration,
    IWebHostEnvironment environment)
{
    public const string CookieName = "recomp_test_persona";
    private readonly IDataProtector protector =
        dataProtection.CreateProtector("12WeekRecomp.TestPersona.v1");

    public bool IsEnabled
    {
        get
        {
            if (!configuration.GetValue<bool>(
                    "WebRecomp:TestPersonaAccountsEnabled"))
            {
                return false;
            }

            if (environment.IsDevelopment())
                return true;

            var siteName =
                Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")
                ?? string.Empty;

            return string.Equals(
                       siteName,
                       "recomp-test",
                       StringComparison.OrdinalIgnoreCase)
                   || siteName.Contains(
                       "recomp-test",
                       StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task<bool> TrySignInAsync(
        string? email,
        string? password,
        HttpResponse response,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled
            || !TestPersonaCatalog.TryGet(email, out var spec)
            || spec is null
            || !string.Equals(
                password,
                TestPersonaCatalog.SharedPassword,
                StringComparison.Ordinal))
        {
            return false;
        }

        var externalSubject =
            TestPersonaCatalog.ExternalSubject(spec.Email);

        var user = await db.UserAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ExternalSubject == externalSubject,
                cancellationToken);

        if (user is null
            || !string.Equals(
                user.AccountStatusCode,
                "ACTIVE",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var protectedValue = protector.Protect(spec.Email);

        response.Cookies.Append(
            CookieName,
            protectedValue,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });

        return true;
    }

    public async Task<ClaimsPrincipal?> TryRestorePrincipalAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled
            || httpContext.User.Identity?.IsAuthenticated == true)
        {
            return null;
        }

        if (!httpContext.Request.Cookies.TryGetValue(
                CookieName,
                out var protectedValue)
            || string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        string email;
        try
        {
            email = protector.Unprotect(protectedValue);
        }
        catch
        {
            DeleteCookie(httpContext.Response);
            return null;
        }

        if (!TestPersonaCatalog.TryGet(email, out var spec)
            || spec is null)
        {
            DeleteCookie(httpContext.Response);
            return null;
        }

        var externalSubject =
            TestPersonaCatalog.ExternalSubject(spec.Email);

        var user = await db.UserAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ExternalSubject == externalSubject,
                cancellationToken);

        if (user is null
            || !string.Equals(
                user.AccountStatusCode,
                "ACTIVE",
                StringComparison.OrdinalIgnoreCase))
        {
            DeleteCookie(httpContext.Response);
            return null;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new("oid", user.ExternalSubject),
            new("preferred_username", spec.Email),
            new("email", spec.Email),
            new("name", user.DisplayName ?? spec.DisplayName),
            new("recomp_test_persona", "true")
        };

        var identity = new ClaimsIdentity(
            claims,
            authenticationType: "RecompTestPersona",
            nameType: "name",
            roleType: ClaimTypes.Role);

        return new ClaimsPrincipal(identity);
    }

    public void SignOut(HttpResponse response)
        => DeleteCookie(response);

    private static void DeleteCookie(HttpResponse response)
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
