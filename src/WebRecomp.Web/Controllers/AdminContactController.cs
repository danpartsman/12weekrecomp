using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin/contact")]
public sealed class AdminContactController(
    RecompDbContext db,
    AppUserContextService userContext) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        CancellationToken cancellationToken)
    {
        var model = await BuildIndexAsync(cancellationToken, search);
        return View(model);
    }

    [HttpPost("settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(
        [Bind(Prefix = "Settings")]
        AdminContactSettingsInput input,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildIndexAsync(cancellationToken, null);
            invalidModel.Settings = input;
            return View("Index", invalidModel);
        }

        var context = await userContext.GetAsync(
            HttpContext,
            cancellationToken);

        var now = DateTime.UtcNow;

        var settings = await db.ContactSettings
            .SingleOrDefaultAsync(
                x => x.SettingsKey == "DEFAULT",
                cancellationToken);

        if (settings is null)
        {
            settings = new ContactSetting
            {
                ContactSettingsId = Guid.NewGuid(),
                SettingsKey = "DEFAULT",
                DestinationEmail = Clean(input.DestinationEmail),
                ContactEnabled = input.ContactEnabled,
                UpdatedByUserId = context.User.UserId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            db.ContactSettings.Add(settings);
        }
        else
        {
            settings.DestinationEmail =
                Clean(input.DestinationEmail);

            settings.ContactEnabled =
                input.ContactEnabled;

            settings.UpdatedByUserId =
                context.User.UserId;

            settings.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        TempData["ContactAdminSuccess"] =
            "Contact settings saved.";

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("message/{id:guid}")]
    public async Task<IActionResult> Message(
        Guid id,
        CancellationToken cancellationToken)
    {
        var message = await db.ContactMessages
            .SingleOrDefaultAsync(
                x => x.ContactMessageId == id,
                cancellationToken);

        if (message is null)
            return NotFound();

        if (message.StatusCode == "NEW")
        {
            var context = await userContext.GetAsync(
                HttpContext,
                cancellationToken);

            message.StatusCode = "READ";
            message.ReadAtUtc = DateTime.UtcNow;
            message.ReadByUserId = context.User.UserId;

            await db.SaveChangesAsync(cancellationToken);
        }

        var model = new AdminContactMessageDetailViewModel
        {
            ContactMessageId = message.ContactMessageId,
            SenderName = message.SenderName,
            SenderEmail = message.SenderEmail,
            Subject = message.Subject,
            Message = message.Message,
            StatusCode = message.StatusCode,
            SubmittedAtUtc = message.SubmittedAtUtc,
            ReadAtUtc = message.ReadAtUtc,
            EmailNotificationStatus = message.EmailNotificationStatus,
            EmailSentAtUtc = message.EmailSentAtUtc,
            EmailError = message.EmailError
        };

        return View(model);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        string? search,
        CancellationToken cancellationToken)
    {
        var query = db.ContactMessages.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.SenderName.Contains(term) ||
                x.SenderEmail.Contains(term) ||
                x.Subject.Contains(term));
        }

        var rows = await query.OrderByDescending(x => x.SubmittedAtUtc)
            .Take(5000)
            .ToListAsync(cancellationToken);

        static string Csv(string? value) =>
            $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Name,Email,Subject,Status,Submitted UTC,Email Status");
        foreach (var row in rows)
            csv.AppendLine(string.Join(",",
                Csv(row.SenderName), Csv(row.SenderEmail), Csv(row.Subject),
                Csv(row.StatusCode), Csv(row.SubmittedAtUtc.ToString("O")),
                Csv(row.EmailNotificationStatus)));

        return File(
            System.Text.Encoding.UTF8.GetBytes(csv.ToString()),
            "text/csv",
            $"12wr-contacts-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private async Task<AdminContactIndexViewModel> BuildIndexAsync(
        CancellationToken cancellationToken,
        string? search)
    {
        var settings = await db.ContactSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.SettingsKey == "DEFAULT",
                cancellationToken);

        var messageQuery = db.ContactMessages
            .AsNoTracking()
            .Where(x => x.StatusCode != "ARCHIVED");

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            messageQuery = messageQuery.Where(x =>
                x.SenderName.Contains(term) ||
                x.SenderEmail.Contains(term) ||
                x.Subject.Contains(term));
        }

        var messages = await messageQuery
            .OrderByDescending(x => x.SubmittedAtUtc)
            .Take(250)
            .Select(x => new AdminContactMessageRow
            {
                ContactMessageId = x.ContactMessageId,
                SenderName = x.SenderName,
                SenderEmail = x.SenderEmail,
                Subject = x.Subject,
                StatusCode = x.StatusCode,
                SubmittedAtUtc = x.SubmittedAtUtc
            })
            .ToListAsync(cancellationToken);

        return new AdminContactIndexViewModel
        {
            Settings = new AdminContactSettingsInput
            {
                DestinationEmail = settings?.DestinationEmail,
                ContactEnabled = settings?.ContactEnabled ?? true
            },
            Messages = messages,
            UnreadCount = messages.Count(x => x.StatusCode == "NEW"),
            Search = search
        };
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
