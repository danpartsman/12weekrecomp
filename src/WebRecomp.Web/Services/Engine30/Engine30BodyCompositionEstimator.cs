namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Engine 3.0 body-composition estimate used by workout-energy
/// and later glycogen/fuel calculations.
///
/// Source hierarchy:
///
/// 1. Body-fat percentage available:
///      derive fat-free mass directly from body weight.
///
/// 2. Body-fat percentage unavailable:
///      estimate FFM from age, sex, height and weight using the
///      Saboor et al. adult anthropometric equation.
///
/// This service does NOT treat consumer-scale "muscle mass" as
/// equivalent to whole-body fat-free mass.
/// </summary>
public sealed record Engine30BodyCompositionInput(
    int AgeYears,
    string SexCode,
    decimal HeightIn,
    decimal WeightLb,

    /// <summary>
    /// Fraction, not whole percent.
    /// Example: 25% body fat = 0.25.
    /// Null means unavailable.
    /// </summary>
    decimal? BodyFatFraction = null);

public sealed record Engine30BodyCompositionResult(
    int AgeYears,
    string SexCode,

    decimal HeightIn,
    decimal HeightCm,

    decimal WeightLb,
    decimal WeightKg,

    decimal? BodyFatFraction,
    decimal? BodyFatPercent,

    decimal FatFreeMassLb,
    decimal FatFreeMassKg,

    decimal FatFreeMassFraction,

    string BodyCompositionSourceCode,
    string ConfidenceCode,
    string FormulaCode);

public sealed class Engine30BodyCompositionEstimator
{
    private const decimal PoundsPerKg = 2.2046226218m;
    private const decimal CmPerInch = 2.54m;

    /*
        Saboor et al. 2023 anthropometric FFM equation:

        FFM kg =
            0.28 × weight kg
          + 0.57 × height cm
          + 7.35 × sex
          + 0.03 × age
          - 70.61

        sex:
            male   = 1
            female = 0

        Published validation population:
            adults age 18-69.

        Engine 3.0 currently supports adults age 19+.
        Estimates above age 69 are allowed but explicitly marked
        as extrapolated / lower confidence.
    */

    public Engine30BodyCompositionResult Calculate(
        Engine30BodyCompositionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        ValidateBasicInput(input);

        var normalizedSex =
            NormalizeSex(input.SexCode);

        var weightKg =
            input.WeightLb / PoundsPerKg;

        var heightCm =
            input.HeightIn * CmPerInch;

        decimal fatFreeMassKg;
        decimal? normalizedBodyFatFraction;
        string sourceCode;
        string confidenceCode;
        string formulaCode;

        if (input.BodyFatFraction.HasValue)
        {
            ValidateBodyFat(
                input.BodyFatFraction.Value);

            normalizedBodyFatFraction =
                input.BodyFatFraction.Value;

            /*
                FFM includes all non-fat body mass.

                This is preferable to guessing FFM from sex/body size
                when a usable body-fat estimate is available.

                We still do not call this "measured FFM" because the
                body-fat value itself may be an estimate.
            */
            fatFreeMassKg =
                weightKg
                * (1m - input.BodyFatFraction.Value);

            sourceCode =
                "BODY_FAT_DERIVED";

            confidenceCode =
                "MEDIUM";

            formulaCode =
                "WEIGHT_MINUS_FAT_MASS";
        }
        else
        {
            normalizedBodyFatFraction =
                null;

            var sexValue =
                normalizedSex == "MALE"
                    ? 1m
                    : 0m;

            fatFreeMassKg =
                0.28m * weightKg
                + 0.57m * heightCm
                + 7.35m * sexValue
                + 0.03m * input.AgeYears
                - 70.61m;

            if (input.AgeYears <= 69)
            {
                sourceCode =
                    "ANTHROPOMETRIC_ESTIMATE";

                confidenceCode =
                    "MEDIUM";
            }
            else
            {
                /*
                    The source equation was validated through age 69.

                    Engine 3.0 permits calculation above that age
                    so older adults are not arbitrarily excluded,
                    but the result is explicitly labeled as an
                    extrapolation rather than a validated estimate.
                */
                sourceCode =
                    "ANTHROPOMETRIC_EXTRAPOLATED";

                confidenceCode =
                    "LOW";
            }

            formulaCode =
                "SABOOR_FFM_2023";
        }

        ValidateCalculatedFfm(
            fatFreeMassKg,
            weightKg);

        var fatFreeMassLb =
            fatFreeMassKg * PoundsPerKg;

        var fatFreeMassFraction =
            fatFreeMassKg / weightKg;

        return new Engine30BodyCompositionResult(
            AgeYears:
                input.AgeYears,

            SexCode:
                normalizedSex,

            HeightIn:
                Round(input.HeightIn, 2),

            HeightCm:
                Round(heightCm, 2),

            WeightLb:
                Round(input.WeightLb, 2),

            WeightKg:
                Round(weightKg, 2),

            BodyFatFraction:
                normalizedBodyFatFraction.HasValue
                    ? Round(
                        normalizedBodyFatFraction.Value,
                        4)
                    : null,

            BodyFatPercent:
                normalizedBodyFatFraction.HasValue
                    ? Round(
                        normalizedBodyFatFraction.Value * 100m,
                        2)
                    : null,

            FatFreeMassLb:
                Round(fatFreeMassLb, 2),

            FatFreeMassKg:
                Round(fatFreeMassKg, 2),

            FatFreeMassFraction:
                Round(
                    fatFreeMassFraction,
                    4),

            BodyCompositionSourceCode:
                sourceCode,

            ConfidenceCode:
                confidenceCode,

            FormulaCode:
                formulaCode);
    }

    private static void ValidateBasicInput(
        Engine30BodyCompositionInput input)
    {
        if (input.AgeYears < 19)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.AgeYears),
                "Engine 3.0 currently supports adults age 19 and older.");
        }

        if (input.AgeYears > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.AgeYears),
                "Age is outside the supported range.");
        }

        if (input.HeightIn <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.HeightIn),
                "Height must be greater than zero.");
        }

        if (input.WeightLb <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.WeightLb),
                "Weight must be greater than zero.");
        }
    }

    private static void ValidateBodyFat(
        decimal bodyFatFraction)
    {
        /*
            These broad boundaries match the application's current
            accepted body-fat input range.

            They are validation boundaries, not recommendations.
        */
        if (bodyFatFraction < 0.02m ||
            bodyFatFraction > 0.70m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bodyFatFraction),
                "Body-fat fraction must be between 0.02 and 0.70.");
        }
    }

    private static void ValidateCalculatedFfm(
        decimal fatFreeMassKg,
        decimal totalWeightKg)
    {
        if (fatFreeMassKg <= 0m)
        {
            throw new InvalidOperationException(
                "Calculated fat-free mass must be greater than zero.");
        }

        if (fatFreeMassKg >= totalWeightKg)
        {
            throw new InvalidOperationException(
                "Calculated fat-free mass must be less than total body weight.");
        }
    }

    private static string NormalizeSex(
        string sexCode)
    {
        if (string.IsNullOrWhiteSpace(
            sexCode))
        {
            throw new ArgumentException(
                "Sex is required for anthropometric body-composition estimation.",
                nameof(sexCode));
        }

        return sexCode
            .Trim()
            .ToUpperInvariant() switch
        {
            "M" or "MALE" =>
                "MALE",

            "F" or "FEMALE" =>
                "FEMALE",

            _ =>
                throw new ArgumentException(
                    $"Unsupported sex code '{sexCode}'.",
                    nameof(sexCode))
        };
    }

    private static decimal Round(
        decimal value,
        int decimals)
        => Math.Round(
            value,
            decimals,
            MidpointRounding.AwayFromZero);
}
