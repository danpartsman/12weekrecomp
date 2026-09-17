using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.ViewComponents;

/// <summary>
/// B2.33 DASHBOARD RECOMMENDATION UX
///
/// The Dashboard already has the recommended workout in the Workout card.
/// This component supplies only:
///   - Keep Original / Accept Change controls in the Workout card
///   - the matching proposed nutrition inserted into the Nutrition card
///
/// A prior user decision on this date suppresses both proposal surfaces.
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

        // Once the user has explicitly chosen Keep Original or Accept Change
        // for this date, the proposal should stay dismissed on subsequent
        // Dashboard loads. This intentionally checks decision history for
        // the date, rather than only the newest regenerated recommendation.
        var decisionAlreadyMade =
            await db.Recommendations
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ClientId
                            == context.Client.ClientId
                        && x.ProgramId
                            == context.Program.ProgramId
                        && x.ActionDate
                            == date
                        && x.UserSelectedWorkoutTypeCode
                            != null,
                    cancellationToken);

        if (decisionAlreadyMade)
        {
            return View(
                new DashboardPlanDecisionDisplayModel
                {
                    Show = false,
                    DecisionAlreadyMade = true
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
                            == date
                        && x.ScheduleChangeRecommended)
                .OrderByDescending(
                    x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(
                    cancellationToken);

        if (recommendation is null)
        {
            return View(
                new DashboardPlanDecisionDisplayModel
                {
                    Show = false
                });
        }

        return View(
            new DashboardPlanDecisionDisplayModel
            {
                Show = true,
                Date = date,

                ProposedWorkout =
                    string.IsNullOrWhiteSpace(
                        recommendedWorkout)
                        ? recommendation
                            .RecommendedWorkoutTypeCode
                        : recommendedWorkout.Trim(),

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
                    recommendation.TimingNote
            });
    }
}

public sealed class DashboardPlanDecisionDisplayModel
{
    public bool Show { get; init; }

    public bool DecisionAlreadyMade { get; init; }

    public DateOnly Date { get; init; }

    public string ProposedWorkout { get; init; } =
        "";

    public decimal ProposedCalories { get; init; }

    public decimal ProposedProteinG { get; init; }

    public decimal ProposedCarbsG { get; init; }

    public decimal ProposedFatG { get; init; }

    public string? TimingNote { get; init; }
}