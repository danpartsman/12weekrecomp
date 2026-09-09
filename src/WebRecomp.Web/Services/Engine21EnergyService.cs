namespace WebRecomp.Web.Services;

/// <summary>
/// Engine 2.1 population/reference energy snapshot.
///
/// Research-supported:
/// - ten Haaf resting-energy population equation
/// - 2023 DRI inactive and low-active adult TEE equations
/// - broad step-count activity classifications
///
/// Engineering rule:
/// Step count alone is not treated as a precise PAL or calorie conversion.
/// Until better activity/context or longitudinal data exist:
///   < 5,000 habitual steps/day -> DRI inactive reference
///   >= 5,000 habitual steps/day -> midpoint of DRI inactive and low-active
///
/// Formal exercise is NOT included here. It remains a separate 12WR model.
/// </summary>
public sealed record Engine21EnergyReference(
    int AgeYears,
    decimal WeightKg,
    decimal HeightCm,
    decimal Bmi,
    decimal RestingEnergyKcal,
    decimal DriInactiveTeeKcal,
    decimal DriLowActiveTeeKcal,
    decimal? HabitualStepsPerDay,
    int ValidRecentStepDays,
    string StepSourceCode,
    string HabitualStepBandCode,
    decimal ProvisionalNonWorkoutEnergyKcal,
    string NonWorkoutEnergyModelCode,
    string ConfidenceCode);

/// <summary>
/// Engine 2.1 dietary-fat recommendation.
///
/// Fat is established from the person's average prescribed recomp energy,
/// subject to a body-weight floor. It is not workout-specific.
/// </summary>
public sealed record Engine21FatTarget(
    decimal AveragePrescribedEnergyKcal,
    decimal FatFromEnergyG,
    decimal FatFloorG,
    decimal RecommendedFatG,
    string GoverningRuleCode);

public sealed class Engine21EnergyService
{
    private const decimal PoundsPerKg = 2.2046226218m;
    private const decimal CmPerInch = 2.54m;

    public Engine21EnergyReference CalculateReference(
        DateOnly actionDate,
        DateOnly dateOfBirth,
        string sexCode,
        decimal heightIn,
        decimal weightLb,
        int? baselineStepsDay,
        IReadOnlyCollection<int?> recentDailySteps)
    {
        if (heightIn <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(heightIn),
                "Height must be greater than zero.");

        if (weightLb <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(weightLb),
                "Weight must be greater than zero.");

        var age = CalculateAge(dateOfBirth, actionDate);

        if (age < 19)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dateOfBirth),
                "The current Engine 2.1 adult energy model requires age 19 or older.");
        }

        var weightKg = weightLb / PoundsPerKg;
        var heightCm = heightIn * CmPerInch;
        var heightM = heightCm / 100m;
        var maleIndicator = MaleIndicator(sexCode);
        var bmi = weightKg / (heightM * heightM);

        // ------------------------------------------------------------
        // 1. RESTING ENERGY REFERENCE
        // ------------------------------------------------------------
        //
        // ten Haaf & Weijs:
        //
        // REE =
        // 11.936 * weight_kg
        // + 587.728 * height_m
        // - 8.129 * age
        // + 191.027 * sex(M=1,F=0)
        // + 29.279
        //
        // This is a population RMR reference, not measured metabolism.

        var restingEnergy =
            11.936m * weightKg
            + 587.728m * heightM
            - 8.129m * age
            + 191.027m * maleIndicator
            + 29.279m;

        // ------------------------------------------------------------
        // 2. 2023 DRI TOTAL-ENERGY REFERENCES
        // ------------------------------------------------------------
        //
        // These equations already estimate TOTAL energy expenditure.
        // Do not separately add TEF to these values.
        //
        // We calculate both Inactive and Low Active so Engine 2.1 can
        // maintain an uncertainty band rather than pretend steps identify
        // one exact activity multiplier.

        decimal driInactiveTee;
        decimal driLowActiveTee;

        if (maleIndicator == 1m)
        {
            driInactiveTee =
                753.07m
                - 10.83m * age
                + 6.50m * heightCm
                + 14.10m * weightKg;

            driLowActiveTee =
                581.47m
                - 10.83m * age
                + 8.30m * heightCm
                + 14.94m * weightKg;
        }
        else
        {
            driInactiveTee =
                584.90m
                - 7.01m * age
                + 5.72m * heightCm
                + 11.71m * weightKg;

            driLowActiveTee =
                575.77m
                - 7.01m * age
                + 6.60m * heightCm
                + 12.14m * weightKg;
        }

        // ------------------------------------------------------------
        // 3. HABITUAL STEP ESTIMATE
        // ------------------------------------------------------------

        var validRecentSteps = recentDailySteps
            .Where(x =>
                x.HasValue
                && x.Value > 0
                && x.Value <= 100000)
            .Select(x => x!.Value)
            .ToArray();

        decimal? habitualSteps = null;
        string stepSource;
        string confidence;

        // Prefer a real recent habitual average when enough data exist.
        // Use up to the most recent 14 valid observations so the estimate
        // can become more stable as data accumulate.
        if (validRecentSteps.Length >= 7)
        {
            habitualSteps =
                validRecentSteps
                    .TakeLast(14)
                    .Average(x => (decimal)x);

            stepSource = "RECENT_AVERAGE";
            confidence = "MODERATE";
        }
        else if (baselineStepsDay.HasValue
                 && baselineStepsDay.Value > 0)
        {
            habitualSteps = baselineStepsDay.Value;
            stepSource = "PROFILE_BASELINE";
            confidence = "LOW";
        }
        else if (validRecentSteps.Length >= 3)
        {
            habitualSteps =
                validRecentSteps.Average(x => (decimal)x);

            stepSource = "PARTIAL_RECENT_AVERAGE";
            confidence = "LOW";
        }
        else
        {
            stepSource = "MISSING";
            confidence = "VERY_LOW";
        }

        var stepBand =
            ClassifyHabitualSteps(habitualSteps);

        // ------------------------------------------------------------
        // 4. PROVISIONAL NON-WORKOUT ENERGY
        // ------------------------------------------------------------
        //
        // IMPORTANT:
        //
        // Step-count categories and DRI PAL categories are not equivalent.
        // Free-living step counts do not reliably identify an exact PAL.
        //
        // Therefore Engine 2.1 starts conservatively:
        //
        // Missing / <5,000:
        //      use DRI Inactive.
        //
        // >=5,000:
        //      use the midpoint between DRI Inactive and Low Active.
        //
        // We deliberately do NOT move to DRI Active or Very Active from
        // step counts alone. Formal workouts are modeled separately, and
        // later daily-activity/context and longitudinal response data can
        // personalize this estimate.

        decimal provisionalNonWorkoutEnergy;
        string nonWorkoutEnergyModel;

        if (!habitualSteps.HasValue
            || habitualSteps.Value < 5000m)
        {
            provisionalNonWorkoutEnergy =
                driInactiveTee;

            nonWorkoutEnergyModel =
                "DRI_INACTIVE";
        }
        else
        {
            provisionalNonWorkoutEnergy =
                (driInactiveTee + driLowActiveTee) / 2m;

            nonWorkoutEnergyModel =
                "DRI_INACTIVE_LOW_ACTIVE_MIDPOINT";
        }

        return new Engine21EnergyReference(
            AgeYears:
                age,

            WeightKg:
                Round(weightKg, 2),

            HeightCm:
                Round(heightCm, 2),

            Bmi:
                Round(bmi, 2),

            RestingEnergyKcal:
                Round(restingEnergy, 0),

            DriInactiveTeeKcal:
                Round(driInactiveTee, 0),

            DriLowActiveTeeKcal:
                Round(driLowActiveTee, 0),

            HabitualStepsPerDay:
                habitualSteps.HasValue
                    ? Round(habitualSteps.Value, 0)
                    : null,

            ValidRecentStepDays:
                validRecentSteps.Length,

            StepSourceCode:
                stepSource,

            HabitualStepBandCode:
                stepBand,

            ProvisionalNonWorkoutEnergyKcal:
                Round(provisionalNonWorkoutEnergy, 0),

            NonWorkoutEnergyModelCode:
                nonWorkoutEnergyModel,

            ConfidenceCode:
                confidence);
    }

    public Engine21FatTarget CalculateFatTarget(
        decimal currentWeightLb,
        decimal averagePrescribedEnergyKcal,
        IReadOnlyDictionary<string, decimal> settings)
    {
        if (currentWeightLb <= 0m)
            throw new ArgumentOutOfRangeException(nameof(currentWeightLb));

        if (averagePrescribedEnergyKcal <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(averagePrescribedEnergyKcal));
        }

        var fatPct =
            Setting(
                settings,
                "FAT_TARGET_ENERGY_PCT",
                0.20m);

        var fatMinGPerKg =
            Setting(
                settings,
                "FAT_MIN_G_PER_KG",
                0.50m);

        var fatRoundingG =
            Setting(
                settings,
                "FAT_ROUNDING_G",
                5m);

        var weightKg =
            currentWeightLb / PoundsPerKg;

        var fatFromEnergy =
            averagePrescribedEnergyKcal
            * fatPct
            / 9m;

        var fatFloor =
            weightKg
            * fatMinGPerKg;

        var governingRule =
            fatFloor > fatFromEnergy
                ? "WEIGHT_FLOOR"
                : "ENERGY_PERCENT";

        var recommendedFat =
            RoundToIncrement(
                Math.Max(
                    fatFloor,
                    fatFromEnergy),
                fatRoundingG);

        return new Engine21FatTarget(
            AveragePrescribedEnergyKcal:
                Round(
                    averagePrescribedEnergyKcal,
                    2),

            FatFromEnergyG:
                Round(
                    fatFromEnergy,
                    2),

            FatFloorG:
                Round(
                    fatFloor,
                    2),

            RecommendedFatG:
                recommendedFat,

            GoverningRuleCode:
                governingRule);
    }

    /// <summary>
    /// Stable dietary fat is based on the average prescribed nutrition
    /// energy for the full seven-day period, not an individual workout day.
    /// </summary>
    public decimal CalculateSevenDayAveragePrescribedEnergy(
        IReadOnlyCollection<decimal> prescribedDailyCalories)
    {
        if (prescribedDailyCalories.Count != 7)
        {
            throw new ArgumentException(
                "Exactly seven daily prescriptions are required.",
                nameof(prescribedDailyCalories));
        }

        if (prescribedDailyCalories.Any(x => x <= 0m))
        {
            throw new ArgumentException(
                "All daily prescriptions must be greater than zero.",
                nameof(prescribedDailyCalories));
        }

        return prescribedDailyCalories.Average();
    }

    private static string ClassifyHabitualSteps(
        decimal? habitualSteps)
    {
        if (!habitualSteps.HasValue)
            return "MISSING";

        if (habitualSteps.Value < 5000m)
            return "SEDENTARY";

        if (habitualSteps.Value < 7500m)
            return "LOW_ACTIVE";

        if (habitualSteps.Value < 10000m)
            return "SOMEWHAT_ACTIVE";

        if (habitualSteps.Value < 12500m)
            return "ACTIVE";

        return "HIGHLY_ACTIVE";
    }

    private static int CalculateAge(
        DateOnly dateOfBirth,
        DateOnly actionDate)
    {
        if (dateOfBirth > actionDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dateOfBirth),
                "Date of birth cannot be after the calculation date.");
        }

        var age =
            actionDate.Year
            - dateOfBirth.Year;

        if (actionDate < dateOfBirth.AddYears(age))
            age--;

        return age;
    }

    private static decimal MaleIndicator(
        string sexCode)
    {
        if (string.IsNullOrWhiteSpace(sexCode))
        {
            throw new ArgumentException(
                "Sex is required by the current population energy equations.",
                nameof(sexCode));
        }

        return sexCode
            .Trim()
            .ToUpperInvariant() switch
        {
            "M" or "MALE" => 1m,
            "F" or "FEMALE" => 0m,

            _ => throw new ArgumentException(
                $"Unsupported SexCode '{sexCode}' for the current population energy equations.",
                nameof(sexCode))
        };
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



