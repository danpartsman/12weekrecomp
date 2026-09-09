namespace WebRecomp.Web.Services.Engine30;

public static class Engine30FuelReadinessBandCodes
{
    public const string Adequate = "ADEQUATE";
    public const string SlightlyReduced = "SLIGHTLY_REDUCED";
    public const string ModeratelyLow = "MODERATELY_LOW";
    public const string Low = "LOW";
    public const string VeryLow = "VERY_LOW";
}

public static class Engine30FuelDemandClassCodes
{
    public const string None = "NONE";
    public const string VeryLow = "VERY_LOW";
    public const string Low = "LOW";
    public const string Moderate = "MODERATE";
    public const string High = "HIGH";
    public const string VeryHigh = "VERY_HIGH";
    public const string Extreme = "EXTREME";
}

/// <summary>
/// DRAFT, centralized Engine 3.0 fuel-state calibration.
///
/// The historical four-day restoration equation and readiness bands are
/// preserved from the frozen fuel architecture. The old user-facing FuelScore
/// is intentionally NOT resurrected. Rescue caps are selected from the actual
/// planned glycogen demand for the session and remain explicit/versionable.
/// </summary>
public static class Engine30FuelStateDefaults
{
    public const int HistoricalWindowDays = 4;
    public const decimal BackgroundBaseCarbCreditFraction = 0.20m;
    public const decimal FunctionalTargetMaximum = 0.90m;
    public const decimal ThreeModerateHardDayBonus = 0.05m;
    public const decimal FourModerateHardDayBonus = 0.10m;
    public const decimal CarbRoundingG = 5m;

    public static string DemandClass(decimal plannedGlycogenDemandG)
        => plannedGlycogenDemandG switch
        {
            <= 0m => Engine30FuelDemandClassCodes.None,
            < 20m => Engine30FuelDemandClassCodes.VeryLow,
            < 40m => Engine30FuelDemandClassCodes.Low,
            < 65m => Engine30FuelDemandClassCodes.Moderate,
            < 90m => Engine30FuelDemandClassCodes.High,
            < 130m => Engine30FuelDemandClassCodes.VeryHigh,
            _ => Engine30FuelDemandClassCodes.Extreme
        };

    public static decimal RecoveryAllocationCapG(string demandClassCode)
        => NormalizeDemandClass(demandClassCode) switch
        {
            Engine30FuelDemandClassCodes.None => 0m,
            Engine30FuelDemandClassCodes.VeryLow => 40m,
            Engine30FuelDemandClassCodes.Low => 50m,
            Engine30FuelDemandClassCodes.Moderate => 55m,
            Engine30FuelDemandClassCodes.High => 65m,
            Engine30FuelDemandClassCodes.VeryHigh => 75m,
            Engine30FuelDemandClassCodes.Extreme => 90m,
            _ => 35m
        };

    public static decimal PreWorkoutRescueCapG(string demandClassCode)
        => NormalizeDemandClass(demandClassCode) switch
        {
            Engine30FuelDemandClassCodes.None => 0m,
            Engine30FuelDemandClassCodes.VeryLow => 0m,
            Engine30FuelDemandClassCodes.Low => 25m,
            Engine30FuelDemandClassCodes.Moderate => 25m,
            Engine30FuelDemandClassCodes.High => 35m,
            Engine30FuelDemandClassCodes.VeryHigh => 45m,
            Engine30FuelDemandClassCodes.Extreme => 55m,
            _ => 25m
        };

    public static bool IsModerateHard(string demandClassCode)
        => NormalizeDemandClass(demandClassCode) is
            Engine30FuelDemandClassCodes.Moderate
            or Engine30FuelDemandClassCodes.High
            or Engine30FuelDemandClassCodes.VeryHigh
            or Engine30FuelDemandClassCodes.Extreme;

    public static bool IsHard(string demandClassCode)
        => NormalizeDemandClass(demandClassCode) is
            Engine30FuelDemandClassCodes.High
            or Engine30FuelDemandClassCodes.VeryHigh
            or Engine30FuelDemandClassCodes.Extreme;

    private static string NormalizeDemandClass(string code)
        => string.IsNullOrWhiteSpace(code)
            ? Engine30FuelDemandClassCodes.None
            : code.Trim().ToUpperInvariant();
}

public sealed record Engine30FuelLedgerDay(
    DateOnly Date,
    decimal GlycogenDemandG,
    decimal RestorationCreditG,
    decimal? ActualCarbohydrateG,
    decimal BaseRestCarbohydrateG,
    string DemandClassCode,
    bool ModerateHard,
    bool Hard,
    bool HasCarbohydrateEvidence,
    bool IsProjected);

public sealed record Engine30FuelWindow(
    decimal TotalGlycogenDemandG,
    decimal TotalRestorationCreditG,
    decimal AbsoluteRestorationRatio,
    short ModerateHardDays,
    short HardDays,
    IReadOnlyList<Engine30FuelLedgerDay> Days);

public sealed record Engine30ObservedFuelDayInput(
    DateOnly Date,
    decimal GlycogenDemandG,
    decimal BaseRestCarbohydrateG,
    decimal? ActualCarbohydrateG);

public sealed record Engine30FuelReadinessResult(
    decimal PlannedGlycogenDemandG,
    string DemandClassCode,
    decimal PlannedRestorationFraction,
    decimal FunctionalTargetFraction,
    decimal TargetHistoricalDemandG,
    decimal IncomingRestorationCreditG,
    decimal IncomingAchievementRatio,
    string IncomingReadinessBandCode,
    decimal HistoricalShortfallG,
    decimal RecoveryAllocationCapG,
    decimal RecoveryAllocationG,
    decimal PreWorkoutRescueCapG,
    decimal PreWorkoutRescueG,
    decimal ProjectedAchievementRatio,
    string ProjectedReadinessBandCode,
    bool RescueRecommended,
    bool RescueCanReachAdequateReadiness,
    IReadOnlyList<string> ReasonCodes);

public sealed record Engine30ProjectedFuelDayInput(
    DateOnly Date,
    decimal GlycogenDemandG,
    decimal PrescribedCarbohydrateG,
    decimal BaseRestCarbohydrateG);

/// <summary>
/// Pure Engine 3.0 four-day fuel ledger + rescue calculator.
///
/// Important boundaries:
/// - glycogen demand is supplied by the Engine 3.0 glycogen model;
/// - carbohydrate evidence is supplied separately;
/// - missing actual carbohydrate is preserved as missing evidence;
/// - rescue is a timing/allocation recommendation inside prescribed carbs,
///   never an extra calorie bucket;
/// - no workout calorie value is used as a glycogen proxy.
/// </summary>
public sealed class Engine30FuelStateService
{
    public Engine30FuelWindow BuildHistoricalWindow(
        IReadOnlyCollection<Engine30ObservedFuelDayInput> priorDays)
    {
        ArgumentNullException.ThrowIfNull(priorDays);

        if (priorDays.Count != Engine30FuelStateDefaults.HistoricalWindowDays)
        {
            throw new ArgumentException(
                $"Engine 3.0 historical fuel window requires exactly {Engine30FuelStateDefaults.HistoricalWindowDays} days.",
                nameof(priorDays));
        }

        var ordered = priorDays.OrderBy(x => x.Date).ToArray();
        EnsureConsecutiveDates(ordered.Select(x => x.Date).ToArray());

        var ledger = ordered.Select(BuildObservedLedgerDay).ToArray();
        return Summarize(ledger);
    }

    public Engine30FuelLedgerDay BuildProjectedDay(
        Engine30ProjectedFuelDayInput input)
    {
        ValidateDemandAndCarbs(
            input.GlycogenDemandG,
            input.PrescribedCarbohydrateG,
            input.BaseRestCarbohydrateG);

        var demandClass =
            Engine30FuelStateDefaults.DemandClass(input.GlycogenDemandG);

        var credit = CalculateRestorationCredit(
            input.GlycogenDemandG,
            input.PrescribedCarbohydrateG,
            input.BaseRestCarbohydrateG);

        return new Engine30FuelLedgerDay(
            Date: input.Date,
            GlycogenDemandG: Round(input.GlycogenDemandG, 2),
            RestorationCreditG: credit,
            ActualCarbohydrateG: null,
            BaseRestCarbohydrateG: Round(input.BaseRestCarbohydrateG, 2),
            DemandClassCode: demandClass,
            ModerateHard: Engine30FuelStateDefaults.IsModerateHard(demandClass),
            Hard: Engine30FuelStateDefaults.IsHard(demandClass),
            HasCarbohydrateEvidence: false,
            IsProjected: true);
    }

    public Engine30FuelWindow RollForward(
        Engine30FuelWindow current,
        Engine30FuelLedgerDay projectedDay)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(projectedDay);

        if (current.Days.Count != Engine30FuelStateDefaults.HistoricalWindowDays)
        {
            throw new InvalidOperationException(
                $"Engine 3.0 fuel rolling requires exactly {Engine30FuelStateDefaults.HistoricalWindowDays} incoming days.");
        }

        var ordered = current.Days.OrderBy(x => x.Date).ToArray();
        var expectedDate = ordered[^1].Date.AddDays(1);

        if (projectedDay.Date != expectedDate)
        {
            throw new InvalidOperationException(
                $"Projected fuel day must be {expectedDate:yyyy-MM-dd}; received {projectedDay.Date:yyyy-MM-dd}.");
        }

        var next = ordered.Skip(1).Append(projectedDay).ToArray();
        return Summarize(next);
    }

    public Engine30FuelReadinessResult CalculateReadiness(
        Engine30FuelWindow incomingWindow,
        decimal plannedGlycogenDemandG,
        decimal plannedRestorationFraction,
        decimal? maxCarbohydrateAvailableBeforeWorkoutG = null)
    {
        ArgumentNullException.ThrowIfNull(incomingWindow);

        if (plannedGlycogenDemandG < 0m)
            throw new ArgumentOutOfRangeException(nameof(plannedGlycogenDemandG));

        if (plannedRestorationFraction < 0m || plannedRestorationFraction > 1m)
            throw new ArgumentOutOfRangeException(nameof(plannedRestorationFraction));

        if (maxCarbohydrateAvailableBeforeWorkoutG.HasValue
            && maxCarbohydrateAvailableBeforeWorkoutG.Value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCarbohydrateAvailableBeforeWorkoutG));
        }

        var functionalTarget = plannedRestorationFraction;

        if (incomingWindow.ModerateHardDays >= 4)
            functionalTarget += Engine30FuelStateDefaults.FourModerateHardDayBonus;
        else if (incomingWindow.ModerateHardDays >= 3)
            functionalTarget += Engine30FuelStateDefaults.ThreeModerateHardDayBonus;

        functionalTarget = Math.Min(
            Engine30FuelStateDefaults.FunctionalTargetMaximum,
            functionalTarget);

        var targetHistoricalDemand =
            incomingWindow.TotalGlycogenDemandG * functionalTarget;

        var incomingAchievement =
            targetHistoricalDemand <= 0m
                ? 1m
                : Math.Min(
                    1m,
                    incomingWindow.TotalRestorationCreditG / targetHistoricalDemand);

        var shortfall = Math.Max(
            0m,
            targetHistoricalDemand - incomingWindow.TotalRestorationCreditG);

        var demandClass =
            Engine30FuelStateDefaults.DemandClass(plannedGlycogenDemandG);

        var recoveryCap =
            Engine30FuelStateDefaults.RecoveryAllocationCapG(demandClass);

        var recoveryAllocation =
            Math.Min(shortfall, recoveryCap);

        var preWorkoutCap =
            Engine30FuelStateDefaults.PreWorkoutRescueCapG(demandClass);

        if (maxCarbohydrateAvailableBeforeWorkoutG.HasValue)
        {
            preWorkoutCap = Math.Min(
                preWorkoutCap,
                maxCarbohydrateAvailableBeforeWorkoutG.Value);
        }

        var preWorkoutRescue =
            Math.Min(recoveryAllocation, preWorkoutCap);

        var projectedAchievement =
            targetHistoricalDemand <= 0m
                ? 1m
                : Math.Min(
                    1m,
                    (incomingWindow.TotalRestorationCreditG + preWorkoutRescue)
                    / targetHistoricalDemand);

        var incomingBand = ReadinessBand(incomingAchievement);
        var projectedBand = ReadinessBand(projectedAchievement);

        var reasons = new List<string>();

        if (incomingWindow.TotalGlycogenDemandG <= 0m)
            reasons.Add("NO_HISTORICAL_GLYCOGEN_DEMAND");
        if (incomingWindow.ModerateHardDays >= 4)
            reasons.Add("FOUR_PLUS_MODERATE_HARD_DAYS");
        else if (incomingWindow.ModerateHardDays >= 3)
            reasons.Add("THREE_MODERATE_HARD_DAYS");
        if (shortfall > 0m)
            reasons.Add("HISTORICAL_RESTORATION_SHORTFALL");
        if (preWorkoutRescue > 0m)
            reasons.Add("PREWORKOUT_CARB_RESCUE_AVAILABLE");
        if (maxCarbohydrateAvailableBeforeWorkoutG.HasValue
            && preWorkoutRescue >= maxCarbohydrateAvailableBeforeWorkoutG.Value
            && maxCarbohydrateAvailableBeforeWorkoutG.Value < Engine30FuelStateDefaults.PreWorkoutRescueCapG(demandClass))
        {
            reasons.Add("PREWORKOUT_TIMING_LIMIT_APPLIED");
        }

        return new Engine30FuelReadinessResult(
            PlannedGlycogenDemandG: Round(plannedGlycogenDemandG, 2),
            DemandClassCode: demandClass,
            PlannedRestorationFraction: Round(plannedRestorationFraction, 4),
            FunctionalTargetFraction: Round(functionalTarget, 4),
            TargetHistoricalDemandG: Round(targetHistoricalDemand, 2),
            IncomingRestorationCreditG: Round(incomingWindow.TotalRestorationCreditG, 2),
            IncomingAchievementRatio: Round(incomingAchievement, 4),
            IncomingReadinessBandCode: incomingBand,
            HistoricalShortfallG: Round(shortfall, 2),
            RecoveryAllocationCapG: recoveryCap,
            RecoveryAllocationG: Round(recoveryAllocation, 2),
            PreWorkoutRescueCapG: preWorkoutCap,
            PreWorkoutRescueG: Round(preWorkoutRescue, 2),
            ProjectedAchievementRatio: Round(projectedAchievement, 4),
            ProjectedReadinessBandCode: projectedBand,
            RescueRecommended: preWorkoutRescue > 0m,
            RescueCanReachAdequateReadiness:
                projectedBand == Engine30FuelReadinessBandCodes.Adequate,
            ReasonCodes: reasons);
    }

    public static string ReadinessBand(decimal achievementRatio)
    {
        if (achievementRatio < 0m)
            throw new ArgumentOutOfRangeException(nameof(achievementRatio));

        return achievementRatio switch
        {
            >= 1.00m => Engine30FuelReadinessBandCodes.Adequate,
            >= 0.85m => Engine30FuelReadinessBandCodes.SlightlyReduced,
            >= 0.70m => Engine30FuelReadinessBandCodes.ModeratelyLow,
            >= 0.50m => Engine30FuelReadinessBandCodes.Low,
            _ => Engine30FuelReadinessBandCodes.VeryLow
        };
    }

    private static Engine30FuelLedgerDay BuildObservedLedgerDay(
        Engine30ObservedFuelDayInput input)
    {
        if (input.GlycogenDemandG < 0m)
            throw new ArgumentOutOfRangeException(nameof(input.GlycogenDemandG));
        if (input.BaseRestCarbohydrateG < 0m)
            throw new ArgumentOutOfRangeException(nameof(input.BaseRestCarbohydrateG));
        if (input.ActualCarbohydrateG.HasValue && input.ActualCarbohydrateG.Value < 0m)
            throw new ArgumentOutOfRangeException(nameof(input.ActualCarbohydrateG));

        var demandClass =
            Engine30FuelStateDefaults.DemandClass(input.GlycogenDemandG);

        var hasEvidence = input.ActualCarbohydrateG.HasValue;

        var credit =
            hasEvidence
                ? CalculateRestorationCredit(
                    input.GlycogenDemandG,
                    input.ActualCarbohydrateG!.Value,
                    input.BaseRestCarbohydrateG)
                : 0m;

        return new Engine30FuelLedgerDay(
            Date: input.Date,
            GlycogenDemandG: Round(input.GlycogenDemandG, 2),
            RestorationCreditG: credit,
            ActualCarbohydrateG: input.ActualCarbohydrateG,
            BaseRestCarbohydrateG: Round(input.BaseRestCarbohydrateG, 2),
            DemandClassCode: demandClass,
            ModerateHard: Engine30FuelStateDefaults.IsModerateHard(demandClass),
            Hard: Engine30FuelStateDefaults.IsHard(demandClass),
            HasCarbohydrateEvidence: hasEvidence,
            IsProjected: false);
    }

    private static decimal CalculateRestorationCredit(
        decimal demandG,
        decimal carbohydrateG,
        decimal baseRestCarbohydrateG)
    {
        ValidateDemandAndCarbs(demandG, carbohydrateG, baseRestCarbohydrateG);

        if (demandG <= 0m)
            return 0m;

        var rawCredit = Math.Min(
            demandG,
            Math.Max(0m, carbohydrateG - baseRestCarbohydrateG)
            + Math.Min(carbohydrateG, baseRestCarbohydrateG)
              * Engine30FuelStateDefaults.BackgroundBaseCarbCreditFraction);

        return RoundToIncrement(
            rawCredit,
            Engine30FuelStateDefaults.CarbRoundingG);
    }

    private static Engine30FuelWindow Summarize(
        IReadOnlyList<Engine30FuelLedgerDay> days)
    {
        var demand = days.Sum(x => x.GlycogenDemandG);
        var credit = days.Sum(x => x.RestorationCreditG);

        var ratio =
            demand <= 0m
                ? 1m
                : Math.Min(1m, credit / demand);

        return new Engine30FuelWindow(
            TotalGlycogenDemandG: Round(demand, 2),
            TotalRestorationCreditG: Round(credit, 2),
            AbsoluteRestorationRatio: Round(ratio, 4),
            ModerateHardDays: checked((short)days.Count(x => x.ModerateHard)),
            HardDays: checked((short)days.Count(x => x.Hard)),
            Days: days.OrderBy(x => x.Date).ToArray());
    }

    private static void ValidateDemandAndCarbs(
        decimal demandG,
        decimal carbohydrateG,
        decimal baseRestCarbohydrateG)
    {
        if (demandG < 0m)
            throw new ArgumentOutOfRangeException(nameof(demandG));
        if (carbohydrateG < 0m)
            throw new ArgumentOutOfRangeException(nameof(carbohydrateG));
        if (baseRestCarbohydrateG < 0m)
            throw new ArgumentOutOfRangeException(nameof(baseRestCarbohydrateG));
    }

    private static void EnsureConsecutiveDates(DateOnly[] dates)
    {
        for (var i = 1; i < dates.Length; i++)
        {
            if (dates[i] != dates[i - 1].AddDays(1))
            {
                throw new ArgumentException(
                    "Engine 3.0 fuel-window dates must be consecutive calendar days.");
            }
        }
    }

    private static decimal RoundToIncrement(decimal value, decimal increment)
        => Math.Round(value / increment, 0, MidpointRounding.AwayFromZero) * increment;

    private static decimal Round(decimal value, int decimals)
        => Math.Round(value, decimals, MidpointRounding.AwayFromZero);
}
