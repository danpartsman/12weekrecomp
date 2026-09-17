using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.CatalogSync;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin/exercise-catalog")]
public sealed class AdminCatalogSyncController(
    CatalogSyncCoordinator coordinator,
    AppUserContextService userContext) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await BuildModelAsync(cancellationToken));

    [HttpPost("settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(
        [Bind(Prefix = "Settings")] AdminCatalogSyncSettingsInput input,
        CancellationToken cancellationToken)
    {
        if (!CatalogSyncConstants.IsSupportedRefreshInterval(input.RefreshIntervalDays))
            ModelState.AddModelError("Settings.RefreshIntervalDays", "Choose 1, 3, 7, 14, or 30 days.");

        if (!ModelState.IsValid)
        {
            var invalid = await BuildModelAsync(cancellationToken);
            invalid.Settings = input;
            return View("Index", invalid);
        }

        var context = await userContext.GetAsync(HttpContext, cancellationToken);

        try
        {
            await coordinator.SaveSettingsAsync(
                new CatalogSyncSettingsUpdate(
                    input.ActiveProviderCode,
                    input.MediaProviderCode,
                    input.AutoSyncEnabled,
                    input.RefreshIntervalDays,
                    context.User.UserId),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalid = await BuildModelAsync(cancellationToken);
            invalid.Settings = input;
            return View("Index", invalid);
        }

        TempData["CatalogSyncSuccess"] = "Exercise catalog synchronization settings saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("refresh-now")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshNow(CancellationToken cancellationToken)
    {
        try
        {
            var result = await coordinator.RunNowAsync(cancellationToken);

            TempData["CatalogSyncSuccess"] = result.StatusCode switch
            {
                "SUCCESS" => $"Catalog refreshed: {result.Added} added, {result.Updated} updated, {result.Deactivated} deactivated, {result.ActiveCount} active.",
                "NO_CHANGE" => "Catalog checked. The provider dataset has not changed.",
                "BUSY" => "A catalog synchronization is already running.",
                _ => result.Message ?? result.StatusCode
            };
        }
        catch (Exception ex)
        {
            TempData["CatalogSyncError"] = $"Catalog refresh failed: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<AdminCatalogSyncViewModel> BuildModelAsync(CancellationToken cancellationToken)
    {
        var status = await coordinator.GetStatusAsync(cancellationToken);

        return new AdminCatalogSyncViewModel
        {
            Settings = new AdminCatalogSyncSettingsInput
            {
                ActiveProviderCode = status.Settings.ActiveProviderCode,
                MediaProviderCode = status.Settings.MediaProviderCode,
                AutoSyncEnabled = status.Settings.AutoSyncEnabled,
                RefreshIntervalDays = status.Settings.RefreshIntervalDays
            },
            CatalogProviders = status.CatalogProviders,
            MediaProviders = status.MediaProviders,
            RecentRuns = status.RecentRuns,
            LastCheckedAtUtc = status.Settings.LastCheckedAtUtc,
            LastSuccessfulSyncAtUtc = status.Settings.LastSuccessfulSyncAtUtc,
            LastDatasetSha256 = status.Settings.LastDatasetSha256,
            LastProviderVersion = status.Settings.LastProviderVersion,
            LastError = status.Settings.LastError
        };
    }
}
