namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Recomp Engine 3.0
/// Bucket #1 - Base Living Energy
///
/// Estimates resting/base-living energy before:
/// - occupation
/// - habitual daily activity
/// - steps
/// - chores
/// - formal exercise
/// - goal deficit/surplus
///
/// Uses the Mifflin-St Jeor resting-energy equation.
///
/// Male:
///   REE = 10 * weightKg
///       + 6.25 * heightCm
///       - 5 * ageYears
///       + 5
///
/// Female:
///   REE = 10 * weightKg
///       + 6.25 * heightCm
///       - 5 * ageYears
///       - 161
///
/// IMPORTANT:
/// This is an estimated population resting-energy value.
/// It is not measured metabolism and is not TDEE.
/// No activity multiplier is permitted in this calculator.
/// </summary>
public sealed record Engine30BaseLivingInput(
    DateOnly ActionDate,
    DateOnly DateOfBirth,
    string SexCode,
    decimal HeightIn,
    decimal WeightLb);

public sealed record Engine30BaseLivingResult(
    int AgeYears,
    string SexCode,
    decimal HeightIn,
    decimal HeightCm,
    decimal WeightLb,
    decimal WeightKg,
    decimal BaseLivingEnergyKcal,
    string FormulaCode);

public sealed class Engine30BaseLivingCalculator
{
    private const decimal PoundsPerKg = 2.2046226218m;
    private const decimal CmPerInch = 2.54m;

    public Engine30BaseLivingResult Calculate(
        Engine30BaseLivingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.DateOfBirth > input.ActionDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.DateOfBirth),
                "Date of birth cannot be after the calculation date.");
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

        var ageYears =
            CalculateAge(
                input.DateOfBirth,
                input.ActionDate);

        if (ageYears < 19)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.DateOfBirth),
                "Recomp Engine 3.0 currently supports adults age 19 and older.");
        }

        var normalizedSex =
            NormalizeSex(input.SexCode);

        var sexConstant =
            normalizedSex switch
            {
                "MALE" => 5m,
                "FEMALE" => -161m,
                _ => throw new InvalidOperationException(
                    "Unexpected normalized sex code.")
            };

        var weightKg =
            input.WeightLb / PoundsPerKg;

        var heightCm =
            input.HeightIn * CmPerInch;

        var baseLivingEnergy =
            10m * weightKg
            + 6.25m * heightCm
            - 5m * ageYears
            + sexConstant;

        return new Engine30BaseLivingResult(
            AgeYears:
                ageYears,

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

            BaseLivingEnergyKcal:
                Round(baseLivingEnergy, 2),

            FormulaCode:
                "MIFFLIN_ST_JEOR_1990");
    }

    private static int CalculateAge(
        DateOnly dateOfBirth,
        DateOnly actionDate)
    {
        var age =
            actionDate.Year
            - dateOfBirth.Year;

        if (dateOfBirth.AddYears(age) > actionDate)
        {
            age--;
        }

        return age;
    }

    private static string NormalizeSex(
        string sexCode)
    {
        if (string.IsNullOrWhiteSpace(sexCode))
        {
            throw new ArgumentException(
                "Sex is required for the Mifflin-St Jeor equation.",
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
                    $"Unsupported sex code '{sexCode}'. " +
                    "The current Mifflin-St Jeor implementation requires MALE or FEMALE.",
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
