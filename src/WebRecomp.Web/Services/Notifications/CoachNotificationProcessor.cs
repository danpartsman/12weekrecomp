using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Coaching;

namespace WebRecomp.Web.Services.Notifications;

public sealed class CoachNotificationProcessor(RecompDbContext db, NotificationFiles files, IEmailService email,
    IPhoneNotificationSender phone, ILogger<CoachNotificationProcessor> logger)
{
    public async Task QueueConfigAsync(Guid clientId, int revision, CancellationToken ct)
    {
        _ = files.Identity; // Establish the activation boundary before processing new events.
        var item = await db.CoachAccountabilityEvents.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == clientId && x.Revision == revision && x.Kind == "CONFIG", ct);
        if (item is null || item.CreatedAtUtc < files.Identity.ActiveSinceUtc) return;
        var previous = await db.CoachAccountabilityEvents.AsNoTracking().Where(x => x.ClientId == clientId && x.Kind == "CONFIG" && x.Revision < revision)
            .OrderByDescending(x => x.Revision).Select(x => x.PayloadJson).FirstOrDefaultAsync(ct);
        var change = CoachNotificationChange.Compare(previous, item.PayloadJson);
        if (!change.HasChanges) return;
        var owner = await OwnerAsync(clientId, item.ActorUserId, ct);
        if (owner is null) return;
        var client = await db.Clients.AsNoTracking().SingleAsync(x => x.ClientId == clientId, ct);
        var coach = await db.UserAccounts.AsNoTracking().SingleAsync(x => x.UserId == item.ActorUserId, ct);
        var path = $"/accountability-preview/clients/{clientId:D}" + (change.AgreementChanged ? "" : "/snap");
        var url = files.BaseUrl + path;
        files.Enqueue(new() { Id = "event-" + item.EventId.ToString("N"), ClientId = clientId, CoachUserId = item.ActorUserId,
            UserId = owner.UserId, Subject = change.Subject, Body = change.EmailBody(coach.PreferredDisplayName ?? coach.DisplayName ?? "Your coach", url, client.TimeZone), Url = path });
    }
    private async Task<UserAccount?> OwnerAsync(Guid clientId, Guid coach, CancellationToken ct)
    {
        if (!await db.Clients.AnyAsync(x => x.ClientId == clientId && x.ClientStatusCode == "ACTIVE", ct)
            || !await db.CoachClientRelationships.AnyAsync(x => x.ClientId == clientId && x.CoachUserId == coach && x.StatusCode == "ACTIVE" && x.RevokedAtUtc == null, ct)) return null;
        var ids = await db.UserClientAccess.AsNoTracking().Where(x => x.ClientId == clientId && x.AccessRoleCode == "OWNER" && x.RevokedAtUtc == null).Select(x => x.UserId).Distinct().Take(2).ToListAsync(ct);
        // Never guess a notification recipient if ownership is ambiguous.
        if (ids.Count != 1) { logger.LogWarning("Coaching notification skipped because ownership is not unique."); return null; }
        return await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == ids[0] && x.AccountStatusCode == "ACTIVE", ct);
    }
    public async Task RunAsync(CancellationToken ct)
    {
        var since = files.Identity.ActiveSinceUtc;
        var recent = DateTime.UtcNow.AddDays(-7); if (recent > since) since = recent;
        var events = await db.CoachAccountabilityEvents.AsNoTracking().Where(x => x.Kind == "CONFIG" && x.CreatedAtUtc >= since)
            .OrderByDescending(x => x.CreatedAtUtc).Take(500).Select(x => new { x.ClientId, x.Revision }).ToListAsync(ct);
        foreach (var item in events)
        {
            try { await QueueConfigAsync(item.ClientId, item.Revision, ct); }
            catch (IOException) { /* Another worker is already recording this event. */ }
        }
        await QueueDailyReviewsAsync(ct);
        if (!files.DeliveryEnabled) return;
        var pending = files.QueueIds().Select(id => files.Read<NotificationDelivery>($"queue/{id}"))
            .Where(x => x is not null && !x.Finished && x.NextAttemptUtc <= DateTime.UtcNow && x.CreatedAtUtc > DateTime.UtcNow.AddDays(-7))
            .OrderBy(x => x!.CreatedAtUtc).Take(50).ToArray();
        foreach (var delivery in pending) await DeliverAsync(delivery!.Id, ct);
    }
    private async Task QueueDailyReviewsAsync(CancellationToken ct)
    {
        var agreements = await db.CoachAccountabilityAgreements.AsNoTracking().ToListAsync(ct);
        foreach (var agreement in agreements)
        {
            var owner = await OwnerAsync(agreement.ClientId, agreement.CoachUserId, ct); if (owner is null) continue;
            var settings = files.User(owner.UserId);
            if (!settings.DailyReview || !settings.Devices.Any(d => files.OwnsDevice(owner.UserId, d.Endpoint))) continue;
            var approved = await db.CoachAccountabilityApprovals.AsNoTracking().AnyAsync(x => x.ClientId == agreement.ClientId && x.OwnerUserId == owner.UserId && x.TermsHash == agreement.TermsHash && x.TermsVersion == agreement.TermsVersion, ct);
            if (!approved) continue;
            var client = await db.Clients.AsNoTracking().SingleAsync(x => x.ClientId == agreement.ClientId, ct);
            TimeZoneInfo zone; try { zone = TimeZoneInfo.FindSystemTimeZoneById(client.TimeZone); } catch { continue; }
            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
            var state = AccountabilityDocument.Parse(agreement.StateJson);
            if (!DailyReviewDue(state, DateOnly.FromDateTime(now.DateTime), now.TimeOfDay, settings.ReviewTime)) continue;
            var path = $"/accountability-preview/clients/{client.ClientId:D}";
            files.Enqueue(new() { Id = $"daily-{client.ClientId:N}-{owner.UserId:N}-{now:yyyyMMdd}", ClientId = client.ClientId, CoachUserId = agreement.CoachUserId,
                UserId = owner.UserId, Subject = "Review today's fees and incentives", Body = "Open your agreement to review today's calculated fees, incentives and pending items.", Url = path, Email = false });
        }
    }
    public static bool DailyReviewDue(AccountabilityState state, DateOnly today, TimeSpan localTime, string reviewTime)
    {
        if (!TimeOnly.TryParseExact(reviewTime, "HH:mm", out var at) || localTime < at.ToTimeSpan()
            || !DateOnly.TryParseExact(state.Agreement.Start, "yyyy-MM-dd", out var start) || today < start) return false;
        if (state.Agreement.Mode == "weeks" && today >= start.AddDays(state.Agreement.Weeks * 7)) return false;
        if (DateOnly.TryParseExact(state.Agreement.EndedOn, "yyyy-MM-dd", out var ended) && today > ended) return false;
        return state.Enabled || state.SnapWorkouts.Any(x => x.Date == today.ToString("yyyy-MM-dd"));
    }
    private async Task DeliverAsync(string id, CancellationToken ct)
    {
        FileStream lease;
        try { lease = files.Lease($"queue/{id}"); } catch (IOException) { return; }
        using (lease)
        {
            var item = files.Read<NotificationDelivery>($"queue/{id}"); if (item is null || item.Finished) return;
            var owner = await OwnerAsync(item.ClientId, item.CoachUserId, ct);
            if (owner?.UserId != item.UserId) { item.EmailDone = item.PushDone = true; files.Write($"queue/{id}", item); return; }
            var settings = files.User(owner.UserId);
            try
            {
                if (!item.EmailDone)
                {
                    if (!item.Email || !settings.Email) item.EmailDone = true;
                    else
                    {
                        var destination = Address(owner.ContactEmail) ?? Address(owner.EmailNormalized);
                        if (destination is not null) item.EmailDone = (await email.SendAsync(new(destination, "12WR: " + item.Subject, item.Body), ct)).Success;
                    }
                    files.Write($"queue/{id}", item);
                }
                if (!item.PushDone)
                {
                    foreach (var device in settings.Devices.Where(d => files.OwnsDevice(owner.UserId, d.Endpoint)))
                    {
                        var deviceId = NotificationFiles.DeviceId(device.Endpoint);
                        if (item.DeliveredDevices.Contains(deviceId)) continue;
                        if (!await phone.SendAsync(device, item.Subject, item.Url, item.Id, ct)) files.RemoveDevice(owner.UserId, device.Endpoint);
                        item.DeliveredDevices.Add(deviceId);
                        files.Write($"queue/{id}", item);
                    }
                    item.PushDone = true;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { logger.LogWarning("Coaching notification delivery will retry after {FailureType}.", e.GetType().Name); }
            item.Attempts++;
            item.NextAttemptUtc = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(6, item.Attempts))));
            files.Write($"queue/{id}", item);
        }
    }
    private static string? Address(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return new MailAddress(value.Trim()).Address; } catch (FormatException) { return null; }
    }
}
public sealed class CoachNotificationWorker(IServiceScopeFactory scopes, NotificationFiles files, ILogger<CoachNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = files.Identity;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        do
        {
            try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<CoachNotificationProcessor>().RunAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogWarning("Coaching notification worker will retry after {FailureType}.", e.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
