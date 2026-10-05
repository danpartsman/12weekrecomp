namespace WebRecomp.Web.Services;

/// <summary>
/// Compatibility shim retained through v1014C so old references are easy to
/// identify before v1014D. Current planning code uses
/// WeeklyPlanningAssessmentService + PlanningWeek directly.
/// </summary>
[Obsolete(
    "Use WeeklyPlanningAssessmentService and PlanningWeek. "
    + "Legacy Engine 2 regional recovery is isolated in LegacyEngine2RegionRecoveryService.")]
public sealed class WeeklyStrategyService(
    WeeklyPlanningAssessmentService planning)
{
    public static DateOnly WeekStart(
        DateOnly date)
        => PlanningWeek.Start(date);

    public Task<WeeklyStrategyAssessment> AssessWeekAsync(
        Guid programId,
        Guid configVersionId,
        DateOnly actionDate,
        string planCode,
        byte availableTrainingDaysWeek,
        CancellationToken cancellationToken = default)
        => planning.AssessWeekAsync(
            programId,
            configVersionId,
            actionDate,
            planCode,
            availableTrainingDaysWeek,
            cancellationToken);
}
