using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin/engine")]
public sealed class AdminEngineController(
    RecompEngineConfigurationService engineConfiguration,
    ConfigurationService configuration,
    RecompTrainingPrescriptionService trainingPrescription) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? versionCode,
        CancellationToken cancellationToken)
    {
        var requestedVersion =
            string.IsNullOrWhiteSpace(versionCode)
                ? "CFG-1007.0"
                : versionCode.Trim();

        var snapshot =
            await engineConfiguration.LoadByVersionCodeAsync(
                requestedVersion,
                cancellationToken);

        var rows = snapshot.Parameters
            .Select(x => new AdminEngineParameterRow
            {
                Key = x.Definition.Key,
                Category = x.Definition.Category,
                DisplayName = x.Definition.DisplayName,
                Description = x.Definition.Description,

                CurrentValue = x.EffectiveValue,
                DefaultValue = x.Definition.DefaultValue,

                Unit = x.Definition.Unit,

                MinimumValue = x.Definition.MinimumValue,
                MaximumValue = x.Definition.MaximumValue,

                BasisType = x.Definition.BasisType,
                Rationale = x.Definition.Rationale,

                DefaultVersion = x.Definition.DefaultVersion,

                IntroducedEngineVersion =
                    x.Definition.IntroducedEngineVersion,

                EffectOnCalculation =
                    RecompEngineCalculationCatalog.ParameterEffect(
                        x.Definition.Category,
                        x.Definition.Key),

                Uses12WrDefault = x.Uses12WrDefault,

                IsModifiedFromDefault =
                    x.EffectiveValue != x.Definition.DefaultValue
            })
            .ToArray();

        var calculationGroups =
            RecompEngineCalculationCatalog.All
                .OrderBy(x => CalculationSortOrder(x.Category))
                .Select(x =>
                {
                    var isAcuteRecovery =
                        string.Equals(
                            x.Category,
                            "Model Calibration - Acute Recovery",
                            StringComparison.OrdinalIgnoreCase);

                    return new AdminEngineCalculationGroup
                    {
                        Category = x.Category,
                        Title = x.Title,
                        Summary = x.Summary,

                        FormulaLines = x.FormulaLines,
                        DependencyLines = x.DependencyLines,

                        IntegrationStatusCode =
                            isAcuteRecovery
                                ? "STAGED"
                                : "IMPLEMENTED",

                        IntegrationStatusLabel =
                            isAcuteRecovery
                                ? "Staged for Engine 2.0"
                                : "Calculation implemented",

                        IntegrationStatusDescription =
                            isAcuteRecovery
                                ? "This calculation is implemented and validated, but it is not yet allowed to modify the live workout recommendation."
                                : "This calculation is already part of the deterministic calculation model. CFG-1007.0 remains DRAFT until explicitly published and assigned."
                    };
                })
                .ToArray();

        var workoutTypes =
            await configuration.GetWorkoutTypeListAsync(
                snapshot.ConfigVersion.ConfigVersionId,
                cancellationToken);

        var trainingPreviews =
            workoutTypes
                .Select(workout =>
                {
                    var prescription =
                        trainingPrescription.BuildPlanned(
                            workout,
                            snapshot.Settings);

                    return new AdminTrainingPrescriptionPreviewRow
                    {
                        WorkoutTypeCode =
                            prescription.WorkoutTypeCode,

                        WorkoutName =
                            prescription.WorkoutName,

                        PrescriptionModeCode =
                            prescription.PrescriptionModeCode,

                        TrainingPurpose =
                            prescription.TrainingPurpose,

                        TargetArea =
                            prescription.TargetArea,

                        RecommendedMinutes =
                            prescription.RecommendedMinutes,

                        ApproximateWorkingSets =
                            prescription.ApproximateWorkingSets,

                        TargetRpeMinimum =
                            prescription.TargetRpeMinimum,

                        TargetRpeMaximum =
                            prescription.TargetRpeMaximum,

                        RpeScopeCode =
                            prescription.RpeScopeCode,

                        IntensityGuidance =
                            prescription.IntensityGuidance,

                        PhysiologicalPurpose =
                            prescription.PhysiologicalPurpose,

                        ExpectedTrainingCost =
                            prescription.ExpectedTrainingCost,

                        VolumeBasis =
                            prescription.VolumeBasis
                    };
                })
                .ToArray();

        return View(new AdminEngineViewModel
        {
            VersionCode =
                snapshot.ConfigVersion.VersionCode,

            StatusCode =
                snapshot.ConfigVersion.StatusCode,

            Description =
                snapshot.ConfigVersion.Description,

            PublishedAtUtc =
                snapshot.ConfigVersion.PublishedAtUtc,

            EffectiveFromUtc =
                snapshot.ConfigVersion.EffectiveFromUtc,

            IsValid =
                snapshot.IsValid,

            ValidationIssues =
                snapshot.ValidationIssues
                    .Select(x => x.Message)
                    .ToArray(),

            CalculationGroups =
                calculationGroups,

            TrainingPreviews =
                trainingPreviews,

            Parameters =
                rows
        });
    }

    private static int CalculationSortOrder(string category) =>
        category switch
        {
            "Model Calibration - Fuel Demand" => 10,
            "Model Calibration - Fuel Restoration" => 20,
            "Model Calibration - Workout Intensity" => 30,
            "Model Calibration - Workout Density" => 40,
            "Model Calibration - Recovery Load" => 50,
            "Model Calibration - Acute Recovery" => 60,
            "Model Calibration - Training Volume" => 70,
            "Model Calibration - Training Intensity" => 80,
            "Model Calibration - Aerobic Conditioning" => 90,
            _ => 999
        };
}
