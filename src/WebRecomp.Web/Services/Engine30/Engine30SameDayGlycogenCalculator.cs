namespace WebRecomp.Web.Services.Engine30;

public sealed record Engine30SameDayGlycogenSessionResult(
    int SessionIndex,
    decimal FreshPoolModeledDepletionG,
    decimal SequentialDepletionG,
    decimal SequentialCarbohydrateReloadG);

public sealed record Engine30SameDayGlycogenResult(
    decimal FreshPoolModeledDepletionG,
    decimal SequentialDepletionG,
    decimal SequentialCarbohydrateReloadG,
    IReadOnlyList<Engine30SameDayGlycogenSessionResult> Sessions,
    IReadOnlyList<string> ReasonCodes);

/// <summary>
/// Carries regional muscle-glycogen state across multiple workouts performed
/// on the same calendar day.
///
/// A second workout must not begin from a fictitious fully-restored glycogen
/// pool when the first workout used the same muscle region. The effective
/// regional depletion fraction calculated for each workout is applied to the
/// glycogen remaining after all earlier same-day sessions.
///
/// Different anatomical regions remain independent: a lower-body session does
/// not automatically deplete an upper-body pool.
///
/// This is the same state principle already used inside Engine 3 composite
/// sessions, extended across separate WorkoutSession rows.
/// </summary>
public sealed class Engine30SameDayGlycogenCalculator
{
    public Engine30SameDayGlycogenResult Calculate(
        IReadOnlyList<Engine30WorkoutGlycogenAdapterResult> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (sessions.Count == 0)
        {
            return new Engine30SameDayGlycogenResult(
                0m,
                0m,
                0m,
                Array.Empty<Engine30SameDayGlycogenSessionResult>(),
                new[] { "NO_SAME_DAY_WORKOUTS" });
        }

        var regionState =
            new Dictionary<string, RegionState>(
                StringComparer.OrdinalIgnoreCase);

        var sessionResults =
            new List<Engine30SameDayGlycogenSessionResult>(
                sessions.Count);

        decimal freshPoolTotal = 0m;
        decimal sequentialTotal = 0m;
        decimal reloadTotal = 0m;

        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];

            freshPoolTotal +=
                Math.Max(
                    0m,
                    session.EstimatedGlycogenDepletedG);

            var regions =
                session.Regions?
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.MuscleGroupCode)
                        && x.RegionBaselineGlycogenG > 0m
                        && x.EffectiveRegionalDepletionFraction > 0m)
                    .ToArray()
                ?? Array.Empty<Engine30WorkoutGlycogenRegionResult>();

            decimal sequentialSessionDepletion;

            if (regions.Length == 0)
            {
                // Compatibility fallback for an unmapped legacy profile.
                sequentialSessionDepletion =
                    Math.Max(
                        0m,
                        session.EstimatedGlycogenDepletedG);
            }
            else
            {
                sequentialSessionDepletion = 0m;

                foreach (var region in regions)
                {
                    var code =
                        region.MuscleGroupCode
                            .Trim()
                            .ToUpperInvariant();

                    if (!regionState.TryGetValue(
                            code,
                            out var state))
                    {
                        state =
                            new RegionState(
                                region.RegionBaselineGlycogenG,
                                region.RegionBaselineGlycogenG);

                        regionState.Add(
                            code,
                            state);
                    }
                    else
                    {
                        var tolerance =
                            Math.Max(
                                0.10m,
                                state.BaselineGlycogenG * 0.01m);

                        if (Math.Abs(
                                state.BaselineGlycogenG
                                - region.RegionBaselineGlycogenG)
                            > tolerance)
                        {
                            throw new InvalidOperationException(
                                $"Same-day glycogen region '{code}' has inconsistent baseline pools.");
                        }
                    }

                    var effectiveFraction =
                        Math.Clamp(
                            region.EffectiveRegionalDepletionFraction,
                            0m,
                            1m);

                    var depleted =
                        state.RemainingGlycogenG
                        * effectiveFraction;

                    depleted =
                        Math.Min(
                            state.RemainingGlycogenG,
                            Math.Max(
                                0m,
                                depleted));

                    state.RemainingGlycogenG =
                        Math.Max(
                            0m,
                            state.RemainingGlycogenG
                            - depleted);

                    sequentialSessionDepletion +=
                        depleted;
                }
            }

            var carbohydratePerGlycogen =
                session.EstimatedGlycogenDepletedG > 0m
                    ? session.CarbohydrateReloadRequirementG
                      / session.EstimatedGlycogenDepletedG
                    : 0m;

            carbohydratePerGlycogen =
                Math.Max(
                    0m,
                    carbohydratePerGlycogen);

            var sequentialReload =
                sequentialSessionDepletion
                * carbohydratePerGlycogen;

            sequentialTotal +=
                sequentialSessionDepletion;

            reloadTotal +=
                sequentialReload;

            sessionResults.Add(
                new Engine30SameDayGlycogenSessionResult(
                    SessionIndex: index,
                    FreshPoolModeledDepletionG:
                        Round(
                            session.EstimatedGlycogenDepletedG),
                    SequentialDepletionG:
                        Round(
                            sequentialSessionDepletion),
                    SequentialCarbohydrateReloadG:
                        Round(
                            sequentialReload)));
        }

        var reasons =
            new List<string>
            {
                "SAME_DAY_REGIONAL_GLYCOGEN_STATE",
                "NO_FRESH_POOL_RESET_BETWEEN_SESSIONS"
            };

        if (sessions.Count > 1)
            reasons.Add("MULTIPLE_SAME_DAY_WORKOUTS");

        if (sequentialTotal + 0.01m < freshPoolTotal)
            reasons.Add("OVERLAPPING_MUSCLE_POOL_REDUCTION_APPLIED");

        return new Engine30SameDayGlycogenResult(
            FreshPoolModeledDepletionG:
                Round(
                    freshPoolTotal),
            SequentialDepletionG:
                Round(
                    sequentialTotal),
            SequentialCarbohydrateReloadG:
                Round(
                    reloadTotal),
            Sessions:
                sessionResults,
            ReasonCodes:
                reasons);
    }

    private static decimal Round(
        decimal value)
        => Math.Round(
            value,
            2,
            MidpointRounding.AwayFromZero);

    private sealed class RegionState(
        decimal baselineGlycogenG,
        decimal remainingGlycogenG)
    {
        public decimal BaselineGlycogenG { get; } =
            baselineGlycogenG;

        public decimal RemainingGlycogenG { get; set; } =
            remainingGlycogenG;
    }
}
