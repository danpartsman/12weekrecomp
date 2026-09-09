using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

[AllowAnonymous]
[Route("trial")]
public sealed class TrialController(
    AccessEntitlementService entitlements,
    PendingAccessCodeService pendingCodes) : Controller
{
    public const string FreeTrialCode = "FREETRIAL7";

    [HttpGet("start")]
    public async Task<IActionResult> Start(
        string? variant = null,
        CancellationToken cancellationToken = default)
    {
        var validation =
            await entitlements.ValidateCodeAsync(
                FreeTrialCode,
                cancellationToken);

        if (!validation.Success
            || string.IsNullOrWhiteSpace(validation.NormalizedCode))
        {
            return RedirectToAction(
                "Index",
                "Access",
                new { code = FreeTrialCode });
        }

        pendingCodes.Set(
            HttpContext,
            validation.NormalizedCode);

        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(
                "Complete",
                "Access");
        }

        return Challenge(
            new OpenIdConnectChallengeProperties
            {
                RedirectUri = "/access/complete",
                Prompt = "create"
            },
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
