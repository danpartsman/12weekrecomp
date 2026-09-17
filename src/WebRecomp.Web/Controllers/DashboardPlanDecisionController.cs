using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutIntelligence;

namespace WebRecomp.Web.Controllers;

/// <summary>
/// B2.32.1
///
/// Keep Plan:
///   Records the user's decision when a Recommendation exists.
///   PlannedWorkouts is not modified.
///
/// Accept Change:
///   Reloads the latest recommendation for the authenticated
///   client/program/date and updates only that date's PlannedWorkout.
///
/// Nutrition is NOT independently written here. The existing Engine 3.0
/// plan/prescription path remains authoritative and recalculates from the
/// accepted Weekly Planned workout.
/// </summary>
public sealed class DashboardPlanDecisionController(
    RecompDbContext db,
    AppUserContextService userContext) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> KeepPlan(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null
            || context.Program is null)
        {
            return RedirectToAction(
                "Index",
                "Intake");
        }

        var recommendation =
            await LatestRecommendationAsync(
                context.Client.ClientId,
                context.Program.ProgramId,
                date,
                cancellationToken);

        if (recommendation is not null)
        {
            var planned =
                await db.PlannedWorkouts
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x =>
                            x.ProgramId
                                == context.Program.ProgramId
                            && x.PlannedDate
                                == date,
                        cancellationToken);

            recommendation.UserSelectedWorkoutTypeCode =
                planned?.WorkoutTypeCode
                ?? recommendation.PlannedWorkoutTypeCode;

            await db.SaveChangesAsync(
                cancellationToken);
        }

        TempData["DashboardPlanDecisionStatus"] =
            "Keeping the original Weekly Plan. "
            + "Workout and plan nutrition remain unchanged.";

        return RedirectToAction(
            "Index",
            "Dashboard",
            new
            {
                date =
                    date.ToString("yyyy-MM-dd")
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptChange(
        DateOnly date,
        string? recommendedWorkoutName,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null
            || context.Program is null)
        {
            return RedirectToAction(
                "Index",
                "Intake");
        }

        // Server authority:
        // never trust a posted workout type or posted macro values.
        var recommendation =
            await LatestRecommendationAsync(
                context.Client.ClientId,
                context.Program.ProgramId,
                date,
                cancellationToken);

        if (recommendation is null
            || !recommendation.ScheduleChangeRecommended
            || string.IsNullOrWhiteSpace(
                recommendation.RecommendedWorkoutTypeCode))
        {
            TempData["DashboardPlanDecisionStatus"] =
                "That recommendation is no longer current. "
                + "The Weekly Plan was not changed.";

            return RedirectToAction(
                "Index",
                "Dashboard",
                new
                {
                    date =
                        date.ToString("yyyy-MM-dd")
                });
        }

        var rawRecommendedCode =
            recommendation
                .RecommendedWorkoutTypeCode
                .Trim();

        var normalizedRecommendedCode =
            Engine30WorkoutUiCatalogService
                .NormalizeCode(
                    rawRecommendedCode);

        string engineWorkoutTypeCode =
            normalizedRecommendedCode;

        string? systemWorkoutCode =
            null;

        string acceptedDisplayName =
            string.IsNullOrWhiteSpace(
                recommendedWorkoutName)
                ? rawRecommendedCode
                : recommendedWorkoutName.Trim();

        // Preserve a specific displayed System Workout recipe
        // only when its Engine category agrees with the
        // server-side recommended Engine category.
        if (!string.IsNullOrWhiteSpace(
                recommendedWorkoutName))
        {
            var displayedRecipe =
                SystemWorkoutRecipeCatalog
                    .PresentationRecipes
                    .FirstOrDefault(
                        x =>
                            x.DisplayName.Equals(
                                recommendedWorkoutName.Trim(),
                                StringComparison.OrdinalIgnoreCase));

            if (displayedRecipe is not null)
            {
                var displayedEngineCode =
                    Engine30WorkoutUiCatalogService
                        .NormalizeCode(
                            displayedRecipe
                                .EngineWorkoutTypeCode);

                if (displayedEngineCode.Equals(
                        normalizedRecommendedCode,
                        StringComparison.OrdinalIgnoreCase))
                {
                    engineWorkoutTypeCode =
                        displayedRecipe
                            .EngineWorkoutTypeCode;

                    systemWorkoutCode =
                        displayedRecipe.SystemCode;

                    acceptedDisplayName =
                        displayedRecipe.DisplayName;
                }
            }
        }

        // Also support a recommendation code that is itself
        // a System Workout code.
        if (systemWorkoutCode is null
            && SystemWorkoutRecipeCatalog.TryGet(
                rawRecommendedCode,
                out var directRecipe)
            && directRecipe is not null)
        {
            var directEngineCode =
                Engine30WorkoutUiCatalogService
                    .NormalizeCode(
                        directRecipe
                            .EngineWorkoutTypeCode);

            if (directEngineCode.Equals(
                    normalizedRecommendedCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                engineWorkoutTypeCode =
                    directRecipe
                        .EngineWorkoutTypeCode;

                systemWorkoutCode =
                    directRecipe.SystemCode;

                acceptedDisplayName =
                    directRecipe.DisplayName;
            }
        }

        var planned =
            await db.PlannedWorkouts
                .SingleOrDefaultAsync(
                    x =>
                        x.ProgramId
                            == context.Program.ProgramId
                        && x.PlannedDate
                            == date,
                    cancellationToken);

        var now =
            DateTime.UtcNow;

        if (planned is null)
        {
            planned =
                new PlannedWorkout
                {
                    PlannedWorkoutId =
                        Guid.NewGuid(),

                    ClientId =
                        context.Client.ClientId,

                    ProgramId =
                        context.Program.ProgramId,

                    PlannedDate =
                        date,

                    WorkoutTypeCode =
                        engineWorkoutTypeCode,

                    SystemWorkoutCode =
                        systemWorkoutCode,

                    SourceCode =
                        "CLIENT",

                    CreatedAtUtc =
                        now,

                    UpdatedAtUtc =
                        now
                };

            db.PlannedWorkouts.Add(
                planned);
        }
        else
        {
            planned.WorkoutTypeCode =
                engineWorkoutTypeCode;

            // The accepted recommendation replaces the old
            // planned workout identity.
            planned.WorkoutTemplateId =
                null;

            planned.SystemWorkoutCode =
                systemWorkoutCode;

            // Do not carry duration/RPE overrides that belonged
            // to the workout being replaced. The accepted workout
            // receives its current plan defaults.
            planned.PlannedDurationMin =
                null;

            planned.PlannedRpe =
                null;

            planned.SourceCode =
                "CLIENT";

            planned.UpdatedAtUtc =
                now;
        }

        recommendation.UserSelectedWorkoutTypeCode =
            rawRecommendedCode;

        await db.SaveChangesAsync(
            cancellationToken);

        TempData["DashboardPlanDecisionStatus"] =
            $"Accepted {acceptedDisplayName}. "
            + $"The Weekly Plan for {date:dddd, MMM d} was updated. "
            + "Engine 3.0 will use the accepted workout to recalculate "
            + "the plan nutrition targets.";

        return RedirectToAction(
            "Index",
            "Dashboard",
            new
            {
                date =
                    date.ToString("yyyy-MM-dd")
            });
    }

    private async Task<Recommendation?>
        LatestRecommendationAsync(
            Guid clientId,
            Guid programId,
            DateOnly date,
            CancellationToken cancellationToken)
        =>
            await db.Recommendations
                .Where(
                    x =>
                        x.ClientId
                            == clientId
                        && x.ProgramId
                            == programId
                        && x.ActionDate
                            == date)
                .OrderByDescending(
                    x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(
                    cancellationToken);
}