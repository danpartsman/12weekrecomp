using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

[AllowAnonymous]
[Route("request-more-information")]
public sealed class RequestInformationController(
    RecompDbContext db,
    IEmailService emailService) : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["PublicPage"] = true;
        return View(new RequestInformationInput());
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("access-code")]
    public async Task<IActionResult> Submit(
        RequestInformationInput model,
        CancellationToken cancellationToken)
    {
        ViewData["PublicPage"] = true;
        if (!string.IsNullOrWhiteSpace(model.Company))
            return RedirectToAction(nameof(Thanks));
        if (!ModelState.IsValid)
            return View("Index", model);

        var now = DateTime.UtcNow;
        var message = new ContactMessage
        {
            ContactMessageId = Guid.NewGuid(),
            SenderName = model.Name.Trim(),
            SenderEmail = model.Email.Trim(),
            Subject = "Request More Information",
            Message = "Public website Request More Information submission.",
            StatusCode = "NEW",
            SubmittedAtUtc = now,
            EmailNotificationStatus = "NOT_CONFIGURED"
        };

        db.ContactMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        var settings = await db.ContactSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SettingsKey == "DEFAULT", cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings?.DestinationEmail))
        {
            message.EmailNotificationStatus = "PENDING";
            var result = await emailService.SendAsync(
                new OutboundEmail(
                    settings.DestinationEmail.Trim(),
                    "12WR: Request More Information",
                    $"New information request from {message.SenderName} ({message.SenderEmail}).",
                    ReplyToAddress: message.SenderEmail,
                    ReplyToName: message.SenderName),
                cancellationToken);
            message.EmailNotificationStatus = result.Success ? "SENT" : "FAILED";
            message.EmailSentAtUtc = result.Success ? DateTime.UtcNow : null;
            message.EmailError = result.Success ? null : (result.Error ?? "Unknown email delivery error.")[..Math.Min(result.Error?.Length ?? 30, 2000)];
            await db.SaveChangesAsync(cancellationToken);
        }

        return RedirectToAction(nameof(Thanks));
    }

    [HttpGet("thanks")]
    public IActionResult Thanks()
    {
        ViewData["PublicPage"] = true;
        return View();
    }
}

public sealed class RequestInformationInput
{
    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = "";

    // Honeypot: legitimate visitors never fill this.
    public string? Company { get; set; }
}
