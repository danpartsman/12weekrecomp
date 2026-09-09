namespace WebRecomp.Web.Services;

/// <summary>
/// Person-level inputs required by the current Engine 2.1 energy pipeline.
/// </summary>
public sealed record Engine21PersonEnergyInput(
    DateOnly ActionDate,
    DateOnly DateOfBirth,
    string SexCode,
    decimal HeightIn,
    decimal WeightLb,
    int? BaselineStepsDay,
    IReadOnlyCollection<int?> RecentDailySteps,
    string PlanCode);

/// <summary>
/// First integrated Engine 2.1 energy result.
///
/// Calculation sequence:
///
/// person/profile
/// -> population + habitual energy reference
/// -> weekly maintenance estimate
/// -> deficit selection
/// -> periodized weekly energy budget
/// -> average prescribed energy
/// -> stable dietary-fat target
///
/// Protein and carbohydrate remain separate physiological calculations.
/// </summary>
public sealed record Engine21EnergyPipelineResult(
    Engine21EnergyReference EnergyReference,
    Engine21DeficitResult Deficit,
    Engine21WeeklyEnergyBudget WeeklyBudget,
    Engine21FatTarget FatTarget);

public sealed class Engine21PipelineService(
    Engine21EnergyService energyService,
    Engine21WeeklyEnergyService weeklyEnergyService,
    Engine21DeficitService deficitService)
{
    public Engine21EnergyPipelineResult Calculate(
        Engine21PersonEnergyInput person,
        IReadOnlyCollection<Engine21PlannedEnergyDay> plannedDays,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var reference =
            energyService.CalculateReference(
                actionDate:
                    person.ActionDate,

                dateOfBirth:
                    person.DateOfBirth,

                sexCode:
                    person.SexCode,

                heightIn:
                    person.HeightIn,

                weightLb:
                    person.WeightLb,

                baselineStepsDay:
                    person.BaselineStepsDay,

                recentDailySteps:
                    person.RecentDailySteps);

        // First pass establishes weekly maintenance without imposing
        // an energy deficit. All maintenance arithmetic remains owned
        // by Engine21WeeklyEnergyService rather than being duplicated here.
        var maintenancePass =
            weeklyEnergyService.Calculate(
                provisionalNonWorkoutEnergyKcal:
                    reference.ProvisionalNonWorkoutEnergyKcal,

                restingEnergyKcal:
                    reference.RestingEnergyKcal,

                targetDeficitPct:
                    0m,

                plannedDays:
                    plannedDays,

                settings:
                    settings);

        var deficit =
            deficitService.Calculate(
                planCode:
                    person.PlanCode,

                weeklyMaintenanceEnergyKcal:
                    maintenancePass.WeeklyMaintenanceEnergyKcal,

                bmi:
                    reference.Bmi,

                settings:
                    settings);

        // Second pass distributes the approved weekly deficit using
        // the normalized Engine 2.1 category-allocation policy.
        var weeklyBudget =
            weeklyEnergyService.Calculate(
                provisionalNonWorkoutEnergyKcal:
                    reference.ProvisionalNonWorkoutEnergyKcal,

                restingEnergyKcal:
                    reference.RestingEnergyKcal,

                targetDeficitPct:
                    deficit.EffectiveDeficitPct,

                plannedDays:
                    plannedDays,

                settings:
                    settings);

        // Both services independently derive the same authoritative
        // weekly prescription. If future rule changes cause them to
        // disagree, fail loudly rather than hiding an energy mismatch.
        if (weeklyBudget.WeeklyPrescribedEnergyKcal
            != deficit.WeeklyPrescribedEnergyKcal)
        {
            throw new InvalidOperationException(
                $"Engine 2.1 weekly energy reconciliation failed. " +
                $"Deficit service produced {deficit.WeeklyPrescribedEnergyKcal} kcal " +
                $"but weekly allocation produced {weeklyBudget.WeeklyPrescribedEnergyKcal} kcal.");
        }

        var fat =
            energyService.CalculateFatTarget(
                currentWeightLb:
                    person.WeightLb,

                averagePrescribedEnergyKcal:
                    weeklyBudget.AveragePrescribedEnergyKcal,

                settings:
                    settings);

        return new Engine21EnergyPipelineResult(
            EnergyReference:
                reference,

            Deficit:
                deficit,

            WeeklyBudget:
                weeklyBudget,

            FatTarget:
                fat);
    }
}
