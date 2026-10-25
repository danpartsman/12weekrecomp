using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Controllers;

[Authorize, Route("accountability-preview/clients/{clientId:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountabilityPreviewController(AppUserContextService users, CoachAccountabilityPreviewService preview,
    LocalAuthService auth, AccountabilityApprovalReceipt receipts, IConfiguration configuration) : Controller
{
    private bool Enabled => AccountabilityPreview.Enabled(configuration);
    private bool CoachRole => User.IsInRole("Coach") || User.IsInRole("Administrator");
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid clientId, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        var context = await users.GetAsync(HttpContext, ct);
        if (!await preview.CanApproveAsync(context.User.UserId, clientId, ct)) return NotFound();
        return View(new AccountabilityPreviewViewModel(clientId, context.Client?.ClientName ?? "Client", CanApprove: true, ReviewPage: true));
    }
    [HttpGet("snap")]
    public async Task<IActionResult> Snap(Guid clientId, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        var user = (await users.GetAsync(HttpContext, ct)).User;
        if (!await preview.CanViewAsync(user.UserId, clientId, CoachRole, ct)) return NotFound();
        var name = await preview.ClientNameAsync(clientId, ct); if (name is null) return NotFound();
        return View(new AccountabilityPreviewViewModel(clientId, name, CanApprove: await preview.CanApproveAsync(user.UserId, clientId, ct),
            ReviewPage: true, CanAssignSnap: CoachRole && await preview.CanAssignSnapAsync(user.UserId, clientId, ct)));
    }
    [HttpGet("exercises")]
    public async Task<IActionResult> Exercises(Guid clientId, string? q, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (q?.Length > 80) return BadRequest("Use up to 80 characters.");
        var user = (await users.GetAsync(HttpContext, ct)).User;
        if (!CoachRole || !await preview.CanAssignSnapAsync(user.UserId, clientId, ct)) return NotFound();
        return Json(await preview.ExercisesAsync(clientId, q, ct));
    }
    [HttpGet("evidence")]
    public async Task<IActionResult> Evidence(Guid clientId, DateOnly? date, CancellationToken ct, bool monthly = false)
    {
        if (!Enabled) return NotFound();
        if (!ModelState.IsValid) return BadRequest("Choose a valid date.");
        var user = (await users.GetAsync(HttpContext, ct)).User;
        if (!await preview.CanViewAsync(user.UserId, clientId, CoachRole, ct)) return NotFound();
        try { var data = await preview.EvidenceAsync(user, clientId, date, ct, monthly); return data is null ? NotFound() : Json(data); }
        catch (ArgumentOutOfRangeException) { return BadRequest("Choose a date within the last 12 weeks through today."); }
    }
    [HttpPost("approve"), ValidateAntiForgeryToken, EnableRateLimiting("accountability-approval")]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Approve(Guid clientId, ApprovalInput input, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!ModelState.IsValid || !ValidTerms(input.Terms)) return BadRequest("Review valid preview terms and confirm agreement.");
        var user = (await users.GetAsync(HttpContext, ct)).User;
        if (!await preview.CanApproveAsync(user.UserId, clientId, ct)) return NotFound();
        if (!input.Agree || !await auth.VerifyCurrentPasswordReadOnlyAsync(user, input.Username, input.Password, ct))
            return BadRequest("Approval failed. Confirm agreement and use the signed-in client's username and password.");
        var token = receipts.Issue(user.UserId, clientId, input.Terms, user.EmailNormalized ?? "Client");
        var receipt = receipts.Read(token, clientId, input.Terms)!;
        return Json(new { token, receipt.ApprovedAt, receipt.ExpiresAt, receipt.SignedBy, previewOnly = true });
    }
    [HttpPost("approval-status"), ValidateAntiForgeryToken, RequestSizeLimit(65536)]
    public async Task<IActionResult> ApprovalStatus(Guid clientId, ReceiptInput input, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!ModelState.IsValid || !ValidTerms(input.Terms)) return BadRequest();
        var user = (await users.GetAsync(HttpContext, ct)).User;
        if (!await preview.CanViewAsync(user.UserId, clientId, CoachRole, ct)) return NotFound();
        var receipt = receipts.Read(input.Token, clientId, input.Terms);
        if (receipt is null || !await preview.IsOwnerAsync(receipt.UserId, clientId, ct)) return Json(new { approved = false });
        return Json(new { approved = true, receipt.ApprovedAt, receipt.ExpiresAt, receipt.SignedBy, previewOnly = true });
    }
    private static bool ValidTerms(string terms)
    {
        if (string.IsNullOrWhiteSpace(terms) || terms.Length > 32768) return false;
        try { using var doc = JsonDocument.Parse(terms); return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("version", out var version) && version.TryGetInt32(out var v) && v == 2
            && ValidAgreement(doc.RootElement)
            && ((doc.RootElement.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True)
                || (doc.RootElement.TryGetProperty("snapWorkouts", out var snaps) && snaps.ValueKind == JsonValueKind.Array && snaps.GetArrayLength() > 0)); }
        catch (JsonException) { return false; }
    }
    private static bool ValidAgreement(JsonElement root)
    {
        if (!root.TryGetProperty("agreement", out var agreement) || agreement.ValueKind != JsonValueKind.Object
            || !agreement.TryGetProperty("start", out var start) || start.ValueKind != JsonValueKind.String
            || !DateOnly.TryParseExact(start.GetString(), "yyyy-MM-dd", out var from)
            || !agreement.TryGetProperty("mode", out var mode) || mode.ValueKind != JsonValueKind.String) return false;
        if (mode.GetString() == "weeks") return agreement.TryGetProperty("weeks", out var weeks) && weeks.TryGetInt32(out var count) && count is >= 1 and <= 52;
        if (mode.GetString() != "open") return false;
        return !agreement.TryGetProperty("endedOn", out var end) || end.ValueKind == JsonValueKind.Null
            || (end.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(end.GetString(), "yyyy-MM-dd", out var until) && until >= from);
    }
    public sealed class ApprovalInput
    {
        [Required, StringLength(320)] public string Username { get; set; } = "";
        [Required, StringLength(1024), DataType(DataType.Password)] public string Password { get; set; } = "";
        [Required, StringLength(32768)] public string Terms { get; set; } = "";
        public bool Agree { get; set; }
    }
    public sealed class ReceiptInput
    {
        [Required, StringLength(8192)] public string Token { get; set; } = "";
        [Required, StringLength(32768)] public string Terms { get; set; } = "";
    }
}
