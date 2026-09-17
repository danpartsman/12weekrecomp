namespace WebRecomp.Web.Services.Engine30;

public sealed record Engine30EnergyCarryoverDayResult(
    DateOnly Date,
    Engine30PrescriptionDayResult BaselinePrescription,
    Engine30PrescriptionDayResult AdjustedPrescription,
    decimal TotalCorrectionAppliedKcal,
    decimal? ActualCaloriesKcal,
    decimal? ActualVarianceKcal,
    decimal EligibleCorrectionGeneratedKcal);

public sealed record Engine30EnergyCarryoverResult(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    DateOnly AsOfDate,
    decimal SurplusRecoveryFraction,
    decimal TotalHistoricalOverageKcal,
    decimal TotalEligibleCorrectionKcal,
    decimal CorrectionAppliedBeforeAsOfDateKcal,
    decimal CorrectionAppliedOnAsOfDateKcal,
    decimal CorrectionAppliedAfterAsOfDateKcal,
    decimal UnallocatedCorrectionKcal,
    IReadOnlyList<Engine30EnergyCarryoverDayResult> Days,
    IReadOnlyList<string> ReasonCodes);

/// <summary>
/// Engine 3.0 actual-intake ENERGY carryover.
///
/// This is intentionally separate from the glycogen/fuel ledger.
///
/// Rules:
/// - only ACTUAL CALORIES on completed prior calendar days are evidence;
/// - only positive calorie variance generates correction;
/// - under-target intake is not "banked" into future calories;
/// - the Admin-configured surplus-recovery fraction determines how much
///   overage is eligible for recovery;
/// - eligible correction is assigned only to later days in the SAME
///   prescription week;
/// - lower-protection categories absorb correction first;
/// - each day is bounded by its Admin carryover cap;
/// - each day remains above both its category minimum-maintenance floor and
///   its physiological protein/fat/carbohydrate floor;
/// - Major Strength can therefore be configured with a zero correction cap;
/// - any correction that cannot be placed safely expires as unallocated
///   variance rather than creating a punitive starvation target.
///
/// The service never changes expenditure and never treats glycogen reload
/// calories as an energy-expenditure bucket.
/// </summary>
public sealed class Engine30EnergyCarryoverService
{
    private const decimal EnergyIncrementKcal = 10m;
    private const decimal CarbIncrementG = 5m;

    public Engine30EnergyCarryoverResult Apply(
        Engine30WeeklyPrescriptionResult protectedPrescription,
        Engine30GoalEnergyPolicy policy,
        DateOnly asOfDate,
        IReadOnlyDictionary<DateOnly, decimal?> actualCaloriesByDate)
    {
        ArgumentNullException.ThrowIfNull(protectedPrescription);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(actualCaloriesByDate);

        Engine30GoalEnergyPolicyService.Validate(
            policy);

        if (protectedPrescription.Days.Count != 7)
        {
            throw new ArgumentException(
                "Energy carryover requires exactly seven prescription days.");
        }

        var ordered =
            protectedPrescription.Days
                .OrderBy(x => x.Date)
                .ToArray();

        var weekStart =
            ordered[0].Date;

        var weekEnd =
            ordered[^1].Date;

        if (weekEnd != weekStart.AddDays(6))
        {
            throw new ArgumentException(
                "Energy carryover requires seven contiguous calendar days.");
        }

        var states =
            ordered
                .Select(x =>
                    new MutableDay(
                        Baseline:
                            x,
                        Current:
                            x,
                        CorrectionAppliedKcal:
                            0m,
                        ActualCaloriesKcal:
                            actualCaloriesByDate.TryGetValue(
                                x.Date,
                                out var actual)
                                ? actual
                                : null,
                        ActualVarianceKcal:
                            null,
                        EligibleCorrectionGeneratedKcal:
                            0m))
                .ToArray();

        var reasons =
            new List<string>
            {
                "ENERGY_CARRYOVER_ACTUAL_INTAKE_LEDGER_ACTIVE"
            };

        var totalHistoricalOverage =
            0m;

        var totalEligible =
            0m;

        var unallocated =
            0m;

        foreach (
            var historical in
            states.Where(x => x.Baseline.Date < asOfDate)
        ) {
            if (!historical.ActualCaloriesKcal.HasValue)
            {
                reasons.Add(
                    "ENERGY_CARRYOVER_MISSING_CALORIE_EVIDENCE");

                continue;
            }

            var variance =
                historical.ActualCaloriesKcal.Value
                - historical.Current.PrescribedEnergyKcal;

            historical.ActualVarianceKcal =
                Math.Round(
                    variance,
                    2,
                    MidpointRounding.AwayFromZero);

            if (variance <= 0m)
            {
                if (variance < 0m)
                {
                    reasons.Add(
                        "UNDER_TARGET_INTAKE_NOT_BANKED");
                }

                continue;
            }

            var overage =
                Math.Round(
                    variance,
                    2,
                    MidpointRounding.AwayFromZero);

            totalHistoricalOverage +=
                overage;

            var eligible =
                FloorToIncrement(
                    overage
                    * policy.SurplusRecoveryFraction,
                    EnergyIncrementKcal);

            historical.EligibleCorrectionGeneratedKcal =
                eligible;

            totalEligible +=
                eligible;

            if (eligible <= 0m)
                continue;

            var remaining =
                eligible;

            foreach (
                var target in
                states
                    .Where(x =>
                        x.Baseline.Date
                        > historical.Baseline.Date)
                    .OrderBy(x =>
                        policy.Category(
                            x.Baseline.WorkoutCategoryCode)
                        .ProtectionPriority)
                    .ThenBy(x => x.Baseline.Date)
            ) {
                if (remaining <= 0m)
                    break;

                var category =
                    policy.Category(
                        target.Baseline.WorkoutCategoryCode);

                var capRemaining =
                    Math.Max(
                        0m,
                        category.CarryoverCorrectionCapKcal
                        - target.CorrectionAppliedKcal);

                if (capRemaining <= 0m)
                    continue;

                var protectedFloor =
                    ProtectedEnergyFloor(
                        protectedPrescription,
                        target.Current,
                        category);

                var energyHeadroom =
                    Math.Max(
                        0m,
                        target.Current.PrescribedEnergyKcal
                        - protectedFloor);

                if (energyHeadroom <= 0m)
                    continue;

                var possible =
                    Math.Min(
                        remaining,
                        Math.Min(
                            capRemaining,
                            energyHeadroom));

                var requested =
                    FloorToIncrement(
                        possible,
                        EnergyIncrementKcal);

                if (requested <= 0m)
                    continue;

                var before =
                    target.Current.PrescribedEnergyKcal;

                var reduced =
                    ReduceEnergy(
                        target.Current,
                        protectedPrescription.StableProteinG,
                        protectedPrescription.StableFatG,
                        requested);

                var actualApplied =
                    before
                    - reduced.PrescribedEnergyKcal;

                if (actualApplied <= 0m)
                    continue;

                target.Current =
                    reduced;

                target.CorrectionAppliedKcal +=
                    actualApplied;

                remaining -=
                    actualApplied;
            }

            if (remaining > 0m)
            {
                unallocated +=
                    remaining;

                reasons.Add(
                    "ENERGY_CARRYOVER_CAPACITY_EXHAUSTED");
            }
        }

        var totalApplied =
            states.Sum(
                x => x.CorrectionAppliedKcal);

        if (totalEligible > 0m)
        {
            reasons.Add(
                "PRIOR_CALORIE_SURPLUS_ELIGIBLE_FOR_RECOVERY");

            if (totalApplied > 0m)
            {
                reasons.Add(
                    "PRIOR_CALORIE_SURPLUS_PARTIALLY_RECOVERED");
            }
            else
            {
                reasons.Add(
                    "PRIOR_CALORIE_SURPLUS_NOT_RECOVERED");
            }
        }

        if (
            states.Any(x =>
                x.Baseline.WorkoutCategoryCode.Equals(
                    Engine30PrescriptionCategoryCodes.MajorStrength,
                    StringComparison.OrdinalIgnoreCase)
                && x.CorrectionAppliedKcal == 0m)
            && totalEligible > 0m
        ) {
            reasons.Add(
                "MAJOR_STRENGTH_ENERGY_PROTECTION_PRESERVED");
        }

        var beforeAsOf =
            states
                .Where(x => x.Baseline.Date < asOfDate)
                .Sum(x => x.CorrectionAppliedKcal);

        var onAsOf =
            states
                .Where(x => x.Baseline.Date == asOfDate)
                .Sum(x => x.CorrectionAppliedKcal);

        var afterAsOf =
            states
                .Where(x => x.Baseline.Date > asOfDate)
                .Sum(x => x.CorrectionAppliedKcal);

        var results =
            states
                .Select(x =>
                    new Engine30EnergyCarryoverDayResult(
                        Date:
                            x.Baseline.Date,
                        BaselinePrescription:
                            x.Baseline,
                        AdjustedPrescription:
                            x.Current,
                        TotalCorrectionAppliedKcal:
                            x.CorrectionAppliedKcal,
                        ActualCaloriesKcal:
                            x.ActualCaloriesKcal,
                        ActualVarianceKcal:
                            x.ActualVarianceKcal,
                        EligibleCorrectionGeneratedKcal:
                            x.EligibleCorrectionGeneratedKcal))
                .ToArray();

        return new Engine30EnergyCarryoverResult(
            WeekStart:
                weekStart,
            WeekEnd:
                weekEnd,
            AsOfDate:
                asOfDate,
            SurplusRecoveryFraction:
                policy.SurplusRecoveryFraction,
            TotalHistoricalOverageKcal:
                Math.Round(
                    totalHistoricalOverage,
                    2,
                    MidpointRounding.AwayFromZero),
            TotalEligibleCorrectionKcal:
                Math.Round(
                    totalEligible,
                    2,
                    MidpointRounding.AwayFromZero),
            CorrectionAppliedBeforeAsOfDateKcal:
                Math.Round(
                    beforeAsOf,
                    2,
                    MidpointRounding.AwayFromZero),
            CorrectionAppliedOnAsOfDateKcal:
                Math.Round(
                    onAsOf,
                    2,
                    MidpointRounding.AwayFromZero),
            CorrectionAppliedAfterAsOfDateKcal:
                Math.Round(
                    afterAsOf,
                    2,
                    MidpointRounding.AwayFromZero),
            UnallocatedCorrectionKcal:
                Math.Round(
                    unallocated,
                    2,
                    MidpointRounding.AwayFromZero),
            Days:
                results,
            ReasonCodes:
                reasons
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray());
    }

    private static decimal ProtectedEnergyFloor(
        Engine30WeeklyPrescriptionResult weekly,
        Engine30PrescriptionDayResult day,
        Engine30CategoryEnergyPolicy category)
    {
        var categoryFloor =
            CeilingToIncrement(
                day.MaintenanceEnergyKcal
                * category.MinimumMaintenanceFraction,
                EnergyIncrementKcal);

        var macroFloor =
            CeilingToIncrement(
                weekly.StableProteinG * 4m
                + weekly.StableFatG * 9m
                + day.PhysiologicalCarbFloorG * 4m,
                EnergyIncrementKcal);

        return Math.Max(
            categoryFloor,
            macroFloor);
    }

    private static Engine30PrescriptionDayResult ReduceEnergy(
        Engine30PrescriptionDayResult day,
        decimal stableProteinG,
        decimal stableFatG,
        decimal requestedReductionKcal)
    {
        var newEnergy =
            Math.Max(
                0m,
                day.PrescribedEnergyKcal
                - requestedReductionKcal);

        var fixedKcal =
            stableProteinG * 4m
            + stableFatG * 9m;

        var energyBalancedCarbs =
            RoundToIncrement(
                Math.Max(
                    0m,
                    (newEnergy - fixedKcal) / 4m),
                CarbIncrementG);

        decimal carbs;
        string governing;

        if (
            day.PhysiologicalCarbFloorG
            > day.StandardCarbUpperBoundaryG
        ) {
            carbs =
                day.PhysiologicalCarbFloorG;

            governing =
                "PHYSIOLOGICAL_FLOOR_EXCEEDS_STANDARD_MAX";
        }
        else if (
            energyBalancedCarbs
            < day.PhysiologicalCarbFloorG
        ) {
            carbs =
                day.PhysiologicalCarbFloorG;

            governing =
                "PHYSIOLOGICAL_FLOOR";
        }
        else
        {
            carbs =
                Math.Min(
                    energyBalancedCarbs,
                    day.StandardCarbUpperBoundaryG);

            governing =
                "ENERGY_CARRYOVER_ADJUSTED";
        }

        var macroKcal =
            Math.Round(
                stableProteinG * 4m
                + carbs * 4m
                + stableFatG * 9m,
                0,
                MidpointRounding.AwayFromZero);

        return new Engine30PrescriptionDayResult(
            Date:
                day.Date,
            WorkoutCategoryCode:
                day.WorkoutCategoryCode,
            CategoryWeight:
                day.CategoryWeight,
            MaintenanceEnergyKcal:
                day.MaintenanceEnergyKcal,
            PrescribedEnergyKcal:
                newEnergy,
            ProteinG:
                stableProteinG,
            FatG:
                stableFatG,
            PhysiologicalCarbFloorG:
                day.PhysiologicalCarbFloorG,
            EnergyBalancedCarbsG:
                energyBalancedCarbs,
            StandardCarbUpperBoundaryG:
                day.StandardCarbUpperBoundaryG,
            RecommendedCarbsG:
                carbs,
            MacroDerivedEnergyKcal:
                macroKcal,
            MacroEnergyVarianceKcal:
                Math.Round(
                    macroKcal - newEnergy,
                    2,
                    MidpointRounding.AwayFromZero),
            CarbGoverningRuleCode:
                governing);
    }

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

    private static decimal FloorToIncrement(
        decimal value,
        decimal increment)
    {
        if (increment <= 0m)
            return value;

        return Math.Floor(
                   value / increment)
               * increment;
    }

    private static decimal CeilingToIncrement(
        decimal value,
        decimal increment)
    {
        if (increment <= 0m)
            return value;

        return Math.Ceiling(
                   value / increment)
               * increment;
    }

    private sealed class MutableDay(
        Engine30PrescriptionDayResult Baseline,
        Engine30PrescriptionDayResult Current,
        decimal CorrectionAppliedKcal,
        decimal? ActualCaloriesKcal,
        decimal? ActualVarianceKcal,
        decimal EligibleCorrectionGeneratedKcal)
    {
        public Engine30PrescriptionDayResult Baseline { get; } =
            Baseline;

        public Engine30PrescriptionDayResult Current { get; set; } =
            Current;

        public decimal CorrectionAppliedKcal { get; set; } =
            CorrectionAppliedKcal;

        public decimal? ActualCaloriesKcal { get; } =
            ActualCaloriesKcal;

        public decimal? ActualVarianceKcal { get; set; } =
            ActualVarianceKcal;

        public decimal EligibleCorrectionGeneratedKcal { get; set; } =
            EligibleCorrectionGeneratedKcal;
    }
}
