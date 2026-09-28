using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin/training-email")]
public sealed class AdminTrainingEmailController(
    RecompDbContext db,
    IEmailService emailService,
    ILogger<AdminTrainingEmailController> logger) : Controller
{
    private const string Subject = "Your 12WR training check-in";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var recipients = await BuildRecipientsAsync(cancellationToken);
        return View(recipients);
    }

    [HttpPost("send")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(
        Guid? userId,
        bool sendAll,
        CancellationToken cancellationToken)
    {
        if (sendAll == userId.HasValue)
            return BadRequest("Choose one user or all active users.");

        var recipients = await BuildRecipientsAsync(cancellationToken);
        var chosen = sendAll
            ? recipients
            : recipients.Where(x => x.UserId == userId).ToList();
        if (chosen.Count == 0)
            return NotFound();

        var sent = 0;
        var failed = 0;
        foreach (var recipient in chosen)
        {
            // The same contact address can belong to more than one account.
            // Only one message per address is included in the prepared audience.
            var result = await emailService.SendAsync(
                new OutboundEmail(recipient.Email, Subject, recipient.Body),
                cancellationToken);
            if (result.Success)
            {
                sent++;
                logger.LogInformation("Training check-in accepted for account {UserId}", recipient.UserId);
            }
            else
            {
                failed++;
                logger.LogWarning("Training check-in failed for account {UserId}: {Error}",
                    recipient.UserId, result.Error);
            }
        }

        TempData["TrainingEmailStatus"] =
            $"Email provider accepted {sent} message(s); {failed} failed. Check application logs for failures.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<TrainingEmailRecipient>> BuildRecipientsAsync(CancellationToken cancellationToken)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var start = today.AddDays(-6);
        var priorStart = start.AddDays(-7);

        var users = await db.UserAccounts.AsNoTracking()
            .Where(x => x.AccountStatusCode == "ACTIVE")
            .Select(x => new { x.UserId, x.PreferredDisplayName, x.DisplayName, x.ContactEmail, x.EmailNormalized })
            .ToListAsync(cancellationToken);
        var access = await db.UserClientAccess.AsNoTracking()
            .Where(x => x.RevokedAtUtc == null)
            .Join(db.Clients.AsNoTracking().Where(x => x.ClientStatusCode == "ACTIVE"),
                a => a.ClientId, c => c.ClientId, (a, c) => new { a.UserId, a.ClientId })
            .ToListAsync(cancellationToken);
        var clientIds = access.Select(x => x.ClientId).Distinct().ToList();
        var workouts = await db.WorkoutSessions.AsNoTracking()
            .Where(x => clientIds.Contains(x.ClientId)
                && x.CompletedFlag
                && x.WorkoutDate >= priorStart
                && x.WorkoutDate <= today)
            .Select(x => new { x.ClientId, x.WorkoutDate, x.DurationMin, x.SessionRpe, x.WorkoutTypeCode })
            .ToListAsync(cancellationToken);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<TrainingEmailRecipient>();
        foreach (var user in users.OrderBy(x => x.DisplayName))
        {
            var email = string.IsNullOrWhiteSpace(user.ContactEmail)
                ? user.EmailNormalized?.Trim() : user.ContactEmail.Trim();
            if (string.IsNullOrWhiteSpace(email) ||
                !System.Net.Mail.MailAddress.TryCreate(email, out var parsed) ||
                !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase) ||
                !seen.Add(email))
                continue;

            var ownedClients = access.Where(x => x.UserId == user.UserId)
                .Select(x => x.ClientId).ToHashSet();
            var recent = workouts.Where(x => ownedClients.Contains(x.ClientId)
                && x.WorkoutDate >= start).ToList();
            var previousCount = workouts.Count(x => ownedClients.Contains(x.ClientId)
                && x.WorkoutDate < start);
            var name = (user.PreferredDisplayName ?? user.DisplayName ?? "there").Trim();
            if (name.Length == 0) name = "there";
            var minutes = Math.Round(recent.Sum(x => Math.Max(0, x.DurationMin)), 0);
            var rpe = recent.Where(x => x.SessionRpe.HasValue).Select(x => x.SessionRpe!.Value).ToList();
            var observation = recent.Count == 0
                ? "There are no completed workouts logged for this period yet. Completing your next session will start a useful training record."
                : previousCount == 0
                    ? "This gives you a starting point. Keep logging completed sessions so you can compare one week with the next."
                    : recent.Count > previousCount
                        ? $"You logged {recent.Count - previousCount} more completed session(s) than in the prior seven days. Keep the schedule manageable and pay attention to recovery."
                        : recent.Count < previousCount
                            ? $"You logged {previousCount - recent.Count} fewer completed session(s) than in the prior seven days. Choose a realistic next session and build from there."
                            : "Your completed session count matches the prior seven days. Review how the sessions felt before deciding whether to add more.";
            var body = $"Hi {name},\n\n" +
                $"Here is your 12WR training snapshot for {start:MMM d}–{today:MMM d, yyyy}:\n\n" +
                $"Completed workouts: {recent.Count}\n" +
                $"Training time: {minutes.ToString("0", CultureInfo.InvariantCulture)} minutes\n" +
                (rpe.Count > 0 ? $"Average logged effort (RPE): {rpe.Average():0.0}/10 across {rpe.Count} session(s)\n" : "") +
                $"\n{observation}\n\n" +
                "For your next workout, pick a session you can fit into your week. Log its duration, reps, and effort when you finish. " +
                "If a session felt unusually hard, review your recent training and give yourself room to recover before increasing the workload.\n\n" +
                "Open 12WR to review your workouts and plan your next session.\n\n" +
                "Keep showing up,\nThe 12WR team";
            result.Add(new TrainingEmailRecipient(user.UserId, email, name, recent.Count, minutes, body));
        }
        return result;
    }

    public sealed record TrainingEmailRecipient(
        Guid UserId, string Email, string Name, int CompletedWorkouts, decimal Minutes, string Body);
}
