using System.Globalization;
using System.Text;
using System.Text.Json;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

/// <summary>
/// Engine 3.0 macro-prescription diagnostic.
///
/// Dormant during ordinary regression. The dedicated runner supplies:
///   ENGINE30_MACRO_DIAGNOSTIC_FIXTURE_PATH
///   ENGINE30_MACRO_DIAGNOSTIC_OUTPUT_DIR
///
/// The diagnostic calls the real Engine 3.0 runtime and prescription services.
/// It does not reproduce the prescription formulas.
/// </summary>
public sealed class Engine30MacroPrescriptionDiagnosticTests
{
    private const decimal WeightLb = 180m;
    private const decimal HeightIn = 69m;
    private const decimal BodyFatPct = 20m;
    private const decimal BodyFatFraction = 0.20m;
    private const decimal PoundsPerKg = 2.2046226218m;

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
    public async Task ActiveProfiles_WriteMacroPrescriptionDiagnostic()
    {
        var fixturePath =
            Environment.GetEnvironmentVariable(
                "ENGINE30_MACRO_DIAGNOSTIC_FIXTURE_PATH");

        var outputDirectory =
            Environment.GetEnvironmentVariable(
                "ENGINE30_MACRO_DIAGNOSTIC_OUTPUT_DIR");

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

        var workoutRuntime =
            new Engine30WorkoutRuntimeService(
                provider);

        var dailyRuntime =
            new Engine30DailyRuntimeService(
                workoutRuntime);

        var prescriptionService =
            new Engine30PrescriptionService();

        var restDays =
            await BuildRestDays(
                dailyRuntime);

        var scenarioRows =
            new List<ScenarioRow>();

        var dayRows =
            new List<DayRow>();

        var report =
            new StringBuilder();

        AppendHeader(
            report);

        foreach (var fixture in fixtures)
        {
            var target =
                await CalculateDay(
                    dailyRuntime,
                    ActionDate,
                    new Engine30WorkoutSessionInput(
                        WorkoutTypeCode:
                            fixture.WorkoutTypeCode,
                        PlannedDurationMinutes:
                            60m));

            var inputs =
                new List<Engine30PrescriptionDayInput>(7)
                {
                    new(
                        Date:
                            target.ActionDate,
                        MaintenanceEnergyKcal:
                            target.Energy.MaintenanceEnergyKcal,
                        WorkoutCategoryCode:
                            fixture.WorkoutCategoryCode,
                        GlycogenReloadRequirementG:
                            target.Glycogen.CarbohydrateReloadRequirementG)
                };

            inputs.AddRange(
                restDays.Select(
                    x =>
                        new Engine30PrescriptionDayInput(
                            Date:
                                x.ActionDate,
                            MaintenanceEnergyKcal:
                                x.Energy.MaintenanceEnergyKcal,
                            WorkoutCategoryCode:
                                Engine30PrescriptionCategoryCodes.RestRecoveryOther,
                            GlycogenReloadRequirementG:
                                0m)));

            var prescription =
                prescriptionService.Calculate(
                    "PLAN_2",
                    WeightLb,
                    HeightIn,
                    inputs);

            ValidatePrescription(
                fixture.WorkoutTypeCode,
                prescription);

            var rx =
                prescription.Days.Single(
                    x => x.Date == ActionDate);

            var weightKg =
                WeightLb / PoundsPerKg;

            var ffmKg =
                WeightLb * (1m - BodyFatFraction)
                / PoundsPerKg;

            scenarioRows.Add(
                new ScenarioRow(
                    WorkoutTypeCode:
                        fixture.WorkoutTypeCode,
                    WorkoutCategoryCode:
                        fixture.WorkoutCategoryCode,
                    ResolvedDurationMinutes:
                        target.Workout?.ResolvedSession.AnalysisDuration.Minutes
                        ?? 0m,
                    Rpe:
                        target.Workout?.ResolvedSession.AnalysisRpe.Value
                        ?? 0m,
                    MaintenanceKcal:
                        target.Energy.MaintenanceEnergyKcal,
                    GlycogenReloadG:
                        target.Glycogen.CarbohydrateReloadRequirementG,
                    PrescribedKcal:
                        rx.PrescribedEnergyKcal,
                    WeeklyMaintenanceKcal:
                        prescription.WeeklyMaintenanceEnergyKcal,
                    WeeklyPrescribedKcal:
                        prescription.WeeklyPrescribedEnergyKcal,
                    RequestedDeficitPct:
                        prescription.RequestedDeficitPct * 100m,
                    EffectiveDeficitPct:
                        prescription.EffectiveDeficitPct * 100m,
                    Bmi:
                        prescription.Bmi,
                    StableProteinG:
                        prescription.StableProteinG,
                    ProteinGPerKgBodyWeight:
                        prescription.StableProteinG / weightKg,
                    ProteinGPerKgFfm:
                        prescription.StableProteinG / ffmKg,
                    StableFatG:
                        prescription.StableFatG,
                    FatGPerKgBodyWeight:
                        prescription.StableFatG / weightKg,
                    BaseRestCarbsG:
                        prescription.BaseRestCarbsG,
                    PhysiologicalCarbFloorG:
                        rx.PhysiologicalCarbFloorG,
                    EnergyBalancedCarbsG:
                        rx.EnergyBalancedCarbsG,
                    StandardCarbUpperBoundaryG:
                        rx.StandardCarbUpperBoundaryG,
                    RecommendedCarbsG:
                        rx.RecommendedCarbsG,
                    CarbsGPerKgBodyWeight:
                        rx.RecommendedCarbsG / weightKg,
                    MacroDerivedEnergyKcal:
                        rx.MacroDerivedEnergyKcal,
                    MacroEnergyVarianceKcal:
                        rx.MacroEnergyVarianceKcal,
                    CarbGoverningRuleCode:
                        rx.CarbGoverningRuleCode,
                    ReasonCodes:
                        string.Join(
                            "|",
                            prescription.ReasonCodes)));

            foreach (var day in prescription.Days.OrderBy(x => x.Date))
            {
                var dayWorkout =
                    day.Date == ActionDate
                        ? fixture.WorkoutTypeCode
                        : "REST";

                var pKcal =
                    day.ProteinG * 4m;

                var cKcal =
                    day.RecommendedCarbsG * 4m;

                var fKcal =
                    day.FatG * 9m;

                var macroKcal =
                    pKcal + cKcal + fKcal;

                dayRows.Add(
                    new DayRow(
                        ScenarioWorkoutTypeCode:
                            fixture.WorkoutTypeCode,
                        Date:
                            day.Date,
                        DayWorkoutTypeCode:
                            dayWorkout,
                        WorkoutCategoryCode:
                            day.WorkoutCategoryCode,
                        MaintenanceKcal:
                            day.MaintenanceEnergyKcal,
                        PrescribedKcal:
                            day.PrescribedEnergyKcal,
                        ProteinG:
                            day.ProteinG,
                        FatG:
                            day.FatG,
                        PhysiologicalCarbFloorG:
                            day.PhysiologicalCarbFloorG,
                        EnergyBalancedCarbsG:
                            day.EnergyBalancedCarbsG,
                        StandardCarbUpperBoundaryG:
                            day.StandardCarbUpperBoundaryG,
                        RecommendedCarbsG:
                            day.RecommendedCarbsG,
                        CarbsGPerKgBodyWeight:
                            day.RecommendedCarbsG / weightKg,
                        MacroDerivedEnergyKcal:
                            day.MacroDerivedEnergyKcal,
                        MacroEnergyVarianceKcal:
                            day.MacroEnergyVarianceKcal,
                        ProteinEnergyPct:
                            macroKcal <= 0m
                                ? 0m
                                : pKcal / macroKcal * 100m,
                        CarbEnergyPct:
                            macroKcal <= 0m
                                ? 0m
                                : cKcal / macroKcal * 100m,
                        FatEnergyPct:
                            macroKcal <= 0m
                                ? 0m
                                : fKcal / macroKcal * 100m,
                        CarbGoverningRuleCode:
                            day.CarbGoverningRuleCode));
            }
        }

        AppendScenarioTable(
            report,
            scenarioRows);

        AppendFocusedTable(
            report,
            scenarioRows);

        AppendWeekExamples(
            report,
            dayRows);

        WriteOutputs(
            outputDirectory,
            scenarioRows,
            dayRows,
            report);
    }

    private static async Task<IReadOnlyList<Engine30DailyRuntimeResult>>
        BuildRestDays(
            Engine30DailyRuntimeService dailyRuntime)
    {
        var results =
            new List<Engine30DailyRuntimeResult>(6);

        for (var i = 1; i <= 6; i++)
        {
            results.Add(
                await CalculateDay(
                    dailyRuntime,
                    ActionDate.AddDays(i),
                    session:
                        null));
        }

        return results;
    }

    private static Task<Engine30DailyRuntimeResult>
        CalculateDay(
            Engine30DailyRuntimeService dailyRuntime,
            DateOnly date,
            Engine30WorkoutSessionInput? session)
    {
        return dailyRuntime.CalculateAsync(
            new Engine30DailyRuntimeRequest(
                ActionDate:
                    date,
                DateOfBirth:
                    DateOfBirth,
                SexCode:
                    "MALE",
                HeightIn:
                    HeightIn,
                WeightLb:
                    WeightLb,
                BodyFatFraction:
                    BodyFatFraction,
                OccupationalRoles:
                    new[]
                    {
                        new Engine30OccupationalRoleInput(
                            "Office",
                            "SEDENTARY",
                            40m,
                            0.20m)
                    },
                LifestyleActivityCode:
                    "MINIMAL",
                WorkoutSession:
                    session));
    }

    private static void ValidatePrescription(
        string scenario,
        Engine30WeeklyPrescriptionResult prescription)
    {
        Assert.Equal(
            7,
            prescription.Days.Count);

        Assert.InRange(
            Math.Abs(
                prescription.WeeklyMaintenanceEnergyKcal
                - prescription.Days.Sum(x => x.MaintenanceEnergyKcal)),
            0m,
            0.05m);

        Assert.InRange(
            Math.Abs(
                prescription.WeeklyPrescribedEnergyKcal
                - prescription.Days.Sum(x => x.PrescribedEnergyKcal)),
            0m,
            0.05m);

        var expectedDeficitPct =
            prescription.WeeklyMaintenanceEnergyKcal <= 0m
                ? 0m
                : (prescription.WeeklyMaintenanceEnergyKcal
                   - prescription.WeeklyPrescribedEnergyKcal)
                  / prescription.WeeklyMaintenanceEnergyKcal;

        Assert.InRange(
            Math.Abs(
                expectedDeficitPct
                - prescription.EffectiveDeficitPct),
            0m,
            0.0001m);

        foreach (var day in prescription.Days)
        {
            Assert.True(
                day.ProteinG >= 0m
                && day.FatG >= 0m
                && day.RecommendedCarbsG >= 0m,
                $"{scenario}/{day.Date}: negative macro.");

            Assert.Equal(
                prescription.StableProteinG,
                day.ProteinG);

            Assert.Equal(
                prescription.StableFatG,
                day.FatG);

            Assert.True(
                day.RecommendedCarbsG + 0.01m
                >= day.PhysiologicalCarbFloorG,
                $"{scenario}/{day.Date}: carbs below physiological floor.");

            if (day.PhysiologicalCarbFloorG
                <= day.StandardCarbUpperBoundaryG)
            {
                Assert.True(
                    day.RecommendedCarbsG
                    <= day.StandardCarbUpperBoundaryG + 0.01m,
                    $"{scenario}/{day.Date}: carbs above standard upper boundary.");
            }

            Assert.InRange(
                Math.Abs(
                    day.MacroEnergyVarianceKcal),
                0m,
                10m);

            var macroKcal =
                day.ProteinG * 4m
                + day.RecommendedCarbsG * 4m
                + day.FatG * 9m;

            Assert.InRange(
                Math.Abs(
                    macroKcal
                    - day.MacroDerivedEnergyKcal),
                0m,
                1.01m);
        }
    }

    private static void AppendHeader(
        StringBuilder report)
    {
        report.AppendLine(
            "12 WEEK RECOMP - ENGINE 3.0 MACRO PRESCRIPTION DIAGNOSTIC");
        report.AppendLine(
            "======================================================================================================================");
        report.AppendLine(
            $"Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine(
            "Configuration: CFG-3000.0 active workout profiles");
        report.AppendLine(
            "Plan: PLAN_2");
        report.AppendLine(
            "Reference subject: male, age 40, 69 in, 180 lb, 20% body fat");
        report.AppendLine(
            "Lifestyle: MINIMAL | Occupation: sedentary office, 40 h/week, EOAF 0.20");
        report.AppendLine(
            "Scenario design: one target workout day plus six REST days");
        report.AppendLine();
        report.AppendLine(
            "IMPORTANT: This is a diagnostic decomposition. It does not change or scientifically freeze protein, fat, carbohydrate, or energy-policy coefficients.");
        report.AppendLine();
    }

    private static void AppendScenarioTable(
        StringBuilder report,
        IReadOnlyList<ScenarioRow> rows)
    {
        report.AppendLine(
            "======================================================================================================================");
        report.AppendLine(
            "27-WORKOUT TARGET-DAY MACRO SUMMARY");
        report.AppendLine(
            "======================================================================================================================");
        report.AppendLine(
            $"{"WORKOUT",-25} {"CAT",-21} {"RX",6} {"P",5} {"F",5} {"CARB",6} {"FLOOR",6} {"EBAL",6} {"C/KG",6} {"VAR",6} RULE");
        report.AppendLine(
            new string('-', 119));

        foreach (var row in rows)
        {
            report.AppendLine(
                $"{row.WorkoutTypeCode,-25} " +
                $"{row.WorkoutCategoryCode,-21} " +
                $"{row.PrescribedKcal,6:0} " +
                $"{row.StableProteinG,5:0} " +
                $"{row.StableFatG,5:0} " +
                $"{row.RecommendedCarbsG,6:0} " +
                $"{row.PhysiologicalCarbFloorG,6:0} " +
                $"{row.EnergyBalancedCarbsG,6:0} " +
                $"{row.CarbsGPerKgBodyWeight,6:0.00} " +
                $"{row.MacroEnergyVarianceKcal,6:0} " +
                $"{row.CarbGoverningRuleCode}");
        }
    }

    private static void AppendFocusedTable(
        StringBuilder report,
        IReadOnlyList<ScenarioRow> rows)
    {
        report.AppendLine();
        report.AppendLine(
            "======================================================================================================================");
        report.AppendLine(
            "FOCUSED NUTRITION CALIBRATION");
        report.AppendLine(
            "======================================================================================================================");

        var first =
            rows.First();

        report.AppendLine(
            $"Stable protein: {first.StableProteinG:0} g/day | " +
            $"{first.ProteinGPerKgBodyWeight:0.00} g/kg body weight | " +
            $"{first.ProteinGPerKgFfm:0.00} g/kg FFM");

        report.AppendLine(
            $"Stable fat: {first.StableFatG:0} g/day | " +
            $"{first.FatGPerKgBodyWeight:0.00} g/kg body weight");

        report.AppendLine(
            $"Base rest carbs: {first.BaseRestCarbsG:0} g/day");

        report.AppendLine();

        foreach (var code in new[]
        {
            "REST",
            "ZONE2_CARDIO",
            "MAJOR_LEGS",
            "MAJOR_PUSH",
            "MAJOR_PULL",
            "FULL_BODY_STRENGTH",
            "ACCESSORY",
            "TRUE_HIIT",
            "ORANGETHEORY"
        })
        {
            var row =
                rows.Single(
                    x => x.WorkoutTypeCode.Equals(
                        code,
                        StringComparison.OrdinalIgnoreCase));

            report.AppendLine(
                $"{code,-22} " +
                $"maint={row.MaintenanceKcal,7:0} | " +
                $"gly={row.GlycogenReloadG,7:0.##}g | " +
                $"rx={row.PrescribedKcal,6:0} | " +
                $"P={row.StableProteinG,4:0} | " +
                $"C={row.RecommendedCarbsG,4:0} ({row.CarbsGPerKgBodyWeight:0.00}g/kg) | " +
                $"F={row.StableFatG,4:0} | " +
                $"floor={row.PhysiologicalCarbFloorG,4:0} | " +
                $"rule={row.CarbGoverningRuleCode}");
        }

        report.AppendLine();
        report.AppendLine(
            "The governing-rule columns distinguish carbohydrate required by physiological floors from carbohydrate added simply to reconcile the day's energy target.");
    }

    private static void AppendWeekExamples(
        StringBuilder report,
        IReadOnlyList<DayRow> dayRows)
    {
        foreach (var scenario in new[]
        {
            "REST",
            "MAJOR_LEGS",
            "TRUE_HIIT",
            "ORANGETHEORY"
        })
        {
            report.AppendLine();
            report.AppendLine(
                "----------------------------------------------------------------------------------------------------------------------");
            report.AppendLine(
                $"7-DAY DISTRIBUTION - {scenario}");
            report.AppendLine(
                $"{"DATE",-10} {"DAY",-18} {"CAT",-21} {"MAINT",6} {"RX",6} {"P",4} {"C",4} {"F",4} {"FLOOR",5} {"EBAL",5} {"VAR",5} RULE");
            report.AppendLine(
                new string('-', 115));

            foreach (var row in dayRows
                         .Where(x =>
                             x.ScenarioWorkoutTypeCode.Equals(
                                 scenario,
                                 StringComparison.OrdinalIgnoreCase))
                         .OrderBy(x => x.Date))
            {
                report.AppendLine(
                    $"{row.Date:yyyy-MM-dd} " +
                    $"{row.DayWorkoutTypeCode,-18} " +
                    $"{row.WorkoutCategoryCode,-21} " +
                    $"{row.MaintenanceKcal,6:0} " +
                    $"{row.PrescribedKcal,6:0} " +
                    $"{row.ProteinG,4:0} " +
                    $"{row.RecommendedCarbsG,4:0} " +
                    $"{row.FatG,4:0} " +
                    $"{row.PhysiologicalCarbFloorG,5:0} " +
                    $"{row.EnergyBalancedCarbsG,5:0} " +
                    $"{row.MacroEnergyVarianceKcal,5:0} " +
                    $"{row.CarbGoverningRuleCode}");
            }
        }
    }

    private static void WriteOutputs(
        string outputDirectory,
        IReadOnlyList<ScenarioRow> scenarioRows,
        IReadOnlyList<DayRow> dayRows,
        StringBuilder report)
    {
        File.WriteAllText(
            Path.Combine(
                outputDirectory,
                "Engine30_Macro_Prescription_Report.txt"),
            report.ToString());

        var scenarioCsv =
            new StringBuilder();

        scenarioCsv.AppendLine(
            "WorkoutTypeCode,WorkoutCategoryCode,ResolvedDurationMinutes,RPE,MaintenanceKcal,GlycogenReloadG,PrescribedKcal,WeeklyMaintenanceKcal,WeeklyPrescribedKcal,RequestedDeficitPct,EffectiveDeficitPct,BMI,StableProteinG,ProteinGPerKgBodyWeight,ProteinGPerKgFFM,StableFatG,FatGPerKgBodyWeight,BaseRestCarbsG,PhysiologicalCarbFloorG,EnergyBalancedCarbsG,StandardCarbUpperBoundaryG,RecommendedCarbsG,CarbsGPerKgBodyWeight,MacroDerivedEnergyKcal,MacroEnergyVarianceKcal,CarbGoverningRuleCode,ReasonCodes");

        foreach (var row in scenarioRows)
        {
            scenarioCsv.AppendLine(
                string.Join(
                    ",",
                    Csv(row.WorkoutTypeCode),
                    Csv(row.WorkoutCategoryCode),
                    Num(row.ResolvedDurationMinutes),
                    Num(row.Rpe),
                    Num(row.MaintenanceKcal),
                    Num(row.GlycogenReloadG),
                    Num(row.PrescribedKcal),
                    Num(row.WeeklyMaintenanceKcal),
                    Num(row.WeeklyPrescribedKcal),
                    Num(row.RequestedDeficitPct),
                    Num(row.EffectiveDeficitPct),
                    Num(row.Bmi),
                    Num(row.StableProteinG),
                    Num(row.ProteinGPerKgBodyWeight),
                    Num(row.ProteinGPerKgFfm),
                    Num(row.StableFatG),
                    Num(row.FatGPerKgBodyWeight),
                    Num(row.BaseRestCarbsG),
                    Num(row.PhysiologicalCarbFloorG),
                    Num(row.EnergyBalancedCarbsG),
                    Num(row.StandardCarbUpperBoundaryG),
                    Num(row.RecommendedCarbsG),
                    Num(row.CarbsGPerKgBodyWeight),
                    Num(row.MacroDerivedEnergyKcal),
                    Num(row.MacroEnergyVarianceKcal),
                    Csv(row.CarbGoverningRuleCode),
                    Csv(row.ReasonCodes)));
        }

        File.WriteAllText(
            Path.Combine(
                outputDirectory,
                "Engine30_Macro_Scenario_Summary.csv"),
            scenarioCsv.ToString());

        var dayCsv =
            new StringBuilder();

        dayCsv.AppendLine(
            "ScenarioWorkoutTypeCode,Date,DayWorkoutTypeCode,WorkoutCategoryCode,MaintenanceKcal,PrescribedKcal,ProteinG,FatG,PhysiologicalCarbFloorG,EnergyBalancedCarbsG,StandardCarbUpperBoundaryG,RecommendedCarbsG,CarbsGPerKgBodyWeight,MacroDerivedEnergyKcal,MacroEnergyVarianceKcal,ProteinEnergyPct,CarbEnergyPct,FatEnergyPct,CarbGoverningRuleCode");

        foreach (var row in dayRows)
        {
            dayCsv.AppendLine(
                string.Join(
                    ",",
                    Csv(row.ScenarioWorkoutTypeCode),
                    row.Date.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture),
                    Csv(row.DayWorkoutTypeCode),
                    Csv(row.WorkoutCategoryCode),
                    Num(row.MaintenanceKcal),
                    Num(row.PrescribedKcal),
                    Num(row.ProteinG),
                    Num(row.FatG),
                    Num(row.PhysiologicalCarbFloorG),
                    Num(row.EnergyBalancedCarbsG),
                    Num(row.StandardCarbUpperBoundaryG),
                    Num(row.RecommendedCarbsG),
                    Num(row.CarbsGPerKgBodyWeight),
                    Num(row.MacroDerivedEnergyKcal),
                    Num(row.MacroEnergyVarianceKcal),
                    Num(row.ProteinEnergyPct),
                    Num(row.CarbEnergyPct),
                    Num(row.FatEnergyPct),
                    Csv(row.CarbGoverningRuleCode)));
        }

        File.WriteAllText(
            Path.Combine(
                outputDirectory,
                "Engine30_Macro_Week_Detail.csv"),
            dayCsv.ToString());
    }

    private static string Num(decimal value)
        => value.ToString(
            "0.####",
            CultureInfo.InvariantCulture);

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

    private sealed record ScenarioRow(
        string WorkoutTypeCode,
        string WorkoutCategoryCode,
        decimal ResolvedDurationMinutes,
        decimal Rpe,
        decimal MaintenanceKcal,
        decimal GlycogenReloadG,
        decimal PrescribedKcal,
        decimal WeeklyMaintenanceKcal,
        decimal WeeklyPrescribedKcal,
        decimal RequestedDeficitPct,
        decimal EffectiveDeficitPct,
        decimal Bmi,
        decimal StableProteinG,
        decimal ProteinGPerKgBodyWeight,
        decimal ProteinGPerKgFfm,
        decimal StableFatG,
        decimal FatGPerKgBodyWeight,
        decimal BaseRestCarbsG,
        decimal PhysiologicalCarbFloorG,
        decimal EnergyBalancedCarbsG,
        decimal StandardCarbUpperBoundaryG,
        decimal RecommendedCarbsG,
        decimal CarbsGPerKgBodyWeight,
        decimal MacroDerivedEnergyKcal,
        decimal MacroEnergyVarianceKcal,
        string CarbGoverningRuleCode,
        string ReasonCodes);

    private sealed record DayRow(
        string ScenarioWorkoutTypeCode,
        DateOnly Date,
        string DayWorkoutTypeCode,
        string WorkoutCategoryCode,
        decimal MaintenanceKcal,
        decimal PrescribedKcal,
        decimal ProteinG,
        decimal FatG,
        decimal PhysiologicalCarbFloorG,
        decimal EnergyBalancedCarbsG,
        decimal StandardCarbUpperBoundaryG,
        decimal RecommendedCarbsG,
        decimal CarbsGPerKgBodyWeight,
        decimal MacroDerivedEnergyKcal,
        decimal MacroEnergyVarianceKcal,
        decimal ProteinEnergyPct,
        decimal CarbEnergyPct,
        decimal FatEnergyPct,
        string CarbGoverningRuleCode);

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
                    $"Macro diagnostic fixture does not contain workout '{workoutTypeCode}'.");
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