namespace WebRecomp.Web.Services.Engine30;

/// <summary>
/// Engine 3.0 goal-energy policy diagnostic layer.
///
/// The category energy policy is retained for comparison, reporting and
/// guardrail diagnostics, but it no longer creates calories by back-filling
/// carbohydrate. The authoritative prescription now comes from physiological
/// macros: stable protein, stable fat, background carbohydrate, and calculated
/// workout glycogen replacement.
///
/// This keeps goal/category policy observable without allowing it to overwrite
/// the glycogen model.
/// </summary>
public sealed class Engine30RecompEnergyPolicyAllocator
{
    public Engine30WeeklyPrescriptionResult Apply(
        Engine30WeeklyPrescriptionResult baseline,
        Engine30GoalEnergyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(policy);

        Engine30GoalEnergyPolicyService.Validate(policy);

        var normalizedPlan =
            NormalizePlan(baseline.PlanCode);

        if (!policy.PlanCode.Equals(
                normalizedPlan,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Energy policy '{policy.PlanCode}' does not match prescription plan '{normalizedPlan}'.");
        }

        if (baseline.Days.Count != 7)
        {
            throw new ArgumentException(
                "Engine 3.0 recomp policy allocation requires exactly seven prescription days.");
        }

        var reasons =
            baseline.ReasonCodes.ToList();

        reasons.Add(
            "ADMIN_GOAL_CATEGORY_ENERGY_POLICY_DIAGNOSTIC_ONLY");

        foreach (var day in baseline.Days)
        {
            var category =
                policy.Category(day.WorkoutCategoryCode);

            var minimum =
                day.MaintenanceEnergyKcal
                * category.MinimumMaintenanceFraction;

            var target =
                day.MaintenanceEnergyKcal
                * category.TargetMaintenanceFraction;

            var maximum =
                day.MaintenanceEnergyKcal
                * category.MaximumMaintenanceFraction;

            if (day.PrescribedEnergyKcal < minimum - 0.001m)
            {
                reasons.Add(
                    "PHYSIOLOGY_FIRST_BELOW_CATEGORY_MINIMUM_DIAGNOSTIC");
            }

            if (day.PrescribedEnergyKcal > maximum + 0.001m)
            {
                reasons.Add(
                    "PHYSIOLOGY_FIRST_ABOVE_CATEGORY_MAXIMUM_DIAGNOSTIC");
            }

            if (Math.Abs(day.PrescribedEnergyKcal - target) <= 10m)
            {
                reasons.Add(
                    "PHYSIOLOGY_FIRST_NEAR_CATEGORY_TARGET_DIAGNOSTIC");
            }
        }

        /*
         * Deliberately return the physiology-first macro prescription without
         * changing calories or carbohydrate. The plan/category energy policy
         * is informational unless a future explicit non-carbohydrate safety
         * mechanism is introduced.
         */
        return baseline with
        {
            PlanCode = normalizedPlan,
            ReasonCodes = reasons
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    public static string NormalizeCategory(
        string categoryCode)
        => (categoryCode ?? "")
            .Trim()
            .ToUpperInvariant() switch
        {
            Engine30PrescriptionCategoryCodes.MajorStrength
                => Engine30PrescriptionCategoryCodes.MajorStrength,

            Engine30PrescriptionCategoryCodes.AccessoryStrength
                => Engine30PrescriptionCategoryCodes.AccessoryStrength,

            Engine30PrescriptionCategoryCodes.ConditioningCardio
                => Engine30PrescriptionCategoryCodes.ConditioningCardio,

            Engine30PrescriptionCategoryCodes.RestRecoveryOther
                => Engine30PrescriptionCategoryCodes.RestRecoveryOther,

            _ => throw new ArgumentException(
                $"Unsupported Engine 3.0 prescription category '{categoryCode}'.")
        };

    public static string NormalizePlan(
        string planCode)
    {
        var normalized =
            (planCode ?? "")
                .Trim()
                .ToUpperInvariant()
                .Replace("-", "_")
                .Replace(" ", "_");

        return normalized switch
        {
            "P1" or "PLAN1" or "PLAN_1" => "PLAN_1",
            "P2" or "PLAN2" or "PLAN_2" => "PLAN_2",
            "P3" or "PLAN3" or "PLAN_3" => "PLAN_3",
            "P4" or "PLAN4" or "PLAN_4" => "PLAN_4",
            "P5" or "PLAN5" or "PLAN_5" => "PLAN_5",

            _ => throw new ArgumentException(
                $"Unsupported Engine 3.0 plan '{planCode}'.")
        };
    }
}
