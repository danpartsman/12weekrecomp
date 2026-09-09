namespace WebRecomp.Web.Services.Engine30;

public sealed record Engine30MuscleGlycogenInput(
    string MuscleGroupCode,
    decimal WholeBodySkeletalMuscleFraction,
    decimal RecruitmentFraction,
    decimal DepletionFraction);

public sealed record Engine30MuscleGlycogenResult(
    string MuscleGroupCode,
    decimal EstimatedMuscleMassKg,
    decimal EffectiveRecruitedMuscleMassKg,
    decimal EstimatedAvailableGlycogenG,
    decimal EstimatedGlycogenDepletedG,
    decimal TargetGlycogenRestorationG,
    decimal CarbohydrateReloadRequirementG);

public sealed record Engine30GlycogenDemandInput(
    decimal FatFreeMassLb,
    decimal SkeletalMuscleFractionOfFfm,
    decimal BaselineGlycogenGPerKgMuscle,
    decimal TargetRestorationFraction,
    decimal CarbohydratePerGlycogenG,
    IReadOnlyCollection<Engine30MuscleGlycogenInput> MuscleGroups);

public sealed record Engine30GlycogenDemandResult(
    decimal FatFreeMassLb,
    decimal FatFreeMassKg,
    decimal EstimatedWholeBodySkeletalMuscleMassKg,
    decimal EstimatedGlycogenDepletedG,
    decimal TargetGlycogenRestorationG,
    decimal CarbohydrateReloadRequirementG,
    decimal CarbohydrateReloadEnergyKcal,
    IReadOnlyList<Engine30MuscleGlycogenResult> MuscleGroups);

/// <summary>
/// Engine 3.0 glycogen-demand calculator.
/// This calculation is deliberately separate from workout calorie expenditure.
/// Muscle fractions, recruitment, depletion, and reload coefficients are
/// configuration-owned inputs. Reload kcal is diagnostic only and must not
/// be added to maintenance as another energy bucket.
/// </summary>
public sealed class Engine30GlycogenDemandCalculator
{
    private const decimal PoundsPerKg = 2.2046226218m;
    private const decimal KcalPerGramCarbohydrate = 4m;

    public Engine30GlycogenDemandResult Calculate(
        Engine30GlycogenDemandInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.MuscleGroups);
        Validate(input);

        var ffmKg = input.FatFreeMassLb / PoundsPerKg;
        var skeletalMuscleKg = ffmKg * input.SkeletalMuscleFractionOfFfm;

        var results = new List<Engine30MuscleGlycogenResult>();
        decimal depletedTotal = 0m;
        decimal restorationTotal = 0m;
        decimal carbTotal = 0m;

        foreach (var group in input.MuscleGroups)
        {
            ValidateGroup(group);

            var groupMuscleKg =
                skeletalMuscleKg * group.WholeBodySkeletalMuscleFraction;

            var recruitedMuscleKg =
                groupMuscleKg * group.RecruitmentFraction;

            var availableGlycogenG =
                recruitedMuscleKg * input.BaselineGlycogenGPerKgMuscle;

            var depletedG =
                availableGlycogenG * group.DepletionFraction;

            var restorationG =
                depletedG * input.TargetRestorationFraction;

            var carbReloadG =
                restorationG * input.CarbohydratePerGlycogenG;

            depletedTotal += depletedG;
            restorationTotal += restorationG;
            carbTotal += carbReloadG;

            results.Add(
                new Engine30MuscleGlycogenResult(
                    group.MuscleGroupCode.Trim().ToUpperInvariant(),
                    Round(groupMuscleKg),
                    Round(recruitedMuscleKg),
                    Round(availableGlycogenG),
                    Round(depletedG),
                    Round(restorationG),
                    Round(carbReloadG)));
        }

        return new Engine30GlycogenDemandResult(
            Round(input.FatFreeMassLb),
            Round(ffmKg),
            Round(skeletalMuscleKg),
            Round(depletedTotal),
            Round(restorationTotal),
            Round(carbTotal),
            Round(carbTotal * KcalPerGramCarbohydrate),
            results);
    }

    private static void Validate(Engine30GlycogenDemandInput input)
    {
        if (input.FatFreeMassLb <= 0m)
            throw new ArgumentOutOfRangeException(nameof(input.FatFreeMassLb));

        ValidateFraction(
            input.SkeletalMuscleFractionOfFfm,
            nameof(input.SkeletalMuscleFractionOfFfm),
            false);

        if (input.BaselineGlycogenGPerKgMuscle <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.BaselineGlycogenGPerKgMuscle));

        ValidateFraction(
            input.TargetRestorationFraction,
            nameof(input.TargetRestorationFraction),
            true);

        if (input.CarbohydratePerGlycogenG <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.CarbohydratePerGlycogenG));

        if (input.MuscleGroups.Sum(
                x => x.WholeBodySkeletalMuscleFraction) > 1m)
            throw new ArgumentException(
                "Muscle-group fractions cannot sum above 1.00.",
                nameof(input.MuscleGroups));
    }

    private static void ValidateGroup(Engine30MuscleGlycogenInput group)
    {
        if (string.IsNullOrWhiteSpace(group.MuscleGroupCode))
            throw new ArgumentException("Muscle-group code is required.");

        ValidateFraction(
            group.WholeBodySkeletalMuscleFraction,
            nameof(group.WholeBodySkeletalMuscleFraction),
            false);

        ValidateFraction(
            group.RecruitmentFraction,
            nameof(group.RecruitmentFraction),
            true);

        ValidateFraction(
            group.DepletionFraction,
            nameof(group.DepletionFraction),
            true);
    }

    private static void ValidateFraction(
        decimal value,
        string name,
        bool allowZero)
    {
        var minimum = allowZero ? 0m : 0.0000001m;

        if (value < minimum || value > 1m)
            throw new ArgumentOutOfRangeException(
                name,
                "Fraction must be between 0 and 1.");
    }

    private static decimal Round(decimal value, int decimals = 2)
        => Math.Round(
            value,
            decimals,
            MidpointRounding.AwayFromZero);
}
