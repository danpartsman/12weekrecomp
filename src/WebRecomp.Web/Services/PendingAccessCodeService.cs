using Microsoft.AspNetCore.DataProtection;

namespace WebRecomp.Web.Services;

public sealed class PendingAccessCodeService
{
    private const string CookieName = "recomp_pending_access";
    private readonly IDataProtector protector;

    public PendingAccessCodeService(
        IDataProtectionProvider dataProtectionProvider)
    {
        protector = dataProtectionProvider.CreateProtector(
            "WebRecomp.PendingAccessCode.v1006");
    }

    public void Set(HttpContext httpContext, string code)
    {
        var protectedValue = protector.Protect(
            code.Trim().ToUpperInvariant());

        httpContext.Response.Cookies.Append(
            CookieName,
            protectedValue,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddHours(4)
            });
    }

    public string? Get(HttpContext httpContext)
    {
        if (!httpContext.Request.Cookies.TryGetValue(
                CookieName,
                out var protectedValue)
            || string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        try
        {
            return protector.Unprotect(protectedValue);
        }
        catch
        {
            return null;
        }
    }

    public bool HasPendingCode(HttpContext httpContext)
    {
        return !string.IsNullOrWhiteSpace(Get(httpContext));
    }

    public void Clear(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(
            CookieName,
            new CookieOptions
            {
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
    }
}
