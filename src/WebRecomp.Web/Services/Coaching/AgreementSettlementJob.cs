using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.Web.Services.Dashboard3;
using WebRecomp.Web.Controllers;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Notifications;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Services.Coaching;

public sealed class AgreementSettlementJob(
    RecompDbContext db, NotificationFiles files, ICoachSummaryPrescriptionSource prescriptions, INutritionFoodLogStore foodLog,
    SimpleCoachingService coaching, AccountabilityLedger ledger, IConfiguration configuration,
    ILogger<AgreementSettlementJob> logger, TimeProvider? clock = null)
{
    private sealed record Progress(DateOnly ClosedThrough);

    // Explicit one-time authorization for one accepted pair and date. Store the
    // pair fingerprint here instead of identifiers from the production account.
    private const string CatchUpPair = "217e6a07cbec1ae9dc296a7dd100e678ce27380a465dabc4487be9ac6ca548ac";
    private static readonly DateOnly CatchUpDate = new(2026, 11, 7);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (!SimpleCoaching.Enabled(configuration))
            throw new InvalidOperationException("Agreement fee settlement is disabled.");
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        const string activationKey = "financial-agreements/settlement-activation";
        const string firstRunKey = "financial-agreements/settlement-first-run";
        DateTime activated;
        bool hasLegacyActivation;
        using (files.Lease(activationKey))
        {
            var legacy = files.Read<DateTime?>(activationKey);
            hasLegacyActivation = legacy.HasValue;
            var saved = legacy ?? files.Read<DateTime?>(firstRunKey);
            activated = saved ?? now;
            if (saved is null) files.Write(firstRunKey, activated);
        }

        var pairs = await db.CoachClientRelationships.AsNoTracking()
            .Where(link => link.StatusCode == "ACTIVE" && link.RevokedAtUtc == null && link.ClientId.HasValue)
            .Select(link => new { ClientId = link.ClientId!.Value, link.CoachUserId, link.AcceptedAtUtc }).ToListAsync(cancellationToken);
        var posted = 0;
        var failures = 0;
        foreach (var pair in pairs.GroupBy(link => new { link.ClientId, link.CoachUserId })
                     .Select(group => group.OrderByDescending(link => link.AcceptedAtUtc).First()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                posted += await PostPairAsync(pair.ClientId, pair.CoachUserId, pair.AcceptedAtUtc, activated,
                    hasLegacyActivation, now, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                failures++;
                logger.LogWarning("Agreement settlement failed for one pair with {FailureType}.", error.GetType().Name);
            }
            finally { db.ChangeTracker.Clear(); }
        }
        if (failures > 0)
            throw new InvalidOperationException($"Agreement settlement failed for {failures} pair(s). Posted {posted} ledger entries before the failures.");
        return posted;
    }

    private async Task<int> PostPairAsync(Guid clientId, Guid coach, DateTime? linkedAt, DateTime activated,
        bool hasLegacyActivation, DateTime now, CancellationToken ct)
    {
        var pairKey = $"financial-agreements/{clientId:N}-{coach:N}";
        var agreement = files.Read<CoachFinancialAgreement>(pairKey);
        if (agreement is null) return 0;
        var versions = AgreementSettlement.AcceptedVersions(agreement);
        if (versions.Count == 0) return 0;
        var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(x => x.ClientId == clientId && x.ClientStatusCode == "ACTIVE", ct);
        if (client is null) return 0;
        var owners = await db.UserClientAccess.AsNoTracking()
            .Where(x => x.ClientId == clientId && x.AccessRoleCode == "OWNER" && x.RevokedAtUtc == null)
            .Select(x => x.UserId).Distinct().Take(2).ToListAsync(ct);
        if (owners.Count != 1 || !await db.UserAccounts.AsNoTracking().AnyAsync(x =>
                x.UserId == owners[0] && x.AccountStatusCode == "ACTIVE", ct)) return 0;

        var zone = TimeZoneInfo.FindSystemTimeZoneById(client.TimeZone);
        DateTime LocalTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(LocalTime(utc));
        DateOnly ClosedThrough(DateTime utc)
        {
            var localTime = LocalTime(utc);
            return DateOnly.FromDateTime(localTime).AddDays(localTime.TimeOfDay < TimeSpan.FromMinutes(1) ? -2 : -1);
        }
        var first = linkedAt is { } linked ? LocalDate(linked).AddDays(1) : DateOnly.MinValue;
        // Preserve the previous activation cutoff. A fresh installation starts
        // with the period that closed at the first trigger, even for weekly rules.
        if (hasLegacyActivation && LocalDate(activated).AddDays(1) > first)
            first = LocalDate(activated).AddDays(1);
        var activationClose = hasLegacyActivation ? first : ClosedThrough(activated);
        var closed = ClosedThrough(now);
        var progressKey = $"financial-agreements/settlement-{clientId:N}-{coach:N}";
        using var lease = files.Lease(progressKey);
        var posted = 0;

        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{clientId:N}:{coach:N}")));
        var authorizedPair = fingerprint == CatchUpPair && (linkedAt is null || LocalDate(linkedAt.Value) <= CatchUpDate);
        var acceptedOnDate = authorizedPair
            ? versions.Where(v => v.Revision == 2 && v.AcceptedBy == owners[0]
                && v.AcceptedUtc is { } accepted && LocalDate(accepted) == CatchUpDate).ToArray()
            : [];
        if (hasLegacyActivation && acceptedOnDate.Length != 0 && first > CatchUpDate.AddDays(1))
        {
            first = CatchUpDate.AddDays(1);
            activationClose = first;
        }
        if (acceptedOnDate.Length != 0 && closed >= CatchUpDate && closed <= CatchUpDate.AddDays(2))
        {
            var receiptKey = progressKey + "-accepted-day-20261107";
            if (files.Read<bool?>(receiptKey) != true)
            {
                var catchUp = AgreementSettlement.DuePeriods(acceptedOnDate, owners[0], zone,
                    CatchUpDate, CatchUpDate, CatchUpDate, includeAcceptanceDay: true);
                posted += await PostPeriodsAsync(client, coach, owners[0], owners[0], agreement, catchUp, ct);
                files.Write(receiptKey, true);
            }
        }

        if (closed < first || closed < activationClose) return posted;
        var previous = files.Read<Progress>(progressKey);

        // Reconcile missed closes and recent edits only when this job is explicitly triggered.
        var earliestClose = previous is null ? activationClose : previous.ClosedThrough.AddDays(-13);
        if (earliestClose < activationClose) earliestClose = activationClose;
        if (earliestClose < first) earliestClose = first;
        var periods = AgreementSettlement.DuePeriods(versions, owners[0], zone, first, earliestClose, closed);
        posted += await PostPeriodsAsync(client, coach, owners[0], coach, agreement, periods, ct);
        if (previous is null || previous.ClosedThrough < closed)
            files.Write(progressKey, new Progress(closed));
        return posted;
    }

    private async Task<int> PostPeriodsAsync(Client client, Guid coach, Guid owner, Guid actor,
        CoachFinancialAgreement agreement, IReadOnlyList<AgreementPeriod> periods, CancellationToken ct)
    {
        var clientId = client.ClientId;
        if (periods.Count != 0)
        {
            var from = periods.Min(x => x.Start);
            var through = periods.Max(x => x.End);
            var row = await db.CoachAccountabilityAgreements.SingleOrDefaultAsync(x => x.ClientId == clientId, ct);
            var dayEvidence = await LoadEvidenceAsync(client, owner, agreement, from, through,
                periods.Any(p => p.Agreement.Rules[p.RuleIndex].Enabled && (p.RuleIndex < 4 || p.RuleIndex is 5 or 8)),
                periods.Any(p => p.Agreement.Rules[p.RuleIndex].Enabled && p.RuleIndex == 7),
                periods.Any(p => p.Agreement.Rules[p.RuleIndex].Enabled && (p.RuleIndex == 8 || (p.RuleIndex < 4 && p.Agreement.Rules[p.RuleIndex].LimitMode == "system-percent"))), ct);
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
                if (entries.Count == 0)
                    throw new InvalidOperationException("The ledger changed while agreement fees were being prepared.");
                ledger.Append(row, actor, entries);
                await db.SaveChangesAsync(ct);
            }
            return entries.Count;
        }
        return 0;
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
