using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Notifications;

namespace WebRecomp.Web.Services.Coaching;

public sealed record SnapChatPost(Guid Id, long Sequence, Guid AuthorId, string AuthorName, bool Coach, string Body, DateTimeOffset CreatedUtc);
public sealed class SnapLiveState
{
    public Guid ClientId { get; set; }
    public Guid CoachId { get; set; }
    public string WorkoutId { get; set; } = "";
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? SavedUtc { get; set; }
    public int ProgressRevision { get; set; }
    public Dictionary<Guid, string> AddOnResponses { get; set; } = [];
    public Dictionary<string, string> ExerciseTokens { get; set; } = [];
    public Dictionary<string, List<AccountabilitySet>> Sets { get; set; } = [];
    public List<SnapChatPost> Messages { get; set; } = [];
    public HashSet<string> ImportedNoteKeys { get; set; } = [];
}
public sealed record SnapLiveView(int Revision, string AssignmentToken, int ProgressRevision,
    DateTimeOffset? StartedUtc, DateTimeOffset? SavedUtc, bool Finished, DateTimeOffset? FinishedUtc,
    Dictionary<string, List<AccountabilitySet>> Sets, IReadOnlyList<SnapChatPost> Messages,
    bool HasEarlierMessages, string WorkoutName);
public sealed record SnapNotesPanel(Guid ClientId, string WorkoutId);
public sealed record SnapLiveAccess(CoachAccountabilityAgreement Agreement, AccountabilityState State,
    AccountabilityWorkout Workout, Guid CoachId, bool IsCoach, UserAccount Actor)
{
    public Guid ClientUserId { get; init; }
    public string ClientName { get; init; } = "Client";
    public string CoachName { get; init; } = "Coach";
}

/// <summary>Private, encrypted App Service storage. Draft writes never call the
/// financial ledger or the canonical workout engine. File leases serialize tabs,
/// chat appends and final submission without changing shared agreement revisions.</summary>
public sealed class SnapLiveService(RecompDbContext db, NotificationFiles files, SimpleCoachingService simple, CoachNotificationProcessor? notifications = null, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public static string Key(Guid client, string workout)
    {
        if (!AccountabilityDocument.Id(workout)) throw new ArgumentException("Choose an assigned workout.");
        return $"snap-live/{client:N}-{workout}";
    }
    public static string AssignmentToken(AccountabilityWorkout workout) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { workout.Id, workout.Name, workout.Date, workout.Exercises,
            workout.TimeLimitMinutes, workout.Cap, workout.LatePenalty, workout.MinimumCharge, workout.CompletionReward,
            workout.OverrideSchedule, workout.OverrideHealthConflicts }, AccountabilityDocument.Json))));
    private static string ExerciseToken(AccountabilityExercise exercise) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(exercise, AccountabilityDocument.Json))));
    public async Task<SnapLiveAccess> AllowedAsync(Guid actor, Guid client, string id, CancellationToken ct)
    {
        _ = Key(client, id);
        if (!await db.Clients.AnyAsync(x => x.ClientId == client && x.ClientStatusCode == "ACTIVE", ct)) throw new UnauthorizedAccessException();
        var row = await db.CoachAccountabilityAgreements.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == client, ct)
            ?? throw new UnauthorizedAccessException();
        var state = AccountabilityDocument.Parse(row.StateJson);
        var workout = state.SnapWorkouts.SingleOrDefault(x => x.Simple && x.Id == id) ?? throw new UnauthorizedAccessException();
        var coach = (await SnapWorkoutAssignments.OwnersAsync(db, client, ct)).GetValueOrDefault(id);
        if (coach == Guid.Empty || !await db.CoachClientRelationships.AnyAsync(x => x.ClientId == client && x.CoachUserId == coach
            && x.StatusCode == "ACTIVE" && x.RevokedAtUtc == null, ct)) throw new UnauthorizedAccessException();
        var owners = await db.UserClientAccess.AsNoTracking().Where(x => x.ClientId == client && x.AccessRoleCode == "OWNER" && x.RevokedAtUtc == null)
            .Select(x => x.UserId).Distinct().Take(2).ToArrayAsync(ct);
        if (owners.Length != 1 || (actor != owners[0] && actor != coach)) throw new UnauthorizedAccessException();
        var participants = await db.UserAccounts.AsNoTracking().Where(x => (x.UserId == owners[0] || x.UserId == coach)
            && x.AccountStatusCode == "ACTIVE").ToArrayAsync(ct);
        var user = participants.SingleOrDefault(x => x.UserId == actor) ?? throw new UnauthorizedAccessException();
        var clientUser = participants.SingleOrDefault(x => x.UserId == owners[0]) ?? throw new UnauthorizedAccessException();
        var coachUser = participants.SingleOrDefault(x => x.UserId == coach) ?? throw new UnauthorizedAccessException();
        var allowed = new SnapLiveAccess(row, state, workout, coach, actor == coach && actor != owners[0], user)
        {
            ClientUserId = clientUser.UserId, ClientName = Name(clientUser, "Client"), CoachName = Name(coachUser, "Coach")
        };
        ImportNotes(allowed);
        return allowed;
    }
    private static string Name(UserAccount user, string fallback) => user.PreferredDisplayName ?? user.DisplayName ?? fallback;
    private void ImportNotes(SnapLiveAccess allowed)
    {
        // Retain original assignment/completion notes in the same append-only thread.
        // Stable content keys make refreshes and retries safe without touching billing.
        var result = allowed.State.SnapResults.GetValueOrDefault(allowed.Workout.Id);
        var created = allowed.Workout.CreatedAtUtc ?? new DateTimeOffset(AccountabilityDocument.Date(allowed.Workout.Date).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var candidates = new[]
        {
            (Role: "coach", Body: allowed.Workout.CoachNote, Author: allowed.CoachId, AuthorName: allowed.CoachName, At: created),
            (Role: "client", Body: result?.ClientNote, Author: allowed.ClientUserId, AuthorName: allowed.ClientName, At: result?.CompletedAtUtc ?? created)
        }.Where(x => !string.IsNullOrWhiteSpace(x.Body)).Select(x => (Note: x,
            Key: x.Role + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x.Body!.Trim()))))).ToArray();
        if (candidates.Length == 0) return;
        var key = Key(allowed.Agreement.ClientId, allowed.Workout.Id);
        var saved = ReadStored(allowed.Agreement.ClientId, allowed.CoachId, allowed.Workout.Id);
        if (candidates.All(x => saved.ImportedNoteKeys.Contains(x.Key))) return;
        using (files.Lease(key))
        {
            var live = ReadStored(allowed.Agreement.ClientId, allowed.CoachId, allowed.Workout.Id);
            var changed = false;
            foreach (var candidate in candidates)
            {
                if (live.ImportedNoteKeys.Contains(candidate.Key)) continue;
                var note = candidate.Note;
                var first = !live.ImportedNoteKeys.Any(x => x.StartsWith(note.Role + ":", StringComparison.Ordinal));
                var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key + ":" + candidate.Key)).AsSpan(0, 16));
                live.Messages.Add(new(id, (live.Messages.LastOrDefault()?.Sequence ?? 0) + 1, note.Author,
                    note.AuthorName, note.Role == "coach", note.Body!.Trim(), first ? note.At : time.GetUtcNow()));
                live.ImportedNoteKeys.Add(candidate.Key); changed = true;
            }
            if (changed) files.Write(key, live);
        }
    }
    private SnapLiveState ReadStored(Guid client, Guid coach, string workoutId)
    {
        var state = files.Read<SnapLiveState>(Key(client, workoutId)) ?? new() { ClientId = client, CoachId = coach, WorkoutId = workoutId };
        if (state.ClientId != client || state.CoachId != coach || state.WorkoutId != workoutId) throw new UnauthorizedAccessException();
        return state;
    }
    public SnapLiveState ReadSaved(Guid client, Guid coach, AccountabilityWorkout workout)
    {
        var state = ReadStored(client, coach, workout.Id);
        // Assignment edits retain draft sets only for unchanged exercise rows.
        state.Sets = workout.Exercises.Where(e => state.ExerciseTokens.GetValueOrDefault(e.Id) == ExerciseToken(e))
            .Where(e => state.Sets.ContainsKey(e.Id)).ToDictionary(e => e.Id, e => state.Sets[e.Id]);
        return state;
    }
    public SnapLiveView View(SnapLiveAccess allowed, long? before = null)
    {
        var live = ReadSaved(allowed.Agreement.ClientId, allowed.CoachId, allowed.Workout);
        var final = allowed.State.SnapResults.GetValueOrDefault(allowed.Workout.Id);
        var messages = live.Messages.Where(x => !before.HasValue || x.Sequence < before).OrderBy(x => x.Sequence).TakeLast(100).ToArray();
        return new(allowed.Agreement.Revision, AssignmentToken(allowed.Workout), live.ProgressRevision,
            live.StartedUtc, live.SavedUtc, final?.Finalized == true, final?.CompletedAtUtc,
            final?.Finalized == true ? final.Sets : live.StartedUtc.HasValue ? live.Sets : final?.Sets ?? [], messages,
            messages.Length > 0 && live.Messages.Any(x => x.Sequence < messages[0].Sequence), allowed.Workout.Name);
    }
    public async Task RespondToAddOnAsync(Guid actor, Guid client, string id, Guid offerId, string decision, CancellationToken ct)
    {
        var allowed = await AllowedAsync(actor, client, id, ct);
        if (allowed.IsCoach || allowed.ClientUserId != actor) throw new UnauthorizedAccessException();
        var preMobility = allowed.Workout.PreMobility?.Id == offerId;
        if (!preMobility && allowed.Workout.Finisher?.Id != offerId)
            throw new ArgumentException("This optional exercise changed. Review the current offer.");
        if (preMobility && allowed.State.SnapResults.GetValueOrDefault(id)?.Finalized == true)
            throw new ArgumentException("This workout is already finished.");

        var key = Key(client, id);
        using (files.Lease(key))
        {
            var saved = ReadStored(client, allowed.CoachId, id);
            var current = saved.AddOnResponses.GetValueOrDefault(offerId);
            if (current == decision) return;
            if (!(current is null && decision is ("accepted" or "skipped") ||
                current == "accepted" && decision == "completed"))
                throw new ArgumentException("Review the current optional exercise before responding.");
            saved.AddOnResponses[offerId] = decision;
            files.Write(key, saved);
        }
    }

    public async Task<SnapLiveView> StartAsync(Guid actor, Guid client, string id, CancellationToken ct)
    {
        var allowed = await AllowedAsync(actor, client, id, ct);
        if (allowed.IsCoach) throw new UnauthorizedAccessException();
        var key = Key(client, id);
        using (files.Lease(key))
        {
            var live = ReadSaved(client, allowed.CoachId, allowed.Workout);
            if (!live.StartedUtc.HasValue && allowed.State.SnapResults.GetValueOrDefault(id)?.Finalized != true)
            {
                live.StartedUtc = time.GetUtcNow();
                // Seed older manually-saved progress once when upgrading a workout.
                if (allowed.State.SnapResults.GetValueOrDefault(id) is { } prior) live.Sets = prior.Sets;
                live.ExerciseTokens = allowed.Workout.Exercises.ToDictionary(e => e.Id, ExerciseToken);
                files.Write(key, live);
            }
        }
        if (notifications is not null)
            try { await notifications.QueueLiveStartAsync(client, id, ct); }
            catch (Exception error) when (error is not OperationCanceledException) { /* The notification worker recovers the saved start. */ }
        return View(allowed);
    }
    public async Task<SnapLiveView> SaveAsync(Guid actor, Guid client, string id, string token, int expected,
        Dictionary<string, List<AccountabilitySet>> sets, CancellationToken ct)
    {
        var allowed = await AllowedAsync(actor, client, id, ct);
        if (allowed.IsCoach) throw new UnauthorizedAccessException();
        using (files.Lease(Key(client, id)))
        {
            var live = ReadSaved(client, allowed.CoachId, allowed.Workout);
            if (live.StartedUtc.HasValue && allowed.State.SnapResults.GetValueOrDefault(id)?.Finalized != true
                && token == AssignmentToken(allowed.Workout) && expected != live.ProgressRevision
                && AccountabilityDocument.Canonical(JsonSerializer.Serialize(live.Sets, AccountabilityDocument.Json))
                    == AccountabilityDocument.Canonical(JsonSerializer.Serialize(sets, AccountabilityDocument.Json)))
                return View(allowed); // A response may have been lost after a successful save.
            ValidateDraft(allowed.Workout, allowed.State, token, expected, live, sets);
            live.Sets = sets; live.ProgressRevision++; live.SavedUtc = time.GetUtcNow();
            live.ExerciseTokens = allowed.Workout.Exercises.ToDictionary(e => e.Id, ExerciseToken);
            files.Write(Key(client, id), live);
        }
        return View(allowed);
    }
    public static void ValidateDraft(AccountabilityWorkout workout, AccountabilityState state, string token, int expected,
        SnapLiveState live, Dictionary<string, List<AccountabilitySet>> sets)
    {
        if (state.SnapResults.GetValueOrDefault(workout.Id)?.Finalized == true) throw new ArgumentException("This workout is already finished. Reload to review the submitted results.");
        if (!live.StartedUtc.HasValue) throw new ArgumentException("Press Start workout before recording sets.");
        if (token != AssignmentToken(workout)) throw new ArgumentException("Your coach changed this assignment. Your entries are retained on screen; reload before continuing.");
        if (expected != live.ProgressRevision) throw new AccountabilityConflictException();
        AccountabilityDocument.ValidateResult(state, workout.Id, new() { Sets = sets });
    }
    public async Task<SnapLiveView> MessageAsync(Guid actor, Guid client, string id, Guid requestId, string body, CancellationToken ct)
    {
        if (requestId == Guid.Empty || string.IsNullOrWhiteSpace(body) || body.Trim().Length > 2000) throw new ArgumentException("Enter a message of 1–2,000 characters.");
        var allowed = await AllowedAsync(actor, client, id, ct);
        using (files.Lease(Key(client, id)))
        {
            var live = ReadSaved(client, allowed.CoachId, allowed.Workout);
            var existing = live.Messages.FirstOrDefault(m => m.Id == requestId);
            if (existing is not null)
            {
                if (existing.AuthorId != actor || existing.Body != body.Trim()) throw new ArgumentException("This message identifier was already used. Reload before sending.");
                return View(allowed);
            }
            if (live.Messages.Count >= 2000) throw new ArgumentException("This workout's conversation is full.");
            live.Messages.Add(new(requestId, (live.Messages.LastOrDefault()?.Sequence ?? 0) + 1, actor,
                allowed.Actor.PreferredDisplayName ?? allowed.Actor.DisplayName ?? (allowed.IsCoach ? "Coach" : "Client"), allowed.IsCoach, body.Trim(), time.GetUtcNow()));
            files.Write(Key(client, id), live);
        }
        return View(allowed);
    }
    public async Task FinishAsync(Guid actor, Guid client, string id, string token, int expected, AccountabilityResult result, CancellationToken ct)
    {
        var allowed = await AllowedAsync(actor, client, id, ct);
        if (allowed.IsCoach) throw new UnauthorizedAccessException();
        using (files.Lease(Key(client, id)))
        {
            var live = ReadSaved(client, allowed.CoachId, allowed.Workout);
            if (token != AssignmentToken(allowed.Workout) || expected != live.ProgressRevision) throw new AccountabilityConflictException();
            result.Finalized = true;
            await simple.ResultAsync(actor, client, false, allowed.Agreement.Revision, id, result, ct, allowed.CoachId);
            live.ProgressRevision++; live.SavedUtc = time.GetUtcNow(); live.Sets = result.Sets;
            live.ExerciseTokens = allowed.Workout.Exercises.ToDictionary(e => e.Id, ExerciseToken);
            files.Write(Key(client, id), live);
        }
    }
    public static string ProgressUrl(Guid client, string id) => $"/clients/{client:D}/snap?assignment={Uri.EscapeDataString(id)}#live-{Uri.EscapeDataString(id)}";
}
