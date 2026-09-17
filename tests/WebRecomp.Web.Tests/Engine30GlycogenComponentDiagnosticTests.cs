using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

/// <summary>
/// Engine 3.0 glycogen-component diagnostic.
///
/// Dormant during normal regression. The dedicated runner supplies:
///   ENGINE30_GLYCOGEN_DIAGNOSTIC_FIXTURE_PATH
///   ENGINE30_GLYCOGEN_DIAGNOSTIC_OUTPUT_DIR
///
/// The diagnostic calls the real runtime/profile path. For composite detail,
/// it invokes the production adapter's private ResolveCompositeSegment method
/// and then the real sequential glycogen calculator. For resistance detail,
/// it uses the production workload/demand calculators with the exact active
/// profile/runtime inputs and reflects the adapter's private constants.
/// No physiology coefficient is independently invented here.
/// </summary>
public sealed class Engine30GlycogenComponentDiagnosticTests
{
    private const decimal ReferenceWeightLb = 180m;
    private const decimal ReferenceBodyFatFraction = 0.20m;
    private const decimal ReferenceHeightIn = 69m;
    private const decimal SkeletalMuscleFractionOfFfm = 0.528m;
    private const decimal BaselineGlycogenGPerKgMuscle = 23m;

    private static readonly DateOnly ActionDate =
        new(2026, 9, 7);

    private static readonly DateOnly DateOfBirth =
        ActionDate.AddYears(-40);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

    [Fact]
    public async Task ActiveProfiles_WriteGlycogenComponentDiagnostic()
    {
        var fixturePath =
            Environment.GetEnvironmentVariable(
                "ENGINE30_GLYCOGEN_DIAGNOSTIC_FIXTURE_PATH");

        var outputDirectory =
            Environment.GetEnvironmentVariable(
                "ENGINE30_GLYCOGEN_DIAGNOSTIC_OUTPUT_DIR");

        if (string.IsNullOrWhiteSpace(fixturePath)
            || string.IsNullOrWhiteSpace(outputDirectory))
        {
            return;
        }

        Assert.True(
            File.Exists(fixturePath),
            $"Fixture not found: {fixturePath}");

        Directory.CreateDirectory(
            outputDirectory);

        var fixtures =
            JsonSerializer.Deserialize<List<WorkoutFixture>>(
                File.ReadAllText(fixturePath),
                JsonOptions)
            ?? throw new InvalidOperationException(
                "Workout fixtures could not be deserialized.");

        Assert.Equal(
            27,
            fixtures.Count);

        var provider =
            new FixtureProfileProvider(
                fixtures);

        var runtime =
            new Engine30WorkoutRuntimeService(
                provider);

        var baseLiving =
            new Engine30BaseLivingCalculator()
                .Calculate(
                    new Engine30BaseLivingInput(
                        ActionDate:
                            ActionDate,
                        DateOfBirth:
                            DateOfBirth,
                        SexCode:
                            "MALE",
                        HeightIn:
                            ReferenceHeightIn,
                        WeightLb:
                            ReferenceWeightLb));

        var body =
            new Engine30BodyCompositionEstimator()
                .Calculate(
                    new Engine30BodyCompositionInput(
                        AgeYears:
                            baseLiving.AgeYears,
                        SexCode:
                            "MALE",
                        HeightIn:
                            ReferenceHeightIn,
                        WeightLb:
                            ReferenceWeightLb,
                        BodyFatFraction:
                            ReferenceBodyFatFraction));

        var summaries =
            new List<SummaryRow>();

        var resistanceRows =
            new List<ResistanceRow>();

        var compositeRows =
            new List<CompositeRow>();

        var report =
            new StringBuilder();

        AppendHeader(
            report,
            baseLiving.BaseLivingEnergyKcal,
            body.FatFreeMassLb);

        foreach (var fixture in fixtures)
        {
            var profileResolution =
                await provider.ResolveAsync(
                    fixture.WorkoutTypeCode);

            var profile =
                profileResolution.Profile;

            var session =
                new Engine30WorkoutSessionInput(
                    WorkoutTypeCode:
                        fixture.WorkoutTypeCode,
                    PlannedDurationMinutes:
                        60m);

            var result =
                await runtime.CalculateAsync(
                    new Engine30WorkoutRuntimeRequest(
                        Session:
                            session,
                        BaseLivingEnergyKcal:
                            baseLiving.BaseLivingEnergyKcal,
                        WeightLb:
                            ReferenceWeightLb,
                        FatFreeMassLb:
                            body.FatFreeMassLb,
                        SkeletalMuscleFractionOfFfm:
                            SkeletalMuscleFractionOfFfm,
                        BaselineGlycogenGPerKgMuscle:
                            BaselineGlycogenGPerKgMuscle));

            summaries.Add(
                new SummaryRow(
                    WorkoutTypeCode:
                        fixture.WorkoutTypeCode,
                    Family:
                        profile.WorkoutFamilyCode,
                    ResolvedDurationMinutes:
                        result.ResolvedSession.AnalysisDuration.Minutes,
                    GrossWorkoutKcal:
                        result.Energy.GrossWorkoutEnergyKcal,
                    GlycogenModelCode:
                        result.Glycogen.ModelCode,
                    GlycogenDepletedG:
                        result.Glycogen.EstimatedGlycogenDepletedG,
                    CarbReloadG:
                        result.Glycogen.CarbohydrateReloadRequirementG,
                    GlycogenEnergyRatio:
                        result.Energy.GrossWorkoutEnergyKcal <= 0m
                            ? 0m
                            : result.Glycogen.EstimatedGlycogenDepletedG
                              * 4m
                              / result.Energy.GrossWorkoutEnergyKcal));

            if (profile.WorkoutFamilyCode.Equals(
                    Engine30WorkoutFamilyCodes.Resistance,
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendResistanceDetail(
                    report,
                    fixture.WorkoutTypeCode,
                    profile,
                    result,
                    body.FatFreeMassLb,
                    resistanceRows);
            }

            if (profile.WorkoutFamilyCode.Equals(
                    Engine30WorkoutFamilyCodes.Composite,
                    StringComparison.OrdinalIgnoreCase))
            {
                AppendCompositeDetail(
                    report,
                    fixture.WorkoutTypeCode,
                    profile,
                    result,
                    body.FatFreeMassLb,
                    compositeRows);
            }
        }

        AppendSummaryTable(
            report,
            summaries);

        AppendFocusedComparison(
            report,
            summaries);

        WriteOutputs(
            outputDirectory,
            summaries,
            resistanceRows,
            compositeRows,
            report);
    }

    private static void AppendHeader(
        StringBuilder report,
        decimal baseLivingKcal,
        decimal fatFreeMassLb)
    {
        report.AppendLine(
            "12 WEEK RECOMP - ENGINE 3.0 GLYCOGEN COMPONENT DIAGNOSTIC");
        report.AppendLine(
            "==============================================================================================================");
        report.AppendLine(
            $"Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine(
            "Configuration: CFG-3000.0 active workout profiles");
        report.AppendLine(
            "Reference subject: male, age 40, 69 in, 180 lb, 20% body fat");
        report.AppendLine(
            $"Base Living: {baseLivingKcal:0.##} kcal/day");
        report.AppendLine(
            $"Fat-free mass: {fatFreeMassLb:0.##} lb");
        report.AppendLine(
            $"Skeletal-muscle fraction of FFM: {SkeletalMuscleFractionOfFfm:0.###}");
        report.AppendLine(
            $"Baseline glycogen: {BaselineGlycogenGPerKgMuscle:0.##} g/kg muscle");
        report.AppendLine();
        report.AppendLine(
            "IMPORTANT: This is a diagnostic decomposition. It does not change or scientifically freeze any coefficient.");
        report.AppendLine();
    }

    private static void AppendSummaryTable(
        StringBuilder report,
        IReadOnlyList<SummaryRow> rows)
    {
        report.AppendLine();
        report.AppendLine(
            "==============================================================================================================");
        report.AppendLine(
            "ALL-WORKOUT GLYCOGEN SUMMARY");
        report.AppendLine(
            "GLY/E = glycogen grams * 4 / gross workout kcal. This is diagnostic only; glycogen grams are NOT an energy bucket.");
        report.AppendLine(
            "==============================================================================================================");
        report.AppendLine(
            $"{"WORKOUT",-25} {"FAMILY",-11} {"MIN",5} {"GROSS",7} {"GLY_G",7} {"LOAD_G",7} {"GLY/E",7} MODEL");
        report.AppendLine(
            new string('-', 104));

        foreach (var row in rows)
        {
            report.AppendLine(
                $"{row.WorkoutTypeCode,-25} " +
                $"{row.Family,-11} " +
                $"{row.ResolvedDurationMinutes,5:0} " +
                $"{row.GrossWorkoutKcal,7:0} " +
                $"{row.GlycogenDepletedG,7:0.##} " +
                $"{row.CarbReloadG,7:0.##} " +
                $"{row.GlycogenEnergyRatio,7:0.00} " +
                $"{row.GlycogenModelCode}");
        }
    }

    private static void AppendFocusedComparison(
        StringBuilder report,
        IReadOnlyList<SummaryRow> rows)
    {
        report.AppendLine();
        report.AppendLine(
            "==============================================================================================================");
        report.AppendLine(
            "FOCUSED CALIBRATION COMPARISON");
        report.AppendLine(
            "==============================================================================================================");

        foreach (var code in new[]
        {
            "ORANGETHEORY",
            "MAJOR_LEGS",
            "MAJOR_PUSH",
            "MAJOR_PULL",
            "FULL_BODY_STRENGTH",
            "ACCESSORY",
            "RESISTANCE_CIRCUIT"
        })
        {
            var row =
                rows.Single(
                    x => x.WorkoutTypeCode.Equals(
                        code,
                        StringComparison.OrdinalIgnoreCase));

            report.AppendLine(
                $"{code,-24} glycogen={row.GlycogenDepletedG,7:0.##} g | " +
                $"gross={row.GrossWorkoutKcal,7:0.##} kcal | " +
                $"GLY/E={row.GlycogenEnergyRatio,6:0.00}");
        }

        report.AppendLine();
        report.AppendLine(
            "Interpretation must come from the detailed region/segment inputs below; GLY/E alone is not a validity test.");
    }

    private static void AppendResistanceDetail(
        StringBuilder report,
        string workoutCode,
        Engine30WorkoutAdminProfile profile,
        Engine30WorkoutRuntimeResult runtime,
        decimal fatFreeMassLb,
        List<ResistanceRow> outputRows)
    {
        var segment =
            runtime.ResolvedSession.Segments.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"{workoutCode}: resistance session has no resolved segment.");

        var details =
            segment.Resistance;

        var duration =
            segment.ActualDurationMinutes
            ?? segment.PlannedDurationMinutes;

        var referenceDuration =
            profile.GlycogenReferenceDurationMinutes > 0m
                ? profile.GlycogenReferenceDurationMinutes
                : profile.DefaultDurationMinutes > 0m
                    ? profile.DefaultDurationMinutes
                    : 60m;

        var durationScale =
            referenceDuration > 0m
                ? duration / referenceDuration
                : 1m;

        var totalSetScale =
            details?.TotalHardSets is > 0m
            && profile.ResistanceReferenceHardSets > 0m
                ? details.TotalHardSets.Value
                  / profile.ResistanceReferenceHardSets
                : durationScale;

        var overrides =
            details?.MuscleGroups?
                .ToDictionary(
                    x => x.MuscleGroupCode.Trim().ToUpperInvariant(),
                    StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, Engine30WorkoutMuscleWork>(
                StringComparer.OrdinalIgnoreCase);

        var groups =
            profile.MuscleRegions
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.MuscleRegionCode)
                    && x.WholeBodyMuscleFraction > 0m
                    && x.RecruitmentFraction > 0m)
                .Select(region =>
                {
                    overrides.TryGetValue(
                        region.MuscleRegionCode,
                        out var overrideWork);

                    return new Engine30ResistanceMuscleGlycogenWorkInput(
                        MuscleGroupCode:
                            region.MuscleRegionCode,
                        WholeBodySkeletalMuscleFraction:
                            region.WholeBodyMuscleFraction,
                        RecruitmentFraction:
                            overrideWork?.RecruitmentFraction
                            ?? region.RecruitmentFraction,
                        HardSets:
                            overrideWork?.HardSets
                            ?? region.HardSets * totalSetScale,
                        PercentOneRepMax:
                            details?.AveragePercentOneRepMax);
                })
                .ToArray();

        var adapter =
            new Engine30WorkoutGlycogenAdapter();

        var workload =
            new Engine30ResistanceGlycogenWorkloadCalculator()
                .Calculate(
                    new Engine30ResistanceGlycogenWorkloadInput(
                        DurationMinutes:
                            duration,
                        TrainingStatusCode:
                            Engine30ResistanceGlycogenTrainingStatusCodes.Trained,
                        MuscleGroups:
                            groups),
                    new Engine30ResistanceGlycogenWorkloadConfig(
                        ReferenceDurationMinutes:
                            referenceDuration,
                        ReferenceHardSetsPerMuscleGroup:
                            PrivateDecimalConstant(
                                "ReferenceHardSetsPerMuscleGroup"),
                        ReferenceDepletionFraction:
                            profile.GlycogenReferenceDepletionFraction,
                        HardSetSensitivity:
                            PrivateDecimalConstant(
                                "ResistanceHardSetSensitivity"),
                        DurationSensitivity:
                            PrivateDecimalConstant(
                                "ResistanceDurationSensitivity"),
                        TrainedFactor:
                            1.00m,
                        UntrainedFactor:
                            1.10m,
                        ReferencePercentOneRepMax:
                            null,
                        PercentOneRepMaxSensitivity:
                            0m,
                        MinimumDepletionFraction:
                            profile.GlycogenMinimumDepletionFraction,
                        MaximumDepletionFraction:
                            profile.GlycogenMaximumDepletionFraction));

        var adapterInput =
            new Engine30WorkoutGlycogenAdapterInput(
                Session:
                    runtime.ResolvedSession,
                Profile:
                    profile,
                FatFreeMassLb:
                    fatFreeMassLb,
                SkeletalMuscleFractionOfFfm:
                    SkeletalMuscleFractionOfFfm,
                BaselineGlycogenGPerKgMuscle:
                    BaselineGlycogenGPerKgMuscle);

        var demand =
            InvokeCalculateDemand(
                adapter,
                adapterInput,
                workload.ResolvedMuscleGroups);

        Assert.InRange(
            Math.Abs(
                demand.EstimatedGlycogenDepletedG
                - runtime.Glycogen.EstimatedGlycogenDepletedG),
            0m,
            0.05m);

        report.AppendLine();
        report.AppendLine(
            "--------------------------------------------------------------------------------------------------------------");
        report.AppendLine(
            $"RESISTANCE DETAIL - {workoutCode}");
        report.AppendLine(
            $"Duration {duration:0.##} min | Ref duration {referenceDuration:0.##} | " +
            $"Profile ref hard sets {profile.ResistanceReferenceHardSets:0.##} | " +
            $"Runtime total hard sets {(details?.TotalHardSets?.ToString("0.##", CultureInfo.InvariantCulture) ?? "-")}");
        report.AppendLine(
            $"{"REGION",-14} {"MUSCLE_FX",9} {"RECRUIT",8} {"SETS",7} {"SET_RATIO",9} {"DUR_RATIO",9} {"WORK_FX",8} {"DEPLETE",8} {"RECR_KG",9} {"GLY_G",8}");
        report.AppendLine(
            new string('-', 108));

        foreach (var work in workload.MuscleResults)
        {
            var resolved =
                workload.ResolvedMuscleGroups.Single(
                    x => x.MuscleGroupCode.Equals(
                        work.MuscleGroupCode,
                        StringComparison.OrdinalIgnoreCase));

            var oneGroupDemand =
                InvokeCalculateDemand(
                    adapter,
                    adapterInput,
                    new[] { resolved });

            var demandMuscle =
                oneGroupDemand.MuscleGroups.Single();

            outputRows.Add(
                new ResistanceRow(
                    WorkoutTypeCode:
                        workoutCode,
                    MuscleGroupCode:
                        work.MuscleGroupCode,
                    WholeBodyMuscleFraction:
                        work.WholeBodySkeletalMuscleFraction,
                    RecruitmentFraction:
                        work.RecruitmentFraction,
                    HardSets:
                        work.HardSets,
                    HardSetRatio:
                        work.HardSetRatio,
                    DurationRatio:
                        work.DurationRatio,
                    BaseWorkloadFactor:
                        work.BaseWorkloadFactor,
                    TrainingStatusFactor:
                        work.TrainingStatusFactor,
                    ResolvedDepletionFraction:
                        work.ResolvedDepletionFraction,
                    EffectiveRecruitedMuscleMassKg:
                        demandMuscle.EffectiveRecruitedMuscleMassKg,
                    GlycogenContributionG:
                        oneGroupDemand.EstimatedGlycogenDepletedG));

            report.AppendLine(
                $"{work.MuscleGroupCode,-14} " +
                $"{work.WholeBodySkeletalMuscleFraction,9:0.000} " +
                $"{work.RecruitmentFraction,8:0.000} " +
                $"{work.HardSets,7:0.##} " +
                $"{work.HardSetRatio,9:0.000} " +
                $"{work.DurationRatio,9:0.000} " +
                $"{work.BaseWorkloadFactor,8:0.000} " +
                $"{work.ResolvedDepletionFraction,8:0.0000} " +
                $"{demandMuscle.EffectiveRecruitedMuscleMassKg,9:0.00} " +
                $"{oneGroupDemand.EstimatedGlycogenDepletedG,8:0.##}");
        }

        report.AppendLine(
            $"TOTAL {workoutCode}: {demand.EstimatedGlycogenDepletedG:0.##} g glycogen");
    }

    private static void AppendCompositeDetail(
        StringBuilder report,
        string workoutCode,
        Engine30WorkoutAdminProfile profile,
        Engine30WorkoutRuntimeResult runtime,
        decimal fatFreeMassLb,
        List<CompositeRow> outputRows)
    {
        var adapter =
            new Engine30WorkoutGlycogenAdapter();

        var adapterInput =
            new Engine30WorkoutGlycogenAdapterInput(
                Session:
                    runtime.ResolvedSession,
                Profile:
                    profile,
                FatFreeMassLb:
                    fatFreeMassLb,
                SkeletalMuscleFractionOfFfm:
                    SkeletalMuscleFractionOfFfm,
                BaselineGlycogenGPerKgMuscle:
                    BaselineGlycogenGPerKgMuscle);

        var resolveMethod =
            typeof(Engine30WorkoutGlycogenAdapter)
                .GetMethod(
                    "ResolveCompositeSegment",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "ResolveCompositeSegment was not found.");

        var runtimeByCode =
            runtime.ResolvedSession.Segments
                .ToDictionary(
                    x => x.SegmentCode.Trim().ToUpperInvariant(),
                    StringComparer.OrdinalIgnoreCase);

        var configuredSegments =
            profile.Segments
                .Where(x => x.Enabled && x.DurationMinutes > 0m)
                .OrderBy(x => x.DisplayOrder)
                .ToArray();

        var sequentialInputs =
            new List<Engine30CompositeGlycogenSegmentInput>();

        var segmentMeta =
            new Dictionary<string, SegmentMeta>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var configured in configuredSegments)
        {
            var code =
                configured.SegmentCode.Trim().ToUpperInvariant();

            var segment =
                runtimeByCode[code];

            var resolvedGroups =
                (IReadOnlyCollection<Engine30MuscleGlycogenInput>)(
                    resolveMethod.Invoke(
                        adapter,
                        new object[]
                        {
                            adapterInput,
                            configured,
                            segment
                        })
                    ?? throw new InvalidOperationException(
                        $"{workoutCode}/{code}: ResolveCompositeSegment returned null."));

            sequentialInputs.Add(
                new Engine30CompositeGlycogenSegmentInput(
                    SegmentCode:
                        configured.SegmentCode,
                    MuscleGroups:
                        resolvedGroups));

            segmentMeta[code] =
                new SegmentMeta(
                    ModelCode:
                        configured.GlycogenModelCode ?? "CONTINUOUS",
                    ModalityCode:
                        segment.ModalityCode,
                    DurationMinutes:
                        segment.ActualDurationMinutes
                        ?? segment.PlannedDurationMinutes,
                    Rpe:
                        segment.ActualRpe
                        ?? segment.PlannedRpeOverride
                        ?? segment.DefaultRpe);
        }

        var sequential =
            new Engine30SequentialCompositeGlycogenCalculator()
                .Calculate(
                    new Engine30SequentialCompositeGlycogenInput(
                        FatFreeMassLb:
                            fatFreeMassLb,
                        SkeletalMuscleFractionOfFfm:
                            SkeletalMuscleFractionOfFfm,
                        BaselineGlycogenGPerKgMuscle:
                            BaselineGlycogenGPerKgMuscle,
                        TargetRestorationFraction:
                            profile.TargetRestorationFraction,
                        CarbohydratePerGlycogenG:
                            profile.CarbohydratePerGlycogenG,
                        Segments:
                            sequentialInputs));

        Assert.InRange(
            Math.Abs(
                sequential.EstimatedGlycogenDepletedG
                - runtime.Glycogen.EstimatedGlycogenDepletedG),
            0m,
            0.05m);

        report.AppendLine();
        report.AppendLine(
            "--------------------------------------------------------------------------------------------------------------");
        report.AppendLine(
            $"COMPOSITE DETAIL - {workoutCode}");
        report.AppendLine(
            $"{"SEGMENT",-12} {"MODEL",-11} {"MODALITY",-12} {"MIN",5} {"RPE",5} {"REGION",-12} {"MUSCLE_FX",9} {"RECRUIT",8} {"DEPLETE",8} {"BEFORE_G",9} {"RECR_AVAIL",10} {"SEG_G",8} {"AFTER_G",9}");
        report.AppendLine(
            new string('-', 139));

        foreach (var exposure in sequential.Exposures)
        {
            var meta =
                segmentMeta[
                    exposure.SegmentCode.Trim().ToUpperInvariant()];

            outputRows.Add(
                new CompositeRow(
                    WorkoutTypeCode:
                        workoutCode,
                    SegmentCode:
                        exposure.SegmentCode,
                    ModelCode:
                        meta.ModelCode,
                    ModalityCode:
                        meta.ModalityCode,
                    DurationMinutes:
                        meta.DurationMinutes,
                    Rpe:
                        meta.Rpe,
                    MuscleGroupCode:
                        exposure.MuscleGroupCode,
                    WholeBodyMuscleFraction:
                        exposure.WholeBodySkeletalMuscleFraction,
                    RecruitmentFraction:
                        exposure.RecruitmentFraction,
                    DepletionFraction:
                        exposure.DepletionFraction,
                    RegionRemainingBeforeG:
                        exposure.RegionRemainingBeforeG,
                    RecruitedAvailableGlycogenG:
                        exposure.RecruitedAvailableGlycogenG,
                    SegmentGlycogenDepletedG:
                        exposure.SegmentGlycogenDepletedG,
                    RegionRemainingAfterG:
                        exposure.RegionRemainingAfterG));

            report.AppendLine(
                $"{exposure.SegmentCode,-12} " +
                $"{meta.ModelCode,-11} " +
                $"{meta.ModalityCode,-12} " +
                $"{meta.DurationMinutes,5:0.##} " +
                $"{meta.Rpe,5:0.0} " +
                $"{exposure.MuscleGroupCode,-12} " +
                $"{exposure.WholeBodySkeletalMuscleFraction,9:0.000} " +
                $"{exposure.RecruitmentFraction,8:0.000} " +
                $"{exposure.DepletionFraction,8:0.0000} " +
                $"{exposure.RegionRemainingBeforeG,9:0.##} " +
                $"{exposure.RecruitedAvailableGlycogenG,10:0.##} " +
                $"{exposure.SegmentGlycogenDepletedG,8:0.##} " +
                $"{exposure.RegionRemainingAfterG,9:0.##}");
        }

        report.AppendLine(
            $"TOTAL {workoutCode}: {sequential.EstimatedGlycogenDepletedG:0.##} g glycogen");

        foreach (var region in sequential.Regions)
        {
            report.AppendLine(
                $"  REGION {region.MuscleGroupCode}: baseline {region.BaselineGlycogenG:0.##} g, " +
                $"depleted {region.TotalDepletedG:0.##} g, remaining {region.RemainingGlycogenG:0.##} g " +
                $"({region.RemainingFraction:P1})");
        }
    }

    private static Engine30GlycogenDemandResult InvokeCalculateDemand(
        Engine30WorkoutGlycogenAdapter adapter,
        Engine30WorkoutGlycogenAdapterInput input,
        IReadOnlyCollection<Engine30MuscleGlycogenInput> groups)
    {
        var method =
            typeof(Engine30WorkoutGlycogenAdapter)
                .GetMethod(
                    "CalculateDemand",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "CalculateDemand was not found.");

        return
            (Engine30GlycogenDemandResult)(
                method.Invoke(
                    adapter,
                    new object[]
                    {
                        input,
                        groups
                    })
                ?? throw new InvalidOperationException(
                    "CalculateDemand returned null."));
    }

    private static decimal PrivateDecimalConstant(
        string name)
    {
        var field =
            typeof(Engine30WorkoutGlycogenAdapter)
                .GetField(
                    name,
                    BindingFlags.NonPublic
                    | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"Private adapter constant '{name}' was not found.");

        var value =
            field.IsLiteral
                ? field.GetRawConstantValue()
                : field.GetValue(null);

        return Convert.ToDecimal(
            value,
            CultureInfo.InvariantCulture);
    }

    private static void WriteOutputs(
        string outputDirectory,
        IReadOnlyList<SummaryRow> summaries,
        IReadOnlyList<ResistanceRow> resistanceRows,
        IReadOnlyList<CompositeRow> compositeRows,
        StringBuilder report)
    {
        File.WriteAllText(
            Path.Combine(
                outputDirectory,
                "Engine30_Glycogen_Diagnostic_Report.txt"),
            report.ToString());

        var summaryCsv =
            new StringBuilder();

        summaryCsv.AppendLine(
            "WorkoutTypeCode,Family,ResolvedDurationMinutes,GrossWorkoutKcal,GlycogenModelCode,GlycogenDepletedG,CarbReloadG,GlycogenEnergyRatio");

        foreach (var row in summaries)
        {
            summaryCsv.AppendLine(
                string.Join(
                    ",",
                    Csv(row.WorkoutTypeCode),
                    Csv(row.Family),
                    Num(row.ResolvedDurationMinutes),
                    Num(row.GrossWorkoutKcal),
                    Csv(row.GlycogenModelCode),
                    Num(row.GlycogenDepletedG),
                    Num(row.CarbReloadG),
                    Num(row.GlycogenEnergyRatio)));
        }

        File.WriteAllText(
            Path.Combine(
                outputDirectory,
                "Engine30_Glycogen_AllWorkouts.csv"),
            summaryCsv.ToString());

        var resistanceCsv =
            new StringBuilder();

        resistanceCsv.AppendLine(
            "WorkoutTypeCode,MuscleGroupCode,WholeBodyMuscleFraction,RecruitmentFraction,HardSets,HardSetRatio,DurationRatio,BaseWorkloadFactor,TrainingStatusFactor,ResolvedDepletionFraction,EffectiveRecruitedMuscleMassKg,GlycogenContributionG");

        foreach (var row in resistanceRows)
        {
            resistanceCsv.AppendLine(
                string.Join(
                    ",",
                    Csv(row.WorkoutTypeCode),
                    Csv(row.MuscleGroupCode),
                    Num(row.WholeBodyMuscleFraction),
                    Num(row.RecruitmentFraction),
                    Num(row.HardSets),
                    Num(row.HardSetRatio),
                    Num(row.DurationRatio),
                    Num(row.BaseWorkloadFactor),
                    Num(row.TrainingStatusFactor),
                    Num(row.ResolvedDepletionFraction),
                    Num(row.EffectiveRecruitedMuscleMassKg),
                    Num(row.GlycogenContributionG)));
        }

        File.WriteAllText(
            Path.Combine(
                outputDirectory,
                "Engine30_Glycogen_Resistance_Detail.csv"),
            resistanceCsv.ToString());

        var compositeCsv =
            new StringBuilder();

        compositeCsv.AppendLine(
            "WorkoutTypeCode,SegmentCode,ModelCode,ModalityCode,DurationMinutes,RPE,MuscleGroupCode,WholeBodyMuscleFraction,RecruitmentFraction,DepletionFraction,RegionRemainingBeforeG,RecruitedAvailableGlycogenG,SegmentGlycogenDepletedG,RegionRemainingAfterG");

        foreach (var row in compositeRows)
        {
            compositeCsv.AppendLine(
                string.Join(
                    ",",
                    Csv(row.WorkoutTypeCode),
                    Csv(row.SegmentCode),
                    Csv(row.ModelCode),
                    Csv(row.ModalityCode),
                    Num(row.DurationMinutes),
                    Num(row.Rpe),
                    Csv(row.MuscleGroupCode),
                    Num(row.WholeBodyMuscleFraction),
                    Num(row.RecruitmentFraction),
                    Num(row.DepletionFraction),
                    Num(row.RegionRemainingBeforeG),
                    Num(row.RecruitedAvailableGlycogenG),
                    Num(row.SegmentGlycogenDepletedG),
                    Num(row.RegionRemainingAfterG)));
        }

        File.WriteAllText(
            Path.Combine(
                outputDirectory,
                "Engine30_Glycogen_Composite_Detail.csv"),
            compositeCsv.ToString());
    }

    private static string Num(decimal value)
        => value.ToString(
            "0.####",
            CultureInfo.InvariantCulture);

    private static string Num(decimal? value)
        => value.HasValue
            ? Num(value.Value)
            : "";

    private static string Csv(string? value)
    {
        value ??= "";

        if (value.Contains('"'))
            value = value.Replace("\"", "\"\"");

        return value.IndexOfAny(
                new[] { ',', '"', '\r', '\n' }) >= 0
            ? $"\"{value}\""
            : value;
    }

    private sealed record SummaryRow(
        string WorkoutTypeCode,
        string Family,
        decimal ResolvedDurationMinutes,
        decimal GrossWorkoutKcal,
        string GlycogenModelCode,
        decimal GlycogenDepletedG,
        decimal CarbReloadG,
        decimal GlycogenEnergyRatio);

    private sealed record ResistanceRow(
        string WorkoutTypeCode,
        string MuscleGroupCode,
        decimal WholeBodyMuscleFraction,
        decimal RecruitmentFraction,
        decimal? HardSets,
        decimal HardSetRatio,
        decimal DurationRatio,
        decimal BaseWorkloadFactor,
        decimal TrainingStatusFactor,
        decimal ResolvedDepletionFraction,
        decimal EffectiveRecruitedMuscleMassKg,
        decimal GlycogenContributionG);

    private sealed record CompositeRow(
        string WorkoutTypeCode,
        string SegmentCode,
        string ModelCode,
        string ModalityCode,
        decimal DurationMinutes,
        decimal Rpe,
        string MuscleGroupCode,
        decimal WholeBodyMuscleFraction,
        decimal RecruitmentFraction,
        decimal DepletionFraction,
        decimal RegionRemainingBeforeG,
        decimal RecruitedAvailableGlycogenG,
        decimal SegmentGlycogenDepletedG,
        decimal RegionRemainingAfterG);

    private sealed record SegmentMeta(
        string ModelCode,
        string ModalityCode,
        decimal DurationMinutes,
        decimal Rpe);

    private sealed record WorkoutFixture(
        string WorkoutTypeCode,
        string DisplayName,
        string WorkoutCategoryCode,
        decimal DefaultMinutes,
        int DefaultRestSec,
        string? ProfileVersionCode,
        int? ProfileVersionNumber,
        string? ProfileJsonBase64);

    private sealed class FixtureProfileProvider(
        IReadOnlyList<WorkoutFixture> fixtures)
        : IEngine30WorkoutRuntimeProfileProvider
    {
        private readonly IReadOnlyDictionary<string, WorkoutFixture> _fixtures =
            fixtures.ToDictionary(
                x => x.WorkoutTypeCode,
                StringComparer.OrdinalIgnoreCase);

        private readonly Engine30WorkoutProfileService _baselineBuilder =
            new(null!);

        private static readonly JsonSerializerOptions ProfileJsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };

        public Task<Engine30WorkoutRuntimeProfileResolution> ResolveAsync(
            string workoutTypeCode,
            CancellationToken cancellationToken = default)
        {
            if (!_fixtures.TryGetValue(
                    workoutTypeCode,
                    out var fixture))
            {
                throw new KeyNotFoundException(
                    $"Glycogen diagnostic fixture does not contain workout '{workoutTypeCode}'.");
            }

            Engine30WorkoutAdminProfile profile;
            string sourceCode;

            if (!string.IsNullOrWhiteSpace(
                    fixture.ProfileJsonBase64))
            {
                var json =
                    Encoding.Unicode.GetString(
                        Convert.FromBase64String(
                            fixture.ProfileJsonBase64));

                profile =
                    JsonSerializer.Deserialize<Engine30WorkoutAdminProfile>(
                        json,
                        ProfileJsonOptions)
                    ?? throw new InvalidOperationException(
                        $"Stored Admin profile for {fixture.WorkoutTypeCode} could not be deserialized.");

                sourceCode =
                    Engine30WorkoutRuntimeProfileSourceCodes.AdminActiveProfile;
            }
            else
            {
                var workout =
                    new WorkoutTypeDefinition
                    {
                        WorkoutTypeDefinitionId = Guid.NewGuid(),
                        ConfigVersionId = Guid.NewGuid(),
                        WorkoutTypeCode = fixture.WorkoutTypeCode,
                        DisplayName = fixture.DisplayName,
                        WorkoutCategoryCode = fixture.WorkoutCategoryCode,
                        WeeklyRoleCode = "OTHER_LOAD",
                        FuelPriorityCode = "AUTO",
                        SchedulePrincipleCode = "SCHEDULE_NORMALLY",
                        DefaultMinutes = fixture.DefaultMinutes,
                        DefaultRestSec = checked((short)fixture.DefaultRestSec),
                        IsSelectable = true,
                        IsActive = true
                    };

                profile =
                    _baselineBuilder.BuildBaselineProfile(
                        workout);

                sourceCode =
                    Engine30WorkoutRuntimeProfileSourceCodes.BaselineFallback;
            }

            Engine30WorkoutProfileService.Validate(
                profile);

            return Task.FromResult(
                new Engine30WorkoutRuntimeProfileResolution(
                    Profile:
                        profile,
                    Provenance:
                        new Engine30WorkoutRuntimeProfileProvenance(
                            ConfigVersionCode:
                                "CFG-3000.0",
                            WorkoutTypeDefinitionId:
                                Guid.NewGuid(),
                            WorkoutTypeCode:
                                fixture.WorkoutTypeCode,
                            WorkoutDisplayName:
                                fixture.DisplayName,
                            SourceCode:
                                sourceCode,
                            ProfileVersionId:
                                fixture.ProfileVersionNumber.HasValue
                                    ? Guid.NewGuid()
                                    : null,
                            ProfileVersionCode:
                                string.IsNullOrWhiteSpace(
                                    fixture.ProfileVersionCode)
                                        ? null
                                        : fixture.ProfileVersionCode,
                            ProfileVersionNumber:
                                fixture.ProfileVersionNumber,
                            IsBaseline:
                                !fixture.ProfileVersionNumber.HasValue
                                || fixture.ProfileVersionNumber.Value == 1,
                            ProfileCreatedAtUtc:
                                null)));
        }
    }
}