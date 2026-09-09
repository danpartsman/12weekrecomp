using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record Engine21DailyMacroInput(
    decimal WeightLb,
    PlanDefinition Plan,
    WorkoutTypeDefinition Workout,
    decimal FuelDemandG,
    decimal FuelRestorationCreditG,
    decimal FuelAbsoluteRestorationRatio,
    short ModerateHardDays,
    short HardDays,
    decimal ProteinG,
    decimal FatG,
    decimal IntendedEnergyTargetKcal);

public sealed record Engine21DailyMacroResult(
    Engine21CarbRequirementResult CarbRequirement,
    Engine21MacroReconciliationResult Reconciliation);

public sealed class Engine21DailyMacroService(
    Engine21CarbRequirementService carbRequirement,
    Engine21MacroReconciliationService reconciliation)
{
    public Engine21DailyMacroResult Calculate(
        Engine21DailyMacroInput input,
        IReadOnlyDictionary<string, decimal> settings)
    {
        Validate(input);

        //
        // STEP 1:
        // Determine the physiological carbohydrate floor from
        // the existing Engine 2.0 glycogen/readiness machinery.
        //
        var carb =
            carbRequirement.Calculate(
                new Engine21CarbRequirementInput(
                    WeightLb:
                        input.WeightLb,

                    Plan:
                        input.Plan,

                    Workout:
                        input.Workout,

                    FuelDemandG:
                        input.FuelDemandG,

                    FuelRestorationCreditG:
                        input.FuelRestorationCreditG,

                    FuelAbsoluteRestorationRatio:
                        input.FuelAbsoluteRestorationRatio,

                    ModerateHardDays:
                        input.ModerateHardDays,

                    HardDays:
                        input.HardDays),
                settings);

        //
        // STEP 2:
        // Reconcile that physiological requirement against
        // stable protein, stable fat, and the Engine 2.1
        // intended daily energy allocation.
        //
        var reconciled =
            reconciliation.Calculate(
                new Engine21MacroReconciliationInput(
                    WeightLb:
                        input.WeightLb,

                    ProteinG:
                        input.ProteinG,

                    FatG:
                        input.FatG,

                    PhysiologicalCarbFloorG:
                        carb.PhysiologicalCarbFloorG,

                    IntendedEnergyTargetKcal:
                        input.IntendedEnergyTargetKcal),
                settings);

        return new Engine21DailyMacroResult(
            CarbRequirement:
                carb,

            Reconciliation:
                reconciled);
    }

    private static void Validate(
        Engine21DailyMacroInput input)
    {
        if (input.WeightLb <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.WeightLb));

        if (input.ProteinG < 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.ProteinG));

        if (input.FatG < 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.FatG));

        if (input.IntendedEnergyTargetKcal <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(input.IntendedEnergyTargetKcal));
    }
}
