using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
    LocalAuthService localAuth,
    LocalPasswordResetService passwordReset) : Controller
{
    [HttpGet]
    public IActionResult SignIn(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("AfterSignIn", "Home");

        SetPublicAuthViewData();
        return View(new LocalSignInViewModel
        {
            ReturnUrl = SafeReturnUrl(returnUrl),
            RememberMe = true
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
    public IActionResult ForgotPassword()
    {
        SetPublicAuthViewData();
        return View(new LocalForgotPasswordViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("local-auth")]
    public async Task<IActionResult> ForgotPassword(
        LocalForgotPasswordViewModel model,
        CancellationToken cancellationToken)
    {
        SetPublicAuthViewData();

        if (!localAuth.IsAvailable)
        {
            AddNativeAuthUnavailableError();
            return View(model);
        }

        if (!ModelState.IsValid)
            return View(model);

        var ticket =
            await passwordReset.CreateTicketAsync(
                model.Email,
                cancellationToken);

        if (ticket is not null)
        {
            var resetUrl =
                BuildPasswordResetUrl(
                    ticket.Token);

            if (!string.IsNullOrWhiteSpace(resetUrl))
            {
                await passwordReset.SendResetEmailAsync(
                    ticket,
                    resetUrl,
                    cancellationToken);
            }
        }

        // Always show the same result. Do not reveal whether an account exists.
        return RedirectToAction(
            nameof(ForgotPasswordConfirmation));
    }

    [HttpGet]
    public IActionResult ForgotPasswordConfirmation()
    {
        SetPublicAuthViewData();
        return View();
    }

    [HttpGet]
    public IActionResult ResetPassword(
        string? token)
    {
        SetPublicAuthViewData();

        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToAction(
                nameof(ForgotPassword));
        }

        return View(
            new LocalResetPasswordViewModel
            {
                Token = token
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("local-auth")]
    public async Task<IActionResult> ResetPassword(
        LocalResetPasswordViewModel model,
        CancellationToken cancellationToken)
    {
        SetPublicAuthViewData();

        if (!localAuth.IsAvailable)
        {
            AddNativeAuthUnavailableError();
            return View(model);
        }

        if (!ModelState.IsValid)
            return View(model);

        var result =
            await passwordReset.ResetAsync(
                model.Token,
                model.Password,
                cancellationToken);

        switch (result.Status)
        {
            case LocalPasswordResetStatus.Success:
                return RedirectToAction(
                    nameof(ResetPasswordConfirmation));

            case LocalPasswordResetStatus.Unavailable:
                AddNativeAuthUnavailableError();
                return View(model);

            default:
                ModelState.AddModelError(
                    "",
                    "This password reset link is invalid or has expired. Request a new reset link.");
                return View(model);
        }
    }

    [HttpGet]
    public IActionResult ResetPasswordConfirmation()
    {
        SetPublicAuthViewData();
        return View();
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
                    "That email is already associated with a 12 Week Recomp account. Use Forgot password to create or reset its 12WR password.");
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

        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction("Index", "Home");
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
                    ? DateTimeOffset.UtcNow.AddDays(7)
                    : null
            });
    }

    private void SetPublicAuthViewData()
    {
        ViewData["PublicPage"] = true;
        ViewData["NativeAuthAvailable"] = localAuth.IsAvailable;
    }

    private void AddNativeAuthUnavailableError()
        => ModelState.AddModelError("", NativeAuthUnavailableMessage);

    private const string NativeAuthUnavailableMessage =
        "12 Week Recomp account sign-in is temporarily unavailable. Please try again shortly.";

    private string? BuildPasswordResetUrl(
        string token)
    {
        var host =
            Request.Host.Host
                .Trim()
                .ToLowerInvariant();

        var knownHost =
            host is "test.12weekrecomp.com"
                or "12weekrecomp.com"
                or "www.12weekrecomp.com"
                or "recomp-test.azurewebsites.net"
                or "recomp-live.azurewebsites.net"
                or "localhost"
                or "127.0.0.1";

        if (!knownHost)
            return null;

        return Url.Action(
            nameof(ResetPassword),
            "Account",
            new { token },
            Request.Scheme,
            Request.Host.Value);
    }

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
