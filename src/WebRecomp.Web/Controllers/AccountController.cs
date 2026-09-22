using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

[AllowAnonymous]
public sealed class AccountController(
    PendingAccessCodeService pendingCodes,
    TestPersonaSessionService testPersonaSessions) : Controller
{
    [HttpGet]
    public IActionResult SignIn(string? returnUrl = null)
    {
        var destination =
            pendingCodes.HasPendingCode(HttpContext)
                ? "/access/complete"
                : !string.IsNullOrWhiteSpace(returnUrl)
                  && Url.IsLocalUrl(returnUrl)
                    ? returnUrl
                    : "/Home/AfterSignIn";

        return Challenge(
            new OpenIdConnectChallengeProperties
            {
                RedirectUri = destination,
                Prompt = "select_account"
            },
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet]
    public IActionResult CreateAccount(string? returnUrl = null)
    {
        var destination =
            pendingCodes.HasPendingCode(HttpContext)
                ? "/access/complete"
                : !string.IsNullOrWhiteSpace(returnUrl)
                  && Url.IsLocalUrl(returnUrl)
                    ? returnUrl
                    : "/Home/AfterSignIn";

        return Challenge(
            new OpenIdConnectChallengeProperties
            {
                RedirectUri = destination,
                Prompt = "create"
            },
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = 403;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Logout()
    {
        if (User.HasClaim(
                "recomp_test_persona",
                "true"))
        {
            testPersonaSessions.SignOut(Response);
            return RedirectToAction("Index", "Home");
        }

        // Entra can perform a promptless full OIDC logout when the ID token
        // contains the optional login_hint claim. Older sessions without that
        // claim fall back to a local Recomp sign-out instead of showing an
        // account-selection prompt during logout.
        var loginHint = User.FindFirst("login_hint")?.Value;

        if (string.IsNullOrWhiteSpace(loginHint))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        return base.SignOut(
            new AuthenticationProperties
            {
                RedirectUri = "/"
            },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
