namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Same-day muscle filter. This does not rewrite the weekly plan and does not
/// remove the weekly Push / Pull / Legs intention. It answers a narrower
/// question: given the muscles a planned workout would train, which of those
/// muscles are locally painful or sore enough to leave out.
///
/// A pain or soreness flag with no region is not a location. Callers must
/// keep the existing whole-day safety rule for that case.
/// </summary>
public static class Engine30MuscleRegionCodes
{
    public const string Push = "PUSH";
    public const string Pull = "PULL";
    public const string Legs = "LEGS";
    public const string Core = "CORE";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Push,
            Pull,
            Legs,
            Core
        };
}

public static class Engine30LocalConstraintKindCodes
{
    public const string Pain = "PAIN";
    public const string Soreness = "SORENESS";
}

public static class Engine30LocalWorkoutDecisionCodes
{
    public const string KeepPlannedMuscles = "KEEP_PLANNED_MUSCLES";
    public const string PartialWithoutLimitedRegions = "PARTIAL_WITHOUT_LIMITED_REGIONS";
    public const string NoSafeRegion = "NO_SAFE_REGION";
}

public sealed record Engine30LocalMuscleConstraint(
    string RegionCode,
    string KindCode,
    byte? Score = null);

public sealed record Engine30LocalMuscleConstraintResult(
    string DecisionCode,
    IReadOnlyList<string> PlannedRegionCodes,
    IReadOnlyList<string> KeptRegionCodes,
    IReadOnlyList<string> ExcludedRegionCodes,
    IReadOnlyList<string> ReasonCodes);

public sealed class Engine30LocalMuscleConstraintService
{
    public const byte SorenessExcludeAtOrAbove = 6;

    public Engine30LocalMuscleConstraintResult Evaluate(
        IReadOnlyList<string> plannedRegionCodes,
        IReadOnlyList<Engine30LocalMuscleConstraint>? constraints)
    {
        ArgumentNullException.ThrowIfNull(plannedRegionCodes);

        var planned =
            NormalizeRegions(plannedRegionCodes);

        var excluded =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var reasons =
            new List<string>();

        foreach (var constraint in constraints ?? [])
        {
            if (!TryNormalizeRegion(constraint.RegionCode, out var region))
            {
                reasons.Add("IGNORED_UNKNOWN_REGION");
                continue;
            }

            if (IsPain(constraint))
            {
                if (excluded.Add(region))
                    reasons.Add($"LOCAL_PAIN_{region}");

                continue;
            }

            if (IsExcludingSoreness(constraint)
                && excluded.Add(region))
            {
                reasons.Add($"LOCAL_SORENESS_{region}");
            }
        }

        var kept =
            planned
                .Where(region => !excluded.Contains(region))
                .ToArray();

        var removedFromPlan =
            planned
                .Where(excluded.Contains)
                .ToArray();

        string decision;

        if (planned.Count == 0 || kept.Length == 0)
        {
            decision = Engine30LocalWorkoutDecisionCodes.NoSafeRegion;
            reasons.Add("NO_PLANNED_REGION_REMAINS");
        }
        else if (removedFromPlan.Length == 0)
        {
            decision = Engine30LocalWorkoutDecisionCodes.KeepPlannedMuscles;
        }
        else
        {
            decision = Engine30LocalWorkoutDecisionCodes.PartialWithoutLimitedRegions;
            reasons.Add("PARTIAL_WORKOUT_STILL_AVAILABLE");
        }

        return new Engine30LocalMuscleConstraintResult(
            DecisionCode: decision,
            PlannedRegionCodes: planned,
            KeptRegionCodes: kept,
            ExcludedRegionCodes: removedFromPlan,
            ReasonCodes: reasons
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static bool IsPain(Engine30LocalMuscleConstraint constraint)
    {
        return string.Equals(
            constraint.KindCode,
            Engine30LocalConstraintKindCodes.Pain,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludingSoreness(Engine30LocalMuscleConstraint constraint)
    {
        if (!string.Equals(
                constraint.KindCode,
                Engine30LocalConstraintKindCodes.Soreness,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return constraint.Score is null
            || constraint.Score >= SorenessExcludeAtOrAbove;
    }

    private static IReadOnlyList<string> NormalizeRegions(
        IReadOnlyList<string> regionCodes)
    {
        var seen =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var normalized =
            new List<string>();

        foreach (var regionCode in regionCodes)
        {
            if (TryNormalizeRegion(regionCode, out var region)
                && seen.Add(region))
            {
                normalized.Add(region);
            }
        }

        return normalized;
    }

    private static bool TryNormalizeRegion(
        string? regionCode,
        out string region)
    {
        region = "";

        if (string.IsNullOrWhiteSpace(regionCode))
            return false;

        var candidate = regionCode.Trim().ToUpperInvariant();

        if (!Engine30MuscleRegionCodes.All.Contains(candidate))
            return false;

        region = candidate;
        return true;
    }
}
