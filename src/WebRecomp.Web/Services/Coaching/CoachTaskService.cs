using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.Coaching;

public sealed record CoachTaskParty(Guid ClientId, Guid CoachId, Guid OwnerId, Guid RelationshipId,
    string ClientName, string CoachName, string TimeZone);

public sealed class CoachTaskService(RecompDbContext db, SimpleCoachingService coaching, AccountabilityLedger ledger,
    TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public async Task<CoachTaskParty?> PartyAsync(Guid clientId, Guid coachId, CancellationToken ct)
    {
        var pair = await (from relationship in db.CoachClientRelationships.AsNoTracking()
            join client in db.Clients.AsNoTracking() on relationship.ClientId equals (Guid?)client.ClientId
            join coach in db.UserAccounts.AsNoTracking() on relationship.CoachUserId equals coach.UserId
            where client.ClientId == clientId && coach.UserId == coachId && relationship.StatusCode == "ACTIVE"
                && relationship.RevokedAtUtc == null && client.ClientStatusCode == "ACTIVE" && coach.AccountStatusCode == "ACTIVE"
            orderby relationship.AcceptedAtUtc descending, relationship.CoachClientRelationshipId
            select new { relationship.CoachClientRelationshipId, client.ClientName, client.TimeZone,
                CoachName = coach.PreferredDisplayName ?? coach.DisplayName ?? "Coach" }).FirstOrDefaultAsync(ct);
        if (pair is null) return null;
        var owners = await db.UserClientAccess.AsNoTracking()
            .Where(x => x.ClientId == clientId && x.AccessRoleCode == "OWNER" && x.RevokedAtUtc == null)
            .Select(x => x.UserId).Distinct().Take(2).ToListAsync(ct);
        if (owners.Count != 1 || owners[0] == coachId || !await db.UserAccounts.AsNoTracking()
                .AnyAsync(x => x.UserId == owners[0] && x.AccountStatusCode == "ACTIVE", ct)) return null;
        return new(clientId, coachId, owners[0], pair.CoachClientRelationshipId, pair.ClientName, pair.CoachName, pair.TimeZone);
    }

    public async Task<IReadOnlyList<CoachTaskAssignment>> ReadAsync(CoachTaskParty party, CancellationToken ct) =>
        (await LatestAsync(party.ClientId, ct)).Where(task => BelongsTo(task, party))
            .OrderBy(task => task.CompletedUtc.HasValue).ThenBy(task => task.DueUtc).ToArray();

    public async Task AssignAsync(CoachTaskParty party, Guid requestId, string? title, string? notes, string billing,
        string? unit, decimal? minimum, decimal? maximum, decimal rate, decimal lateFee, DateTime dueLocal, CancellationToken ct)
    {
        var cleanTitle = (title ?? "").Trim();
        var cleanNotes = notes?.Trim();
        if (requestId == Guid.Empty || cleanTitle.Length is 0 or > 120 || cleanNotes?.Length > 2000)
            throw new ArgumentException("Enter a title up to 120 characters and notes up to 2,000 characters.");
        if (billing is not ("fixed" or "quantity" or "minute" or "hour"))
            throw new ArgumentException("Choose a fee type.");
        var label = billing == "quantity" ? (unit ?? "").Trim() : billing == "fixed" ? "task" : "minutes";
        if (label.Length is 0 or > 40) throw new ArgumentException("Enter a quantity unit up to 40 characters.");
        var min = billing == "fixed" ? 1m : minimum;
        var max = billing == "fixed" ? 1m : maximum;
        if (min is null || max is null || min < 0 || max <= 0 || min > max || max > 100000m
            || decimal.Round(min.Value, 2) != min.Value || decimal.Round(max.Value, 2) != max.Value)
            throw new ArgumentException("Enter a minimum and positive maximum with up to two decimal places.");
        if (!AccountabilityDocument.Money(rate) || !AccountabilityDocument.Money(lateFee)
            || AccountabilityAssessment.Cents((billing == "hour" ? max.Value / 60 : max.Value) * rate) > 10000m)
            throw new ArgumentException("Use dollar amounts with up to two decimal places and a maximum task fee of $10,000.");

        var now = time.GetUtcNow();
        var zone = SnapAvailabilityClock.Zone(party.TimeZone);
        if (zone.IsInvalidTime(dueLocal))
            throw new ArgumentException("Choose a deadline outside the skipped local hour.");
        var due = SnapAvailabilityClock.Utc(DateOnly.FromDateTime(dueLocal), TimeOnly.FromDateTime(dueLocal), zone, end: true);
        if (due <= now) throw new ArgumentException("Choose a deadline in the future.");
        var row = await coaching.EnsureAsync(party.CoachId, party.ClientId, ct);
        if ((await LatestAsync(party.ClientId, ct)).Any(task => task.Id == requestId)) return;
        var created = new CoachTaskAssignment(requestId, party.CoachId, party.OwnerId, party.RelationshipId,
            cleanTitle, cleanNotes, billing, label, min.Value, max.Value, rate, lateFee, due, now);
        ledger.Append(row, party.CoachId, [], created);
        await ledger.SaveAsync(ct);
    }

    public async Task<bool> UpdateAsync(CoachTaskParty party, Guid id, int version, string command, decimal? quantity,
        CancellationToken ct)
    {
        var row = await db.CoachAccountabilityAgreements.SingleOrDefaultAsync(x => x.ClientId == party.ClientId, ct);
        if (row is null) return false;
        var task = (await LatestAsync(party.ClientId, ct)).SingleOrDefault(t => t.Id == id && BelongsTo(t, party));
        if (task is null) return false;
        if (task.Version != version) throw new AccountabilityConflictException();
        if (task.CompletedUtc.HasValue) throw new ArgumentException("This task is already complete.");
        var now = time.GetUtcNow();
        var ticks = task.ActiveTicks(now);
        switch (command)
        {
            case "start" when !task.RunningSinceUtc.HasValue:
                if (task.IsTimed && ticks >= (long)(task.Maximum * TimeSpan.TicksPerMinute))
                    throw new ArgumentException("The maximum billable time has been reached. Complete the task to submit it.");
                task = task with { FirstStartedUtc = task.FirstStartedUtc ?? now, RunningSinceUtc = now };
                break;
            case "stop" when task.RunningSinceUtc.HasValue:
                task = task with { LastStoppedUtc = now, RunningSinceUtc = null, ElapsedTicks = ticks };
                break;
            case "complete" when task.FirstStartedUtc.HasValue:
                if (task.Billing == "quantity" && (quantity is null || quantity < task.Minimum || quantity > task.Maximum
                        || decimal.Round(quantity.Value, 2) != quantity.Value))
                    throw new ArgumentException($"Enter a quantity between {task.Minimum:0.##} and {task.Maximum:0.##} {task.Unit}.");
                if (task.IsTimed && ticks < (long)(task.Minimum * TimeSpan.TicksPerMinute))
                    throw new ArgumentException($"Log at least {task.Minimum:0.##} active minutes before completing this task.");
                task = task with { CompletedUtc = now, Quantity = task.Billing == "quantity" ? quantity : null,
                    LastStoppedUtc = task.RunningSinceUtc.HasValue ? now : task.LastStoppedUtc,
                    RunningSinceUtc = null, ElapsedTicks = ticks };
                break;
            default:
                throw new ArgumentException("That action is unavailable for the task’s current status.");
        }

        var entries = new List<AccountabilityLedgerEntry>();
        task = task with { Version = task.Version + 1 };
        if (command == "complete" || (command == "stop" && task.IsTimed))
        {
            var total = task.WorkFee(now);
            if (total > task.WorkFeePosted)
                entries.Add(Entry(task, party, row, party.OwnerId, now, $"work:{task.Version}", "fee",
                    "Task fee", total - task.WorkFeePosted));
            task = task with { WorkFeePosted = total };
        }
        task = AssessLate(task, party, row, party.OwnerId, now, entries);
        ledger.Append(row, party.OwnerId, entries, task);
        await ledger.SaveAsync(ct);
        return true;
    }

    public async Task<int> PostOverdueAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var clientIds = await TaskEvents().Select(e => e.ClientId).Distinct().ToListAsync(ct);
        var posted = 0;
        foreach (var clientId in clientIds)
        {
            var row = await db.CoachAccountabilityAgreements.SingleAsync(x => x.ClientId == clientId, ct);
            var tasks = await LatestAsync(clientId, ct);
            var postedForClient = 0;
            foreach (var group in tasks.Where(task => IsLate(task, now)).GroupBy(task => task.CoachId))
            {
                var party = await PartyAsync(clientId, group.Key, ct);
                if (party is null) continue;
                foreach (var task in group.Where(t => BelongsTo(t, party)))
                {
                    var entries = new List<AccountabilityLedgerEntry>();
                    var assessed = AssessLate(task, party, row, party.CoachId, now, entries);
                    ledger.Append(row, party.CoachId, entries, assessed);
                    postedForClient += entries.Count;
                }
            }
            if (postedForClient > 0)
            {
                await ledger.SaveAsync(ct);
                posted += postedForClient;
            }
            db.Entry(row).State = EntityState.Detached;
        }
        return posted;
    }

    private IQueryable<CoachAccountabilityEvent> TaskEvents() => db.CoachAccountabilityEvents.AsNoTracking()
        .Where(e => e.Kind == "RESULT" && e.PayloadJson.Contains("\"taskAssignment\":"));

    private async Task<IReadOnlyList<CoachTaskAssignment>> LatestAsync(Guid clientId, CancellationToken ct)
    {
        var payloads = await TaskEvents().Where(e => e.ClientId == clientId).OrderByDescending(e => e.Revision)
            .Select(e => e.PayloadJson).ToListAsync(ct);
        var latest = new Dictionary<Guid, CoachTaskAssignment>();
        foreach (var payload in payloads)
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("taskAssignment", out var item)
                && item.Deserialize<CoachTaskAssignment>(AccountabilityDocument.Json) is { } task)
                latest.TryAdd(task.Id, task);
        }
        return latest.Values.ToArray();
    }

    private static bool BelongsTo(CoachTaskAssignment task, CoachTaskParty party) =>
        task.CoachId == party.CoachId && task.OwnerId == party.OwnerId && task.RelationshipId == party.RelationshipId;

    private static bool IsLate(CoachTaskAssignment task, DateTimeOffset now) =>
        task.LateFee > 0 && !task.LateFeePosted && now > task.DueUtc
        && (task.CompletedUtc is null || task.CompletedUtc > task.DueUtc);

    private static CoachTaskAssignment AssessLate(CoachTaskAssignment task, CoachTaskParty party,
        CoachAccountabilityAgreement row, Guid actor, DateTimeOffset now, List<AccountabilityLedgerEntry> entries)
    {
        if (!IsLate(task, now)) return task;
        entries.Add(Entry(task, party, row, actor, task.DueUtc, "late", "late-penalty", "Task deadline penalty", task.LateFee));
        return task with { LateFeePosted = true };
    }

    private static AccountabilityLedgerEntry Entry(CoachTaskAssignment task, CoachTaskParty party,
        CoachAccountabilityAgreement row, Guid actor, DateTimeOffset effective, string suffix, string kind, string label, decimal amount) => new()
    {
        SourceKey = $"task:{task.Id:N}:{suffix}", Date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            effective, SnapAvailabilityClock.Zone(party.TimeZone)).DateTime),
        Kind = kind, Description = $"{label} · {task.Title}", Amount = amount, RawAmount = amount,
        CoachUserId = task.CoachId, ActorUserId = actor, TermsVersion = row.TermsVersion, Automatic = true
    };
}
