namespace WebRecomp.Web.Services;

/// <summary>
/// Engine 2.1 starting weekly energy-deficit selection.
///
/// This service chooses the ENERGY-BALANCE opportunity.
/// It does not independently determine protein, fat or carbohydrate.
///
/// Plan-specific percentages are 12WR engineering defaults.
/// They are bounded by evidence-informed safety/conservatism rules
/// and will later become versioned CFG-1008 settings.
/// </summary>
public sealed record Engine21DeficitResult(
    string PlanCode,
    decimal RequestedDeficitPct,
    decimal EffectiveDeficitPct,
    decimal WeeklyMaintenanceEnergyKcal,
    decimal TargetWeeklyDeficitKcal,
    decimal TargetAverageDailyDeficitKcal,
    decimal WeeklyPrescribedEnergyKcal,
    string ConfidenceCode,
    IReadOnlyList<string> ReasonCodes);

public sealed class Engine21DeficitService
{
    public Engine21DeficitResult Calculate(
        string planCode,
        decimal weeklyMaintenanceEnergyKcal,
        decimal bmi,
        IReadOnlyDictionary<string, decimal> settings)
    {
        if (weeklyMaintenanceEnergyKcal <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weeklyMaintenanceEnergyKcal));
        }

        if (bmi <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bmi));
        }

        var normalizedPlan =
            NormalizePlan(planCode);

        var energyRounding =
            Setting(
                settings,
                "ENERGY_ROUNDING_KCAL",
                10m);

        var maxDailyDeficit =
            Setting(
                settings,
                "DEFICIT_MAX_DAILY_KCAL",
                500m);

        var maxDeficitPct =
            Setting(
                settings,
                "DEFICIT_MAX_PCT",
                0.20m);

        var minimumBmiForDeficit =
            Setting(
                settings,
                "DEFICIT_MIN_BMI",
                18.5m);

        var requestedPct =
            PlanDeficitPct(
                normalizedPlan,
                settings);

        var reasons =
            new List<string>
            {
                PlanReasonCode(normalizedPlan)
            };

        // ------------------------------------------------------------
        // BMI SAFETY / CONTEXT
        // ------------------------------------------------------------
        //
        // BMI does not determine how aggressively Recomp diets someone.
        // It is used here only as a broad safety/context screen.
        //
        // High BMI does NOT automatically increase the deficit because BMI
        // cannot distinguish adipose tissue from muscular body mass.

        if (bmi < minimumBmiForDeficit)
        {
            reasons.Add(
                "BMI_BELOW_DEFICIT_THRESHOLD");

            return new Engine21DeficitResult(
                PlanCode:
                    normalizedPlan,

                RequestedDeficitPct:
                    requestedPct,

                EffectiveDeficitPct:
                    0m,

                WeeklyMaintenanceEnergyKcal:
                    Round(
                        weeklyMaintenanceEnergyKcal,
                        2),

                TargetWeeklyDeficitKcal:
                    0m,

                TargetAverageDailyDeficitKcal:
                    0m,

                WeeklyPrescribedEnergyKcal:
                    RoundToIncrement(
                        weeklyMaintenanceEnergyKcal,
                        energyRounding),

                ConfidenceCode:
                    "MODERATE",

                ReasonCodes:
                    reasons);
        }

        if (bmi >= 30m)
        {
            reasons.Add(
                "BMI_HIGH_CONTEXT_NO_AUTO_AGGRESSION");
        }

        // ------------------------------------------------------------
        // PERCENTAGE BOUND
        // ------------------------------------------------------------

        var boundedPct =
            Math.Clamp(
                requestedPct,
                0m,
                maxDeficitPct);

        if (boundedPct < requestedPct)
        {
            reasons.Add(
                "DEFICIT_PERCENT_CAP_APPLIED");
        }

        var percentageBasedWeeklyDeficit =
            weeklyMaintenanceEnergyKcal
            * boundedPct;

        // ------------------------------------------------------------
        // ABSOLUTE DEFICIT BOUND
        // ------------------------------------------------------------
        //
        // For the initial recomp model, avoid prolonged average deficits
        // greater than approximately 500 kcal/day.
        //
        // This is especially important because the goal is not simply
        // scale-weight reduction; preservation/building of lean tissue
        // remains a primary objective.

        var maxWeeklyDeficit =
            maxDailyDeficit * 7m;

        var targetWeeklyDeficit =
            Math.Min(
                percentageBasedWeeklyDeficit,
                maxWeeklyDeficit);

        if (targetWeeklyDeficit
            < percentageBasedWeeklyDeficit)
        {
            reasons.Add(
                "DEFICIT_ABSOLUTE_CAP_APPLIED");
        }

        var effectivePct =
            targetWeeklyDeficit
            / weeklyMaintenanceEnergyKcal;

        var weeklyPrescribedEnergy =
            weeklyMaintenanceEnergyKcal
            - targetWeeklyDeficit;

        return new Engine21DeficitResult(
            PlanCode:
                normalizedPlan,

            RequestedDeficitPct:
                Round(
                    requestedPct,
                    4),

            EffectiveDeficitPct:
                Round(
                    effectivePct,
                    6),

            WeeklyMaintenanceEnergyKcal:
                Round(
                    weeklyMaintenanceEnergyKcal,
                    2),

            TargetWeeklyDeficitKcal:
                Round(
                    targetWeeklyDeficit,
                    2),

            TargetAverageDailyDeficitKcal:
                Round(
                    targetWeeklyDeficit / 7m,
                    2),

            WeeklyPrescribedEnergyKcal:
                RoundToIncrement(
                    weeklyPrescribedEnergy,
                    energyRounding),

            ConfidenceCode:
                "MODERATE",

            ReasonCodes:
                reasons);
    }

    private static decimal PlanDeficitPct(
        string planCode,
        IReadOnlyDictionary<string, decimal> settings)
        => planCode switch
        {
            // Health Reset:
            // moderate deficit, consistency-first.
            "PLAN_1" =>
                Setting(
                    settings,
                    "DEFICIT_PCT_PLAN_1",
                    0.10m),

            // Fat-Loss Dominant Recomp:
            // strongest starting deficit among standard 12WR plans.
            "PLAN_2" =>
                Setting(
                    settings,
                    "DEFICIT_PCT_PLAN_2",
                    0.15m),

            // Muscle-Building Recomp:
            // small deficit; training and lean-tissue support dominate.
            "PLAN_3" =>
                Setting(
                    settings,
                    "DEFICIT_PCT_PLAN_3",
                    0.075m),

            // Athletic Reconditioning:
            // performance requires a more moderate starting deficit.
            "PLAN_4" =>
                Setting(
                    settings,
                    "DEFICIT_PCT_PLAN_4",
                    0.10m),

            // Pain-Limited / Mobility:
            // conservative because recovery capacity can be limited.
            "PLAN_5" =>
                Setting(
                    settings,
                    "DEFICIT_PCT_PLAN_5",
                    0.10m),

            _ =>
                throw new ArgumentException(
                    $"Unsupported Recomp plan '{planCode}'.",
                    nameof(planCode))
        };

    private static string PlanReasonCode(
        string planCode)
        => planCode switch
        {
            "PLAN_1" => "PLAN_HEALTH_RESET",
            "PLAN_2" => "PLAN_FAT_LOSS_RECOMP",
            "PLAN_3" => "PLAN_MUSCLE_RECOMP",
            "PLAN_4" => "PLAN_ATHLETIC_RECOMP",
            "PLAN_5" => "PLAN_PAIN_LIMITED",
            _ => "PLAN_UNKNOWN"
        };

    private static string NormalizePlan(
        string planCode)
    {
        if (string.IsNullOrWhiteSpace(planCode))
        {
            throw new ArgumentException(
                "Recomp plan is required.",
                nameof(planCode));
        }

        return planCode
            .Trim()
            .ToUpperInvariant();
    }

    private static decimal Setting(
        IReadOnlyDictionary<string, decimal> settings,
        string code,
        decimal fallback)
        => settings.TryGetValue(
            code,
            out var value)
                ? value
                : fallback;

    private static decimal RoundToIncrement(
        decimal value,
        decimal increment)
    {
        if (increment <= 0m)
            return value;

        return Math.Round(
                   value / increment,
                   0,
                   MidpointRounding.AwayFromZero)
               * increment;
    }

    private static decimal Round(
        decimal value,
        int decimals)
        => Math.Round(
            value,
            decimals,
            MidpointRounding.AwayFromZero);
}
