using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebRecomp.Web.Controllers;

[AllowAnonymous]
public sealed class AccountController : Controller
{
    [HttpGet]
    public IActionResult SignIn(string? returnUrl = null)
    {
        var destination = string.IsNullOrWhiteSpace(returnUrl)
            ? "/Home/AfterSignIn"
            : returnUrl;

        return Challenge(
            new OpenIdConnectChallengeProperties
            {
                RedirectUri = destination,
                Prompt = "select_account"
            },
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet]
    public IActionResult CreateAccount()
    {
        return Challenge(
            new OpenIdConnectChallengeProperties
            {
                RedirectUri = "/Home/AfterSignIn",
                Prompt = "create"
            },
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet]
    public async Task<IActionResult> Logout()
    {
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
