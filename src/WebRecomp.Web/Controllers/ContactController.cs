using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[AllowAnonymous]
[Route("contact")]
public sealed class ContactController(
    RecompDbContext db,
    IEmailService emailService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var settings = await db.ContactSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.SettingsKey == "DEFAULT",
                cancellationToken);

        if (settings is not null
            && !settings.ContactEnabled)
        {
            ViewData["PublicPage"] = true;
            return View("Unavailable");
        }

        ViewData["PublicPage"] = true;

        return View(new ContactFormViewModel());
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("access-code")]
    public async Task<IActionResult> Submit(
        ContactFormViewModel model,
        CancellationToken cancellationToken)
    {
        ViewData["PublicPage"] = true;

        var settings = await db.ContactSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.SettingsKey == "DEFAULT",
                cancellationToken);

        if (settings is not null
            && !settings.ContactEnabled)
        {
            return View("Unavailable");
        }

        if (!ModelState.IsValid)
            return View("Index", model);

        var now = DateTime.UtcNow;

        var destinationEmail =
            string.IsNullOrWhiteSpace(settings?.DestinationEmail)
                ? null
                : settings.DestinationEmail.Trim();

        var message = new ContactMessage
        {
            ContactMessageId = Guid.NewGuid(),

            SenderName = model.Name.Trim(),
            SenderEmail = model.Email.Trim(),
            Subject = model.Subject.Trim(),
            Message = model.Message.Trim(),

            StatusCode = "NEW",
            SubmittedAtUtc = now,

            EmailNotificationStatus =
                destinationEmail is null
                    ? "NOT_CONFIGURED"
                    : "PENDING"
        };

        // Save the website submission before attempting email.
        db.ContactMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        if (destinationEmail is not null)
        {
            var body =
$"""
A new Contact Us message was submitted through 12 Week Recomp.

From: {message.SenderName}
Email: {message.SenderEmail}
Subject: {message.Subject}
Received: {message.SubmittedAtUtc:yyyy-MM-dd HH:mm} UTC

Message:

{message.Message}

---
This message is also stored in the 12 Week Recomp Admin Contact inbox.
""";

            var result = await emailService.SendAsync(
                new OutboundEmail(
                    ToAddress: destinationEmail,
                    Subject: $"12WR Contact: {message.Subject}",
                    Body: body,
                    IsHtml: false,
                    ReplyToAddress: message.SenderEmail,
                    ReplyToName: message.SenderName),
                cancellationToken);

            if (result.Success)
            {
                message.EmailNotificationStatus = "SENT";
                message.EmailSentAtUtc = DateTime.UtcNow;
                message.EmailError = null;
            }
            else
            {
                message.EmailNotificationStatus = "FAILED";
                message.EmailError = Truncate(
                    result.Error ?? "Unknown email delivery error.",
                    2000);
            }

            message.EmailSentAtUtc =
                result.Success
                    ? message.EmailSentAtUtc
                    : null;

            await db.SaveChangesAsync(cancellationToken);
        }

        TempData["ContactSubmitted"] = true;

        return RedirectToAction(nameof(Index));
    }

    private static string Truncate(
        string value,
        int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
}
