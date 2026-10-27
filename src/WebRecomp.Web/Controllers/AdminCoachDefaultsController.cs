using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services.Coaching;
namespace WebRecomp.Web.Controllers;
[Authorize(Roles = "Administrator"), Route("admin/coach-defaults")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminCoachDefaultsController(AccountabilityDefaultsStore defaults) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await defaults.ReadAsync(ct));
    [HttpPost(""), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(AccountabilityDefaults input, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try { await defaults.SaveAsync(input, ct); TempData["StatusMessage"] = "Coach defaults saved. Existing client agreements keep their saved terms."; return RedirectToAction(nameof(Index)); }
            catch (ArgumentException e) { ModelState.AddModelError("", e.Message); }
        }
        return View("Index", input);
    }
}
