using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;

namespace WebRecomp.Web.Services;

public sealed record WeekCopyResult(bool Success, bool TargetHadPlan, int CopiedCount, string Message);

/// <summary>
/// Creates plan-aware starting weeks and safely carries a completed weekly plan
/// into a blank future week. User-created schedules are never silently overwritten.
/// </summary>
public sealed class WeeklyPlanService(
    RecompDbContext db,
    Engine30WorkoutUiCatalogService engine30WorkoutCatalog)
{
    private const string Recovery = "REST";

    public static string FriendlyPlanLabel(string? planCode)
        => NormalizePlan(planCode) switch
        {
            "PLAN_2" => "RECOMP — Fat Loss Focus, Muscle Secondary",
            "PLAN_3" => "RECOMP — Muscle Focus, Fat Loss Secondary",
            "PLAN_4" => "RECOMP — Athletic Reconditioning",
            "PLAN_1" => "SPECIALIZED — Health Reset",
            "PLAN_5" => "SPECIALIZED — Pain-Limited / Mobility",
            _ => "Recomp"
        };

    public static string FriendlyPlanGuidance(string? planCode)
        => NormalizePlan(planCode) switch
        {
            "PLAN_2" => "Fat-loss Recomp starts with enough resistance training to protect muscle, adds conditioning for output, and alternates demanding sessions with easier days or recovery so the calorie deficit stays productive.",
            "PLAN_3" => "Muscle-focus Recomp protects three major strength exposures, adds accessory work when your schedule allows, and separates hard days with accessory work, easier aerobic work or recovery to support growth.",
            "PLAN_1" => "Health Reset emphasizes repeatable strength, easy aerobic work, and recovery. The goal is consistency first, not maximum training stress.",
            "PLAN_4" => "Athletic Reconditioning keeps three major strength anchors, adds a fourth demanding athletic/conditioning exposure when training availability supports it, and uses easier aerobic or accessory work plus at least one true recovery day to manage the extra training stress.",
            "PLAN_5" => "Pain-Limited / Mobility begins conservatively with mobility, lighter resistance work, easy aerobic work, and more recovery. Adjust around your real limitations.",
            _ => "Recomp builds a starting week from your plan and training availability. Change any day to fit your real life."
        };

    public IReadOnlyList<string> BuildRecommendedTemplate(
        string? planCode,
        byte availableTrainingDaysWeek,
        string? trainingExperienceCode)
    {
        var plan = NormalizePlan(planCode);
        var available = Math.Clamp((int)availableTrainingDaysWeek, 1, 6);

        var template = plan switch
        {
            "PLAN_1" => HealthReset(Math.Min(available, 5)),
            "PLAN_2" => FatLossRecomp(available),
            "PLAN_3" => MuscleRecomp(available),
            "PLAN_5" => PainLimited(Math.Min(available, 5)),
            _ => AthleticRecomp(available)
        };

        // Beginner defaults still preserve the selected plan direction, but avoid
        // seeding two maximal interval sessions before the user has established a baseline.
        if (string.Equals(trainingExperienceCode, "BEGINNER", StringComparison.OrdinalIgnoreCase))
        {
            template = template
                .Select(x => x is "TRUE_HIIT" or "ORANGETHEORY" ? "GENERAL_CARDIO" : x)
                .ToArray();
        }

        return template;
    }

    /// <summary>
    /// Keep the workout mix and explicit weekday choices, while placing demanding
    /// sessions apart whenever the selected days make that possible.
    /// Monday through Sunday repeats, so Sunday and Monday count as adjacent.
    /// </summary>
    public static IReadOnlyList<string> ApplyDayPreferences(
        IReadOnlyList<string> template,
        IReadOnlyCollection<int>? preferredTrainingDays,
        IReadOnlyCollection<int>? preferredRestDays)
    {
        var training = preferredTrainingDays?.ToHashSet() ?? [];
        var rest = preferredRestDays?.ToHashSet() ?? [];
        var workouts = template.Where(code => code != Recovery).ToArray();
        if (template.Count != 7 || training.Any(day => day is < 0 or > 6)
            || rest.Any(day => day is < 0 or > 6) || training.Overlaps(rest)
            || training.Count > workouts.Length || rest.Count > 7 - workouts.Length)
            throw new ArgumentException("Day preferences cannot fit the recommended workout mix.");

        // Existing templates already provide the intended order when there are
        // no fixed days; optimize only when preferences move sessions.
        if (training.Count == 0 && rest.Count == 0)
            return template;

        string[]? best = null;
        var bestPenalty = int.MaxValue;
        var current = Enumerable.Repeat(Recovery, 7).ToArray();
        var used = new bool[workouts.Length];

        void Place(int day, int remaining, int displacement)
        {
            if (day == 7)
            {
                if (remaining != 0) return;
                var penalty = displacement;
                for (var i = 0; i < 7; i++)
                {
                    var next = current[(i + 1) % 7];
                    if (IsHard(current[i]) && IsHard(next))
                        penalty += 1000;
                    if (current[i] == "MAJOR_LEGS" && IsHard(next))
                        penalty += 250;
                }
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    best = (string[])current.Clone();
                }
                return;
            }

            var mustTrain = training.Contains(day);
            if (!mustTrain)
            {
                current[day] = Recovery;
                Place(day + 1, remaining, displacement + (template[day] == Recovery ? 0 : 2));
            }
            if (rest.Contains(day) || remaining == 0) return;

            for (var i = 0; i < workouts.Length; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                current[day] = workouts[i];
                Place(day + 1, remaining - 1,
                    displacement + (template[day] == workouts[i] ? 0 : 2) + Math.Abs(day - i));
                used[i] = false;
            }
        }

        Place(0, workouts.Length, 0);
        return best ?? throw new ArgumentException("Day preferences cannot fit the recommended workout mix.");
    }

    private static bool IsHard(string code) => code is
        "MAJOR_LEGS" or "MAJOR_PUSH" or "MAJOR_PULL" or
        "FULL_BODY_STRENGTH" or "TRUE_HIIT" or "ORANGETHEORY";
    public async Task<int> SeedRecommendedWeekIfBlankAsync(
        Client client,
        ProgramRecord program,
        DateOnly weekStart,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? preferredTrainingDays = null,
        IReadOnlyCollection<int>? preferredRestDays = null)
    {
        weekStart = WeeklyStrategyService.WeekStart(weekStart);
        var weekEnd = weekStart.AddDays(6);

        var exists = await db.PlannedWorkouts.AnyAsync(
            x => x.ProgramId == program.ProgramId
                 && x.PlannedDate >= weekStart
                 && x.PlannedDate <= weekEnd,
            cancellationToken);

        if (exists)
            return 0;

        var validCodes = await GetValidCodesAsync(program.ConfigVersionId, cancellationToken);
        var template = ApplyDayPreferences(
            BuildRecommendedTemplate(program.SelectedPlanCode,
                program.AvailableTrainingDaysWeek, client.TrainingExperienceCode),
            preferredTrainingDays, preferredRestDays);

        var now = DateTime.UtcNow;
        for (var i = 0; i < 7; i++)
        {
            db.PlannedWorkouts.Add(NewPlannedWorkout(
                client,
                program,
                weekStart.AddDays(i),
                ResolveCode(template[i], validCodes),
                "PLAN_DEFAULT",
                now));
        }

        await db.SaveChangesAsync(cancellationToken);
        return 7;
    }

    public async Task<bool> EnsureCurrentWeekAsync(
        Client client,
        ProgramRecord program,
        DateOnly clientToday,
        CancellationToken cancellationToken = default)
    {
        var currentWeek = WeeklyStrategyService.WeekStart(clientToday);
        var end = currentWeek.AddDays(6);

        var alreadyPlanned = await db.PlannedWorkouts.AnyAsync(
            x => x.ProgramId == program.ProgramId
                 && x.PlannedDate >= currentWeek
                 && x.PlannedDate <= end,
            cancellationToken);

        if (alreadyPlanned)
            return false;

        var firstProgramWeek = WeeklyStrategyService.WeekStart(program.StartDate);
        if (currentWeek <= firstProgramWeek)
        {
            return await SeedRecommendedWeekIfBlankAsync(client, program, currentWeek, cancellationToken) > 0;
        }

        var copy = await CopyWeekIfTargetBlankAsync(
            client,
            program,
            currentWeek.AddDays(-7),
            currentWeek,
            "AUTO_ROLLOVER",
            cancellationToken);

        if (copy.Success && copy.CopiedCount > 0)
            return true;

        return await SeedRecommendedWeekIfBlankAsync(client, program, currentWeek, cancellationToken) > 0;
    }

    public async Task<WeekCopyResult> CopyWeekIfTargetBlankAsync(
        Client client,
        ProgramRecord program,
        DateOnly sourceWeekStart,
        DateOnly targetWeekStart,
        string sourceCode,
        CancellationToken cancellationToken = default)
    {
        sourceWeekStart = WeeklyStrategyService.WeekStart(sourceWeekStart);
        targetWeekStart = WeeklyStrategyService.WeekStart(targetWeekStart);
        var targetEnd = targetWeekStart.AddDays(6);

        var targetHasPlan = await db.PlannedWorkouts.AnyAsync(
            x => x.ProgramId == program.ProgramId
                 && x.PlannedDate >= targetWeekStart
                 && x.PlannedDate <= targetEnd,
            cancellationToken);

        if (targetHasPlan)
        {
            return new WeekCopyResult(
                false,
                true,
                0,
                "Next week already has planned workouts, so Recomp left it unchanged.");
        }

        var sourceEnd = sourceWeekStart.AddDays(6);
        var source = await db.PlannedWorkouts
            .AsNoTracking()
            .Where(x => x.ProgramId == program.ProgramId
                        && x.PlannedDate >= sourceWeekStart
                        && x.PlannedDate <= sourceEnd)
            .OrderBy(x => x.PlannedDate)
            .ToListAsync(cancellationToken);

        if (source.Count == 0)
        {
            return new WeekCopyResult(
                false,
                false,
                0,
                "This week has no saved plan to copy.");
        }

        var validCodes = await GetValidCodesAsync(program.ConfigVersionId, cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var item in source)
        {
            var offset = item.PlannedDate.DayNumber - sourceWeekStart.DayNumber;
            db.PlannedWorkouts.Add(NewPlannedWorkout(
                client,
                program,
                targetWeekStart.AddDays(offset),
                ResolveCode(item.WorkoutTypeCode, validCodes),
                sourceCode,
                now,
                item.WorkoutTemplateId,
                item.SystemWorkoutCode,
                item.PlannedDurationMin,
                item.PlannedRpe));
        }

        await db.SaveChangesAsync(cancellationToken);

        return new WeekCopyResult(
            true,
            false,
            source.Count,
            sourceCode == "AUTO_ROLLOVER"
                ? "Recomp carried last week's plan forward because the new week was blank."
                : "This week's plan was copied to next week. Review it and adjust anything that changes.");
    }

    public async Task<int> ApplyRecommendedWeekFromDateAsync(
        Client client,
        ProgramRecord program,
        DateOnly weekStart,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default)
    {
        weekStart = WeeklyStrategyService.WeekStart(weekStart);
        var weekEnd = weekStart.AddDays(6);
        if (effectiveDate < weekStart) effectiveDate = weekStart;
        if (effectiveDate > weekEnd) return 0;

        var existing = await db.PlannedWorkouts
            .Where(x => x.ProgramId == program.ProgramId
                        && x.PlannedDate >= effectiveDate
                        && x.PlannedDate <= weekEnd)
            .ToListAsync(cancellationToken);

        db.PlannedWorkouts.RemoveRange(existing);

        var validCodes = await GetValidCodesAsync(program.ConfigVersionId, cancellationToken);
        var template = BuildRecommendedTemplate(
            program.SelectedPlanCode,
            program.AvailableTrainingDaysWeek,
            client.TrainingExperienceCode);
        var now = DateTime.UtcNow;
        var added = 0;

        for (var i = 0; i < 7; i++)
        {
            var date = weekStart.AddDays(i);
            if (date < effectiveDate)
                continue;

            db.PlannedWorkouts.Add(NewPlannedWorkout(
                client,
                program,
                date,
                ResolveCode(template[i], validCodes),
                "PLAN_RECOMMENDED",
                now));
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    private async Task<HashSet<string>> GetValidCodesAsync(
        Guid configVersionId,
        CancellationToken cancellationToken)
    {
        _ = configVersionId; // Engine 3.0 UI catalog is intentionally independent of frozen Engine 2.0 config.
        return await engine30WorkoutCatalog.GetActiveCodeSetAsync(cancellationToken);
    }

    private static PlannedWorkout NewPlannedWorkout(
        Client client,
        ProgramRecord program,
        DateOnly date,
        string workoutTypeCode,
        string sourceCode,
        DateTime now,
        Guid? workoutTemplateId = null,
        string? systemWorkoutCode = null,
        decimal? plannedDurationMin = null,
        decimal? plannedRpe = null)
        => new()
        {
            PlannedWorkoutId = Guid.NewGuid(),
            ClientId = client.ClientId,
            ProgramId = program.ProgramId,
            PlannedDate = date,
            WorkoutTypeCode = workoutTypeCode,
            WorkoutTemplateId = workoutTemplateId,
            SystemWorkoutCode = systemWorkoutCode,
            PlannedDurationMin = plannedDurationMin,
            PlannedRpe = plannedRpe,
            SourceCode = sourceCode,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

    private static string ResolveCode(string desiredCode, HashSet<string> valid)
    {
        var normalized =
            Engine30WorkoutUiCatalogService.NormalizeCode(desiredCode);

        if (valid.Contains(normalized))
            return normalized;

        string[] fallbacks = normalized switch
        {
            "MAJOR_PUSH"
                or "MAJOR_PULL"
                or "MAJOR_LEGS"
                or "FULL_BODY_STRENGTH" =>
                ["FULL_BODY_STRENGTH", "ACCESSORY", Recovery],

            "ACCESSORY"
                or "RESISTANCE_CIRCUIT" =>
                ["ACCESSORY", "FULL_BODY_STRENGTH", Recovery],

            "GENERAL_CARDIO"
                or "RUNNING"
                or "TREADMILL"
                or "CYCLING"
                or "ROWING"
                or "ELLIPTICAL"
                or "STAIR_CLIMBER"
                or "SWIMMING"
                or "RUNNING_INTERVALS"
                or "CYCLING_INTERVALS"
                or "ROWING_INTERVALS"
                or "MIXED_CONDITIONING"
                or "TRUE_HIIT"
                or "ORANGETHEORY"
                or "MIXED_GYM_SESSION"
                or "CUSTOM_COMPOSITE" =>
                ["GENERAL_CARDIO", "ZONE2_CARDIO", Recovery],

            "EASY_CARDIO"
                or "ZONE2_CARDIO"
                or "WALKING" =>
                ["ZONE2_CARDIO", "EASY_CARDIO", "WALKING", Recovery],

            "MOBILITY_RECOVERY" =>
                ["MOBILITY_RECOVERY", Recovery],

            _ => [Recovery]
        };

        return fallbacks.FirstOrDefault(valid.Contains)
               ?? valid.FirstOrDefault()
               ?? throw new InvalidOperationException(
                   "No active Engine 3.0 workout types are configured.");
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

    private static string[] HealthReset(int active) => active switch
    {
        1 => ["ACCESSORY", Recovery, Recovery, Recovery, Recovery, Recovery, Recovery],
        2 => ["ACCESSORY", Recovery, "ZONE2_CARDIO", Recovery, Recovery, Recovery, Recovery],
        3 => ["ACCESSORY", Recovery, "ZONE2_CARDIO", Recovery, "ACCESSORY", Recovery, Recovery],
        4 => ["ACCESSORY", "ZONE2_CARDIO", Recovery, "ACCESSORY", Recovery, "ZONE2_CARDIO", Recovery],
        _ => ["ACCESSORY", "ZONE2_CARDIO", Recovery, "ACCESSORY", "ZONE2_CARDIO", "ACCESSORY", Recovery]
    };

    private static string[] FatLossRecomp(int active) => active switch
    {
        1 => ["FULL_BODY_STRENGTH", Recovery, Recovery, Recovery, Recovery, Recovery, Recovery],
        2 => ["FULL_BODY_STRENGTH", Recovery, "GENERAL_CARDIO", Recovery, Recovery, Recovery, Recovery],
        3 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", Recovery, "MAJOR_PULL", Recovery, Recovery],
        4 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", "ZONE2_CARDIO", "MAJOR_PULL", Recovery, Recovery],
        5 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", "ZONE2_CARDIO", "MAJOR_PULL", "ACCESSORY", Recovery],
        _ => ["MAJOR_LEGS", "ZONE2_CARDIO", "MAJOR_PUSH", "ACCESSORY", "MAJOR_PULL", "ZONE2_CARDIO", Recovery]
    };

    private static string[] MuscleRecomp(int active) => active switch
    {
        1 => ["FULL_BODY_STRENGTH", Recovery, Recovery, Recovery, Recovery, Recovery, Recovery],
        2 => ["FULL_BODY_STRENGTH", Recovery, "ACCESSORY", Recovery, Recovery, Recovery, Recovery],
        3 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", Recovery, "MAJOR_PULL", Recovery, Recovery],
        4 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", Recovery, "MAJOR_PULL", "ACCESSORY", Recovery],
        5 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", "ZONE2_CARDIO", "MAJOR_PULL", "ACCESSORY", Recovery],
        _ => ["MAJOR_LEGS", "ACCESSORY", "MAJOR_PUSH", "ZONE2_CARDIO", "MAJOR_PULL", "ACCESSORY", Recovery]
    };

    private static string[] AthleticRecomp(int active) => active switch
    {
        1 => ["FULL_BODY_STRENGTH", Recovery, Recovery, Recovery, Recovery, Recovery, Recovery],
        2 => ["FULL_BODY_STRENGTH", Recovery, "GENERAL_CARDIO", Recovery, Recovery, Recovery, Recovery],
        3 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", Recovery, "MAJOR_PULL", Recovery, Recovery],
        4 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", "ZONE2_CARDIO", "MAJOR_PULL", Recovery, Recovery],

        // With five or six available training days, Athletic Reconditioning
        // intentionally adds a fourth demanding exposure: true conditioning.
        // Three major strength anchors remain the base; Zone 2 / accessory work
        // and a true rest day keep the week from becoming four heavy lifting days.
        5 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", "ZONE2_CARDIO", "MAJOR_PULL", "TRUE_HIIT", Recovery],
        _ => ["MAJOR_LEGS", "ZONE2_CARDIO", "MAJOR_PUSH", "ACCESSORY", "MAJOR_PULL", "TRUE_HIIT", Recovery]
    };

    private static string[] PainLimited(int active) => active switch
    {
        1 => ["MOBILITY_RECOVERY", Recovery, Recovery, Recovery, Recovery, Recovery, Recovery],
        2 => ["MOBILITY_RECOVERY", Recovery, "ACCESSORY", Recovery, Recovery, Recovery, Recovery],
        3 => ["MOBILITY_RECOVERY", Recovery, "ACCESSORY", Recovery, "ZONE2_CARDIO", Recovery, Recovery],
        4 => ["MOBILITY_RECOVERY", "ZONE2_CARDIO", Recovery, "ACCESSORY", Recovery, "ACCESSORY", Recovery],
        _ => ["MOBILITY_RECOVERY", "ZONE2_CARDIO", "ACCESSORY", Recovery, "ACCESSORY", "ZONE2_CARDIO", Recovery]
    };
}

