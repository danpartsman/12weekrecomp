using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record WeekCopyResult(bool Success, bool TargetHadPlan, int CopiedCount, string Message);

/// <summary>
/// Creates plan-aware starting weeks and safely carries a completed weekly plan
/// into a blank future week. User-created schedules are never silently overwritten.
/// </summary>
public sealed class WeeklyPlanService(RecompDbContext db)
{
    private const string Recovery = "RECOVERY_0_RPE2";

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
            "PLAN_2" => "Fat-loss Recomp starts with enough resistance training to protect muscle, adds conditioning for output, and preserves recovery so the calorie deficit stays productive.",
            "PLAN_3" => "Muscle-focus Recomp protects three major strength exposures, adds accessory work when your schedule allows, and uses easier aerobic work and recovery to support growth.",
            "PLAN_1" => "Health Reset emphasizes repeatable strength, easy aerobic work, and recovery. The goal is consistency first, not maximum training stress.",
            "PLAN_4" => "Athletic Reconditioning keeps resistance training at the center, then layers in more conditioning, aerobic recovery, and at least one true recovery day.",
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
                .Select(x => x is "HIIT_45_RPE9" or "OTF_STANDARD" ? "PURE_CARDIO" : x)
                .ToArray();
        }

        return template;
    }

    public async Task<int> SeedRecommendedWeekIfBlankAsync(
        Client client,
        ProgramRecord program,
        DateOnly weekStart,
        CancellationToken cancellationToken = default)
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
        var template = BuildRecommendedTemplate(
            program.SelectedPlanCode,
            program.AvailableTrainingDaysWeek,
            client.TrainingExperienceCode);

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
                now));
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
        => (await db.WorkoutTypeDefinitions
                .Where(x => x.ConfigVersionId == configVersionId && x.IsActive)
                .Select(x => x.WorkoutTypeCode)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static PlannedWorkout NewPlannedWorkout(
        Client client,
        ProgramRecord program,
        DateOnly date,
        string workoutTypeCode,
        string sourceCode,
        DateTime now)
        => new()
        {
            PlannedWorkoutId = Guid.NewGuid(),
            ClientId = client.ClientId,
            ProgramId = program.ProgramId,
            PlannedDate = date,
            WorkoutTypeCode = workoutTypeCode,
            SourceCode = sourceCode,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

    private static string ResolveCode(string desiredCode, HashSet<string> valid)
    {
        if (valid.Contains(desiredCode))
            return desiredCode;

        string[] fallbacks = desiredCode switch
        {
            "MAJOR_PUSH" or "MAJOR_PULL" or "MAJOR_LEGS" or "MAJOR_MIXED" =>
                ["MAJOR_MIXED", "MAJOR_60_RPE8", "ACCESSORY_MIXED", "ACCESSORY_45_RPE7", Recovery],
            "ACCESSORY_UPPER" or "ACCESSORY_LOWER" or "ACCESSORY_MIXED" =>
                ["ACCESSORY_MIXED", "ACCESSORY_45_RPE7", "OTF_STRENGTH_TOTAL", Recovery],
            "HIIT_45_RPE9" or "OTF_STANDARD" or "PURE_CARDIO" =>
                ["PURE_CARDIO", "OTF_STANDARD", "HIIT_45_RPE9", "ZONE_2", "Z2_40_RPE4", Recovery],
            "ZONE_2" or "Z2_40_RPE4" =>
                ["ZONE_2", "Z2_40_RPE4", Recovery],
            "MOBILITY__POSTURE" =>
                ["MOBILITY__POSTURE", Recovery],
            _ => [Recovery]
        };

        return fallbacks.FirstOrDefault(valid.Contains)
               ?? valid.FirstOrDefault()
               ?? throw new InvalidOperationException("No active workout types are configured for this program.");
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
        1 => ["ACCESSORY_MIXED", Recovery, Recovery, Recovery, Recovery, Recovery, Recovery],
        2 => ["ACCESSORY_MIXED", Recovery, "ZONE_2", Recovery, Recovery, Recovery, Recovery],
        3 => ["ACCESSORY_MIXED", Recovery, "ZONE_2", Recovery, "ACCESSORY_MIXED", Recovery, Recovery],
        4 => ["ACCESSORY_MIXED", "ZONE_2", Recovery, "ACCESSORY_MIXED", Recovery, "ZONE_2", Recovery],
        _ => ["ACCESSORY_MIXED", "ZONE_2", Recovery, "ACCESSORY_MIXED", "ZONE_2", "ACCESSORY_MIXED", Recovery]
    };

    private static string[] FatLossRecomp(int active) => active switch
    {
        1 => ["MAJOR_MIXED", Recovery, Recovery, Recovery, Recovery, Recovery, Recovery],
        2 => ["MAJOR_MIXED", Recovery, "PURE_CARDIO", Recovery, Recovery, Recovery, Recovery],
        3 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", Recovery, "MAJOR_PULL", Recovery, Recovery],
        4 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", "HIIT_45_RPE9", "MAJOR_PULL", Recovery, Recovery],
        5 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", "HIIT_45_RPE9", "MAJOR_PULL", "ZONE_2", Recovery],
        _ => ["MAJOR_LEGS", "HIIT_45_RPE9", "MAJOR_PUSH", "ZONE_2", "MAJOR_PULL", "OTF_STANDARD", Recovery]
    };

    private static string[] MuscleRecomp(int active) => active switch
    {
        1 => ["MAJOR_MIXED", Recovery, Recovery, Recovery, Recovery, Recovery, Recovery],
        2 => ["MAJOR_MIXED", Recovery, "ACCESSORY_MIXED", Recovery, Recovery, Recovery, Recovery],
        3 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", Recovery, "MAJOR_PULL", Recovery, Recovery],
        4 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", Recovery, "MAJOR_PULL", "ACCESSORY_MIXED", Recovery],
        5 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", "ZONE_2", "MAJOR_PULL", "ACCESSORY_MIXED", Recovery],
        _ => ["MAJOR_LEGS", "ACCESSORY_UPPER", "MAJOR_PUSH", "ZONE_2", "MAJOR_PULL", "ACCESSORY_MIXED", Recovery]
    };

    private static string[] AthleticRecomp(int active) => active switch
    {
        1 => ["MAJOR_MIXED", Recovery, Recovery, Recovery, Recovery, Recovery, Recovery],
        2 => ["MAJOR_MIXED", Recovery, "PURE_CARDIO", Recovery, Recovery, Recovery, Recovery],
        3 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", Recovery, "MAJOR_PULL", Recovery, Recovery],
        4 => ["MAJOR_LEGS", Recovery, "MAJOR_PUSH", "OTF_STANDARD", "MAJOR_PULL", Recovery, Recovery],
        5 => ["MAJOR_LEGS", "OTF_STANDARD", "MAJOR_PUSH", Recovery, "MAJOR_PULL", "ZONE_2", Recovery],
        _ => ["MAJOR_LEGS", "OTF_STANDARD", "MAJOR_PUSH", "ZONE_2", "MAJOR_PULL", "HIIT_45_RPE9", Recovery]
    };

    private static string[] PainLimited(int active) => active switch
    {
        1 => ["MOBILITY__POSTURE", Recovery, Recovery, Recovery, Recovery, Recovery, Recovery],
        2 => ["MOBILITY__POSTURE", Recovery, "ACCESSORY_MIXED", Recovery, Recovery, Recovery, Recovery],
        3 => ["MOBILITY__POSTURE", Recovery, "ACCESSORY_MIXED", Recovery, "ZONE_2", Recovery, Recovery],
        4 => ["MOBILITY__POSTURE", "ZONE_2", Recovery, "ACCESSORY_MIXED", Recovery, "ACCESSORY_MIXED", Recovery],
        _ => ["MOBILITY__POSTURE", "ZONE_2", "ACCESSORY_MIXED", Recovery, "ACCESSORY_MIXED", "ZONE_2", Recovery]
    };
}
