using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.Web.Services.Dashboard3;
using WebRecomp.Web.Controllers;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Notifications;
using WebRecomp.Web.Services.Scheduling;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Services.Coaching;

public sealed class AgreementSettlementJob(
    RecompDbContext db, NotificationFiles files, ICoachSummaryPrescriptionSource prescriptions, INutritionFoodLogStore foodLog,
    SimpleCoachingService coaching, AccountabilityLedger ledger, IConfiguration configuration,
    ILogger<AgreementSettlementJob> logger, TimeProvider? clock = null) : IScheduledJob
{
    private sealed record Progress(DateOnly ClosedThrough, DateTime LastCheckedUtc = default);

    // Explicit one-time authorization for one accepted pair and date. Store the
    // pair fingerprint here instead of identifiers from the production account.
    private const string CatchUpPair = "217e6a07cbec1ae9dc296a7dd100e678ce27380a465dabc4487be9ac6ca548ac";
    private static readonly DateOnly CatchUpDate = new(2026, 11, 7);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!SimpleCoaching.Enabled(configuration)) return;
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        const string activationKey = "financial-agreements/settlement-activation";
        DateTime activated;
        using (files.Lease(activationKey))
        {
            var saved = files.Read<DateTime?>(activationKey);
            activated = saved ?? now;
            if (saved is null) files.Write(activationKey, activated);
        }

        var pairs = await db.CoachClientRelationships.AsNoTracking()
            .Where(link => link.StatusCode == "ACTIVE" && link.RevokedAtUtc == null && link.ClientId.HasValue)
            .Select(link => new { ClientId = link.ClientId!.Value, link.CoachUserId, link.AcceptedAtUtc }).ToListAsync(cancellationToken);
        foreach (var pair in pairs.GroupBy(link => new { link.ClientId, link.CoachUserId })
                     .Select(group => group.OrderByDescending(link => link.AcceptedAtUtc).First()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await PostPairAsync(pair.ClientId, pair.CoachUserId, pair.AcceptedAtUtc, activated, now, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                logger.LogWarning("Agreement settlement for one pair will retry after {FailureType}.", error.GetType().Name);
            }
            finally { db.ChangeTracker.Clear(); }
        }
    }

    private async Task PostPairAsync(Guid clientId, Guid coach, DateTime? linkedAt, DateTime activated,
        DateTime now, CancellationToken ct)
    {
        var pairKey = $"financial-agreements/{clientId:N}-{coach:N}";
        var agreement = files.Read<CoachFinancialAgreement>(pairKey);
        if (agreement is null) return;
        var versions = AgreementSettlement.AcceptedVersions(agreement);
        if (versions.Count == 0) return;
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == clientId && x.ClientStatusCode == "ACTIVE", ct);
        if (client is null) return;
        var owners = await db.UserClientAccess.AsNoTracking()
            .Where(x => x.ClientId == clientId && x.AccessRoleCode == "OWNER" && x.RevokedAtUtc == null)
            .Select(x => x.UserId).Distinct().Take(2).ToListAsync(ct);
        if (owners.Count != 1 || !await db.UserAccounts.AsNoTracking().AnyAsync(x =>
                x.UserId == owners[0] && x.AccountStatusCode == "ACTIVE", ct)) return;

        var zone = TimeZoneInfo.FindSystemTimeZoneById(client.TimeZone);
        DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));
        var first = LocalDate(linkedAt is { } linked && linked > activated ? linked : activated).AddDays(1);
        var closed = LocalDate(now).AddDays(-1);
        var progressKey = $"financial-agreements/settlement-{clientId:N}-{coach:N}";
        using var lease = files.Lease(progressKey);

        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{clientId:N}:{coach:N}")));
        var authorizedPair = fingerprint == CatchUpPair && (linkedAt is null || LocalDate(linkedAt.Value) <= CatchUpDate);
        var acceptedOnDate = authorizedPair
            ? versions.Where(v => v.Revision == 2 && v.AcceptedBy == owners[0]
                && v.AcceptedUtc is { } accepted && LocalDate(accepted) == CatchUpDate).ToArray()
            : [];
        if (acceptedOnDate.Length != 0 && first > CatchUpDate.AddDays(1))
            first = CatchUpDate.AddDays(1);

        if (acceptedOnDate.Length != 0 && closed >= CatchUpDate && closed <= CatchUpDate.AddDays(2))
        {
            var receiptKey = progressKey + "-accepted-day-20261107";
            if (files.Read<bool?>(receiptKey) != true)
            {
                var catchUp = AgreementSettlement.DuePeriods(acceptedOnDate, owners[0], zone,
                    CatchUpDate, CatchUpDate, CatchUpDate, includeAcceptanceDay: true);
                if (catchUp.Count > 0 && await PostPeriodsAsync(client, coach, owners[0], owners[0], agreement, catchUp, ct))
                    files.Write(receiptKey, true);
            }
        }

        if (closed < first) return;
        var previous = files.Read<Progress>(progressKey);
        if (previous is not null && previous.ClosedThrough >= closed
            && now - previous.LastCheckedUtc < TimeSpan.FromMinutes(15)) return;

        // Resume after outages and revisit recently closed periods for late edits.
        var earliestClose = previous is null ? first : previous.ClosedThrough.AddDays(-13);
        if (earliestClose < first) earliestClose = first;
        var periods = AgreementSettlement.DuePeriods(versions, owners[0], zone, first, earliestClose, closed);
        if (await PostPeriodsAsync(client, coach, owners[0], coach, agreement, periods, ct))
            files.Write(progressKey, new Progress(closed, now));
    }

    private async Task<bool> PostPeriodsAsync(Client client, Guid coach, Guid owner, Guid actor,
        CoachFinancialAgreement agreement, IReadOnlyList<AgreementPeriod> periods, CancellationToken ct)
    {
        var clientId = client.ClientId;
        if (periods.Count != 0)
        {
            var from = periods.Min(x => x.Start);
            var through = periods.Max(x => x.End);
            var row = await db.CoachAccountabilityAgreements.SingleOrDefaultAsync(x => x.ClientId == clientId, ct);
            var dayEvidence = await LoadEvidenceAsync(client, owner, agreement, from, through,
                periods.Any(p => p.RuleIndex < 4 || p.RuleIndex is 5 or 8),
                periods.Any(p => p.RuleIndex == 7),
                periods.Any(p => p.RuleIndex == 8 || (p.RuleIndex < 4 && p.Agreement.Rules[p.RuleIndex].LimitMode == "system-percent")), ct);
            var existing = row is null ? new List<AccountabilityLedgerEntry>() : await ledger.ReadAsync(row, ct);
            var charges = new List<(AccountabilityCharge Charge, int Revision)>();
            foreach (var period in periods)
            {
                var days = Enumerable.Range(0, period.End.DayNumber - period.Start.DayNumber + 1)
                    .Select(i => dayEvidence[period.Start.AddDays(i)]).ToArray();
                if (AgreementSettlement.Assess(coach, period, days) is { } charge)
                    charges.Add((charge, period.Agreement.Revision));
            }
            var entries = charges.GroupBy(x => x.Revision).SelectMany(group =>
                AccountabilityLedger.Delta(group.Select(x => x.Charge), existing, group.Key, actor)).ToList();
            foreach (var entry in entries) entry.CoachUserId = coach;
            if (entries.Count != 0)
            {
                row ??= await coaching.EnsureAsync(coach, clientId, ct);
                await ledger.PrepareAutomaticAsync(row, entries, ct);
                if (entries.Count == 0) return false;
                ledger.Append(row, actor, entries);
                await db.SaveChangesAsync(ct);
            }
        }
        return true;
    }

    private async Task<Dictionary<DateOnly, AgreementDay>> LoadEvidenceAsync(Client client, Guid owner,
        CoachFinancialAgreement agreement, DateOnly from, DateOnly through, bool needsNutrition, bool needsCheatFoods,
        bool needsTargets, CancellationToken ct)
    {
        var id = client.ClientId;
        var nutrition = (await db.NutritionDays.AsNoTracking().Where(x => x.ClientId == id
            && x.NutritionDate >= from && x.NutritionDate <= through).ToListAsync(ct))
            .GroupBy(x => x.NutritionDate).ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.UpdatedAtUtc).First());
        var scale = (await db.Measurements.AsNoTracking().Where(x => x.ClientId == id && !x.IsDeleted
            && x.LocalMeasurementDate >= from && x.LocalMeasurementDate <= through
            && (x.WeightLb != null || x.BodyFatPct != null || x.MuscleMassLb != null || x.BodyWaterLb != null))
            .Select(x => x.LocalMeasurementDate).ToListAsync(ct)).ToHashSet();
        var checkins = (await db.DailyCheckins.AsNoTracking().Where(x => x.ClientId == id
            && x.CheckinDate >= from && x.CheckinDate <= through
            && (x.SleepHours != null || x.EnergyScore != null || x.SorenessScore != null || x.RestingHr != null))
            .Select(x => x.CheckinDate).ToListAsync(ct)).ToHashSet();
        var plans = (await db.PlannedWorkouts.AsNoTracking().Where(x => x.ClientId == id
            && x.PlannedDate >= from && x.PlannedDate <= through && x.WorkoutTypeCode != "REST").ToListAsync(ct))
            .ToLookup(x => x.PlannedDate);
        var workouts = (await db.WorkoutSessions.AsNoTracking().Where(x => x.ClientId == id && x.CompletedFlag
            && x.EntrySourceCode != SnapWorkoutEngine.Source && x.WorkoutDate >= from && x.WorkoutDate <= through)
            .ToListAsync(ct)).ToLookup(x => x.WorkoutDate);
        var targets = new Dictionary<DateOnly, CoachSummaryPrescription>();
        if (needsTargets)
            for (var date = from; date <= through; date = date.AddDays(7))
                foreach (var target in (await prescriptions.LoadAsync(owner, client, date.AddDays(7), ct)).Days)
                    targets.TryAdd(target.Key, target.Value);

        var savedCounts = new Dictionary<(Guid Program, DateOnly Date), int?>();
        var programs = new List<ProgramRecord>();
        if (needsCheatFoods)
        {
            programs = await db.Programs.AsNoTracking().Where(x => x.ClientId == id && x.StartDate <= through)
                .OrderByDescending(x => x.StartDate).ToListAsync(ct);
            var counts = await db.AnalysisRuns.AsNoTracking().Where(x => x.ClientId == id
                && x.EngineVersion == RecompCockpitService.FlexVersion && x.ActionDate >= from && x.ActionDate <= through)
                .OrderByDescending(x => x.StartedAtUtc).ThenByDescending(x => x.AnalysisRunId)
                .Select(x => new { x.ProgramId, x.ActionDate, x.DiagnosticJson }).ToListAsync(ct);
            foreach (var record in counts)
                savedCounts.TryAdd((record.ProgramId, record.ActionDate), RecompCockpitService.ReadFlexibleCount(record.DiagnosticJson));
        }

        var evidence = new Dictionary<DateOnly, AgreementDay>();
        for (var date = from; date <= through; date = date.AddDays(1))
        {
            var review = agreement.Reviews?.GetValueOrDefault(date);
            var planned = plans[date].ToArray();
            var done = workouts[date].ToList();
            // One completion can satisfy one planned session of the same workout type.
            foreach (var plan in planned)
            {
                var match = done.FindIndex(workout => workout.ProgramId == plan.ProgramId
                    && workout.WorkoutTypeCode == plan.WorkoutTypeCode);
                if (match < 0) continue;
                done.RemoveAt(match);
            }
            var intake = nutrition.GetValueOrDefault(date);
            var nutritionComplete = intake is
                { ActualCalories: not null, ActualProteinG: not null, ActualCarbsG: not null, ActualFatG: not null };
            var cheatFoods = review?.CheatFoods;
            if (review?.Excused != true)
            {
                var program = needsCheatFoods
                    ? programs.FirstOrDefault(x => x.ProgramId == intake?.ProgramId)
                        ?? programs.FirstOrDefault(x => x.StartDate <= date)
                    : null;
                if (cheatFoods is null && program is not null)
                    cheatFoods = savedCounts.GetValueOrDefault((program.ProgramId, date));

                var checkFoodLog = needsNutrition && nutritionComplete && intake!.EntrySourceCode == "FOOD_LOG";
                var detectCheatFoods = cheatFoods is null && program?.NutritionFoodLogEnabled == true
                    && (intake?.EntrySourceCode is null or "FOOD_LOG" or "MANUAL");
                if (checkFoodLog || detectCheatFoods)
                {
                    var log = await foodLog.GetDailyLogAsync(id, date, ct);
                    if (checkFoodLog) nutritionComplete = log.Items.Count > 0 && RecompTreatClassifier.Complete(log);
                    if (detectCheatFoods && RecompTreatClassifier.CanCount(log, intake?.EntrySourceCode))
                        cheatFoods = RecompTreatClassifier.Count(log);
                }
            }
            evidence.Add(date, new(intake, targets.GetValueOrDefault(date), nutritionComplete,
                scale.Contains(date) || checkins.Contains(date), planned.Length > 0,
                workouts[date].Count() - done.Count == planned.Length, review?.Excused == true, cheatFoods));
        }
        return evidence;
    }
}
