using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[Route("Account")]
public sealed class AccountMigrationController(
    LocalAuthService localAuth) : Controller
{
    [HttpGet("SetLocalPassword")]
    [AllowAnonymous]
    public async Task<IActionResult> SetLocalPassword(
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return RedirectToAction(
                "MicrosoftSignIn",
                "Account",
                new { returnUrl = "/Account/SetLocalPassword" });
        }

        var externalSubject = GetExternalSubject(User);
        if (string.IsNullOrWhiteSpace(externalSubject))
            return Forbid();

        if (!await localAuth.CanBootstrapAdministratorAsync(
                externalSubject,
                cancellationToken))
        {
            return Forbid();
        }

        return View(new BootstrapLocalPasswordViewModel());
    }

    [HttpPost("SetLocalPassword")]
    [Authorize]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("local-auth")]
    public async Task<IActionResult> SetLocalPassword(
        BootstrapLocalPasswordViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        var externalSubject = GetExternalSubject(User);
        if (string.IsNullOrWhiteSpace(externalSubject))
            return Forbid();

        var result = await localAuth.BootstrapAdministratorCredentialAsync(
            externalSubject,
            model.Password,
            cancellationToken);

        switch (result.Status)
        {
            case LocalCredentialBootstrapStatus.NotAuthorized:
                return Forbid();

            case LocalCredentialBootstrapStatus.Unavailable:
                Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                ModelState.AddModelError(
                    "",
                    "12 Week Recomp account sign-in is temporarily unavailable. Please try again shortly.");
                return View(model);

            case LocalCredentialBootstrapStatus.Created:
            case LocalCredentialBootstrapStatus.AlreadyConfigured:
                await HttpContext.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);

                return RedirectToAction(
                    "SignIn",
                    "Account",
                    new { localPasswordReady = true });

            default:
                return Forbid();
        }
    }

    private static string? GetExternalSubject(ClaimsPrincipal principal)
        => principal.FindFirst("oid")?.Value
            ?? principal.FindFirst(
                "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? principal.FindFirst("sub")?.Value;
}
