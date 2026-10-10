using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Fitdays;

namespace WebRecomp.Web.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FitdaysController(AppUserContextService users, FitdaysPreviewClient fitdays, IMemoryCache cache) : Controller
{
    private const string Cookie = "__Host-RecompFitdaysPreview";
    private sealed record Preview(Guid Owner, FitdaysSession Session, FitdaysSnapshot Snapshot);

    [HttpGet]
    public async Task<IActionResult> Index(long? personId, CancellationToken ct)
    {
        if (!FitdaysPreviewClient.IsTestSite) return NotFound();
        var context = await users.GetAsync(HttpContext, ct);
        if (context.Client is null || context.Program is null) return RedirectToAction("Index", "Intake");
        var preview = GetPreview(context.Client.ClientId);
        var model = new FitdaysPageModel { Snapshot = preview?.Snapshot };
        if (preview != null)
        {
            var selected = personId ?? (preview.Snapshot.People.Count == 1 ? preview.Snapshot.People[0].Id : (long?)null);
            if (selected.HasValue && preview.Snapshot.People.Any(p => p.Id == selected)) model.PersonId = selected;
        }
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("fitdays-preview")]
    public async Task<IActionResult> Connect(FitdaysPageModel model, CancellationToken ct)
    {
        if (!FitdaysPreviewClient.IsTestSite) return NotFound();
        var context = await users.GetAsync(HttpContext, ct);
        if (context.Client is null || context.Program is null) return RedirectToAction("Index", "Intake");
        if (!model.Consent) ModelState.AddModelError(nameof(model.Consent), "Allow Recomp TEST to read your Fitdays profiles and recent weigh-ins to continue.");
        if (!ModelState.IsValid) { ClearPassword(model); return View("Index", model); }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(35));
            var session = await fitdays.LoginAsync(model.Email.Trim(), model.Password, model.Region, timeout.Token);
            ClearPassword(model);
            var snapshot = await fitdays.ReadAsync(session, context.Client.TimeZone, timeout.Token);
            RemovePreview();
            var key = Guid.NewGuid().ToString("N");
            cache.Set("fitdays:" + key, new Preview(context.Client.ClientId, session, snapshot), TimeSpan.FromMinutes(20));
            Response.Cookies.Append(Cookie, key, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromMinutes(20), IsEssential = true });
            return RedirectToAction(nameof(Index));
        }
        catch (FitdaysPreviewException ex) { ModelState.AddModelError("", ex.Message); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        { ModelState.AddModelError("", "Fitdays could not complete the preview. Check your connection details and try again later."); }
        ClearPassword(model);
        return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        if (!FitdaysPreviewClient.IsTestSite) return NotFound();
        var context = await users.GetAsync(HttpContext, ct);
        if (context.Client == null) return RedirectToAction("Index", "Intake");
        if (GetPreview(context.Client.ClientId) != null) RemovePreview();
        Response.Cookies.Delete(Cookie, new CookieOptions { Secure = true, Path = "/" });
        return RedirectToAction(nameof(Index));
    }
    private Preview? GetPreview(Guid owner) => Request.Cookies.TryGetValue(Cookie, out var key) && Guid.TryParseExact(key, "N", out _) && cache.TryGetValue<Preview>("fitdays:" + key, out var p) && p?.Owner == owner ? p : null;
    private void RemovePreview() { if (Request.Cookies.TryGetValue(Cookie, out var key) && Guid.TryParseExact(key, "N", out _)) cache.Remove("fitdays:" + key); }
    private void ClearPassword(FitdaysPageModel model) { model.Password = ""; ModelState.Remove(nameof(model.Password)); }
}
public sealed class FitdaysPageModel
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(256)] public string Password { get; set; } = "";
    [Required, RegularExpression("^(us|eu|cn)$")] public string Region { get; set; } = "us";
    public bool Consent { get; set; }
    public long? PersonId { get; set; }
    public FitdaysSnapshot? Snapshot { get; set; }
}
