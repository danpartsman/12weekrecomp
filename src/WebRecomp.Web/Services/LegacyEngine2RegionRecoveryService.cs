using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

/// <summary>
/// Legacy Engine 2.x regional-load compatibility only.
///
/// v1014C isolates the obsolete CalculatedPush/Pull/Leg/CardioLoad columns here
/// so current planning, Dashboard, Daily Tracking, and Engine 3.0 recovery logic
/// do not depend on them. This service exists only until v1014D removes the
/// remaining Engine 2.x runtime.
/// </summary>
public sealed record RegionRecovery(
    decimal PushRecent,
    decimal PullRecent,
    decimal LegsRecent,
    decimal CardioRecent,
    bool PushRecovered,
    bool PullRecovered,
    bool LegsRecovered,
    bool CardioRecovered,
    decimal Push7,
    decimal Pull7,
    decimal Legs7,
    decimal Cardio7,
    decimal Push14,
    decimal Pull14,
    decimal Legs14,
    decimal Cardio14,
    string RegionPriorityCode,
    string CardioNeedCode);

public sealed class LegacyEngine2RegionRecoveryService(
    RecompDbContext db)
{
    public async Task<RegionRecovery> CalculateAsync(
        Guid programId,
        DateOnly actionDate,
        IReadOnlyDictionary<string, decimal> settings,
        CancellationToken cancellationToken = default)
    {
        var lastDate =
            actionDate.AddDays(-1);

        var start14 =
            actionDate.AddDays(-14);

        var sessions =
            await db.WorkoutSessions
                .Where(x =>
                    x.ProgramId == programId
                    && x.CompletedFlag
                    && x.WorkoutDate >= start14
                    && x.WorkoutDate <= lastDate)
                .ToListAsync(cancellationToken);

        decimal Sum(
            Func<WorkoutSession, decimal?> selector,
            int days)
        {
            var start =
                actionDate.AddDays(-days);

            return sessions
                .Where(x =>
                    x.WorkoutDate >= start
                    && x.WorkoutDate <= lastDate)
                .Sum(x =>
                    selector(x) ?? 0m);
        }

        var pushRecent =
            Sum(
                x => x.CalculatedPushLoad,
                2);

        var pullRecent =
            Sum(
                x => x.CalculatedPullLoad,
                2);

        var legsRecent =
            Sum(
                x => x.CalculatedLegLoad,
                3);

        var cardioRecent =
            Sum(
                x => x.CalculatedCardioLoad,
                2);

        var push7 =
            Sum(
                x => x.CalculatedPushLoad,
                7);

        var pull7 =
            Sum(
                x => x.CalculatedPullLoad,
                7);

        var legs7 =
            Sum(
                x => x.CalculatedLegLoad,
                7);

        var cardio7 =
            Sum(
                x => x.CalculatedCardioLoad,
                7);

        var push14 =
            Sum(
                x => x.CalculatedPushLoad,
                14);

        var pull14 =
            Sum(
                x => x.CalculatedPullLoad,
                14);

        var legs14 =
            Sum(
                x => x.CalculatedLegLoad,
                14);

        var cardio14 =
            Sum(
                x => x.CalculatedCardioLoad,
                14);

        var pushRecovered =
            pushRecent
            < ConfigurationService.Setting(
                settings,
                "PUSH_RECOVERY_LOAD_MAX",
                1.5m);

        var pullRecovered =
            pullRecent
            < ConfigurationService.Setting(
                settings,
                "PULL_RECOVERY_LOAD_MAX",
                1.5m);

        var legsRecovered =
            legsRecent
            < ConfigurationService.Setting(
                settings,
                "LEGS_RECOVERY_LOAD_MAX",
                1.75m);

        var cardioRecovered =
            cardioRecent
            < ConfigurationService.Setting(
                settings,
                "CARDIO_RECOVERY_LOAD_MAX",
                4m);

        var candidates =
            new List<(string Region, decimal Score)>();

        if (pushRecovered)
            candidates.Add(("PUSH", push7 + push14 * 0.35m));

        if (pullRecovered)
            candidates.Add(("PULL", pull7 + pull14 * 0.35m));

        if (legsRecovered)
            candidates.Add(("LEGS", legs7 + legs14 * 0.35m));

        var priority =
            candidates.Count == 0
                ? "NONE"
                : candidates
                    .OrderBy(x => x.Score)
                    .First()
                    .Region;

        var cardioNeed =
            cardio7 < 4.5m
                ? "HIGH"
                : cardio7 < 6.5m
                    ? "MODERATE"
                    : "LOW";

        return new RegionRecovery(
            pushRecent,
            pullRecent,
            legsRecent,
            cardioRecent,
            pushRecovered,
            pullRecovered,
            legsRecovered,
            cardioRecovered,
            push7,
            pull7,
            legs7,
            cardio7,
            push14,
            pull14,
            legs14,
            cardio14,
            priority,
            cardioNeed);
    }
}
