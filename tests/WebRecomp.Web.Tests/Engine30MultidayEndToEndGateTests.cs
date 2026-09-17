using System.Globalization;
using System.Text;
using System.Text.Json;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

/// <summary>
/// Engine 3.0 multi-day behavioral gate.
///
/// Dormant during ordinary regression unless the dedicated runner supplies:
///   ENGINE30_MULTIDAY_E2E_FIXTURE_PATH
///   ENGINE30_MULTIDAY_E2E_OUTPUT_DIR
///
/// Planned week:
///   Mon MAJOR_LEGS
///   Tue REST
///   Wed ZONE2_CARDIO
///   Thu TRUE_HIIT
///   Fri MAJOR_PUSH (planned, then deliberately missed)
///   Sat REST
///   Sun REST
///
/// Actual nutrition deliberately includes both under-target intake and one
/// calorie surplus so the fuel ledger and energy-carryover ledger are exercised
/// independently.
///
/// This test uses the real Engine 3.0 runtime, prescription, Admin category
/// allocator, fuel-state service, and energy-carryover service.
/// </summary>
public sealed class Engine30MultidayEndToEndGateTests
{
    private const decimal WeightLb = 180m;
    private const decimal HeightIn = 69m;
    private const decimal BodyFatFraction = 0.20m;
    private const decimal DashboardRestorationFraction = 0.80m;

    private static readonly DateOnly WeekStart =
        new(2026, 9, 7);

    private static readonly DateOnly DateOfBirth =
        WeekStart.AddYears(-40);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    [Fact]
    public async Task MajorLegs_Rest_Zone2_Hiit_MissedStrength_BehavesCoherently()
    {
        var fixturePath =
            Environment.GetEnvironmentVariable(
                "ENGINE30_MULTIDAY_E2E_FIXTURE_PATH");

        var outputDirectory =
            Environment.GetEnvironmentVariable(
                "ENGINE30_MULTIDAY_E2E_OUTPUT_DIR");

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

        var fixtureByCode =
            fixtures.ToDictionary(
                x => x.WorkoutTypeCode,
                StringComparer.OrdinalIgnoreCase);

        foreach (var code in new[]
        {
            "MAJOR_LEGS",
            "ZONE2_CARDIO",
            "TRUE_HIIT",
            "MAJOR_PUSH"
        })
        {
            Assert.True(
                fixtureByCode.ContainsKey(code),
                $"Active fixture is missing {code}.");
        }

        var provider =
            new FixtureProfileProvider(
                fixtures);

        var dailyRuntime =
            new Engine30DailyRuntimeService(
                new Engine30WorkoutRuntimeService(
                    provider));

        var prescriptionService =
            new Engine30PrescriptionService();

        var policyAllocator =
            new Engine30RecompEnergyPolicyAllocator();

        var fuelState =
            new Engine30FuelStateService();

        var energyCarryover =
            new Engine30EnergyCarryoverService();

        var planned =
            new[]
            {
                new PlannedDay(0, "MAJOR_LEGS"),
                new PlannedDay(1, null),
                new PlannedDay(2, "ZONE2_CARDIO"),
                new PlannedDay(3, "TRUE_HIIT"),
                new PlannedDay(4, "MAJOR_PUSH"),
                new PlannedDay(5, null),
                new PlannedDay(6, null)
            };

        var plannedRuntime =
            new Dictionary<DateOnly, Engine30DailyRuntimeResult>();

        var categories =
            new Dictionary<DateOnly, string>();

        foreach (var plan in planned)
        {
            var date =
                WeekStart.AddDays(
                    plan.DayOffset);

            Engine30WorkoutSessionInput? session =
                plan.WorkoutTypeCode is null
                    ? null
                    : new Engine30WorkoutSessionInput(
                        WorkoutTypeCode:
                            plan.WorkoutTypeCode,
                        PlannedDurationMinutes:
                            60m);

            plannedRuntime[date] =
                await CalculateDay(
                    dailyRuntime,
                    date,
                    session);

            categories[date] =
                plan.WorkoutTypeCode is null
                    ? Engine30PrescriptionCategoryCodes.RestRecoveryOther
                    : fixtureByCode[
                        plan.WorkoutTypeCode].WorkoutCategoryCode;
        }

        var prescriptionInputs =
            planned
                .Select(plan =>
                {
                    var date =
                        WeekStart.AddDays(
                            plan.DayOffset);

                    var day =
                        plannedRuntime[date];

                    return new Engine30PrescriptionDayInput(
                        Date:
                            date,
                        MaintenanceEnergyKcal:
                            day.Energy.MaintenanceEnergyKcal,
                        WorkoutCategoryCode:
                            categories[date],
                        GlycogenReloadRequirementG:
                            day.Glycogen.CarbohydrateReloadRequirementG);
                })
                .ToArray();

        var baselinePrescription =
            prescriptionService.Calculate(
                "PLAN_2",
                WeightLb,
                HeightIn,
                prescriptionInputs);

        var policy =
            FatLossPolicy();

        var protectedPrescription =
            policyAllocator.Apply(
                baselinePrescription,
                policy);

        Assert.Equal(
            7,
            protectedPrescription.Days.Count);

        Assert.Contains(
            "ADMIN_GOAL_CATEGORY_ENERGY_POLICY_APPLIED",
            protectedPrescription.ReasonCodes);

        // ------------------------------------------------------------
        // Planned Friday remains MAJOR_PUSH even though the ACTUAL
        // workout below will be deliberately absent.
        // ------------------------------------------------------------

        var friday =
            WeekStart.AddDays(4);

        var fridayPlannedRuntime =
            plannedRuntime[friday];

        Assert.True(
            fridayPlannedRuntime.Glycogen.EstimatedGlycogenDepletedG > 0m);

        Assert.Equal(
            Engine30PrescriptionCategoryCodes.MajorStrength,
            categories[friday]);

        // ------------------------------------------------------------
        // Actual workout evidence.
        // Monday, Wednesday and Thursday are completed.
        // Friday is MISSED: actual session = null -> actual glycogen 0.
        // ------------------------------------------------------------

        var actualWorkoutCodes =
            new Dictionary<DateOnly, string?>
            {
                [WeekStart] = "MAJOR_LEGS",
                [WeekStart.AddDays(1)] = null,
                [WeekStart.AddDays(2)] = "ZONE2_CARDIO",
                [WeekStart.AddDays(3)] = "TRUE_HIIT",
                [friday] = null
            };

        var actualRuntime =
            new Dictionary<DateOnly, Engine30DailyRuntimeResult>();

        foreach (var pair in actualWorkoutCodes)
        {
            Engine30WorkoutSessionInput? session =
                pair.Value is null
                    ? null
                    : new Engine30WorkoutSessionInput(
                        WorkoutTypeCode:
                            pair.Value,
                        PlannedDurationMinutes:
                            60m);

            actualRuntime[pair.Key] =
                await CalculateDay(
                    dailyRuntime,
                    pair.Key,
                    session);
        }

        Assert.Equal(
            0m,
            actualRuntime[friday]
                .Glycogen
                .EstimatedGlycogenDepletedG);

        Assert.True(
            fridayPlannedRuntime.Glycogen.EstimatedGlycogenDepletedG
            >
            actualRuntime[friday].Glycogen.EstimatedGlycogenDepletedG);

        // ------------------------------------------------------------
        // Actual carbohydrate evidence.
        // These values intentionally stress the rolling fuel state.
        // ------------------------------------------------------------

        var actualCarbs =
            new Dictionary<DateOnly, decimal?>
            {
                [WeekStart] = 80m,
                [WeekStart.AddDays(1)] = 130m,
                [WeekStart.AddDays(2)] = 100m,
                [WeekStart.AddDays(3)] = 180m,
                [friday] = 140m
            };

        // Actual calorie evidence is separate from carbohydrate evidence.
        // Only Tuesday is above the protected target.
        var mondayRx =
            protectedPrescription.Days.Single(
                x => x.Date == WeekStart);

        var tuesdayRx =
            protectedPrescription.Days.Single(
                x => x.Date == WeekStart.AddDays(1));

        var wednesdayRx =
            protectedPrescription.Days.Single(
                x => x.Date == WeekStart.AddDays(2));

        var thursdayRx =
            protectedPrescription.Days.Single(
                x => x.Date == WeekStart.AddDays(3));

        var fridayRx =
            protectedPrescription.Days.Single(
                x => x.Date == friday);

        var actualCalories =
            new Dictionary<DateOnly, decimal?>
            {
                [WeekStart] =
                    mondayRx.PrescribedEnergyKcal - 300m,

                [WeekStart.AddDays(1)] =
                    tuesdayRx.PrescribedEnergyKcal + 400m,

                [WeekStart.AddDays(2)] =
                    wednesdayRx.PrescribedEnergyKcal,

                [WeekStart.AddDays(3)] =
                    thursdayRx.PrescribedEnergyKcal - 250m,

                [friday] =
                    fridayRx.PrescribedEnergyKcal - 100m
            };

        // ------------------------------------------------------------
        // Seed the four-day historical fuel window with four prior
        // zero-demand days. Carbohydrate evidence is present.
        // ------------------------------------------------------------

        var baseRestCarbs =
            protectedPrescription.BaseRestCarbsG;

        var observed =
            Enumerable.Range(1, 4)
                .Select(i =>
                    new Engine30ObservedFuelDayInput(
                        Date:
                            WeekStart.AddDays(-i),
                        GlycogenDemandG:
                            0m,
                        BaseRestCarbohydrateG:
                            baseRestCarbs,
                        ActualCarbohydrateG:
                            baseRestCarbs))
                .OrderBy(x => x.Date)
                .ToList();

        var window =
            fuelState.BuildHistoricalWindow(
                observed);

        var rows =
            new List<GateRow>();

        var completedActualCalories =
            new Dictionary<DateOnly, decimal?>();

        Engine30FuelWindow? preFridayWindow =
            null;

        for (var offset = 0; offset <= 4; offset++)
        {
            var date =
                WeekStart.AddDays(
                    offset);

            var plan =
                planned.Single(
                    x => x.DayOffset == offset);

            var protectedDay =
                protectedPrescription.Days.Single(
                    x => x.Date == date);

            // Energy carryover sees only completed prior-day actuals.
            var carry =
                energyCarryover.Apply(
                    protectedPrescription,
                    policy,
                    date,
                    completedActualCalories);

            var carryDay =
                carry.Days.Single(
                    x => x.Date == date);

            var adjusted =
                carryDay.AdjustedPrescription;

            var plannedDemand =
                plannedRuntime[date]
                    .Glycogen
                    .EstimatedGlycogenDepletedG;

            var readiness =
                fuelState.CalculateReadiness(
                    window,
                    plannedDemand,
                    plan.WorkoutTypeCode is null
                        ? 0m
                        : DashboardRestorationFraction,
                    maxCarbohydrateAvailableBeforeWorkoutG:
                        adjusted.RecommendedCarbsG);

            var actual =
                actualRuntime[date];

            var actualDemand =
                actual.Glycogen.EstimatedGlycogenDepletedG;

            var actualCarb =
                actualCarbs[date];

            var actualCal =
                actualCalories[date];

            rows.Add(
                new GateRow(
                    Date:
                        date,
                    PlannedWorkout:
                        plan.WorkoutTypeCode ?? "REST",
                    ActualWorkout:
                        actualWorkoutCodes[date] ?? "MISSED/REST",
                    PlannedGlycogenDemandG:
                        plannedDemand,
                    ActualGlycogenDemandG:
                        actualDemand,
                    BaselinePrescriptionKcal:
                        protectedDay.PrescribedEnergyKcal,
                    AdjustedPrescriptionKcal:
                        adjusted.PrescribedEnergyKcal,
                    EnergyCarryoverCorrectionKcal:
                        carryDay.TotalCorrectionAppliedKcal,
                    ActualCaloriesKcal:
                        actualCal,
                    ActualCalorieVarianceKcal:
                        actualCal
                        - protectedDay.PrescribedEnergyKcal,
                    PrescribedCarbsG:
                        adjusted.RecommendedCarbsG,
                    ActualCarbsG:
                        actualCarb,
                    IncomingFuelRatio:
                        readiness.IncomingAchievementRatio,
                    IncomingFuelBand:
                        readiness.IncomingReadinessBandCode,
                    HistoricalShortfallG:
                        readiness.HistoricalShortfallG,
                    PreWorkoutRescueG:
                        readiness.PreWorkoutRescueG,
                    ProjectedFuelRatio:
                        readiness.ProjectedAchievementRatio,
                    ProjectedFuelBand:
                        readiness.ProjectedReadinessBandCode,
                    FuelReasons:
                        string.Join(
                            "|",
                            readiness.ReasonCodes)));

            if (date == friday)
            {
                preFridayWindow =
                    window;
            }

            // Current day becomes historical evidence only AFTER the day's
            // readiness and recommendation have been calculated.
            observed.RemoveAt(0);

            observed.Add(
                new Engine30ObservedFuelDayInput(
                    Date:
                        date,
                    GlycogenDemandG:
                        actualDemand,
                    BaseRestCarbohydrateG:
                        baseRestCarbs,
                    ActualCarbohydrateG:
                        actualCarb));

            window =
                fuelState.BuildHistoricalWindow(
                    observed);

            completedActualCalories[date] =
                actualCal;
        }

        Assert.NotNull(
            preFridayWindow);

        // ------------------------------------------------------------
        // Fuel/readiness invariants.
        // ------------------------------------------------------------

        var monday =
            rows.Single(
                x => x.Date == WeekStart);

        var thursday =
            rows.Single(
                x => x.Date == WeekStart.AddDays(3));

        var fridayRow =
            rows.Single(
                x => x.Date == friday);

        Assert.Equal(
            Engine30FuelReadinessBandCodes.Adequate,
            monday.IncomingFuelBand);

        Assert.True(
            thursday.HistoricalShortfallG > 0m,
            "Deliberate Mon/Wed carbohydrate under-fueling should create a historical shortfall before HIIT.");

        Assert.True(
            thursday.PreWorkoutRescueG > 0m,
            "HIIT should receive a non-zero pre-workout rescue under the deliberate fuel shortfall.");

        Assert.True(
            thursday.ProjectedFuelRatio
            >= thursday.IncomingFuelRatio);

        Assert.Equal(
            0m,
            fridayRow.ActualGlycogenDemandG);

        Assert.True(
            fridayRow.PlannedGlycogenDemandG > 0m);

        Assert.True(
            window.TotalGlycogenDemandG
            < preFridayWindow!.TotalGlycogenDemandG,
            "The missed Friday workout must not add the planned MAJOR_PUSH glycogen demand to actual history.");

        // ------------------------------------------------------------
        // Energy carryover invariants.
        // Evaluate Friday BEFORE Friday's actual intake is known.
        // Monday/Thursday deficits are not banked.
        // Tuesday +400 kcal is the only positive prior variance.
        // ------------------------------------------------------------

        var actualBeforeFriday =
            actualCalories
                .Where(x => x.Key < friday)
                .ToDictionary(
                    x => x.Key,
                    x => x.Value);

        var fridayCarry =
            energyCarryover.Apply(
                protectedPrescription,
                policy,
                friday,
                actualBeforeFriday);

        Assert.Equal(
            400m,
            fridayCarry.TotalHistoricalOverageKcal);

        Assert.Equal(
            300m,
            fridayCarry.TotalEligibleCorrectionKcal);

        Assert.Contains(
            "PRIOR_CALORIE_SURPLUS_ELIGIBLE_FOR_RECOVERY",
            fridayCarry.ReasonCodes);

        Assert.Contains(
            "PRIOR_CALORIE_SURPLUS_NOT_RECOVERED",
            fridayCarry.ReasonCodes);

        Assert.DoesNotContain(
            "PRIOR_CALORIE_SURPLUS_PARTIALLY_RECOVERED",
            fridayCarry.ReasonCodes);

        var totalPlaced =
            fridayCarry.Days.Sum(
                x => x.TotalCorrectionAppliedKcal);

        Assert.InRange(
            Math.Abs(
                fridayCarry.TotalEligibleCorrectionKcal
                - totalPlaced
                - fridayCarry.UnallocatedCorrectionKcal),
            0m,
            0.05m);

        foreach (var day in fridayCarry.Days)
        {
            Assert.True(
                day.AdjustedPrescription.PrescribedEnergyKcal
                >=
                day.AdjustedPrescription.ProteinG * 4m
                + day.AdjustedPrescription.FatG * 9m
                + day.AdjustedPrescription.PhysiologicalCarbFloorG * 4m
                - 0.01m,
                $"{day.Date}: carryover violated macro floor.");

            Assert.InRange(
                Math.Abs(
                    day.AdjustedPrescription.MacroEnergyVarianceKcal),
                0m,
                10m);

            if (day.BaselinePrescription.WorkoutCategoryCode.Equals(
                    Engine30PrescriptionCategoryCodes.MajorStrength,
                    StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal(
                    0m,
                    day.TotalCorrectionAppliedKcal);
            }
        }

        WriteOutputs(
            outputDirectory,
            protectedPrescription,
            fridayCarry,
            preFridayWindow!,
            window,
            rows);
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

    private static Engine30GoalEnergyPolicy FatLossPolicy()
    {
        var categories =
            new[]
            {
                new Engine30CategoryEnergyPolicy(
                    "MAJOR_STRENGTH",
                    0.98m,
                    0.95m,
                    1.00m,
                    100,
                    0m),

                new Engine30CategoryEnergyPolicy(
                    "ACCESSORY_STRENGTH",
                    0.86m,
                    0.78m,
                    0.93m,
                    65,
                    150m),

                new Engine30CategoryEnergyPolicy(
                    "CONDITIONING_CARDIO",
                    0.82m,
                    0.72m,
                    0.90m,
                    50,
                    200m),

                new Engine30CategoryEnergyPolicy(
                    "REST_RECOVERY_OTHER",
                    0.74m,
                    0.65m,
                    0.84m,
                    25,
                    300m)
            }
            .ToDictionary(
                x => x.WorkoutCategoryCode,
                StringComparer.OrdinalIgnoreCase);

        return new Engine30GoalEnergyPolicy(
            ConfigVersionCode:
                "CFG-3000.0",
            PlanCode:
                "PLAN_2",
            PlanDisplayName:
                "Fat-Loss Dominant Recomp",
            SurplusRecoveryFraction:
                0.75m,
            Categories:
                categories);
    }

    private static void WriteOutputs(
        string outputDirectory,
        Engine30WeeklyPrescriptionResult prescription,
        Engine30EnergyCarryoverResult fridayCarry,
        Engine30FuelWindow preFridayWindow,
        Engine30FuelWindow postFridayWindow,
        IReadOnlyList<GateRow> rows)
    {
        var report =
            new StringBuilder();

        report.AppendLine(
            "12 WEEK RECOMP - ENGINE 3.0 MULTI-DAY END-TO-END GATE");
        report.AppendLine(
            "====================================================================================================================================");
        report.AppendLine(
            $"Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine(
            "Plan: PLAN_2 | Reference subject: male, age 40, 69 in, 180 lb, 20% body fat");
        report.AppendLine(
            "Sequence: MAJOR_LEGS -> REST -> ZONE2_CARDIO -> TRUE_HIIT -> planned MAJOR_PUSH but MISSED");
        report.AppendLine(
            "Actual carbohydrate stress: 80g -> 130g -> 100g -> 180g -> 140g");
        report.AppendLine(
            "Actual energy stress: Monday -300 kcal, Tuesday +400 kcal, Wednesday target, Thursday -250 kcal, Friday -100 kcal.");
        report.AppendLine();
        report.AppendLine(
            "IMPORTANT: Fuel/carbohydrate carryover and calorie-energy carryover are independent ledgers.");
        report.AppendLine();

        report.AppendLine(
            $"{"DATE",-10} {"PLAN",-17} {"ACTUAL",-17} {"PGLY",6} {"AGLY",6} {"RX0",6} {"RXADJ",6} {"ECORR",6} {"ACAL",6} {"Dkcal",6} {"PC",5} {"AC",5} {"IN%",6} {"BAND",-16} {"SHORT",6} {"RESC",5} {"OUT%",6}");
        report.AppendLine(
            new string('-', 151));

        foreach (var row in rows)
        {
            report.AppendLine(
                $"{row.Date:yyyy-MM-dd} " +
                $"{row.PlannedWorkout,-17} " +
                $"{row.ActualWorkout,-17} " +
                $"{row.PlannedGlycogenDemandG,6:0} " +
                $"{row.ActualGlycogenDemandG,6:0} " +
                $"{row.BaselinePrescriptionKcal,6:0} " +
                $"{row.AdjustedPrescriptionKcal,6:0} " +
                $"{row.EnergyCarryoverCorrectionKcal,6:0} " +
                $"{row.ActualCaloriesKcal,6:0} " +
                $"{row.ActualCalorieVarianceKcal,6:0} " +
                $"{row.PrescribedCarbsG,5:0} " +
                $"{row.ActualCarbsG,5:0} " +
                $"{row.IncomingFuelRatio * 100m,6:0.0} " +
                $"{row.IncomingFuelBand,-16} " +
                $"{row.HistoricalShortfallG,6:0} " +
                $"{row.PreWorkoutRescueG,5:0} " +
                $"{row.ProjectedFuelRatio * 100m,6:0.0}");
        }

        report.AppendLine();
        report.AppendLine(
            "ENERGY CARRYOVER AS OF FRIDAY BEFORE FRIDAY ACTUALS");
        report.AppendLine(
            "--------------------------------------------------");
        report.AppendLine(
            $"Historical positive overage: {fridayCarry.TotalHistoricalOverageKcal:0.##} kcal");
        report.AppendLine(
            $"Eligible correction @ {fridayCarry.SurplusRecoveryFraction:P0}: {fridayCarry.TotalEligibleCorrectionKcal:0.##} kcal");
        report.AppendLine(
            $"Applied before Friday: {fridayCarry.CorrectionAppliedBeforeAsOfDateKcal:0.##} kcal");
        report.AppendLine(
            $"Applied Friday: {fridayCarry.CorrectionAppliedOnAsOfDateKcal:0.##} kcal");
        report.AppendLine(
            $"Applied after Friday: {fridayCarry.CorrectionAppliedAfterAsOfDateKcal:0.##} kcal");
        report.AppendLine(
            $"Unallocated: {fridayCarry.UnallocatedCorrectionKcal:0.##} kcal");
        report.AppendLine(
            $"Reasons: {string.Join(", ", fridayCarry.ReasonCodes)}");

        report.AppendLine();
        report.AppendLine(
            "MISSED-WORKOUT CHECK");
        report.AppendLine(
            "--------------------");
        report.AppendLine(
            $"Pre-Friday four-day actual glycogen demand: {preFridayWindow.TotalGlycogenDemandG:0.##} g");
        report.AppendLine(
            $"Post-Friday four-day actual glycogen demand: {postFridayWindow.TotalGlycogenDemandG:0.##} g");
        report.AppendLine(
            "Friday remained planned as MAJOR_PUSH for prescription purposes, but actual workout demand was zero and the planned glycogen demand was not written into actual history.");

        report.AppendLine();
        report.AppendLine(
            "WEEKLY PRESCRIPTION");
        report.AppendLine(
            "-------------------");
        report.AppendLine(
            $"Weekly maintenance: {prescription.WeeklyMaintenanceEnergyKcal:0.##} kcal");
        report.AppendLine(
            $"Weekly prescribed: {prescription.WeeklyPrescribedEnergyKcal:0.##} kcal");
        report.AppendLine(
            $"Effective deficit: {prescription.EffectiveDeficitPct:P1}");
        report.AppendLine(
            $"Stable protein: {prescription.StableProteinG:0.##} g");
        report.AppendLine(
            $"Stable fat: {prescription.StableFatG:0.##} g");
        report.AppendLine(
            $"Base rest carbs: {prescription.BaseRestCarbsG:0.##} g");

        report.AppendLine();
        report.AppendLine(
            "AUTOMATED INVARIANTS: PASS");
        report.AppendLine(
            "- deliberate under-target calories did not create banked future calories");
        report.AppendLine(
            "- Tuesday +400 kcal was the only positive historical overage and produced 300 kcal eligible correction at 75%");
        report.AppendLine(
            "- Major Strength days remained protected from calorie carryover correction");
        report.AppendLine(
            "- macro floors remained protected after correction");
        report.AppendLine(
            "- deliberate carbohydrate under-fueling created a positive historical shortfall before HIIT");
        report.AppendLine(
            "- pre-workout rescue improved or maintained projected readiness");
        report.AppendLine(
            "- missed Friday MAJOR_PUSH preserved the plan but contributed zero actual glycogen demand");

        File.WriteAllText(
            Path.Combine(
                outputDirectory,
                "Engine30_Multiday_E2E_Report.txt"),
            report.ToString());

        var csv =
            new StringBuilder();

        csv.AppendLine(
            "Date,PlannedWorkout,ActualWorkout,PlannedGlycogenDemandG,ActualGlycogenDemandG,BaselinePrescriptionKcal,AdjustedPrescriptionKcal,EnergyCarryoverCorrectionKcal,ActualCaloriesKcal,ActualCalorieVarianceKcal,PrescribedCarbsG,ActualCarbsG,IncomingFuelRatio,IncomingFuelBand,HistoricalShortfallG,PreWorkoutRescueG,ProjectedFuelRatio,ProjectedFuelBand,FuelReasons");

        foreach (var row in rows)
        {
            csv.AppendLine(
                string.Join(
                    ",",
                    row.Date.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture),
                    Csv(row.PlannedWorkout),
                    Csv(row.ActualWorkout),
                    Num(row.PlannedGlycogenDemandG),
                    Num(row.ActualGlycogenDemandG),
                    Num(row.BaselinePrescriptionKcal),
                    Num(row.AdjustedPrescriptionKcal),
                    Num(row.EnergyCarryoverCorrectionKcal),
                    Num(row.ActualCaloriesKcal),
                    Num(row.ActualCalorieVarianceKcal),
                    Num(row.PrescribedCarbsG),
                    Num(row.ActualCarbsG),
                    Num(row.IncomingFuelRatio),
                    Csv(row.IncomingFuelBand),
                    Num(row.HistoricalShortfallG),
                    Num(row.PreWorkoutRescueG),
                    Num(row.ProjectedFuelRatio),
                    Csv(row.ProjectedFuelBand),
                    Csv(row.FuelReasons)));
        }

        File.WriteAllText(
            Path.Combine(
                outputDirectory,
                "Engine30_Multiday_E2E_Days.csv"),
            csv.ToString());
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

    private sealed record PlannedDay(
        int DayOffset,
        string? WorkoutTypeCode);

    private sealed record GateRow(
        DateOnly Date,
        string PlannedWorkout,
        string ActualWorkout,
        decimal PlannedGlycogenDemandG,
        decimal ActualGlycogenDemandG,
        decimal BaselinePrescriptionKcal,
        decimal AdjustedPrescriptionKcal,
        decimal EnergyCarryoverCorrectionKcal,
        decimal? ActualCaloriesKcal,
        decimal? ActualCalorieVarianceKcal,
        decimal PrescribedCarbsG,
        decimal? ActualCarbsG,
        decimal IncomingFuelRatio,
        string IncomingFuelBand,
        decimal HistoricalShortfallG,
        decimal PreWorkoutRescueG,
        decimal ProjectedFuelRatio,
        string ProjectedFuelBand,
        string FuelReasons);

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
                    $"E2E fixture does not contain workout '{workoutTypeCode}'.");
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