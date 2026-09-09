using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

[Authorize]
[ApiController]
[Route("engine21-diagnostic")]
public sealed class Engine21DiagnosticController(
    AppUserContextService userContext,
    IServiceProvider services) : ControllerBase
{
    [HttpGet("")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        try
        {
            var context =
                await userContext.GetAsync(
                    HttpContext,
                    cancellationToken);

            if (context.Client is null ||
                context.Program is null)
            {
                return Conflict(new
                {
                    error = "NO_ACTIVE_CLIENT_PROGRAM"
                });
            }

            var engine21 =
                services.GetRequiredService<Engine21RealDataService>();

            var actionDate =
                date ??
                AppUserContextService.GetClientToday(
                    context.Client);

            var result =
                await engine21.CalculateAsync(
                    context.Client,
                    context.Program,
                    actionDate,
                    cancellationToken);

            var reference = result.Pipeline.EnergyReference;
            var deficit = result.Pipeline.Deficit;
            var weekly = result.Pipeline.WeeklyBudget;
            var fat = result.Pipeline.FatTarget;

            var actionEnergyDay =
                weekly.Days.Single(
                    x => x.Date == result.ActionDate);

            var actionFuel =
                result.ActionDateFuelWindow;

            var actionMacro =
                result.ActionDateMacro;

            var actionCarb =
                actionMacro.CarbRequirement;

            var actionReconciliation =
                actionMacro.Reconciliation;

            return Ok(new
            {
                mode = "SHADOW_DIAGNOSTIC_ONLY",
                authoritative = false,

                configuration = new
                {
                    version = result.ConfigVersionCode,
                    actionDate = result.ActionDate,
                    weekStart = result.WeekStart
                },

                body = new
                {
                    weightLb = result.WeightLb,
                    weightMeasurementDate =
                        result.WeightMeasurementDate,
                    ageYears = reference.AgeYears,
                    heightCm = reference.HeightCm,
                    bmi = reference.Bmi
                },

                energyReference = new
                {
                    restingEnergyKcal =
                        reference.RestingEnergyKcal,

                    driInactiveTeeKcal =
                        reference.DriInactiveTeeKcal,

                    driLowActiveTeeKcal =
                        reference.DriLowActiveTeeKcal,

                    habitualStepsPerDay =
                        reference.HabitualStepsPerDay,

                    validRecentStepDays =
                        reference.ValidRecentStepDays,

                    stepSourceCode =
                        reference.StepSourceCode,

                    habitualStepBandCode =
                        reference.HabitualStepBandCode,

                    provisionalNonWorkoutEnergyKcal =
                        reference.ProvisionalNonWorkoutEnergyKcal,

                    nonWorkoutEnergyModelCode =
                        reference.NonWorkoutEnergyModelCode,

                    confidenceCode =
                        reference.ConfidenceCode
                },

                deficit = new
                {
                    deficit.PlanCode,
                    deficit.RequestedDeficitPct,
                    deficit.EffectiveDeficitPct,
                    deficit.TargetWeeklyDeficitKcal,
                    deficit.TargetAverageDailyDeficitKcal,
                    deficit.WeeklyPrescribedEnergyKcal,
                    deficit.ConfidenceCode,
                    deficit.ReasonCodes
                },

                weeklyEnergy = new
                {
                    weekly.WeeklyMaintenanceEnergyKcal,
                    weekly.WeeklyPrescribedEnergyKcal,
                    weekly.AveragePrescribedEnergyKcal,

                    days = weekly.Days.Select(x => new
                    {
                        x.Date,
                        category = x.WorkoutCategoryCode,
                        grossWorkoutKcal =
                            x.GrossWorkoutEnergyKcal,
                        netWorkoutKcal =
                            x.NetWorkoutEnergyKcal,
                        estimatedMaintenanceKcal =
                            x.EstimatedMaintenanceEnergyKcal,
                        x.AllocationWeight,
                        intendedEnergyTargetKcal =
                            x.IntendedEnergyTargetKcal
                    })
                },

                actionDateMacros = new
                {
                    date =
                        result.ActionDate,

                    workoutTypeCode =
                        result.ActionDateWorkoutTypeCode,

                    intendedEnergyTargetKcal =
                        actionEnergyDay.IntendedEnergyTargetKcal,

                    proteinG =
                        actionReconciliation.ProteinG,

                    fatG =
                        actionReconciliation.FatG,

                    fuelWindow = new
                    {
                        demandG =
                            actionFuel.DemandG,

                        restorationCreditG =
                            actionFuel.RestorationCreditG,

                        absoluteRestorationRatio =
                            actionFuel.AbsoluteRestorationRatio,

                        moderateHardDays =
                            actionFuel.ModerateHardDays,

                        hardDays =
                            actionFuel.HardDays
                    },

                    carbohydratePhysiology = new
                    {
                        plannedGlycogenBurnG =
                            actionCarb.PlannedGlycogenBurnG,

                        plannedWorkoutRestorationG =
                            actionCarb.PlannedWorkoutRestorationG,

                        baseWorkoutCarbsG =
                            actionCarb.BaseWorkoutCarbsG,

                        functionalTargetRatio =
                            actionCarb.FunctionalTargetRatio,

                        preRescueReadinessCode =
                            actionCarb.PreRescueReadinessCode,

                        recoveryShortfallG =
                            actionCarb.RecoveryShortfallG,

                        recoveryAllocationG =
                            actionCarb.RecoveryAllocationG,

                        preWorkoutG =
                            actionCarb.PreWorkoutG,

                        projectedReadinessCode =
                            actionCarb.ProjectedReadinessCode,

                        physiologicalCarbFloorG =
                            actionCarb.PhysiologicalCarbFloorG,

                        reasonCodes =
                            actionCarb.ReasonCodes
                    },

                    reconciliation = new
                    {
                        energyBalancedCarbsG =
                            actionReconciliation.EnergyBalancedCarbsG,

                        standardCarbUpperBoundaryG =
                            actionReconciliation.StandardCarbUpperBoundaryG,

                        recommendedCarbsG =
                            actionReconciliation.RecommendedCarbsG,

                        derivedCaloriesKcal =
                            actionReconciliation.DerivedCaloriesKcal,

                        energyVarianceKcal =
                            actionReconciliation.EnergyVarianceKcal,

                        governingRuleCode =
                            actionReconciliation.GoverningRuleCode,

                        reasonCodes =
                            actionReconciliation.ReasonCodes
                    }
                },

                projectedMacros =
                    result.ProjectedMacroDays.Select(x => new
                    {
                        date =
                            x.Date,

                        workoutTypeCode =
                            x.WorkoutTypeCode,

                        intendedEnergyTargetKcal =
                            x.IntendedEnergyTargetKcal,

                        incomingFuelWindow = new
                        {
                            demandG =
                                x.IncomingFuelWindow.DemandG,

                            restorationCreditG =
                                x.IncomingFuelWindow.RestorationCreditG,

                            absoluteRestorationRatio =
                                x.IncomingFuelWindow.AbsoluteRestorationRatio,

                            moderateHardDays =
                                x.IncomingFuelWindow.ModerateHardDays,

                            hardDays =
                                x.IncomingFuelWindow.HardDays
                        },

                        carbohydratePhysiology = new
                        {
                            plannedGlycogenBurnG =
                                x.Macro.CarbRequirement.PlannedGlycogenBurnG,

                            plannedWorkoutRestorationG =
                                x.Macro.CarbRequirement.PlannedWorkoutRestorationG,

                            baseWorkoutCarbsG =
                                x.Macro.CarbRequirement.BaseWorkoutCarbsG,

                            functionalTargetRatio =
                                x.Macro.CarbRequirement.FunctionalTargetRatio,

                            preRescueReadinessCode =
                                x.Macro.CarbRequirement.PreRescueReadinessCode,

                            recoveryShortfallG =
                                x.Macro.CarbRequirement.RecoveryShortfallG,

                            recoveryAllocationG =
                                x.Macro.CarbRequirement.RecoveryAllocationG,

                            preWorkoutG =
                                x.Macro.CarbRequirement.PreWorkoutG,

                            projectedReadinessCode =
                                x.Macro.CarbRequirement.ProjectedReadinessCode,

                            physiologicalCarbFloorG =
                                x.Macro.CarbRequirement.PhysiologicalCarbFloorG
                        },

                        macros = new
                        {
                            proteinG =
                                x.Macro.Reconciliation.ProteinG,

                            fatG =
                                x.Macro.Reconciliation.FatG,

                            energyBalancedCarbsG =
                                x.Macro.Reconciliation.EnergyBalancedCarbsG,

                            standardCarbUpperBoundaryG =
                                x.Macro.Reconciliation.StandardCarbUpperBoundaryG,

                            recommendedCarbsG =
                                x.Macro.Reconciliation.RecommendedCarbsG,

                            derivedCaloriesKcal =
                                x.Macro.Reconciliation.DerivedCaloriesKcal,

                            energyVarianceKcal =
                                x.Macro.Reconciliation.EnergyVarianceKcal,

                            governingRuleCode =
                                x.Macro.Reconciliation.GoverningRuleCode
                        },

                        projectedFuelDay = new
                        {
                            demandG =
                                x.ProjectedFuelDay.DemandG,

                            restorationCreditG =
                                x.ProjectedFuelDay.RestorationCreditG,

                            fuelScore =
                                x.ProjectedFuelDay.MaxFuelScore,

                            moderateHard =
                                x.ProjectedFuelDay.ModerateHard,

                            hard =
                                x.ProjectedFuelDay.Hard
                        },

                        outgoingFuelWindow = new
                        {
                            demandG =
                                x.OutgoingFuelWindow.DemandG,

                            restorationCreditG =
                                x.OutgoingFuelWindow.RestorationCreditG,

                            absoluteRestorationRatio =
                                x.OutgoingFuelWindow.AbsoluteRestorationRatio,

                            moderateHardDays =
                                x.OutgoingFuelWindow.ModerateHardDays,

                            hardDays =
                                x.OutgoingFuelWindow.HardDays
                        }
                    }),

                fat = new
                {
                    fat.AveragePrescribedEnergyKcal,
                    fat.FatFromEnergyG,
                    fat.FatFloorG,
                    fat.RecommendedFatG,
                    fat.GoverningRuleCode
                }
            });
        }
        catch (Exception ex)
        {
            // TEMPORARY v1008 TEST-ENVIRONMENT DIAGNOSTIC.
            // Remove detailed exception output before production.
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    error =
                        "ENGINE21_DIAGNOSTIC_FAILURE",

                    exceptionType =
                        ex.GetType().FullName,

                    message =
                        ex.Message,

                    innerExceptionType =
                        ex.InnerException?.GetType().FullName,

                    innerMessage =
                        ex.InnerException?.Message,

                    stackTrace =
                        ex.StackTrace
                });
        }
    }
}


