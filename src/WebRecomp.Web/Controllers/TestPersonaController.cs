using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[AllowAnonymous]
[Route("test-persona")]
public sealed class TestPersonaController(
    TestPersonaSessionService sessions) : Controller
{
    [HttpGet("")]
    public IActionResult Index(string? email = null)
    {
        if (!sessions.IsEnabled)
            return NotFound();

        ViewData["PublicPage"] = true;

        return View(new TestPersonaLoginViewModel
        {
            Email = string.IsNullOrWhiteSpace(email)
                ? string.Empty
                : email.Trim().ToLowerInvariant()
        });
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        TestPersonaLoginViewModel model,
        CancellationToken cancellationToken)
    {
        if (!sessions.IsEnabled)
            return NotFound();

        ViewData["PublicPage"] = true;

        if (!ModelState.IsValid)
            return View("Index", model);

        var success =
            await sessions.TrySignInAsync(
                model.Email,
                model.Password,
                Response,
                cancellationToken);

        if (!success)
        {
            ModelState.AddModelError(
                string.Empty,
                "That TEST persona email/password combination is not available.");

            return View("Index", model);
        }

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet("logout")]
    public IActionResult Logout()
    {
        sessions.SignOut(Response);
        return RedirectToAction(nameof(Index));
    }
}
