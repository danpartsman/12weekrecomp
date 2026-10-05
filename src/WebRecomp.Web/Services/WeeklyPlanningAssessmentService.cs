using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
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

/// <summary>
/// v1014C planning-only assessment.
///
/// This service evaluates the shape of the SAVED weekly plan. It does not read
/// completed-workout recovery loads and it does not make physiology decisions.
/// </summary>
public sealed class WeeklyPlanningAssessmentService(
    RecompDbContext db,
    Engine30WorkoutUiCatalogService engine30WorkoutCatalog)
{
    public async Task<WeeklyStrategyAssessment> AssessWeekAsync(
        Guid programId,
        Guid configVersionId,
        DateOnly actionDate,
        string planCode,
        byte availableTrainingDaysWeek,
        CancellationToken cancellationToken = default)
    {
        var start =
            PlanningWeek.Start(actionDate);

        var end =
            start.AddDays(6);

        var planned =
            await db.PlannedWorkouts
                .Where(x =>
                    x.ProgramId == programId
                    && x.PlannedDate >= start
                    && x.PlannedDate <= end)
                .ToListAsync(cancellationToken);

        if (planned.Count == 0)
        {
            return new WeeklyStrategyAssessment(
                start,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                "NOT_PLANNED",
                "Set up the week so Recomp can evaluate the training mix.");
        }

        _ = configVersionId;

        var definitions =
            await engine30WorkoutCatalog
                .GetActiveMapAsync(
                    cancellationToken);

        short push = 0;
        short pull = 0;
        short legs = 0;
        short light = 0;
        short conditioning = 0;
        short recovery = 0;
        short generic = 0;
        short zone2 = 0;

        foreach (var item in planned)
        {
            var normalizedCode =
                Engine30WorkoutUiCatalogService.NormalizeCode(
                    item.WorkoutTypeCode);

            if (!definitions.TryGetValue(
                    normalizedCode,
                    out var type))
            {
                continue;
            }

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
                (type.WorkoutCategoryCode ?? string.Empty)
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

        var available =
            Math.Clamp(
                (int)availableTrainingDaysWeek,
                1,
                6);

        var normalizedPlan =
            NormalizePlan(planCode);

        var heavy =
            push + pull + legs;

        var strength =
            heavy + light + generic;

        var notes =
            new List<string>();

        bool good;
        string successNote;

        switch (normalizedPlan)
        {
            case "PLAN_1":
            {
                var healthStrengthTarget =
                    available >= 4
                        ? 2
                        : 1;

                good =
                    strength >= healthStrengthTarget
                    && (available < 3 || zone2 >= 1)
                    && recovery >= 1;

                if (strength < healthStrengthTarget)
                    notes.Add("Add a repeatable strength session.");

                if (available >= 3 && zone2 == 0)
                    notes.Add("Add an easy Zone 2 / aerobic day.");

                if (recovery == 0)
                    notes.Add("Add a recovery day.");

                successNote =
                    "Health Reset week is balanced around repeatable strength, easy aerobic work, and recovery.";

                break;
            }

            case "PLAN_2":
            {
                var strengthGood =
                    available >= 3
                        ? push >= 1
                          && pull >= 1
                          && legs >= 1
                        : strength >= 1;

                var conditioningGood =
                    available < 4
                    || conditioning + zone2 >= 1;

                good =
                    strengthGood
                    && conditioningGood
                    && recovery >= 1;

                if (!strengthGood)
                {
                    notes.Add(
                        available >= 3
                            ? "Include Push, Pull, and Legs strength across the week."
                            : "Add a full-body strength session.");
                }

                if (!conditioningGood)
                    notes.Add("Add one conditioning / cardio session.");

                if (recovery == 0)
                    notes.Add("Add a recovery day.");

                successNote =
                    "Fat-loss Recomp week protects muscle with strength work while adding conditioning and recovery to support a productive deficit.";

                break;
            }

            case "PLAN_3":
            {
                var heavyGood =
                    available >= 3
                        ? push >= 1
                          && pull >= 1
                          && legs >= 1
                        : strength >= 1;

                var accessoryGood =
                    available < 4
                    || light >= 1;

                good =
                    heavyGood
                    && accessoryGood
                    && recovery >= 1;

                if (!heavyGood)
                {
                    notes.Add(
                        available >= 3
                            ? "Include Major Push, Major Pull, and Major Legs."
                            : "Add a full-body strength session.");
                }

                if (!accessoryGood)
                    notes.Add("Add one accessory / lighter strength day.");

                if (recovery == 0)
                    notes.Add("Add a recovery day.");

                if (conditioning > 1)
                {
                    notes.Add(
                        "Muscle-focus Recomp usually needs less hard conditioning so recovery stays available for lifting.");
                }

                successNote =
                    "Muscle-focus Recomp week protects the major lifting sessions, adds accessory work when time allows, and leaves room to recover.";

                break;
            }

            case "PLAN_5":
            {
                var conservativeStrengthTarget =
                    available >= 4
                        ? 2
                        : available >= 2
                            ? 1
                            : 0;

                good =
                    light >= conservativeStrengthTarget
                    && (available < 3 || zone2 >= 1)
                    && recovery >= 2
                    && heavy == 0;

                if (light < conservativeStrengthTarget)
                    notes.Add("Add a lighter or corrective strength session.");

                if (available >= 3 && zone2 == 0)
                    notes.Add("Add an easy Zone 2 / aerobic day if tolerated.");

                if (recovery < 2)
                    notes.Add("Keep more recovery / mobility space in this plan.");

                if (heavy > 0)
                {
                    notes.Add(
                        "Pain-Limited / Mobility is intentionally conservative; review major heavy sessions carefully.");
                }

                successNote =
                    "Pain-Limited / Mobility week is intentionally conservative, with lighter strength, easy aerobic work, mobility, and recovery.";

                break;
            }

            default:
            {
                var heavyGood =
                    available >= 3
                        ? push >= 1
                          && pull >= 1
                          && legs >= 1
                        : strength >= 1;

                // v1013 Athletic Reconditioning uses one hard conditioning
                // exposure at 5+ available training days, not two.
                var conditioningTarget =
                    available >= 5
                        ? 1
                        : 0;

                good =
                    heavyGood
                    && conditioning >= conditioningTarget
                    && recovery >= 1;

                if (!heavyGood)
                {
                    notes.Add(
                        available >= 3
                            ? "Include Major Push, Major Pull, and Major Legs."
                            : "Add a full-body strength session.");
                }

                if (conditioning < conditioningTarget)
                    notes.Add("Add one hard conditioning day.");

                if (recovery == 0)
                    notes.Add("Add a recovery day.");

                successNote =
                    "Athletic Recomp week balances three major strength anchors, a hard conditioning exposure when availability supports it, easier work, and recovery.";

                break;
            }
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
            good
                ? "GOOD"
                : "REVIEW",
            good
                ? successNote
                : string.Join(
                    " ",
                    notes));
    }

    private static string NormalizePlan(
        string? planCode)
        => (planCode ?? string.Empty)
            .Trim()
            .ToUpperInvariant() switch
        {
            "HEALTH"
            or "HEALTH_RESET"
            or "PLAN_1" =>
                "PLAN_1",

            "FAT_LOSS"
            or "FAT_LOSS_RECOMP"
            or "PLAN_2" =>
                "PLAN_2",

            "MUSCLE"
            or "MUSCLE_BUILD"
            or "PLAN_3" =>
                "PLAN_3",

            "PAIN_LIMITED"
            or "MOBILITY"
            or "PLAN_5" =>
                "PLAN_5",

            _ =>
                "PLAN_4"
        };
}
