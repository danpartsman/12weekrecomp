using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[AllowAnonymous]
public sealed class AccountController(
    PendingAccessCodeService pendingCodes,
    TestPersonaSessionService testPersonaSessions,
    LocalAuthService localAuth) : Controller
{
    [HttpGet]
    public IActionResult SignIn(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("AfterSignIn", "Home");

        SetPublicAuthViewData();
        return View(new LocalSignInViewModel
        {
            ReturnUrl = SafeReturnUrl(returnUrl)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("local-auth")]
    public async Task<IActionResult> SignIn(
        LocalSignInViewModel model,
        CancellationToken cancellationToken)
    {
        SetPublicAuthViewData();
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);

        if (!localAuth.IsAvailable)
        {
            AddNativeAuthUnavailableError();
            return View(model);
        }

        if (!ModelState.IsValid)
            return View(model);

        var result = await localAuth.ValidateCredentialsAsync(
            model.Email,
            model.Password,
            cancellationToken);

        if (!result.Success || result.User is null)
        {
            ModelState.AddModelError(
                "",
                result.Unavailable
                    ? NativeAuthUnavailableMessage
                    : result.LockedOut
                        ? "Too many sign-in attempts. Wait about 15 minutes and try again."
                        : "The email or password is not correct.");

            return View(model);
        }

        await SignInLocalUserAsync(
            result.User,
            result.RoleCode,
            model.RememberMe);

        return Redirect(ResolveDestination(model.ReturnUrl));
    }

    [HttpGet]
    public IActionResult CreateAccount(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("AfterSignIn", "Home");

        SetPublicAuthViewData();
        return View(new LocalRegisterViewModel
        {
            ReturnUrl = SafeReturnUrl(returnUrl)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("local-auth")]
    public async Task<IActionResult> CreateAccount(
        LocalRegisterViewModel model,
        CancellationToken cancellationToken)
    {
        SetPublicAuthViewData();
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);

        if (!localAuth.IsAvailable)
        {
            AddNativeAuthUnavailableError();
            return View(model);
        }

        if (!ModelState.IsValid)
            return View(model);

        var result = await localAuth.RegisterAsync(
            model.DisplayName,
            model.Email,
            model.Password,
            cancellationToken);

        switch (result.Status)
        {
            case LocalRegistrationStatus.ExistingLocalAccount:
                ModelState.AddModelError(
                    nameof(model.Email),
                    "That email already has a 12 Week Recomp account. Sign in instead.");
                return View(model);

            case LocalRegistrationStatus.ExistingExternalAccount:
                ModelState.AddModelError(
                    nameof(model.Email),
                    "That email belongs to an existing 12 Week Recomp Microsoft account. Use the legacy Microsoft sign-in below so the existing profile is not duplicated.");
                return View(model);

            case LocalRegistrationStatus.EmailConflict:
                ModelState.AddModelError(
                    nameof(model.Email),
                    "That email needs account support before it can be used. No new account was created.");
                return View(model);

            case LocalRegistrationStatus.Unavailable:
                AddNativeAuthUnavailableError();
                return View(model);
        }

        if (result.User is null)
        {
            ModelState.AddModelError("", "The account could not be created.");
            return View(model);
        }

        await SignInLocalUserAsync(
            result.User,
            result.RoleCode,
            rememberMe: true);

        return Redirect(ResolveDestination(model.ReturnUrl));
    }

    // Compatibility path for existing externally authenticated TEST accounts.
    // New customers no longer need Microsoft/Entra once the native credential
    // schema has been provisioned through the database migration path.
    [HttpGet]
    public IActionResult MicrosoftSignIn(string? returnUrl = null)
        => StartEntraSignIn(returnUrl);

    [HttpGet]
    public IActionResult EntraSignIn(string? returnUrl = null)
        => StartEntraSignIn(returnUrl);

    [HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = 403;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Logout()
    {
        if (User.HasClaim("recomp_test_persona", "true"))
        {
            testPersonaSessions.SignOut(Response);
            return RedirectToAction("Index", "Home");
        }

        var authSource = User.FindFirst("recomp_auth_source")?.Value;
        if (string.Equals(authSource, "LOCAL", StringComparison.OrdinalIgnoreCase))
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToAction("Index", "Home");
        }

        // Existing Entra sessions can still perform their normal provider
        // sign-out during the compatibility period.
        var loginHint = User.FindFirst("login_hint")?.Value;

        if (string.IsNullOrWhiteSpace(loginHint))
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

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

    private async Task SignInLocalUserAsync(
        UserAccount user,
        string roleCode,
        bool rememberMe)
    {
        var displayName =
            user.PreferredDisplayName
            ?? user.DisplayName
            ?? user.EmailNormalized
            ?? "12WR User";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString("D")),
            new("sub", user.ExternalSubject),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Role, roleCode),
            new("recomp_auth_source", "LOCAL")
        };

        if (!string.IsNullOrWhiteSpace(user.EmailNormalized))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.EmailNormalized));
            claims.Add(new Claim("email", user.EmailNormalized));
            claims.Add(new Claim("preferred_username", user.EmailNormalized));
        }

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                AllowRefresh = true,
                ExpiresUtc = rememberMe
                    ? DateTimeOffset.UtcNow.AddDays(30)
                    : null
            });
    }

    private IActionResult StartEntraSignIn(string? returnUrl)
    {
        var destination = ResolveDestination(SafeReturnUrl(returnUrl));

        return Challenge(
            new OpenIdConnectChallengeProperties
            {
                RedirectUri = destination,
                Prompt = "select_account"
            },
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    private void SetPublicAuthViewData()
    {
        ViewData["PublicPage"] = true;
        ViewData["NativeAuthAvailable"] = localAuth.IsAvailable;
    }

    private void AddNativeAuthUnavailableError()
        => ModelState.AddModelError("", NativeAuthUnavailableMessage);

    private const string NativeAuthUnavailableMessage =
        "Native 12WR sign-in is temporarily unavailable while the TEST credential database migration is completed. Existing TEST accounts can use the legacy Microsoft sign-in below.";

    private string? SafeReturnUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : null;

    private string ResolveDestination(string? returnUrl)
        => pendingCodes.HasPendingCode(HttpContext)
            ? "/access/complete"
            : !string.IsNullOrWhiteSpace(returnUrl)
                ? returnUrl
                : "/Home/AfterSignIn";
}
