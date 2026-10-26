using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Notifications;

namespace WebRecomp.Web.Controllers;

[Authorize, Route("notifications")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(AppUserContextService users, NotificationFiles files, CoachNotificationProcessor processor,
    IPhoneNotificationSender phone) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var user = (await users.GetAsync(HttpContext, ct)).User;
        ViewData["PushDeliveryEnabled"] = files.DeliveryEnabled;
        return View(files.User(user.UserId));
    }
    [HttpGet("configuration")]
    public async Task<IActionResult> Configuration(CancellationToken ct)
    {
        var user = (await users.GetAsync(HttpContext, ct)).User;
        var settings = files.User(user.UserId);
        return Json(new { publicKey = files.Identity.PublicKey, endpoints = settings.Devices.Where(d => files.OwnsDevice(user.UserId, d.Endpoint)).Select(d => d.Endpoint), deliveryEnabled = files.DeliveryEnabled });
    }
    [HttpPost("preferences"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Preferences(bool email, bool dailyReview, string reviewTime, CancellationToken ct)
    {
        if (!TimeOnly.TryParseExact(reviewTime, "HH:mm", out _)) return BadRequest("Choose a valid reminder time.");
        var user = (await users.GetAsync(HttpContext, ct)).User;
        using var lease = files.Lease($"users/{user.UserId:N}");
        var settings = files.User(user.UserId);
        settings.Email = email; settings.DailyReview = dailyReview; settings.ReviewTime = reviewTime;
        files.SaveUser(user.UserId, settings);
        TempData["NotificationStatus"] = "Notification preferences saved.";
        return RedirectToAction(nameof(Index));
    }
    [HttpPost("subscribe"), ValidateAntiForgeryToken, RequestSizeLimit(12288)]
    public async Task<IActionResult> Subscribe([FromBody] SubscriptionInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest("The phone subscription is incomplete.");
        var user = (await users.GetAsync(HttpContext, ct)).User;
        try { files.BindDevice(user.UserId, new(input.Endpoint, input.Keys.P256dh, input.Keys.Auth)); return Json(new { saved = true }); }
        catch (ArgumentException e) { return BadRequest(e.Message); }
    }
    [HttpPost("unsubscribe"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Unsubscribe(string endpoint, CancellationToken ct)
    {
        var user = (await users.GetAsync(HttpContext, ct)).User;
        files.RemoveDevice(user.UserId, endpoint); return Json(new { saved = true });
    }
    [HttpPost("test"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(string endpoint, CancellationToken ct)
    {
        var user = (await users.GetAsync(HttpContext, ct)).User;
        var device = files.User(user.UserId).Devices.SingleOrDefault(x => x.Endpoint == endpoint && files.OwnsDevice(user.UserId, x.Endpoint));
        if (device is null) return BadRequest("Enable notifications on this phone first.");
        if (!files.DeliveryEnabled) return BadRequest("External notifications are disabled in this test environment.");
        try { return await phone.SendAsync(device, "Phone notifications are ready.", "/notifications", "12wr-phone-test", ct)
            ? Json(new { accepted = true }) : BadRequest("This phone subscription expired. Enable notifications again."); }
        catch { return StatusCode(503, "The notification could not be sent. Try again shortly."); }
    }
    [AllowAnonymous, HttpPost("run")]
    [RequestSizeLimit(1024)]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        var supplied = Request.Headers["X-12WR-Notification-Job"].ToString();
        var expected = files.Identity.JobKey;
        if (supplied.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected))) return NotFound();
        await processor.RunAsync(ct); return Ok(new { processed = true });
    }
    public sealed class SubscriptionInput
    {
        [Required, StringLength(4096)] public string Endpoint { get; set; } = "";
        [Required] public SubscriptionKeys Keys { get; set; } = new();
    }
    public sealed class SubscriptionKeys
    {
        [Required, StringLength(200)] public string P256dh { get; set; } = "";
        [Required, StringLength(100)] public string Auth { get; set; } = "";
    }
}
