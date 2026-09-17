using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.ViewComponents;

/// <summary>
/// B2.32.1
///
/// Presentation boundary for the Dashboard plan decision.
/// The parent Dashboard supplies the current/original plan.
/// This component loads the persisted authoritative Recommendation
/// for the same authenticated client/program/date and exposes the
/// nutrition prescription that belongs to the proposed workout.
/// </summary>
public sealed class DashboardPlanDecisionViewComponent(
    RecompDbContext db,
    AppUserContextService userContext) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(
        DateOnly date,
        string originalWorkout,
        string recommendedWorkout,
        decimal originalCalories,
        decimal originalProteinG,
        decimal originalCarbsG,
        decimal originalFatG,
        bool showDecision)
    {
        var cancellationToken =
            HttpContext.RequestAborted;

        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (!showDecision
            || context.Client is null
            || context.Program is null)
        {
            return View(
                new DashboardPlanDecisionDisplayModel
                {
                    Show = false
                });
        }

        var recommendation =
            await db.Recommendations
                .AsNoTracking()
                .Where(
                    x =>
                        x.ClientId
                            == context.Client.ClientId
                        && x.ProgramId
                            == context.Program.ProgramId
                        && x.ActionDate
                            == date)
                .OrderByDescending(
                    x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(
                    cancellationToken);

        if (recommendation is null
            || !recommendation.ScheduleChangeRecommended)
        {
            return View(
                new DashboardPlanDecisionDisplayModel
                {
                    Show = false
                });
        }

        var nutritionUnchanged =
            RoundedEqual(
                originalCalories,
                recommendation.RecommendedCalories)
            && RoundedEqual(
                originalProteinG,
                recommendation.RecommendedProteinG)
            && RoundedEqual(
                originalCarbsG,
                recommendation.RecommendedCarbsG)
            && RoundedEqual(
                originalFatG,
                recommendation.RecommendedFatG);

        return View(
            new DashboardPlanDecisionDisplayModel
            {
                Show = true,
                Date = date,

                OriginalWorkout =
                    string.IsNullOrWhiteSpace(
                        originalWorkout)
                        ? "No workout planned"
                        : originalWorkout.Trim(),

                ProposedWorkout =
                    string.IsNullOrWhiteSpace(
                        recommendedWorkout)
                        ? recommendation
                            .RecommendedWorkoutTypeCode
                        : recommendedWorkout.Trim(),

                OriginalCalories =
                    originalCalories,

                OriginalProteinG =
                    originalProteinG,

                OriginalCarbsG =
                    originalCarbsG,

                OriginalFatG =
                    originalFatG,

                ProposedCalories =
                    recommendation
                        .RecommendedCalories,

                ProposedProteinG =
                    recommendation
                        .RecommendedProteinG,

                ProposedCarbsG =
                    recommendation
                        .RecommendedCarbsG,

                ProposedFatG =
                    recommendation
                        .RecommendedFatG,

                TimingNote =
                    recommendation.TimingNote,

                NutritionUnchanged =
                    nutritionUnchanged
            });
    }

    private static bool RoundedEqual(
        decimal left,
        decimal right)
        =>
            Math.Round(
                left,
                0,
                MidpointRounding.AwayFromZero)
            ==
            Math.Round(
                right,
                0,
                MidpointRounding.AwayFromZero);
}

public sealed class DashboardPlanDecisionDisplayModel
{
    public bool Show { get; init; }

    public DateOnly Date { get; init; }

    public string OriginalWorkout { get; init; } =
        "";

    public string ProposedWorkout { get; init; } =
        "";

    public decimal OriginalCalories { get; init; }

    public decimal OriginalProteinG { get; init; }

    public decimal OriginalCarbsG { get; init; }

    public decimal OriginalFatG { get; init; }

    public decimal ProposedCalories { get; init; }

    public decimal ProposedProteinG { get; init; }

    public decimal ProposedCarbsG { get; init; }

    public decimal ProposedFatG { get; init; }

    public string? TimingNote { get; init; }

    public bool NutritionUnchanged { get; init; }
}