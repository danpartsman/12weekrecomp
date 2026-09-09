using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin/engine30-energy-policy")]
public sealed class AdminEngine30EnergyPolicyController(
    Engine30GoalEnergyPolicyService policies)
    : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var domain =
            await policies.LoadAllAsync(
                cancellationToken);

        var vm =
            new AdminEngine30EnergyPolicyPageViewModel
            {
                Plans =
                    domain
                        .Select(ToEditModel)
                        .ToArray()
            };

        return View(vm);
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        AdminEngine30EnergyPolicyEditModel model,
        CancellationToken cancellationToken)
    {
        try
        {
            var domain =
                ToDomain(model);

            await policies.SaveAsync(
                domain,
                cancellationToken);

            TempData["Engine30EnergyPolicySuccess"] =
                $"{domain.PlanDisplayName} energy policy saved.";
        }
        catch (Exception ex)
        {
            TempData["Engine30EnergyPolicyError"] =
                ex.Message;
        }

        return RedirectToAction(
            nameof(Index));
    }

    private static AdminEngine30EnergyPolicyEditModel ToEditModel(
        Engine30GoalEnergyPolicy policy)
    {
        var major =
            policy.Category(
                Engine30PrescriptionCategoryCodes.MajorStrength);

        var accessory =
            policy.Category(
                Engine30PrescriptionCategoryCodes.AccessoryStrength);

        var conditioning =
            policy.Category(
                Engine30PrescriptionCategoryCodes.ConditioningCardio);

        var rest =
            policy.Category(
                Engine30PrescriptionCategoryCodes.RestRecoveryOther);

        return new AdminEngine30EnergyPolicyEditModel
        {
            PlanCode =
                policy.PlanCode,

            PlanDisplayName =
                policy.PlanDisplayName,

            SurplusRecoveryPct =
                policy.SurplusRecoveryFraction * 100m,

            MajorTargetPct =
                major.TargetMaintenanceFraction * 100m,

            MajorMinimumPct =
                major.MinimumMaintenanceFraction * 100m,

            MajorMaximumPct =
                major.MaximumMaintenanceFraction * 100m,

            MajorPriority =
                major.ProtectionPriority,

            MajorCarryoverCapKcal =
                major.CarryoverCorrectionCapKcal,

            AccessoryTargetPct =
                accessory.TargetMaintenanceFraction * 100m,

            AccessoryMinimumPct =
                accessory.MinimumMaintenanceFraction * 100m,

            AccessoryMaximumPct =
                accessory.MaximumMaintenanceFraction * 100m,

            AccessoryPriority =
                accessory.ProtectionPriority,

            AccessoryCarryoverCapKcal =
                accessory.CarryoverCorrectionCapKcal,

            ConditioningTargetPct =
                conditioning.TargetMaintenanceFraction * 100m,

            ConditioningMinimumPct =
                conditioning.MinimumMaintenanceFraction * 100m,

            ConditioningMaximumPct =
                conditioning.MaximumMaintenanceFraction * 100m,

            ConditioningPriority =
                conditioning.ProtectionPriority,

            ConditioningCarryoverCapKcal =
                conditioning.CarryoverCorrectionCapKcal,

            RestTargetPct =
                rest.TargetMaintenanceFraction * 100m,

            RestMinimumPct =
                rest.MinimumMaintenanceFraction * 100m,

            RestMaximumPct =
                rest.MaximumMaintenanceFraction * 100m,

            RestPriority =
                rest.ProtectionPriority,

            RestCarryoverCapKcal =
                rest.CarryoverCorrectionCapKcal
        };
    }

    private static Engine30GoalEnergyPolicy ToDomain(
        AdminEngine30EnergyPolicyEditModel model)
    {
        var plan =
            Engine30RecompEnergyPolicyAllocator.NormalizePlan(
                model.PlanCode);

        var categories =
            new[]
            {
                Category(
                    Engine30PrescriptionCategoryCodes.MajorStrength,
                    model.MajorTargetPct,
                    model.MajorMinimumPct,
                    model.MajorMaximumPct,
                    model.MajorPriority,
                    model.MajorCarryoverCapKcal),

                Category(
                    Engine30PrescriptionCategoryCodes.AccessoryStrength,
                    model.AccessoryTargetPct,
                    model.AccessoryMinimumPct,
                    model.AccessoryMaximumPct,
                    model.AccessoryPriority,
                    model.AccessoryCarryoverCapKcal),

                Category(
                    Engine30PrescriptionCategoryCodes.ConditioningCardio,
                    model.ConditioningTargetPct,
                    model.ConditioningMinimumPct,
                    model.ConditioningMaximumPct,
                    model.ConditioningPriority,
                    model.ConditioningCarryoverCapKcal),

                Category(
                    Engine30PrescriptionCategoryCodes.RestRecoveryOther,
                    model.RestTargetPct,
                    model.RestMinimumPct,
                    model.RestMaximumPct,
                    model.RestPriority,
                    model.RestCarryoverCapKcal)
            }
            .ToDictionary(
                x => x.WorkoutCategoryCode,
                StringComparer.OrdinalIgnoreCase);

        var policy =
            new Engine30GoalEnergyPolicy(
                ConfigVersionCode:
                    Engine30GoalEnergyPolicyService.ConfigVersionCode,
                PlanCode:
                    plan,
                PlanDisplayName:
                    string.IsNullOrWhiteSpace(
                        model.PlanDisplayName)
                        ? plan
                        : model.PlanDisplayName.Trim(),
                SurplusRecoveryFraction:
                    model.SurplusRecoveryPct / 100m,
                Categories:
                    categories);

        Engine30GoalEnergyPolicyService.Validate(
            policy);

        return policy;
    }

    private static Engine30CategoryEnergyPolicy Category(
        string code,
        decimal targetPct,
        decimal minimumPct,
        decimal maximumPct,
        int priority,
        decimal carryoverCapKcal)
        => new(
            WorkoutCategoryCode:
                code,
            TargetMaintenanceFraction:
                targetPct / 100m,
            MinimumMaintenanceFraction:
                minimumPct / 100m,
            MaximumMaintenanceFraction:
                maximumPct / 100m,
            ProtectionPriority:
                priority,
            CarryoverCorrectionCapKcal:
                carryoverCapKcal);
}
