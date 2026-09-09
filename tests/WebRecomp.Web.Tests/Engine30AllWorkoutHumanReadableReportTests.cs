using System.Reflection;
using WebRecomp.Web.Services.Engine30;
using Xunit.Abstractions;

namespace WebRecomp.Web.Tests;

/// <summary>
/// Human-readable Engine 3.0 workout plausibility report.
///
/// IMPORTANT:
/// This is deliberately a VALIDATION REPORT rather than a coefficient-freeze
/// test. It exercises every current workout preset for the same standardized
/// person so humans can inspect relative energy and glycogen behavior.
///
/// A green test means:
/// - every current catalog preset was exercised,
/// - the calculation paths completed,
/// - outputs remained internally sane,
/// - several major directional guardrails held.
///
/// A green test does NOT mean every DRAFT physiological coefficient has been
/// scientifically frozen.
/// </summary>
public sealed class Engine30AllWorkoutHumanReadableReportTests
{
    // ---------------------------------------------------------------------
    // STANDARDIZED TEST SUBJECT
    // ---------------------------------------------------------------------
    //
    // User requested milestone subject: 210-lb man.
    //
    // The remaining attributes are explicit controlled test assumptions,
    // not population substitutions for missing real-user data.
    //
    private const decimal WeightLb = 210m;
    private const decimal HeightIn = 72m;
    private const int AgeYears = 40;
    private const decimal BodyFatFraction = 0.20m;
    private const decimal FatFreeMassLb = 168m;

    /*
        Controlled habitual-activity allowance for THIS comparison report.

        The purpose of this test is to isolate differences among workouts.
        Holding habitual activity constant ensures maintenance differences in
        the table come only from Bucket #3 workout activity.

        This is NOT a population-average recommendation.
    */
    private const decimal HabitualActivityKcal = 300m;

    /*
        Current DRAFT male skeletal-muscle / FFM anchor already used in the
        Engine 3.0 glycogen development tests.

        It remains calibration-owned and is NOT frozen by this report.
    */
    private const decimal SkeletalMuscleFractionOfFfm = 0.528m;
    private const decimal BaselineGlycogenGPerKgMuscle = 23m;

    private readonly Engine30WorkoutEnergyCalculator _energy = new();
    private readonly Engine30CardioGlycogenWorkloadCalculator _cardio = new();
    private readonly Engine30IntervalGlycogenWorkloadCalculator _interval = new();
    private readonly Engine30ResistanceGlycogenWorkloadCalculator _resistance = new();
    private readonly Engine30CardioGlycogenMuscleProfileResolver _profile = new();
    private readonly Engine30SequentialCompositeGlycogenCalculator _sequential = new();
    private readonly Engine30GlycogenDemandCalculator _demand = new();

    private readonly ITestOutputHelper _output;

    public Engine30AllWorkoutHumanReadableReportTests(
        ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Standardized210LbMale_EveryCurrentWorkoutPreset_PrintsHumanReadableReport()
    {
        var definitions =
            GetEveryCatalogDefinition();

        Assert.NotEmpty(
            definitions);

        var baseLiving =
            MifflinMale(
                weightLb:
                    WeightLb,
                heightIn:
                    HeightIn,
                ageYears:
                    AgeYears);

        var baseNoWorkoutMaintenance =
            baseLiving
            + HabitualActivityKcal;

        var rows =
            new List<WorkoutReportRow>();

        foreach (var definition in definitions)
        {
            var duration =
                TestDurationMinutes(
                    definition);

            var energy =
                CalculateWorkoutEnergy(
                    definition,
                    duration,
                    baseLiving);

            var glycogen =
                CalculateWorkoutGlycogen(
                    definition,
                    duration);

            rows.Add(
                new WorkoutReportRow(
                    Definition:
                        definition,

                    DurationMinutes:
                        duration,

                    BaseLivingKcal:
                        baseLiving,

                    HabitualActivityKcal:
                        HabitualActivityKcal,

                    GrossWorkoutKcal:
                        energy.GrossWorkoutKcal,

                    RestingDuringWorkoutKcal:
                        energy.RestingDuringWorkoutKcal,

                    NetWorkoutKcal:
                        energy.NetWorkoutKcal,

                    EstimatedMaintenanceKcal:
                        baseNoWorkoutMaintenance
                        + energy.NetWorkoutKcal,

                    GlycogenDepletedG:
                        glycogen.GlycogenDepletedG,

                    CarbReloadG:
                        glycogen.CarbReloadG,

                    ReloadDiagnosticKcal:
                        glycogen.ReloadDiagnosticKcal,

                    EnergyModel:
                        energy.ModelCode,

                    GlycogenModel:
                        glycogen.ModelCode,

                    MuscleRegions:
                        glycogen.MuscleRegions,

                    ScenarioNote:
                        ScenarioNote(
                            definition,
                            duration)));
        }

        // -----------------------------------------------------------------
        // COVERAGE + BASIC SANITY
        // -----------------------------------------------------------------

        Assert.Equal(
            definitions.Count,
            rows.Count);

        Assert.All(
            rows,
            row =>
            {
                Assert.True(
                    row.DurationMinutes >= 0m);

                Assert.True(
                    row.GrossWorkoutKcal >= 0m);

                Assert.True(
                    row.RestingDuringWorkoutKcal >= 0m);

                Assert.True(
                    row.NetWorkoutKcal >= 0m);

                Assert.True(
                    row.GlycogenDepletedG >= 0m);

                Assert.True(
                    row.CarbReloadG >= 0m);

                Assert.True(
                    row.ReloadDiagnosticKcal >= 0m);

                Assert.True(
                    row.EstimatedMaintenanceKcal
                    >= baseNoWorkoutMaintenance);
            });

        var rest =
            FindOptional(
                rows,
                "REST");

        if (rest is not null)
        {
            Assert.Equal(
                0m,
                rest.NetWorkoutKcal);

            Assert.Equal(
                0m,
                rest.GlycogenDepletedG);
        }

        /*
            Directional checks are intentionally sparse.

            This report is meant to expose questionable DRAFT coefficients
            to human inspection instead of hiding them behind dozens of
            brittle assertions.
        */
        var zone2 =
            FindOptional(
                rows,
                "ZONE2_CARDIO");

        var trueHiit =
            FindOptional(
                rows,
                "TRUE_HIIT");

        if (zone2 is not null &&
            trueHiit is not null)
        {
            Assert.True(
                trueHiit.GlycogenDepletedG
                > zone2.GlycogenDepletedG);
        }

        var legs =
            FindOptional(
                rows,
                "MAJOR_LEGS");

        var accessory =
            rows.FirstOrDefault(
                x =>
                    x.Definition.WorkoutFamilyCode
                        .Equals(
                            Engine30WorkoutFamilyCodes.Resistance,
                            StringComparison.OrdinalIgnoreCase)
                    &&
                    x.Definition.TrainingPatternCode
                        .Equals(
                            Engine30TrainingPatternCodes.ResistanceAccessory,
                            StringComparison.OrdinalIgnoreCase));

        if (legs is not null &&
            accessory is not null)
        {
            Assert.True(
                legs.GlycogenDepletedG
                > accessory.GlycogenDepletedG);
        }

        var otf =
            FindOptional(
                rows,
                "ORANGETHEORY");

        if (zone2 is not null &&
            otf is not null)
        {
            Assert.True(
                otf.GlycogenDepletedG
                > zone2.GlycogenDepletedG);
        }

        PrintReport(
            rows,
            baseLiving,
            baseNoWorkoutMaintenance);
    }

    // =====================================================================
    // REPORT OUTPUT
    // =====================================================================

    private void PrintReport(
        IReadOnlyList<WorkoutReportRow> rows,
        decimal baseLiving,
        decimal baseNoWorkoutMaintenance)
    {
        _output.WriteLine("");
        _output.WriteLine(
            "==============================================================================================================");
        _output.WriteLine(
            "ENGINE 3.0 - ALL WORKOUTS HUMAN-READABLE VALIDATION REPORT");
        _output.WriteLine(
            "STANDARDIZED SUBJECT: 210-LB MALE");
        _output.WriteLine(
            "==============================================================================================================");
        _output.WriteLine("");

        _output.WriteLine(
            "TEST SUBJECT");
        _output.WriteLine(
            "------------");
        _output.WriteLine(
            $"Sex:                         Male");
        _output.WriteLine(
            $"Age:                         {AgeYears} years");
        _output.WriteLine(
            $"Height:                      {HeightIn:0} in / 6 ft 0 in");
        _output.WriteLine(
            $"Weight:                      {WeightLb:0.0} lb");
        _output.WriteLine(
            $"Body fat assumption:         {BodyFatFraction * 100m:0.0}%");
        _output.WriteLine(
            $"Fat-free mass:               {FatFreeMassLb:0.0} lb");
        _output.WriteLine(
            $"Base Living (Mifflin):       {baseLiving:0.0} kcal/day");
        _output.WriteLine(
            $"Habitual Activity control:   {HabitualActivityKcal:0.0} kcal/day");
        _output.WriteLine(
            $"No-workout maintenance:      {baseNoWorkoutMaintenance:0.0} kcal/day");
        _output.WriteLine("");

        _output.WriteLine(
            "IMPORTANT: Habitual Activity is a CONTROLLED TEST CONSTANT in this report so only workout differences move maintenance.");
        _output.WriteLine(
            "IMPORTANT: DRAFT kcal/min, recruitment, depletion and anatomy anchors remain validation inputs, not frozen production coefficients.");
        _output.WriteLine(
            "IMPORTANT: Reload kcal is a MACRO DIAGNOSTIC only. It is NOT added to maintenance.");
        _output.WriteLine("");

        _output.WriteLine(
            "MASTER WORKOUT TABLE");
        _output.WriteLine(
            "--------------------");

        _output.WriteLine(
            new string('-', 282));

        _output.WriteLine(
            $"{"Workout",-28} " +
            $"{"Family",-11} " +
            $"{"Modality",-15} " +
            $"{"Pattern",-22} " +
            $"{"RPE",5} " +
            $"{"Min",5} " +
            $"{"Gross",8} " +
            $"{"Rest",7} " +
            $"{"NET",8} " +
            $"{"Maint",8} " +
            $"{"GlyG",8} " +
            $"{"ReloadG",8} " +
            $"{"METeq",6} " +
            $"{"Gly/E",7} " +
            $"{"Review",-15} " +
            $"{"Model",-22}");

        _output.WriteLine(
            new string('-', 282));

        foreach (var row in rows)
        {
            _output.WriteLine(
                $"{Short(row.Definition.WorkoutTypeCode, 28),-28} " +
                $"{Short(row.Definition.WorkoutFamilyCode, 11),-11} " +
                $"{Short(row.Definition.DefaultModalityCode, 15),-15} " +
                $"{Short(row.Definition.TrainingPatternCode, 22),-22} " +
                $"{row.Definition.DefaultRpe,5:0.0} " +
                $"{row.DurationMinutes,5:0} " +
                $"{row.GrossWorkoutKcal,8:0.0} " +
                $"{row.RestingDuringWorkoutKcal,7:0.0} " +
                $"{row.NetWorkoutKcal,8:0.0} " +
                $"{row.EstimatedMaintenanceKcal,8:0.0} " +
                $"{row.GlycogenDepletedG,8:0.0} " +
                $"{row.CarbReloadG,8:0.0} " +
                $"{ImpliedMetEquivalent(row),6:0.00} " +
                $"{ReloadRatioDisplay(row),7} " +
                $"{HumanReviewFlag(row, rows),-15} " +
                $"{Short(row.GlycogenModel, 22),-22}");
        }

        _output.WriteLine(
            new string('-', 282));
        _output.WriteLine("");

        _output.WriteLine(
            "DETAILED HUMAN-READABLE BREAKDOWN");
        _output.WriteLine(
            "---------------------------------");

        foreach (var row in rows)
        {
            _output.WriteLine("");
            _output.WriteLine(
                $"### {row.Definition.WorkoutTypeCode} - {row.Definition.DisplayName}");

            _output.WriteLine(
                $"Taxonomy: {row.Definition.WorkoutFamilyCode} / {row.Definition.DefaultModalityCode} / {row.Definition.TrainingPatternCode}");

            _output.WriteLine(
                $"Default planned RPE: {row.Definition.DefaultRpe:0.0}");

            _output.WriteLine(
                $"Test scenario: {row.ScenarioNote}");

            _output.WriteLine(
                $"Energy model: {row.EnergyModel}");

            _output.WriteLine(
                $"Energy: gross {row.GrossWorkoutKcal:0.0} - resting {row.RestingDuringWorkoutKcal:0.0} = NET {row.NetWorkoutKcal:0.0} kcal");

            _output.WriteLine(
                $"Estimated maintenance for this controlled day: {row.EstimatedMaintenanceKcal:0.0} kcal");

            _output.WriteLine(
                $"Glycogen model: {row.GlycogenModel}");

            _output.WriteLine(
                $"Muscle regions: {row.MuscleRegions}");

            _output.WriteLine(
                $"Estimated glycogen depleted: {row.GlycogenDepletedG:0.0} g");

            _output.WriteLine(
                $"Carbohydrate restoration: {row.CarbReloadG:0.0} g");

            _output.WriteLine(
                $"Reload diagnostic energy: {row.ReloadDiagnosticKcal:0.0} kcal (NOT an energy bucket)");

            _output.WriteLine(
                $"Implied MET-equivalent from gross workout energy: {ImpliedMetEquivalent(row):0.00}");

            _output.WriteLine(
                $"Reload diagnostic kcal / gross workout kcal: {ReloadRatioDisplay(row)}");

            var reviewFlag =
                HumanReviewFlag(
                    row,
                    rows);

            _output.WriteLine(
                $"Human review flag: {reviewFlag}");

            if (!reviewFlag.Equals(
                    "OK",
                    StringComparison.Ordinal))
            {
                _output.WriteLine(
                    $"Review reason: {HumanReviewReason(row, rows)}");
            }
        }

        _output.WriteLine("");
        _output.WriteLine(
            "ENERGY RANKING - HIGHEST NET WORKOUT ENERGY");
        _output.WriteLine(
            "--------------------------------------------");

        var energyRank = 1;

        foreach (var row in rows
                     .OrderByDescending(
                         x => x.NetWorkoutKcal))
        {
            _output.WriteLine(
                $"{energyRank,2}. {row.Definition.WorkoutTypeCode,-30} {row.NetWorkoutKcal,8:0.0} kcal net");
            energyRank++;
        }

        _output.WriteLine("");
        _output.WriteLine(
            "GLYCOGEN RANKING - HIGHEST ESTIMATED DEPLETION");
        _output.WriteLine(
            "------------------------------------------------");

        var glycogenRank = 1;

        foreach (var row in rows
                     .OrderByDescending(
                         x => x.GlycogenDepletedG))
        {
            _output.WriteLine(
                $"{glycogenRank,2}. {row.Definition.WorkoutTypeCode,-30} {row.GlycogenDepletedG,8:0.0} g");
            glycogenRank++;
        }

        _output.WriteLine("");
        _output.WriteLine(
            "HUMAN REVIEW REMINDER");
        _output.WriteLine(
            "---------------------");
        _output.WriteLine(
            "A passing test means the architecture ran coherently. Review the rankings and absolute values for physiological plausibility.");
        _output.WriteLine(
            "If a result looks wrong to a human, we change the DRAFT calibration/profile - we do not hide it with a passing assertion.");
        _output.WriteLine(
            "REVIEW_ENERGY = another preset with the same modality and duration has nearly identical gross kcal despite a materially different default RPE.");
        _output.WriteLine(
            "REVIEW_GLYCOGEN = reload diagnostic kcal exceeds gross workout kcal. This is a review trigger, NOT proof the glycogen result is impossible.");
        _output.WriteLine(
            "REVIEW_BOTH = both diagnostic triggers fired. These flags do not change calculations and are not pass/fail assertions.");
        _output.WriteLine("");
        _output.WriteLine(
            $"Catalog presets exercised: {rows.Count}");
    }

    // =====================================================================
    // CATALOG ENUMERATION
    // =====================================================================

    private static IReadOnlyList<Engine30WorkoutTypeDefinition> GetEveryCatalogDefinition()
    {
        /*
            Current catalog stores the authoritative preset definitions in a
            private static IReadOnlyList named "Definitions".

            Reflection is intentionally limited to this validation test so the
            report automatically includes every current preset without
            maintaining a second hard-coded workout list.
        */
        var field =
            typeof(Engine30WorkoutTypeCatalog)
                .GetField(
                    "Definitions",
                    BindingFlags.Static
                    | BindingFlags.NonPublic);

        if (field is null)
        {
            throw new InvalidOperationException(
                "Could not locate Engine30WorkoutTypeCatalog.Definitions for the all-workout validation report.");
        }

        var definitions =
            field.GetValue(
                null)
            as IReadOnlyList<Engine30WorkoutTypeDefinition>;

        return definitions
            ?? throw new InvalidOperationException(
                "Engine30WorkoutTypeCatalog.Definitions was not an IReadOnlyList<Engine30WorkoutTypeDefinition>.");
    }

    // =====================================================================
    // ENERGY
    // =====================================================================

    private EnergyReport CalculateWorkoutEnergy(
        Engine30WorkoutTypeDefinition definition,
        decimal durationMinutes,
        decimal baseLiving)
    {
        if (definition.WorkoutTypeCode
            .Equals(
                "REST",
                StringComparison.OrdinalIgnoreCase))
        {
            return new EnergyReport(
                GrossWorkoutKcal:
                    0m,

                RestingDuringWorkoutKcal:
                    0m,

                NetWorkoutKcal:
                    0m,

                ModelCode:
                    "REST / ZERO WORK");
        }

        if (definition.WorkoutFamilyCode
            .Equals(
                Engine30WorkoutFamilyCodes.Composite,
                StringComparison.OrdinalIgnoreCase))
        {
            return CalculateCompositeEnergy(
                definition,
                baseLiving);
        }

        var modality =
            definition.DefaultModalityCode;

        var rateAtReferencePersonAndDefaultRpe =
            ReferenceGrossRateAtDefaultRpe(
                definition);

        var normalizedRate =
            NormalizeRateForEnergyCalculator(
                rateAtReferencePersonAndDefaultRpe,
                definition.DefaultRpe);

        var useFfm =
            definition.WorkoutFamilyCode
                .Equals(
                    Engine30WorkoutFamilyCodes.Resistance,
                    StringComparison.OrdinalIgnoreCase);

        var result =
            _energy.Calculate(
                new Engine30WorkoutEnergyInput(
                    WorkoutTypeCode:
                        definition.WorkoutTypeCode,

                    BaseLivingEnergyKcal:
                        baseLiving,

                    WeightLb:
                        WeightLb,

                    DurationMinutes:
                        durationMinutes,

                    ReferenceWorkoutCaloriesPerMinute:
                        normalizedRate,

                    ReferenceWeightLb:
                        180m,

                    SessionRpe:
                        definition.DefaultRpe,

                    DefaultRestSeconds:
                        useFfm
                            ? 90m
                            : null,

                    ActualRestSeconds:
                        useFfm
                            ? 90m
                            : null,

                    ScalingBasisCode:
                        useFfm
                            ? "FAT_FREE_MASS"
                            : "BODY_WEIGHT",

                    FatFreeMassLb:
                        useFfm
                            ? FatFreeMassLb
                            : null,

                    ReferenceFatFreeMassLb:
                        useFfm
                            ? 130m
                            : null));

        return new EnergyReport(
            GrossWorkoutKcal:
                result.GrossWorkoutEnergyKcal,

            RestingDuringWorkoutKcal:
                result.RestingEnergyDuringWorkoutKcal,

            NetWorkoutKcal:
                result.NetWorkoutEnergyKcal,

            ModelCode:
                useFfm
                    ? "BUCKET3 / FFM"
                    : "BUCKET3 / BODY_WEIGHT");
    }

    private EnergyReport CalculateCompositeEnergy(
        Engine30WorkoutTypeDefinition definition,
        decimal baseLiving)
    {
        var code =
            definition.WorkoutTypeCode.ToUpperInvariant();

        if (code == "ORANGETHEORY")
        {
            var tread =
                SegmentEnergy(
                    workoutCode:
                        code,
                    modality:
                        Engine30WorkoutModalityCodes.Treadmill,
                    durationMinutes:
                        20m,
                    rpe:
                        7m,
                    grossRateAtReference:
                        7.5m,
                    baseLiving:
                        baseLiving,
                    useFfm:
                        false);

            var row =
                SegmentEnergy(
                    workoutCode:
                        code,
                    modality:
                        Engine30WorkoutModalityCodes.Rowing,
                    durationMinutes:
                        20m,
                    rpe:
                        7m,
                    grossRateAtReference:
                        7.0m,
                    baseLiving:
                        baseLiving,
                    useFfm:
                        false);

            var floor =
                SegmentEnergy(
                    workoutCode:
                        code,
                    modality:
                        Engine30WorkoutModalityCodes.Resistance,
                    durationMinutes:
                        20m,
                    rpe:
                        7m,
                    grossRateAtReference:
                        5.0m,
                    baseLiving:
                        baseLiving,
                    useFfm:
                        true);

            return SumEnergy(
                "COMPOSITE: TREAD+ROW+FLOOR",
                tread,
                row,
                floor);
        }

        /*
            Generic composite test profile for any other current composite
            preset. It is intentionally transparent in the human-readable
            scenario note.
        */
        var cardio =
            SegmentEnergy(
                workoutCode:
                    code,
                modality:
                    Engine30WorkoutModalityCodes.GeneralCardio,
                durationMinutes:
                    30m,
                rpe:
                    definition.DefaultRpe,
                grossRateAtReference:
                    6.5m,
                baseLiving:
                    baseLiving,
                useFfm:
                    false);

        var resistance =
            SegmentEnergy(
                workoutCode:
                    code,
                modality:
                    Engine30WorkoutModalityCodes.Resistance,
                durationMinutes:
                    30m,
                rpe:
                    definition.DefaultRpe,
                grossRateAtReference:
                    5.3m,
                baseLiving:
                    baseLiving,
                useFfm:
                    true);

        return SumEnergy(
            "COMPOSITE: CARDIO+RESISTANCE",
            cardio,
            resistance);
    }

    private EnergyReport SegmentEnergy(
        string workoutCode,
        string modality,
        decimal durationMinutes,
        decimal rpe,
        decimal grossRateAtReference,
        decimal baseLiving,
        bool useFfm)
    {
        var normalizedRate =
            NormalizeRateForEnergyCalculator(
                grossRateAtReference,
                rpe);

        var result =
            _energy.Calculate(
                new Engine30WorkoutEnergyInput(
                    WorkoutTypeCode:
                        workoutCode,

                    BaseLivingEnergyKcal:
                        baseLiving,

                    WeightLb:
                        WeightLb,

                    DurationMinutes:
                        durationMinutes,

                    ReferenceWorkoutCaloriesPerMinute:
                        normalizedRate,

                    ReferenceWeightLb:
                        180m,

                    SessionRpe:
                        rpe,

                    DefaultRestSeconds:
                        useFfm
                            ? 90m
                            : null,

                    ActualRestSeconds:
                        useFfm
                            ? 90m
                            : null,

                    ScalingBasisCode:
                        useFfm
                            ? "FAT_FREE_MASS"
                            : "BODY_WEIGHT",

                    FatFreeMassLb:
                        useFfm
                            ? FatFreeMassLb
                            : null,

                    ReferenceFatFreeMassLb:
                        useFfm
                            ? 130m
                            : null));

        return new EnergyReport(
            result.GrossWorkoutEnergyKcal,
            result.RestingEnergyDuringWorkoutKcal,
            result.NetWorkoutEnergyKcal,
            modality);
    }

    private static EnergyReport SumEnergy(
        string modelCode,
        params EnergyReport[] parts)
        => new(
            GrossWorkoutKcal:
                parts.Sum(
                    x => x.GrossWorkoutKcal),

            RestingDuringWorkoutKcal:
                parts.Sum(
                    x => x.RestingDuringWorkoutKcal),

            NetWorkoutKcal:
                parts.Sum(
                    x => x.NetWorkoutKcal),

            ModelCode:
                modelCode);

    private static decimal ReferenceGrossRateAtDefaultRpe(
        Engine30WorkoutTypeDefinition definition)
    {
        var modality =
            definition.DefaultModalityCode.ToUpperInvariant();

        decimal rate =
            modality switch
            {
                "NONE" =>
                    0m,

                "MOBILITY" =>
                    2.2m,

                "GENERAL_CARDIO" =>
                    5.5m,

                "WALKING" =>
                    3.8m,

                "RUNNING" =>
                    9.0m,

                "TREADMILL" =>
                    7.5m,

                "CYCLING" =>
                    6.0m,

                "ROWING" =>
                    7.0m,

                "ELLIPTICAL" =>
                    6.2m,

                "STAIR_CLIMBER" =>
                    8.0m,

                "SWIMMING" =>
                    7.5m,

                "RESISTANCE" =>
                    5.5m,

                "MIXED" =>
                    7.0m,

                _ =>
                    5.5m
            };

        if (definition.WorkoutFamilyCode
            .Equals(
                Engine30WorkoutFamilyCodes.Intervals,
                StringComparison.OrdinalIgnoreCase))
        {
            /*
                DRAFT structured-interval whole-session energy anchors.

                These are average session-energy anchors for the standardized
                60-sec work / 60-sec recovery validation protocol.

                They are NOT work-phase MET values and are NOT frozen
                production coefficients.

                Approximate external plausibility anchors:
                    Running intervals:     8.5 MET
                    Cycling intervals:     8.8 MET
                    Rowing intervals:      7.5 MET
                    Mixed conditioning:    7.0 MET
                    TRUE HIIT:             7.0 MET

                Rates below are those MET-equivalent values converted to
                gross kcal/min for the 180-lb Engine 3.0 reference person.
            */

            rate =
                modality switch
                {
                    "RUNNING" =>
                        11.5666m,

                    "CYCLING" =>
                        11.9748m,

                    "ROWING" =>
                        10.2058m,

                    "MIXED" =>
                        9.5254m,

                    "GENERAL_CARDIO" =>
                        9.5254m,

                    _ =>
                        rate
                };
        }

        if (definition.WorkoutFamilyCode
                .Equals(
                    Engine30WorkoutFamilyCodes.Resistance,
                    StringComparison.OrdinalIgnoreCase)
            &&
            definition.WorkoutTypeCode.Contains(
                "LEGS",
                StringComparison.OrdinalIgnoreCase))
        {
            rate *=
                1.10m;
        }

        if (definition.TrainingPatternCode
            .Equals(
                Engine30TrainingPatternCodes.ResistanceAccessory,
                StringComparison.OrdinalIgnoreCase))
        {
            rate *=
                0.88m;
        }

        return rate;
    }

    private static decimal NormalizeRateForEnergyCalculator(
        decimal desiredRateAtReferenceRpe,
        decimal referenceRpe)
    {
        if (desiredRateAtReferenceRpe == 0m)
        {
            return 0m;
        }

        var intensityFactor =
            Math.Clamp(
                0.70m
                + referenceRpe * 0.04m,
                0.70m,
                1.20m);

        return desiredRateAtReferenceRpe
               / intensityFactor;
    }

    // =====================================================================
    // GLYCOGEN
    // =====================================================================

    private GlycogenReport CalculateWorkoutGlycogen(
        Engine30WorkoutTypeDefinition definition,
        decimal durationMinutes)
    {
        if (definition.WorkoutTypeCode
                .Equals(
                    "REST",
                    StringComparison.OrdinalIgnoreCase)
            ||
            definition.WorkoutFamilyCode
                .Equals(
                    Engine30WorkoutFamilyCodes.Recovery,
                    StringComparison.OrdinalIgnoreCase))
        {
            return new GlycogenReport(
                0m,
                0m,
                0m,
                "RECOVERY / ZERO",
                "none");
        }

        if (definition.WorkoutFamilyCode
            .Equals(
                Engine30WorkoutFamilyCodes.Resistance,
                StringComparison.OrdinalIgnoreCase))
        {
            return ResistanceGlycogen(
                definition,
                durationMinutes);
        }

        if (definition.WorkoutFamilyCode
            .Equals(
                Engine30WorkoutFamilyCodes.Cardio,
                StringComparison.OrdinalIgnoreCase))
        {
            return ContinuousCardioGlycogen(
                definition,
                durationMinutes);
        }

        if (definition.WorkoutFamilyCode
            .Equals(
                Engine30WorkoutFamilyCodes.Intervals,
                StringComparison.OrdinalIgnoreCase))
        {
            return IntervalGlycogen(
                definition,
                durationMinutes);
        }

        if (definition.WorkoutFamilyCode
            .Equals(
                Engine30WorkoutFamilyCodes.Composite,
                StringComparison.OrdinalIgnoreCase))
        {
            return CompositeGlycogen(
                definition);
        }

        throw new InvalidOperationException(
            $"Unsupported workout family '{definition.WorkoutFamilyCode}' in human-readable report.");
    }

    private GlycogenReport ContinuousCardioGlycogen(
        Engine30WorkoutTypeDefinition definition,
        decimal durationMinutes)
    {
        var depletion =
            CardioReferenceDepletion(
                definition);

        var workload =
            _cardio.Calculate(
                new Engine30CardioGlycogenWorkloadInput(
                    ModalityCode:
                        definition.DefaultModalityCode,

                    DurationMinutes:
                        durationMinutes,

                    SessionRpe:
                        definition.DefaultRpe),

                new Engine30CardioGlycogenWorkloadConfig(
                    ReferenceDurationMinutes:
                        durationMinutes,

                    ReferenceDepletionFraction:
                        depletion,

                    ReferenceRpe:
                        Math.Max(
                            1m,
                            definition.DefaultRpe),

                    ReferenceObjectiveRelativeIntensity:
                        null,

                    ReferenceHeartRateZoneIndex:
                        null,

                    IntensitySensitivity:
                        0.50m,

                    Zone1Weight:
                        1m,

                    Zone2Weight:
                        2m,

                    Zone3Weight:
                        3m,

                    Zone4Weight:
                        4m,

                    Zone5Weight:
                        5m,

                    MinimumDepletionFraction:
                        0.01m,

                    MaximumDepletionFraction:
                        0.70m));

        var modalityProfile =
            BuildModalityProfile(
                definition.DefaultModalityCode,
                "REPORT_CARDIO");

        var resolved =
            _profile.Resolve(
                new Engine30GlycogenMuscleProfileExposure(
                    ModalityCode:
                        definition.DefaultModalityCode,

                    DurationMinutes:
                        durationMinutes,

                    ResolvedDepletionFraction:
                        workload.ResolvedDepletionFraction),

                modalityProfile);

        var demand =
            Demand(
                resolved.ResolvedMuscleGroups);

        return FromDemand(
            demand,
            $"CONTINUOUS/{workload.IntensitySourceCode}",
            resolved.ResolvedMuscleGroups);
    }

    private GlycogenReport IntervalGlycogen(
        Engine30WorkoutTypeDefinition definition,
        decimal durationMinutes)
    {
        /*
            Standardized interval report protocol:
            equal work/recovery periods filling the requested test duration.
        */
        var repetitions =
            Math.Max(
                1,
                (int)Math.Round(
                    durationMinutes / 2m,
                    0,
                    MidpointRounding.AwayFromZero));

        var recoveryPeriods =
            repetitions;

        var depletion =
            definition.TrainingPatternCode
                .Equals(
                    Engine30TrainingPatternCodes.Hiit,
                    StringComparison.OrdinalIgnoreCase)
                ? 0.30m
                : 0.22m;

        var workRpe =
            Math.Max(
                1m,
                definition.DefaultRpe);

        var recoveryRpe =
            3m;

        var workload =
            _interval.Calculate(
                new Engine30IntervalGlycogenWorkloadInput(
                    ModalityCode:
                        definition.DefaultModalityCode,

                    WorkIntervalSeconds:
                        60m,

                    RecoveryIntervalSeconds:
                        60m,

                    Repetitions:
                        repetitions,

                    RecoveryPeriods:
                        recoveryPeriods,

                    WorkRpe:
                        workRpe,

                    RecoveryRpe:
                        recoveryRpe),

                new Engine30IntervalGlycogenWorkloadConfig(
                    ReferenceWorkIntervalSeconds:
                        60m,

                    ReferenceRecoveryIntervalSeconds:
                        60m,

                    ReferenceRepetitions:
                        repetitions,

                    ReferenceRecoveryPeriods:
                        recoveryPeriods,

                    ReferenceDepletionFraction:
                        depletion,

                    ReferenceWorkRpe:
                        workRpe,

                    ReferenceRecoveryRpe:
                        recoveryRpe,

                    ReferenceObjectiveRelativeWorkIntensity:
                        null,

                    ReferenceObjectiveRelativeRecoveryIntensity:
                        null,

                    WorkIntensitySensitivity:
                        0.50m,

                    RecoveryIntensitySensitivity:
                        0.25m,

                    RecoveryContributionWeight:
                        0.25m,

                    MinimumDepletionFraction:
                        0.01m,

                    MaximumDepletionFraction:
                        0.75m));

        var totalMinutes =
            workload.WorkPhase.ActualMinutes
            + workload.RecoveryPhase.ActualMinutes;

        var resolved =
            _profile.Resolve(
                new Engine30GlycogenMuscleProfileExposure(
                    ModalityCode:
                        definition.DefaultModalityCode,

                    DurationMinutes:
                        totalMinutes,

                    ResolvedDepletionFraction:
                        workload.ResolvedDepletionFraction),

                BuildModalityProfile(
                    definition.DefaultModalityCode,
                    "REPORT_INTERVAL"));

        var demand =
            Demand(
                resolved.ResolvedMuscleGroups);

        return FromDemand(
            demand,
            $"INTERVAL/{workload.WorkPhase.SourceCode}",
            resolved.ResolvedMuscleGroups);
    }

    private GlycogenReport ResistanceGlycogen(
        Engine30WorkoutTypeDefinition definition,
        decimal durationMinutes)
    {
        var muscleWork =
            ResistanceMuscleWork(
                definition);

        var workload =
            _resistance.Calculate(
                new Engine30ResistanceGlycogenWorkloadInput(
                    DurationMinutes:
                        durationMinutes,

                    TrainingStatusCode:
                        Engine30ResistanceGlycogenTrainingStatusCodes.Trained,

                    MuscleGroups:
                        muscleWork),

                new Engine30ResistanceGlycogenWorkloadConfig(
                    ReferenceDurationMinutes:
                        60m,

                    ReferenceHardSetsPerMuscleGroup:
                        8m,

                    ReferenceDepletionFraction:
                        0.28m,

                    HardSetSensitivity:
                        0.50m,

                    DurationSensitivity:
                        0.25m,

                    TrainedFactor:
                        1.00m,

                    UntrainedFactor:
                        1.10m,

                    ReferencePercentOneRepMax:
                        70m,

                    PercentOneRepMaxSensitivity:
                        0.25m,

                    MinimumDepletionFraction:
                        0.05m,

                    MaximumDepletionFraction:
                        0.65m));

        var demand =
            Demand(
                workload.ResolvedMuscleGroups);

        return FromDemand(
            demand,
            "RESISTANCE/HARD_SETS",
            workload.ResolvedMuscleGroups);
    }

    private GlycogenReport CompositeGlycogen(
        Engine30WorkoutTypeDefinition definition)
    {
        if (definition.WorkoutTypeCode
            .Equals(
                "ORANGETHEORY",
                StringComparison.OrdinalIgnoreCase))
        {
            return OrangetheoryGlycogen();
        }

        /*
            Transparent generic composite validation scenario:
              segment 1 = 30 min mixed cardio
              segment 2 = 30 min mixed resistance

            The shared LOWER_BODY and UPPER_BODY region definitions are kept
            consistent so sequential carry-forward is testable.
        */
        var cardioWorkload =
            _cardio.Calculate(
                new Engine30CardioGlycogenWorkloadInput(
                    ModalityCode:
                        Engine30WorkoutModalityCodes.Mixed,

                    DurationMinutes:
                        30m,

                    SessionRpe:
                        definition.DefaultRpe),

                new Engine30CardioGlycogenWorkloadConfig(
                    ReferenceDurationMinutes:
                        30m,

                    ReferenceDepletionFraction:
                        0.18m,

                    ReferenceRpe:
                        Math.Max(
                            1m,
                            definition.DefaultRpe),

                    ReferenceObjectiveRelativeIntensity:
                        null,

                    ReferenceHeartRateZoneIndex:
                        null,

                    IntensitySensitivity:
                        0.50m,

                    Zone1Weight:
                        1m,

                    Zone2Weight:
                        2m,

                    Zone3Weight:
                        3m,

                    Zone4Weight:
                        4m,

                    Zone5Weight:
                        5m,

                    MinimumDepletionFraction:
                        0.01m,

                    MaximumDepletionFraction:
                        0.70m));

        var cardioProfile =
            _profile.Resolve(
                new Engine30GlycogenMuscleProfileExposure(
                    ModalityCode:
                        Engine30WorkoutModalityCodes.Mixed,

                    DurationMinutes:
                        30m,

                    ResolvedDepletionFraction:
                        cardioWorkload.ResolvedDepletionFraction),

                BuildModalityProfile(
                    Engine30WorkoutModalityCodes.Mixed,
                    "REPORT_COMPOSITE_CARDIO"));

        var resistanceWorkload =
            _resistance.Calculate(
                new Engine30ResistanceGlycogenWorkloadInput(
                    DurationMinutes:
                        30m,

                    TrainingStatusCode:
                        Engine30ResistanceGlycogenTrainingStatusCodes.Trained,

                    MuscleGroups:
                        new[]
                        {
                            new Engine30ResistanceMuscleGlycogenWorkInput(
                                "LOWER_BODY",
                                0.50m,
                                0.60m,
                                4m),

                            new Engine30ResistanceMuscleGlycogenWorkInput(
                                "UPPER_BODY",
                                0.20m,
                                0.65m,
                                4m)
                        }),

                StandardResistanceConfig());

        var result =
            _sequential.Calculate(
                new Engine30SequentialCompositeGlycogenInput(
                    FatFreeMassLb:
                        FatFreeMassLb,

                    SkeletalMuscleFractionOfFfm:
                        SkeletalMuscleFractionOfFfm,

                    BaselineGlycogenGPerKgMuscle:
                        BaselineGlycogenGPerKgMuscle,

                    TargetRestorationFraction:
                        1.00m,

                    CarbohydratePerGlycogenG:
                        1.00m,

                    Segments:
                        new[]
                        {
                            new Engine30CompositeGlycogenSegmentInput(
                                "CARDIO",
                                cardioProfile.ResolvedMuscleGroups),

                            new Engine30CompositeGlycogenSegmentInput(
                                "RESISTANCE",
                                resistanceWorkload.ResolvedMuscleGroups)
                        }));

        return new GlycogenReport(
            GlycogenDepletedG:
                result.EstimatedGlycogenDepletedG,

            CarbReloadG:
                result.CarbohydrateReloadRequirementG,

            ReloadDiagnosticKcal:
                result.CarbohydrateReloadEnergyKcal,

            ModelCode:
                "COMPOSITE/SEQUENTIAL",

            MuscleRegions:
                string.Join(
                    ", ",
                    result.Regions.Select(
                        x => x.MuscleGroupCode)));
    }

    private GlycogenReport OrangetheoryGlycogen()
    {
        // TREAD: continuous cardio.
        var treadWorkload =
            _cardio.Calculate(
                new Engine30CardioGlycogenWorkloadInput(
                    Engine30WorkoutModalityCodes.Treadmill,
                    20m,
                    7m),

                new Engine30CardioGlycogenWorkloadConfig(
                    20m,
                    0.14m,
                    7m,
                    null,
                    null,
                    0.50m,
                    1m,
                    2m,
                    3m,
                    4m,
                    5m,
                    0.01m,
                    0.70m));

        var treadProfile =
            _profile.Resolve(
                new Engine30GlycogenMuscleProfileExposure(
                    Engine30WorkoutModalityCodes.Treadmill,
                    20m,
                    treadWorkload.ResolvedDepletionFraction),

                BuildModalityProfile(
                    Engine30WorkoutModalityCodes.Treadmill,
                    "REPORT_OTF_TREAD"));

        // ROW: intervals.
        var rowWorkload =
            _interval.Calculate(
                new Engine30IntervalGlycogenWorkloadInput(
                    Engine30WorkoutModalityCodes.Rowing,
                    60m,
                    60m,
                    10,
                    10,
                    8m,
                    3m),

                new Engine30IntervalGlycogenWorkloadConfig(
                    60m,
                    60m,
                    10,
                    10,
                    0.18m,
                    8m,
                    3m,
                    null,
                    null,
                    0.50m,
                    0.25m,
                    0.25m,
                    0.01m,
                    0.70m));

        var rowMinutes =
            rowWorkload.WorkPhase.ActualMinutes
            + rowWorkload.RecoveryPhase.ActualMinutes;

        var rowProfile =
            _profile.Resolve(
                new Engine30GlycogenMuscleProfileExposure(
                    Engine30WorkoutModalityCodes.Rowing,
                    rowMinutes,
                    rowWorkload.ResolvedDepletionFraction),

                BuildModalityProfile(
                    Engine30WorkoutModalityCodes.Rowing,
                    "REPORT_OTF_ROW"));

        // FLOOR: resistance.
        var floor =
            _resistance.Calculate(
                new Engine30ResistanceGlycogenWorkloadInput(
                    20m,
                    Engine30ResistanceGlycogenTrainingStatusCodes.Trained,
                    new[]
                    {
                        new Engine30ResistanceMuscleGlycogenWorkInput(
                            "LOWER_BODY",
                            0.50m,
                            0.60m,
                            4m),

                        new Engine30ResistanceMuscleGlycogenWorkInput(
                            "UPPER_BODY",
                            0.20m,
                            0.70m,
                            4m)
                    }),

                StandardResistanceConfig());

        var result =
            _sequential.Calculate(
                new Engine30SequentialCompositeGlycogenInput(
                    FatFreeMassLb,
                    SkeletalMuscleFractionOfFfm,
                    BaselineGlycogenGPerKgMuscle,
                    1.00m,
                    1.00m,
                    new[]
                    {
                        new Engine30CompositeGlycogenSegmentInput(
                            "TREAD",
                            treadProfile.ResolvedMuscleGroups),

                        new Engine30CompositeGlycogenSegmentInput(
                            "ROW",
                            rowProfile.ResolvedMuscleGroups),

                        new Engine30CompositeGlycogenSegmentInput(
                            "FLOOR",
                            floor.ResolvedMuscleGroups)
                    }));

        return new GlycogenReport(
            result.EstimatedGlycogenDepletedG,
            result.CarbohydrateReloadRequirementG,
            result.CarbohydrateReloadEnergyKcal,
            "OTF/SEQUENTIAL",
            string.Join(
                ", ",
                result.Regions.Select(
                    x => x.MuscleGroupCode)));
    }

    private static Engine30ResistanceGlycogenWorkloadConfig StandardResistanceConfig()
        => new(
            ReferenceDurationMinutes:
                60m,

            ReferenceHardSetsPerMuscleGroup:
                8m,

            ReferenceDepletionFraction:
                0.28m,

            HardSetSensitivity:
                0.50m,

            DurationSensitivity:
                0.25m,

            TrainedFactor:
                1.00m,

            UntrainedFactor:
                1.10m,

            ReferencePercentOneRepMax:
                70m,

            PercentOneRepMaxSensitivity:
                0.25m,

            MinimumDepletionFraction:
                0.05m,

            MaximumDepletionFraction:
                0.65m);

    private Engine30GlycogenDemandResult Demand(
        IReadOnlyCollection<Engine30MuscleGlycogenInput> muscleGroups)
        => _demand.Calculate(
            new Engine30GlycogenDemandInput(
                FatFreeMassLb:
                    FatFreeMassLb,

                SkeletalMuscleFractionOfFfm:
                    SkeletalMuscleFractionOfFfm,

                BaselineGlycogenGPerKgMuscle:
                    BaselineGlycogenGPerKgMuscle,

                TargetRestorationFraction:
                    1.00m,

                CarbohydratePerGlycogenG:
                    1.00m,

                MuscleGroups:
                    muscleGroups));

    private static GlycogenReport FromDemand(
        Engine30GlycogenDemandResult demand,
        string modelCode,
        IReadOnlyCollection<Engine30MuscleGlycogenInput> muscleGroups)
        => new(
            GlycogenDepletedG:
                demand.EstimatedGlycogenDepletedG,

            CarbReloadG:
                demand.CarbohydrateReloadRequirementG,

            ReloadDiagnosticKcal:
                demand.CarbohydrateReloadEnergyKcal,

            ModelCode:
                modelCode,

            MuscleRegions:
                muscleGroups.Count == 0
                    ? "none"
                    : string.Join(
                        ", ",
                        muscleGroups.Select(
                                x => x.MuscleGroupCode)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)));

    private static decimal CardioReferenceDepletion(
        Engine30WorkoutTypeDefinition definition)
    {
        if (definition.TrainingPatternCode
            .Equals(
                Engine30TrainingPatternCodes.Easy,
                StringComparison.OrdinalIgnoreCase))
        {
            return 0.06m;
        }

        if (definition.TrainingPatternCode
            .Equals(
                Engine30TrainingPatternCodes.Zone2,
                StringComparison.OrdinalIgnoreCase))
        {
            return 0.10m;
        }

        if (definition.DefaultModalityCode
            .Equals(
                Engine30WorkoutModalityCodes.Walking,
                StringComparison.OrdinalIgnoreCase))
        {
            return 0.07m;
        }

        return 0.14m;
    }

    private static Engine30CardioGlycogenModalityProfile BuildModalityProfile(
        string modality,
        string profileCode)
    {
        var code =
            modality.ToUpperInvariant();

        IReadOnlyCollection<Engine30CardioMuscleRegionProfile> regions =
            code switch
            {
                "ROWING" =>
                    new[]
                    {
                        new Engine30CardioMuscleRegionProfile(
                            "LOWER_BODY",
                            0.50m,
                            0.75m),

                        new Engine30CardioMuscleRegionProfile(
                            "UPPER_BODY",
                            0.20m,
                            0.45m)
                    },

                "SWIMMING" =>
                    new[]
                    {
                        new Engine30CardioMuscleRegionProfile(
                            "LOWER_BODY",
                            0.50m,
                            0.45m),

                        new Engine30CardioMuscleRegionProfile(
                            "UPPER_BODY",
                            0.20m,
                            0.65m)
                    },

                "MIXED" =>
                    new[]
                    {
                        new Engine30CardioMuscleRegionProfile(
                            "LOWER_BODY",
                            0.50m,
                            0.70m),

                        new Engine30CardioMuscleRegionProfile(
                            "UPPER_BODY",
                            0.20m,
                            0.60m)
                    },

                "STAIR_CLIMBER" =>
                    LowerOnly(
                        0.80m),

                "RUNNING" =>
                    LowerOnly(
                        0.75m),

                "TREADMILL" =>
                    LowerOnly(
                        0.70m),

                "CYCLING" =>
                    LowerOnly(
                        0.70m),

                "ELLIPTICAL" =>
                    LowerOnly(
                        0.65m),

                "WALKING" =>
                    LowerOnly(
                        0.55m),

                _ =>
                    LowerOnly(
                        0.65m)
            };

        return new Engine30CardioGlycogenModalityProfile(
            ProfileCode:
                profileCode,

            ModalityCode:
                modality,

            SourceCode:
                Engine30CardioMuscleProfileSourceCodes.ConfigDefault,

            ConfidenceCode:
                Engine30CardioMuscleProfileConfidenceCodes.Medium,

            MuscleRegions:
                regions);
    }

    private static IReadOnlyCollection<Engine30CardioMuscleRegionProfile> LowerOnly(
        decimal recruitment)
        => new[]
        {
            new Engine30CardioMuscleRegionProfile(
                "LOWER_BODY",
                0.50m,
                recruitment)
        };

    private static IReadOnlyCollection<Engine30ResistanceMuscleGlycogenWorkInput> ResistanceMuscleWork(
        Engine30WorkoutTypeDefinition definition)
    {
        var code =
            definition.WorkoutTypeCode.ToUpperInvariant();

        if (code.Contains(
            "LEGS"))
        {
            return new[]
            {
                new Engine30ResistanceMuscleGlycogenWorkInput(
                    "LOWER_BODY",
                    0.50m,
                    0.85m,
                    8m)
            };
        }

        if (code.Contains(
                "PUSH")
            ||
            code.Contains(
                "PULL"))
        {
            return new[]
            {
                new Engine30ResistanceMuscleGlycogenWorkInput(
                    "UPPER_BODY",
                    0.20m,
                    0.80m,
                    8m)
            };
        }

        if (code.Contains(
                "FULL"))
        {
            return new[]
            {
                new Engine30ResistanceMuscleGlycogenWorkInput(
                    "LOWER_BODY",
                    0.50m,
                    0.70m,
                    6m),

                new Engine30ResistanceMuscleGlycogenWorkInput(
                    "UPPER_BODY",
                    0.20m,
                    0.70m,
                    6m)
            };
        }

        if (definition.TrainingPatternCode
            .Equals(
                Engine30TrainingPatternCodes.ResistanceCircuit,
                StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                new Engine30ResistanceMuscleGlycogenWorkInput(
                    "LOWER_BODY",
                    0.50m,
                    0.65m,
                    6m),

                new Engine30ResistanceMuscleGlycogenWorkInput(
                    "UPPER_BODY",
                    0.20m,
                    0.65m,
                    6m)
            };
        }

        // Accessory / other resistance preset.
        return new[]
        {
            new Engine30ResistanceMuscleGlycogenWorkInput(
                "LOWER_BODY",
                0.50m,
                0.45m,
                4m),

            new Engine30ResistanceMuscleGlycogenWorkInput(
                "UPPER_BODY",
                0.20m,
                0.60m,
                4m)
        };
    }

    // =====================================================================
    // TEST SCENARIOS
    // =====================================================================

    private static decimal TestDurationMinutes(
        Engine30WorkoutTypeDefinition definition)
    {
        if (definition.WorkoutTypeCode
            .Equals(
                "REST",
                StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }

        if (definition.WorkoutFamilyCode
            .Equals(
                Engine30WorkoutFamilyCodes.Recovery,
                StringComparison.OrdinalIgnoreCase))
        {
            return 20m;
        }

        if (definition.WorkoutFamilyCode
            .Equals(
                Engine30WorkoutFamilyCodes.Resistance,
                StringComparison.OrdinalIgnoreCase))
        {
            return definition.TrainingPatternCode
                .Equals(
                    Engine30TrainingPatternCodes.ResistanceAccessory,
                    StringComparison.OrdinalIgnoreCase)
                ? 45m
                : 60m;
        }

        if (definition.WorkoutFamilyCode
            .Equals(
                Engine30WorkoutFamilyCodes.Intervals,
                StringComparison.OrdinalIgnoreCase))
        {
            return 30m;
        }

        if (definition.WorkoutFamilyCode
            .Equals(
                Engine30WorkoutFamilyCodes.Composite,
                StringComparison.OrdinalIgnoreCase))
        {
            return 60m;
        }

        var modality =
            definition.DefaultModalityCode.ToUpperInvariant();

        return modality switch
        {
            "RUNNING" =>
                40m,

            "STAIR_CLIMBER" =>
                30m,

            _ =>
                45m
        };
    }

    // =====================================================================
    // HUMAN-REVIEW DIAGNOSTICS
    // =====================================================================

    private static decimal ImpliedMetEquivalent(
        WorkoutReportRow row)
    {
        if (row.DurationMinutes <= 0m
            ||
            row.GrossWorkoutKcal <= 0m)
        {
            return 0m;
        }

        const decimal poundsPerKg =
            2.2046226218m;

        var weightKg =
            WeightLb
            / poundsPerKg;

        return (row.GrossWorkoutKcal
                / row.DurationMinutes)
               * 60m
               / weightKg;
    }

    private static decimal ReloadToGrossRatio(
        WorkoutReportRow row)
        => row.GrossWorkoutKcal > 0m
            ? row.ReloadDiagnosticKcal
              / row.GrossWorkoutKcal
            : 0m;

    private static string ReloadRatioDisplay(
        WorkoutReportRow row)
        => row.GrossWorkoutKcal > 0m
            ? $"{ReloadToGrossRatio(row):0.00}x"
            : "N/A";

    private static bool HasEnergyProfileCollision(
        WorkoutReportRow row,
        IReadOnlyList<WorkoutReportRow> rows)
        => row.DurationMinutes > 0m
           &&
           rows.Any(
               other =>
                   !other.Definition.WorkoutTypeCode
                       .Equals(
                           row.Definition.WorkoutTypeCode,
                           StringComparison.OrdinalIgnoreCase)
                   &&
                   other.DurationMinutes
                       == row.DurationMinutes
                   &&
                   other.Definition.DefaultModalityCode
                       .Equals(
                           row.Definition.DefaultModalityCode,
                           StringComparison.OrdinalIgnoreCase)
                   &&
                   Math.Abs(
                       other.GrossWorkoutKcal
                       - row.GrossWorkoutKcal)
                       < 0.1m
                   &&
                   Math.Abs(
                       other.Definition.DefaultRpe
                       - row.Definition.DefaultRpe)
                       >= 0.5m);

    private static string HumanReviewFlag(
        WorkoutReportRow row,
        IReadOnlyList<WorkoutReportRow> rows)
    {
        var reviewEnergy =
            HasEnergyProfileCollision(
                row,
                rows);

        var reviewGlycogen =
            row.GrossWorkoutKcal > 0m
            &&
            ReloadToGrossRatio(
                row)
            > 1.00m;

        if (reviewEnergy
            &&
            reviewGlycogen)
        {
            return "REVIEW_BOTH";
        }

        if (reviewEnergy)
        {
            return "REVIEW_ENERGY";
        }

        if (reviewGlycogen)
        {
            return "REVIEW_GLYCOGEN";
        }

        return "OK";
    }

    private static string HumanReviewReason(
        WorkoutReportRow row,
        IReadOnlyList<WorkoutReportRow> rows)
    {
        var reasons =
            new List<string>();

        if (HasEnergyProfileCollision(
                row,
                rows))
        {
            reasons.Add(
                "same modality/duration has nearly identical gross kcal to another preset with a materially different default RPE");
        }

        if (row.GrossWorkoutKcal > 0m
            &&
            ReloadToGrossRatio(
                row)
            > 1.00m)
        {
            reasons.Add(
                "reload diagnostic kcal exceeds gross workout kcal; review recruited-muscle/depletion calibration");
        }

        return reasons.Count == 0
            ? "none"
            : string.Join(
                "; ",
                reasons);
    }
    private static string ScenarioNote(
        Engine30WorkoutTypeDefinition definition,
        decimal duration)
    {
        if (definition.WorkoutTypeCode
            .Equals(
                "ORANGETHEORY",
                StringComparison.OrdinalIgnoreCase))
        {
            return "60 min composite: 20 min treadmill + 20 min row intervals + 20 min resistance floor";
        }

        if (definition.WorkoutFamilyCode
            .Equals(
                Engine30WorkoutFamilyCodes.Composite,
                StringComparison.OrdinalIgnoreCase))
        {
            return "60 min DRAFT composite validation scenario: 30 min mixed cardio + 30 min mixed resistance";
        }

        if (definition.WorkoutFamilyCode
            .Equals(
                Engine30WorkoutFamilyCodes.Intervals,
                StringComparison.OrdinalIgnoreCase))
        {
            return $"{duration:0} min structured intervals using 60-sec work / 60-sec recovery at default work RPE";
        }

        if (definition.WorkoutFamilyCode
            .Equals(
                Engine30WorkoutFamilyCodes.Resistance,
                StringComparison.OrdinalIgnoreCase))
        {
            return $"{duration:0} min resistance session; hard-set volume assigned by preset/muscle-region pattern";
        }

        return $"{duration:0} min continuous session at catalog default RPE";
    }

    // =====================================================================
    // SUBJECT MATH
    // =====================================================================

    private static decimal MifflinMale(
        decimal weightLb,
        decimal heightIn,
        int ageYears)
    {
        const decimal poundsPerKg =
            2.2046226218m;

        const decimal cmPerInch =
            2.54m;

        var weightKg =
            weightLb
            / poundsPerKg;

        var heightCm =
            heightIn
            * cmPerInch;

        return 10m
               * weightKg
               + 6.25m
               * heightCm
               - 5m
               * ageYears
               + 5m;
    }

    private static WorkoutReportRow? FindOptional(
        IReadOnlyList<WorkoutReportRow> rows,
        string code)
        => rows.FirstOrDefault(
            x =>
                x.Definition.WorkoutTypeCode
                    .Equals(
                        code,
                        StringComparison.OrdinalIgnoreCase));

    private static string Short(
        string value,
        int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 1)]
               + "~";
    }

    // =====================================================================
    // LOCAL REPORT RECORDS
    // =====================================================================

    private sealed record EnergyReport(
        decimal GrossWorkoutKcal,
        decimal RestingDuringWorkoutKcal,
        decimal NetWorkoutKcal,
        string ModelCode);

    private sealed record GlycogenReport(
        decimal GlycogenDepletedG,
        decimal CarbReloadG,
        decimal ReloadDiagnosticKcal,
        string ModelCode,
        string MuscleRegions);

    private sealed record WorkoutReportRow(
        Engine30WorkoutTypeDefinition Definition,
        decimal DurationMinutes,
        decimal BaseLivingKcal,
        decimal HabitualActivityKcal,
        decimal GrossWorkoutKcal,
        decimal RestingDuringWorkoutKcal,
        decimal NetWorkoutKcal,
        decimal EstimatedMaintenanceKcal,
        decimal GlycogenDepletedG,
        decimal CarbReloadG,
        decimal ReloadDiagnosticKcal,
        string EnergyModel,
        string GlycogenModel,
        string MuscleRegions,
        string ScenarioNote);
}



