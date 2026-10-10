using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[Route("access")]
public sealed class AccessController(
    AppUserContextService userContext,
    AccessEntitlementService entitlements,
    PendingAccessCodeService pendingCodes) : Controller
{
    [AllowAnonymous]
    [HttpGet("")]
    public IActionResult Index(string? code = null)
    {
SetPageShell();

        return View(new RedeemAccessCodeViewModel
        {
            Code = string.IsNullOrWhiteSpace(code)
                ? ""
                : code.Trim().ToUpperInvariant()
        });
    }

    [AllowAnonymous]
    [HttpPost("continue")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("access-code")]
    public async Task<IActionResult> Continue(
        RedeemAccessCodeViewModel model,
        CancellationToken cancellationToken)
    {
        SetPageShell();

        if (!ModelState.IsValid)
            return View("Index", model);

        var code = model.Code.Trim().ToUpperInvariant();

        // Existing signed-in users can redeem immediately.
        if (User.Identity?.IsAuthenticated == true)
        {
            return await RedeemForCurrentUserAsync(
                code,
                cancellationToken);
        }

        // Anonymous visitor: validate now, but do not consume the code.
        var validation = await entitlements.ValidateCodeAsync(
            code,
            cancellationToken);

        if (!validation.Success
            || string.IsNullOrWhiteSpace(validation.NormalizedCode))
        {
            ModelState.AddModelError(
                nameof(RedeemAccessCodeViewModel.Code),
                validation.Message);

            return View("Index", model);
        }

        pendingCodes.Set(
            HttpContext,
            validation.NormalizedCode);

        TempData["PendingAccessMessage"] =
            validation.PromotionName is not null
                ? $"{validation.PromotionName} recognized. " +
                  $"{validation.AccessDurationDays} days of access will be " +
                  "applied when you create or sign in to your account."
                : $"Your access code is saved. " +
                  $"{validation.AccessDurationDays} days of access will be " +
                  "applied when you create or sign in to your account.";

        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpGet("complete")]
    public async Task<IActionResult> Complete(
        CancellationToken cancellationToken)
    {
        var pendingCode = pendingCodes.Get(HttpContext);

        if (string.IsNullOrWhiteSpace(pendingCode))
            return RedirectToAction("AfterSignIn", "Home");

        return await RedeemForCurrentUserAsync(
            pendingCode,
            cancellationToken);
    }

    private async Task<IActionResult> RedeemForCurrentUserAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(
            HttpContext,
            cancellationToken);

        var result = await entitlements.RedeemAsync(
            context.User,
            code,
            cancellationToken);

        if (!result.Success)
        {
            pendingCodes.Clear(HttpContext);

            SetPageShell();

            ModelState.AddModelError(
                nameof(RedeemAccessCodeViewModel.Code),
                result.Message);

            return View(
                "Index",
                new RedeemAccessCodeViewModel
                {
                    Code = code
                });
        }

        pendingCodes.Clear(HttpContext);

        TempData["AccessActivated"] = result.Message;

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Onboarding");

        return RedirectToAction("CommandCentral", "Dashboard");
    }

    private void SetPageShell()
    {
        if (User.Identity?.IsAuthenticated == true)
            ViewData["OnboardingPage"] = true;
        else
            ViewData["PublicPage"] = true;
    }
}