namespace WebRecomp.Web.Services.Engine30;

public static class Engine30BackgroundCarbohydrateModelCodes
{
    public const string EvidenceCalibratedHabitualV1 =
        "OBLIGATE_GLUCOSE_PLUS_HABITUAL_SUBSTRATE_V1";

    public const string EstimatedNoIndirectCalorimetry =
        "ESTIMATED_NO_INDIRECT_CALORIMETRY";
}

public sealed record Engine30BackgroundCarbohydrateResult(
    decimal ObligatoryGlucoseRequirementG,
    decimal OccupationalActivityCarbohydrateG,
    decimal HomeActivityCarbohydrateG,
    decimal LifestyleActivityCarbohydrateG,
    decimal HabitualActivityCarbohydrateG,
    decimal TotalBackgroundCarbohydrateRequirementG,
    string ModelCode,
    string ConfidenceCode);

/// <summary>
/// Estimates the non-workout carbohydrate requirement that belongs underneath
/// Engine 3.0's separate workout-glycogen reload calculation.
///
/// The model intentionally separates two different physiological jobs:
///
/// 1. A 100 g/day adult obligatory-glucose anchor. The National Academies
///    adult carbohydrate EAR is based on average minimum brain glucose use.
///    This is NOT called muscle-glycogen depletion.
///
/// 2. Additional carbohydrate oxidized by habitual movement above rest
///    (occupation, home activity and lifestyle activity). Engine 3.0 already
///    calculates the above-rest energy of those activities from the person's
///    base-living energy, activity factor and hours. This calculator estimates
///    what fraction of that incremental activity energy came from carbohydrate.
///
/// Gold-standard substrate oxidation requires VO2/VCO2 (indirect calorimetry),
/// which 12WR does not currently receive. Therefore the habitual substrate
/// split is an explicit evidence-calibrated estimate, not false precision.
/// Low-intensity movement is kept predominantly fat-fueled, and the model is
/// bounded below the carbohydrate fraction observed in formal moderate
/// exercise. A future measured-RER input can replace this fallback without
/// changing the prescription architecture.
/// </summary>
public sealed class Engine30BackgroundCarbohydrateCalculator
{
    public const decimal AdultObligatoryGlucoseAnchorG = 100m;
    public const decimal KcalPerGramCarbohydrate = 4m;

    /*
     * Engine habitual activity factors represent ABOVE-REST energy. A factor
     * of 0.30 therefore corresponds approximately to a 1.30-resting-equivalent
     * activity, while 1.00 corresponds approximately to 2.00 resting
     * equivalents averaged over the activity period.
     *
     * Calibration for the incremental activity-energy fraction supplied by
     * carbohydrate:
     *
     *     fraction = 0.15 + 0.25 * activityFactor
     *
     * constrained to 0.20..0.40.
     *
     * This yields 22.5% at factor 0.30, 27.5% at 0.50, 30% at 0.60 and
     * 40% at 1.00. The range deliberately reflects predominantly fat-fueled
     * low-intensity activity while remaining below formal exercise data where
     * carbohydrate contributes about 46% of energy around 44% VO2max and rises
     * sharply with intensity. It is a fallback until measured respiratory
     * exchange data are available.
     */
    public const decimal MinimumHabitualCarbohydrateFraction = 0.20m;
    public const decimal MaximumHabitualCarbohydrateFraction = 0.40m;
    public const decimal HabitualFractionIntercept = 0.15m;
    public const decimal HabitualFractionSlope = 0.25m;

    public Engine30BackgroundCarbohydrateResult Calculate(
        Engine30HabitualActivityResult habitual)
    {
        ArgumentNullException.ThrowIfNull(habitual);

        var occupationalWeeklyCarbKcal =
            habitual.Roles.Sum(
                role =>
                    role.WeeklyActivityEnergyKcal
                    * ResolveIncrementalCarbohydrateFraction(
                        role.EffectiveOccupationalActivityFactor));

        var homeWeeklyCarbKcal =
            habitual.WeeklyHomeActivityEnergyKcal
            * ResolveIncrementalCarbohydrateFraction(
                habitual.HomeActivityFactor);

        var lifestyleWeeklyCarbKcal =
            habitual.WeeklyLifestyleActivityEnergyKcal
            * ResolveIncrementalCarbohydrateFraction(
                habitual.HomeActivityFactor);

        var occupationalG =
            occupationalWeeklyCarbKcal
            / KcalPerGramCarbohydrate
            / 7m;

        var homeG =
            homeWeeklyCarbKcal
            / KcalPerGramCarbohydrate
            / 7m;

        var lifestyleG =
            lifestyleWeeklyCarbKcal
            / KcalPerGramCarbohydrate
            / 7m;

        var habitualG =
            occupationalG
            + homeG
            + lifestyleG;

        var total =
            AdultObligatoryGlucoseAnchorG
            + habitualG;

        return new Engine30BackgroundCarbohydrateResult(
            ObligatoryGlucoseRequirementG:
                AdultObligatoryGlucoseAnchorG,
            OccupationalActivityCarbohydrateG:
                Round(occupationalG, 2),
            HomeActivityCarbohydrateG:
                Round(homeG, 2),
            LifestyleActivityCarbohydrateG:
                Round(lifestyleG, 2),
            HabitualActivityCarbohydrateG:
                Round(habitualG, 2),
            TotalBackgroundCarbohydrateRequirementG:
                Round(total, 2),
            ModelCode:
                Engine30BackgroundCarbohydrateModelCodes
                    .EvidenceCalibratedHabitualV1,
            ConfidenceCode:
                Engine30BackgroundCarbohydrateModelCodes
                    .EstimatedNoIndirectCalorimetry);
    }

    public static decimal ResolveIncrementalCarbohydrateFraction(
        decimal effectiveActivityFactor)
    {
        if (effectiveActivityFactor < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveActivityFactor));
        }

        var raw =
            HabitualFractionIntercept
            + HabitualFractionSlope
              * effectiveActivityFactor;

        return Math.Min(
            MaximumHabitualCarbohydrateFraction,
            Math.Max(
                MinimumHabitualCarbohydrateFraction,
                raw));
    }

    private static decimal Round(
        decimal value,
        int decimals)
        => Math.Round(
            value,
            decimals,
            MidpointRounding.AwayFromZero);
}
