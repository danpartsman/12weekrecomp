using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;

namespace WebRecomp.Web.Services;

public sealed record WeeklyStrategyAssessment(
    DateOnly WeekStart,
    short MajorPushCount,
    short MajorPullCount,
    short MajorLegsCount,
    short LightStrengthCount,
    short PrimaryConditioningCount,
    short RecoveryCount,
    short HeavyGenericCount,
    string StatusCode,
    string Note);

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

public sealed class WeeklyStrategyService(
    RecompDbContext db,
    Engine30WorkoutUiCatalogService engine30WorkoutCatalog)
{
    public static DateOnly WeekStart(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        return date.AddDays(-diff);
    }

    public async Task<WeeklyStrategyAssessment> AssessWeekAsync(
        Guid programId,
        Guid configVersionId,
        DateOnly actionDate,
        string planCode,
        byte availableTrainingDaysWeek,
        CancellationToken cancellationToken = default)
    {
        var start = WeekStart(actionDate);
        var end = start.AddDays(6);

        var planned = await db.PlannedWorkouts
            .Where(x => x.ProgramId == programId && x.PlannedDate >= start && x.PlannedDate <= end)
            .ToListAsync(cancellationToken);

        if (planned.Count == 0)
        {
            return new WeeklyStrategyAssessment(
                start, 0, 0, 0, 0, 0, 0, 0,
                "NOT_PLANNED",
                "Set up the week so Recomp can evaluate the training mix.");
        }

        _ = configVersionId; // Weekly Plan now evaluates the Engine 3.0 user-facing workout catalog.
        var definitions =
            await engine30WorkoutCatalog.GetActiveMapAsync(cancellationToken);

        short push = 0, pull = 0, legs = 0, light = 0, conditioning = 0, recovery = 0, generic = 0, zone2 = 0;

        foreach (var item in planned)
        {
            var normalizedCode =
                Engine30WorkoutUiCatalogService.NormalizeCode(
                    item.WorkoutTypeCode);

            if (!definitions.TryGetValue(normalizedCode, out var type))
                continue;

            switch (normalizedCode)
            {
                case "MAJOR_PUSH":
                    push++;
                    break;

                case "MAJOR_PULL":
                    pull++;
                    break;

                case "MAJOR_LEGS":
                    legs++;
                    break;

                case "FULL_BODY_STRENGTH":
                    generic++;
                    break;
            }

            var category =
                (type.WorkoutCategoryCode ?? "")
                    .Trim()
                    .ToUpperInvariant();

            if (category == "ACCESSORY_STRENGTH")
                light++;

            if (category == "REST_RECOVERY_OTHER")
                recovery++;

            if (normalizedCode is
                "ZONE2_CARDIO"
                or "EASY_CARDIO"
                or "WALKING")
            {
                zone2++;
            }
            else if (category == "CONDITIONING_CARDIO")
            {
                conditioning++;
            }
        }

        var available = Math.Clamp((int)availableTrainingDaysWeek, 1, 6);
        var normalizedPlan = NormalizePlan(planCode);
        var heavy = push + pull + legs;
        var strength = heavy + light + generic;
        var notes = new List<string>();
        bool good;
        string successNote;

        switch (normalizedPlan)
        {
            case "PLAN_1":
                var healthStrengthTarget = available >= 4 ? 2 : 1;
                good = strength >= healthStrengthTarget
                       && (available < 3 || zone2 >= 1)
                       && recovery >= 1;
                if (strength < healthStrengthTarget) notes.Add("Add a repeatable strength session.");
                if (available >= 3 && zone2 == 0) notes.Add("Add an easy Zone 2 / aerobic day.");
                if (recovery == 0) notes.Add("Add a recovery day.");
                successNote = "Health Reset week is balanced around repeatable strength, easy aerobic work, and recovery.";
                break;

            case "PLAN_2":
                var fatLossStrengthGood = available >= 3
                    ? push >= 1 && pull >= 1 && legs >= 1
                    : strength >= 1;
                var fatLossConditioningGood = available < 4 || conditioning >= 1;
                good = fatLossStrengthGood && fatLossConditioningGood && recovery >= 1;
                if (!fatLossStrengthGood)
                    notes.Add(available >= 3 ? "Include Push, Pull, and Legs strength across the week." : "Add a full-body strength session.");
                if (!fatLossConditioningGood) notes.Add("Add one conditioning / cardio session.");
                if (recovery == 0) notes.Add("Add a recovery day.");
                successNote = "Fat-loss Recomp week protects muscle with strength work while adding conditioning and recovery to support a productive deficit.";
                break;

            case "PLAN_3":
                var muscleHeavyGood = available >= 3
                    ? push >= 1 && pull >= 1 && legs >= 1
                    : strength >= 1;
                var accessoryGood = available < 4 || light >= 1;
                good = muscleHeavyGood && accessoryGood && recovery >= 1;
                if (!muscleHeavyGood)
                    notes.Add(available >= 3 ? "Include Major Push, Major Pull, and Major Legs." : "Add a full-body strength session.");
                if (!accessoryGood) notes.Add("Add one accessory / lighter strength day.");
                if (recovery == 0) notes.Add("Add a recovery day.");
                if (conditioning > 1) notes.Add("Muscle-focus Recomp usually needs less hard conditioning so recovery stays available for lifting.");
                successNote = "Muscle-focus Recomp week protects the major lifting sessions, adds accessory work when time allows, and leaves room to recover.";
                break;

            case "PLAN_5":
                var conservativeStrengthTarget = available >= 4 ? 2 : available >= 2 ? 1 : 0;
                good = light >= conservativeStrengthTarget
                       && (available < 3 || zone2 >= 1)
                       && recovery >= 2
                       && heavy == 0;
                if (light < conservativeStrengthTarget) notes.Add("Add a lighter or corrective strength session.");
                if (available >= 3 && zone2 == 0) notes.Add("Add an easy Zone 2 / aerobic day if tolerated.");
                if (recovery < 2) notes.Add("Keep more recovery / mobility space in this plan.");
                if (heavy > 0) notes.Add("Pain-Limited / Mobility is intentionally conservative; review major heavy sessions carefully.");
                successNote = "Pain-Limited / Mobility week is intentionally conservative, with lighter strength, easy aerobic work, mobility, and recovery.";
                break;

            default:
                var athleticHeavyGood = available >= 3
                    ? push >= 1 && pull >= 1 && legs >= 1
                    : strength >= 1;
                var athleticConditioningTarget = available >= 6 ? 2 : available >= 4 ? 1 : 0;
                good = athleticHeavyGood
                       && conditioning >= athleticConditioningTarget
                       && recovery >= 1;
                if (!athleticHeavyGood)
                    notes.Add(available >= 3 ? "Include Major Push, Major Pull, and Major Legs." : "Add a full-body strength session.");
                if (conditioning < athleticConditioningTarget)
                    notes.Add(athleticConditioningTarget == 2 ? "Add a second conditioning day." : "Add one conditioning day.");
                if (recovery == 0) notes.Add("Add a recovery day.");
                successNote = "Athletic Recomp week balances dedicated strength, conditioning, aerobic recovery, and at least one recovery day.";
                break;
        }

        if (good)
            notes.Clear();

        return new WeeklyStrategyAssessment(
            start,
            push,
            pull,
            legs,
            light,
            conditioning,
            recovery,
            generic,
            good ? "GOOD" : "REVIEW",
            good ? successNote : string.Join(" ", notes));
    }

    private static string NormalizePlan(string? planCode)
        => (planCode ?? "").Trim().ToUpperInvariant() switch
        {
            "HEALTH" or "HEALTH_RESET" or "PLAN_1" => "PLAN_1",
            "FAT_LOSS" or "FAT_LOSS_RECOMP" or "PLAN_2" => "PLAN_2",
            "MUSCLE" or "MUSCLE_BUILD" or "PLAN_3" => "PLAN_3",
            "PAIN_LIMITED" or "MOBILITY" or "PLAN_5" => "PLAN_5",
            _ => "PLAN_4"
        };

    public async Task<RegionRecovery> CalculateRegionRecoveryAsync(
        Guid programId,
        DateOnly actionDate,
        IReadOnlyDictionary<string, decimal> settings,
        CancellationToken cancellationToken = default)
    {
        var lastDate = actionDate.AddDays(-1);
        var start14 = actionDate.AddDays(-14);

        var sessions = await db.WorkoutSessions
            .Where(x => x.ProgramId == programId
                        && x.CompletedFlag
                        && x.WorkoutDate >= start14
                        && x.WorkoutDate <= lastDate)
            .ToListAsync(cancellationToken);

        decimal Sum(Func<WorkoutSession, decimal?> selector, int days)
        {
            var start = actionDate.AddDays(-days);
            return sessions
                .Where(x => x.WorkoutDate >= start && x.WorkoutDate <= lastDate)
                .Sum(x => selector(x) ?? 0m);
        }

        var pushRecent = Sum(x => x.CalculatedPushLoad, 2);
        var pullRecent = Sum(x => x.CalculatedPullLoad, 2);
        var legsRecent = Sum(x => x.CalculatedLegLoad, 3);
        var cardioRecent = Sum(x => x.CalculatedCardioLoad, 2);

        var push7 = Sum(x => x.CalculatedPushLoad, 7);
        var pull7 = Sum(x => x.CalculatedPullLoad, 7);
        var legs7 = Sum(x => x.CalculatedLegLoad, 7);
        var cardio7 = Sum(x => x.CalculatedCardioLoad, 7);

        var push14 = Sum(x => x.CalculatedPushLoad, 14);
        var pull14 = Sum(x => x.CalculatedPullLoad, 14);
        var legs14 = Sum(x => x.CalculatedLegLoad, 14);
        var cardio14 = Sum(x => x.CalculatedCardioLoad, 14);

        var pushRecovered = pushRecent < ConfigurationService.Setting(settings, "PUSH_RECOVERY_LOAD_MAX", 1.5m);
        var pullRecovered = pullRecent < ConfigurationService.Setting(settings, "PULL_RECOVERY_LOAD_MAX", 1.5m);
        var legsRecovered = legsRecent < ConfigurationService.Setting(settings, "LEGS_RECOVERY_LOAD_MAX", 1.75m);
        var cardioRecovered = cardioRecent < ConfigurationService.Setting(settings, "CARDIO_RECOVERY_LOAD_MAX", 4m);

        var candidates = new List<(string Region, decimal Score)>();
        if (pushRecovered) candidates.Add(("PUSH", push7 + push14 * 0.35m));
        if (pullRecovered) candidates.Add(("PULL", pull7 + pull14 * 0.35m));
        if (legsRecovered) candidates.Add(("LEGS", legs7 + legs14 * 0.35m));

        var priority = candidates.Count == 0
            ? "NONE"
            : candidates.OrderBy(x => x.Score).First().Region;

        var cardioNeed = cardio7 < 4.5m
            ? "HIGH"
            : cardio7 < 6.5m
                ? "MODERATE"
                : "LOW";

        return new RegionRecovery(
            pushRecent, pullRecent, legsRecent, cardioRecent,
            pushRecovered, pullRecovered, legsRecovered, cardioRecovered,
            push7, pull7, legs7, cardio7,
            push14, pull14, legs14, cardio14,
            priority, cardioNeed);
    }
}
