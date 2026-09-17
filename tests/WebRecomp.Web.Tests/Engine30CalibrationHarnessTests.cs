using System.Globalization;
using System.Text;
using System.Text.Json;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

/// <summary>
/// Permanent Engine 3.0 calibration harness.
///
/// This test is dormant during normal regression runs. It executes only when
/// ENGINE30_CALIBRATION_REQUEST_PATH points to a request JSON file created by
/// scripts/Run-Engine30Calibration.ps1.
///
/// Design rule:
/// This harness CALLS the real Engine 3.0 services. It does not duplicate
/// Engine 3.0 calorie, glycogen, habitual-activity, or prescription formulas.
/// </summary>
public sealed class Engine30CalibrationHarnessTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

    [Fact]
    public async Task ParameterizedPerson_AllActiveWorkouts_CalibrationReport()
    {
        var requestPath =
            Environment.GetEnvironmentVariable(
                "ENGINE30_CALIBRATION_REQUEST_PATH");

        // Normal regression runs do not need Azure/config fixture data.
        // The dedicated runner supplies this path when calibration is requested.
        if (string.IsNullOrWhiteSpace(requestPath))
            return;

        Assert.True(
            File.Exists(requestPath),
            $"Calibration request file not found: {requestPath}");

        var request =
            JsonSerializer.Deserialize<CalibrationRequest>(
                File.ReadAllText(requestPath),
                JsonOptions)
            ?? throw new InvalidOperationException(
                "Calibration request could not be deserialized.");

        ValidateRequest(request);

        Assert.True(
            File.Exists(request.FixturePath),
            $"Workout fixture file not found: {request.FixturePath}");

        var fixtures =
            JsonSerializer.Deserialize<List<WorkoutFixture>>(
                File.ReadAllText(request.FixturePath),
                JsonOptions)
            ?? throw new InvalidOperationException(
                "Workout fixture file could not be deserialized.");

        Assert.Equal(27, fixtures.Count);

        var provider =
            new FixtureProfileProvider(fixtures);

        var workoutRuntime =
            new Engine30WorkoutRuntimeService(provider);

        var dailyRuntime =
            new Engine30DailyRuntimeService(workoutRuntime);

        var prescriptionService =
            new Engine30PrescriptionService();

        var hardFailures =
            new List<string>();

        var reviewFlags =
            new List<string>();

        var rows =
            new List<CalibrationRow>();

        var report =
            new StringBuilder();

        AppendHeader(
            report,
            request,
            fixtures);

        var restDays =
            await BuildRestDays(
                request,
                dailyRuntime);

        foreach (var fixture in fixtures)
        {
            CalibrationRow row;

            try
            {
                row =
                    await BuildWorkoutRow(
                        request,
                        fixture,
                        provider,
                        dailyRuntime,
                        prescriptionService,
                        restDays,
                        hardFailures,
                        reviewFlags);
            }
            catch (Exception ex)
            {
                hardFailures.Add(
                    $"{fixture.WorkoutTypeCode}: {ex.GetType().Name}: {ex.Message}");

                row =
                    CalibrationRow.Error(
                        fixture.WorkoutTypeCode,
                        fixture.DisplayName,
                        ex);
            }

            rows.Add(row);
        }

        AppendWorkoutTable(
            report,
            rows);

        AppendWeeklyEnergyPolicyTable(
            report,
            rows);

        RunCrossWorkoutChecks(
            request,
            rows,
            hardFailures,
            reviewFlags,
            report);

        await RunSensitivityChecks(
            request,
            fixtures,
            provider,
            dailyRuntime,
            hardFailures,
            reviewFlags,
            report);

        AppendSummary(
            report,
            hardFailures,
            reviewFlags);

        WriteOutputs(
            request,
            rows,
            report);

        Assert.Empty(hardFailures);
    }

    private static async Task<IReadOnlyList<Engine30DailyRuntimeResult>>
        BuildRestDays(
            CalibrationRequest request,
            Engine30DailyRuntimeService dailyRuntime)
    {
        var results =
            new List<Engine30DailyRuntimeResult>(6);

        for (var i = 1; i <= 6; i++)
        {
            results.Add(
                await CalculateDay(
                    request,
                    dailyRuntime,
                    request.ActionDate.AddDays(i),
                    session: null));
        }

        return results;
    }

    private static async Task<CalibrationRow>
        BuildWorkoutRow(
            CalibrationRequest request,
            WorkoutFixture fixture,
            FixtureProfileProvider provider,
            Engine30DailyRuntimeService dailyRuntime,
            Engine30PrescriptionService prescriptionService,
            IReadOnlyList<Engine30DailyRuntimeResult> restDays,
            List<string> hardFailures,
            List<string> reviewFlags)
    {
        var profileResolution =
            await provider.ResolveAsync(
                fixture.WorkoutTypeCode);

        var duration =
            ResolveDuration(
                request,
                profileResolution.Profile.DefaultDurationMinutes);

        var session =
            new Engine30WorkoutSessionInput(
                WorkoutTypeCode:
                    fixture.WorkoutTypeCode,
                PlannedDurationMinutes:
                    duration,
                ActualRpe:
                    request.RpeOverride);

        var day =
            await CalculateDay(
                request,
                dailyRuntime,
                request.ActionDate,
                session);

        var prescriptionDays =
            new List<Engine30PrescriptionDayInput>(7)
            {
                new(
                    Date:
                        day.ActionDate,
                    MaintenanceEnergyKcal:
                        day.Energy.MaintenanceEnergyKcal,
                    WorkoutCategoryCode:
                        fixture.WorkoutCategoryCode,
                    GlycogenReloadRequirementG:
                        day.Glycogen.CarbohydrateReloadRequirementG)
            };

        prescriptionDays.AddRange(
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
                request.PlanCode,
                request.WeightLb,
                request.HeightIn,
                prescriptionDays);

        var rx =
            prescription.Days.Single(
                x => x.Date == request.ActionDate);

        var weeklyMaintenanceKcal =
            prescriptionDays.Sum(
                x => x.MaintenanceEnergyKcal);

        var weeklyPrescribedKcal =
            prescription.Days.Sum(
                x => x.PrescribedEnergyKcal);

        var weeklyDeficitKcal =
            Math.Max(
                0m,
                weeklyMaintenanceKcal
                - weeklyPrescribedKcal);

        var weeklySurplusKcal =
            Math.Max(
                0m,
                weeklyPrescribedKcal
                - weeklyMaintenanceKcal);

        var effectiveWeeklyDeficitPct =
            weeklyMaintenanceKcal <= 0m
                ? 0m
                : weeklyDeficitKcal
                  / weeklyMaintenanceKcal
                  * 100m;

        var gross =
            day.Workout?.Energy.GrossWorkoutEnergyKcal
            ?? 0m;

        var resting =
            day.Workout?.Energy.RestingEnergyDuringWorkoutKcal
            ?? 0m;

        var net =
            day.Energy.NetWorkoutActivityEnergyKcal;

        var resolvedDuration =
            day.Workout?.Energy.SessionResolvedDurationMinutes
            ?? 0m;

        var rpe =
            day.Workout?.ResolvedSession.AnalysisRpe.Value
            ?? 0m;

        var weightKg =
            request.WeightLb / 2.2046226218m;

        var durationHours =
            resolvedDuration / 60m;

        var estimatedMet =
            gross <= 0m
            || durationHours <= 0m
            || weightKg <= 0m
                ? 0m
                : gross / weightKg / durationHours;

        var rowFailures =
            new List<string>();

        var rowReviews =
            new List<string>();

        var recomposedMaintenance =
            day.Energy.BaseLivingEnergyKcal
            + day.Energy.HabitualActivityEnergyKcal
            + net;

        if (day.Energy.BaseLivingEnergyKcal <= 0m)
            rowFailures.Add("BASE_NONPOSITIVE");

        if (day.Energy.HabitualActivityEnergyKcal < 0m)
            rowFailures.Add("HABITUAL_NEGATIVE");

        if (gross < 0m)
            rowFailures.Add("GROSS_NEGATIVE");

        if (resting < 0m)
            rowFailures.Add("RESTING_NEGATIVE");

        if (net < 0m)
            rowFailures.Add("NET_NEGATIVE");

        if (net > gross + 0.05m)
            rowFailures.Add("NET_GT_GROSS");

        if (day.Glycogen.EstimatedGlycogenDepletedG < 0m)
            rowFailures.Add("GLYCOGEN_NEGATIVE");

        if (day.Glycogen.CarbohydrateReloadRequirementG < 0m)
            rowFailures.Add("RELOAD_NEGATIVE");

        if (Math.Abs(
                recomposedMaintenance
                - day.Energy.MaintenanceEnergyKcal) > 0.05m)
        {
            rowFailures.Add("MAINT_RECONCILIATION");
        }

        if (day.Glycogen.ReloadEnergyIncludedInMaintenance)
            rowFailures.Add("RELOAD_ENTERED_MAINTENANCE");

        if (rx.ProteinG < 0m
            || rx.RecommendedCarbsG < 0m
            || rx.FatG < 0m)
        {
            rowFailures.Add("NEGATIVE_MACRO");
        }

        if (fixture.WorkoutTypeCode.Equals(
                "REST",
                StringComparison.OrdinalIgnoreCase))
        {
            if (net != 0m)
                rowFailures.Add("REST_NET_NOT_ZERO");

            if (day.Glycogen.EstimatedGlycogenDepletedG != 0m)
                rowFailures.Add("REST_GLYCOGEN_NOT_ZERO");
        }

        if (!fixture.WorkoutTypeCode.Equals(
                "REST",
                StringComparison.OrdinalIgnoreCase)
            && gross > 0m
            && (estimatedMet < 1.5m || estimatedMet > 15m))
        {
            rowReviews.Add("MET_RANGE");
        }

        if (gross > 1200m)
            rowReviews.Add("ENERGY_HIGH");

        if (net > 1000m)
            rowReviews.Add("NET_HIGH");

        if (day.Glycogen.EstimatedGlycogenDepletedG > 250m)
            rowReviews.Add("GLYCOGEN_HIGH");

        if (rx.PrescribedEnergyKcal < 1200m)
            rowReviews.Add("RX_LOW");

        if (rx.PrescribedEnergyKcal > 4500m)
            rowReviews.Add("RX_HIGH");

        if (Math.Abs(rx.MacroEnergyVarianceKcal) > 100m)
            rowReviews.Add("MACRO_VARIANCE");

        if (rx.RecommendedCarbsG > weightKg * 6m)
            rowReviews.Add("CARBS_GT_6GKG");

        foreach (var failure in rowFailures)
        {
            hardFailures.Add(
                $"{fixture.WorkoutTypeCode}: {failure}");
        }

        foreach (var review in rowReviews)
        {
            reviewFlags.Add(
                $"{fixture.WorkoutTypeCode}: {review}");
        }

        var status =
            rowFailures.Count > 0
                ? "RED"
                : rowReviews.Count > 0
                    ? "YELLOW"
                    : "GREEN";

        var profileCode =
            day.Workout?.ProfileProvenance.ProfileVersionCode
            ?? (day.Workout?.ProfileProvenance.SourceCode
                == Engine30WorkoutRuntimeProfileSourceCodes.AdminActiveProfile
                    ? "ACTIVE"
                    : "BASELINE_FALLBACK");

        return new CalibrationRow(
            WorkoutTypeCode:
                fixture.WorkoutTypeCode,
            DisplayName:
                fixture.DisplayName,
            Family:
                day.Workout?.ResolvedSession.WorkoutFamilyCode
                ?? "REST",
            DurationMinutes:
                resolvedDuration,
            Rpe:
                rpe,
            BaseLivingKcal:
                day.Energy.BaseLivingEnergyKcal,
            HabitualActivityKcal:
                day.Energy.HabitualActivityEnergyKcal,
            GrossWorkoutKcal:
                gross,
            RestingDuringWorkoutKcal:
                resting,
            NetWorkoutKcal:
                net,
            MaintenanceKcal:
                day.Energy.MaintenanceEnergyKcal,
            GlycogenDemandG:
                day.Glycogen.EstimatedGlycogenDepletedG,
            CarbReloadG:
                day.Glycogen.CarbohydrateReloadRequirementG,
            PrescribedCalories:
                rx.PrescribedEnergyKcal,
            WeeklyMaintenanceKcal:
                weeklyMaintenanceKcal,
            WeeklyPrescribedKcal:
                weeklyPrescribedKcal,
            WeeklyDeficitKcal:
                weeklyDeficitKcal,
            WeeklySurplusKcal:
                weeklySurplusKcal,
            EffectiveWeeklyDeficitPct:
                effectiveWeeklyDeficitPct,
            ProteinG:
                rx.ProteinG,
            CarbsG:
                rx.RecommendedCarbsG,
            FatG:
                rx.FatG,
            MacroEnergyVarianceKcal:
                rx.MacroEnergyVarianceKcal,
            EstimatedMet:
                estimatedMet,
            ProfileCode:
                profileCode,
            Status:
                status,
            Reasons:
                string.Join(
                    ",",
                    rowFailures.Concat(rowReviews)));
    }

    private static async Task<Engine30DailyRuntimeResult>
        CalculateDay(
            CalibrationRequest request,
            Engine30DailyRuntimeService dailyRuntime,
            DateOnly date,
            Engine30WorkoutSessionInput? session)
    {
        return await dailyRuntime.CalculateAsync(
            new Engine30DailyRuntimeRequest(
                ActionDate:
                    date,
                DateOfBirth:
                    request.ActionDate.AddYears(-request.AgeYears),
                SexCode:
                    request.SexCode,
                HeightIn:
                    request.HeightIn,
                WeightLb:
                    request.WeightLb,
                BodyFatFraction:
                    request.BodyFatPct / 100m,
                OccupationalRoles:
                    BuildOccupationRoles(request),
                LifestyleActivityCode:
                    request.LifestyleActivityCode,
                WorkoutSession:
                    session));
    }

    private static IReadOnlyCollection<Engine30OccupationalRoleInput>
        BuildOccupationRoles(
            CalibrationRequest request)
    {
        if (request.OccupationHoursPerWeek <= 0m
            || string.IsNullOrWhiteSpace(
                request.OccupationActivityCode))
        {
            return Array.Empty<Engine30OccupationalRoleInput>();
        }

        return new[]
        {
            new Engine30OccupationalRoleInput(
                request.OccupationName,
                request.OccupationActivityCode,
                request.OccupationHoursPerWeek,
                request.OccupationActivityFraction)
        };
    }

    private static decimal ResolveDuration(
        CalibrationRequest request,
        decimal profileDefault)
    {
        return request.DurationMode.Equals(
            "FIXED",
            StringComparison.OrdinalIgnoreCase)
                ? request.FixedDurationMinutes
                : profileDefault;
    }

    private static void AppendWeeklyEnergyPolicyTable(
        StringBuilder report,
        IReadOnlyList<CalibrationRow> rows)
    {
        report.AppendLine();
        report.AppendLine(
            "======================================================================================================================");
        report.AppendLine(
            "WEEKLY ENERGY POLICY CHECK");
        report.AppendLine(
            "Each scenario is one target workout day plus six REST days. This section validates weekly energy policy rather than judging the target-day RX in isolation.");
        report.AppendLine(
            "======================================================================================================================");
        report.AppendLine(
            $"{"WORKOUT",-25} {"WK_MAINT",10} {"WK_RX",10} {"DEFICIT",9} {"SURPLUS",9} {"DEF%",7} {"MODE",9}");
        report.AppendLine(
            new string('-', 92));

        foreach (var row in rows)
        {
            var mode =
                row.WeeklyDeficitKcal > 0.05m
                    ? "DEFICIT"
                    : row.WeeklySurplusKcal > 0.05m
                        ? "SURPLUS"
                        : "BALANCED";

            report.AppendLine(
                $"{row.WorkoutTypeCode,-25} " +
                $"{row.WeeklyMaintenanceKcal,10:0} " +
                $"{row.WeeklyPrescribedKcal,10:0} " +
                $"{row.WeeklyDeficitKcal,9:0} " +
                $"{row.WeeklySurplusKcal,9:0} " +
                $"{row.EffectiveWeeklyDeficitPct,7:0.0} " +
                $"{mode,9}");
        }

        report.AppendLine();
        report.AppendLine(
            "DEF% = max(0, weekly maintenance - weekly prescribed energy) / weekly maintenance * 100.");
        report.AppendLine(
            "A high target-day RX can therefore be valid when calories are redistributed toward the workout day while the authoritative weekly budget remains in deficit.");
    }
    private static void RunCrossWorkoutChecks(
        CalibrationRequest request,
        IReadOnlyList<CalibrationRow> rows,
        List<string> hardFailures,
        List<string> reviewFlags,
        StringBuilder report)
    {
        report.AppendLine();
        report.AppendLine(
            "==============================================================================================================");
        report.AppendLine(
            "CROSS-WORKOUT DIRECTIONAL CHECKS");
        report.AppendLine(
            "==============================================================================================================");

        CalibrationRow? Find(string code) =>
            rows.FirstOrDefault(
                x => x.WorkoutTypeCode.Equals(
                    code,
                    StringComparison.OrdinalIgnoreCase));

        var rest = Find("REST");

        if (rest is not null)
        {
            AppendCheck(
                report,
                "REST net workout energy = 0",
                rest.NetWorkoutKcal == 0m,
                hardFailures,
                "REST net workout energy is not zero.");

            AppendCheck(
                report,
                "REST glycogen demand = 0",
                rest.GlycogenDemandG == 0m,
                hardFailures,
                "REST glycogen demand is not zero.");
        }

        var zone2 = Find("ZONE2_CARDIO");
        var hiit = Find("TRUE_HIIT");
        var otf = Find("ORANGETHEORY");
        var legs = Find("MAJOR_LEGS");
        var accessory = Find("ACCESSORY");

        if (zone2 is not null && hiit is not null)
        {
            AppendCheck(
                report,
                "TRUE_HIIT glycogen > ZONE2 glycogen",
                hiit.GlycogenDemandG > zone2.GlycogenDemandG,
                hardFailures,
                "TRUE_HIIT glycogen is not greater than ZONE2.");
        }

        if (zone2 is not null && otf is not null)
        {
            AppendCheck(
                report,
                "ORANGETHEORY glycogen > ZONE2 glycogen",
                otf.GlycogenDemandG > zone2.GlycogenDemandG,
                hardFailures,
                "ORANGETHEORY glycogen is not greater than ZONE2.");
        }

        if (legs is not null && accessory is not null)
        {
            AppendCheck(
                report,
                "MAJOR_LEGS glycogen > ACCESSORY glycogen",
                legs.GlycogenDemandG > accessory.GlycogenDemandG,
                hardFailures,
                "MAJOR_LEGS glycogen is not greater than ACCESSORY.");
        }

        if (request.DurationMode.Equals(
                "FIXED",
                StringComparison.OrdinalIgnoreCase)
            && zone2 is not null
            && hiit is not null
            && hiit.NetWorkoutKcal <= zone2.NetWorkoutKcal)
        {
            reviewFlags.Add(
                "Fixed-duration TRUE_HIIT net workout kcal is not greater than ZONE2.");

            report.AppendLine(
                "YELLOW  Fixed-duration TRUE_HIIT net kcal <= ZONE2 net kcal; review calibration.");
        }
        else if (request.DurationMode.Equals(
                     "FIXED",
                     StringComparison.OrdinalIgnoreCase)
                 && zone2 is not null
                 && hiit is not null)
        {
            report.AppendLine(
                "GREEN   Fixed-duration TRUE_HIIT net kcal > ZONE2 net kcal.");
        }
    }

    private static async Task RunSensitivityChecks(
        CalibrationRequest request,
        IReadOnlyList<WorkoutFixture> fixtures,
        FixtureProfileProvider provider,
        Engine30DailyRuntimeService dailyRuntime,
        List<string> hardFailures,
        List<string> reviewFlags,
        StringBuilder report)
    {
        report.AppendLine();
        report.AppendLine(
            "==============================================================================================================");
        report.AppendLine(
            "SENSITIVITY CHECKS - DURATION AND RPE");
        report.AppendLine(
            "==============================================================================================================");
        report.AppendLine(
            "GREEN = directional response. YELLOW = flat/no response. RED = direction reversed.");
        report.AppendLine();

        var durations =
            new[] { 30m, 45m, 60m, 90m };

        var rpes =
            new[] { 4m, 6m, 8m, 10m };

        foreach (var fixture in fixtures)
        {
            if (fixture.WorkoutTypeCode.Equals(
                    "REST",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var durationGross =
                new List<decimal>();

            var rpeGross =
                new List<decimal>();

            var durationDirection =
                "NOT_RUN";

            var rpeDirection =
                "NOT_RUN";

            try
            {
                var sensitivityProfile =
                    await provider.ResolveAsync(
                        fixture.WorkoutTypeCode);

                var compositeDurationUsesActual =
                    sensitivityProfile.Profile.WorkoutFamilyCode.Equals(
                        Engine30WorkoutFamilyCodes.Composite,
                        StringComparison.OrdinalIgnoreCase);

                foreach (var duration in durations)
                {
                    var sensitivitySession =
                        compositeDurationUsesActual
                            ? new Engine30WorkoutSessionInput(
                                WorkoutTypeCode:
                                    fixture.WorkoutTypeCode,
                                PlannedDurationMinutes:
                                    sensitivityProfile.Profile.DefaultDurationMinutes,
                                ActualDurationMinutes:
                                    duration,
                                ActualRpe:
                                    request.RpeOverride)
                            : new Engine30WorkoutSessionInput(
                                WorkoutTypeCode:
                                    fixture.WorkoutTypeCode,
                                PlannedDurationMinutes:
                                    duration,
                                ActualRpe:
                                    request.RpeOverride);

                    var day =
                        await CalculateDay(
                            request,
                            dailyRuntime,
                            request.ActionDate,
                            sensitivitySession);

                    durationGross.Add(
                        day.Workout?.Energy.GrossWorkoutEnergyKcal
                        ?? 0m);
                }

                durationDirection =
                    ClassifyMonotonic(
                        durationGross);

                if (durationDirection == "RED")
                {
                    hardFailures.Add(
                        $"{fixture.WorkoutTypeCode}: gross energy decreases as duration increases.");
                }
                else if (durationDirection == "YELLOW")
                {
                    reviewFlags.Add(
                        $"{fixture.WorkoutTypeCode}: gross energy shows little/no duration response.");
                }
            }
            catch (Exception ex)
            {
                durationDirection =
                    "RED";

                hardFailures.Add(
                    $"{fixture.WorkoutTypeCode}: duration sensitivity exception {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                foreach (var rpe in rpes)
                {
                    var day =
                        await CalculateDay(
                            request,
                            dailyRuntime,
                            request.ActionDate,
                            new Engine30WorkoutSessionInput(
                                WorkoutTypeCode:
                                    fixture.WorkoutTypeCode,
                                PlannedDurationMinutes:
                                    60m,
                                ActualRpe:
                                    rpe));

                    rpeGross.Add(
                        day.Workout?.Energy.GrossWorkoutEnergyKcal
                        ?? 0m);
                }

                rpeDirection =
                    ClassifyMonotonic(
                        rpeGross);

                if (rpeDirection == "RED")
                {
                    hardFailures.Add(
                        $"{fixture.WorkoutTypeCode}: gross energy decreases as RPE increases.");
                }
                else if (rpeDirection == "YELLOW")
                {
                    reviewFlags.Add(
                        $"{fixture.WorkoutTypeCode}: gross energy shows little/no RPE response.");
                }
            }
            catch (Exception ex)
            {
                rpeDirection =
                    "RED";

                hardFailures.Add(
                    $"{fixture.WorkoutTypeCode}: RPE sensitivity exception {ex.GetType().Name}: {ex.Message}");
            }

            report.AppendLine(
                $"{fixture.WorkoutTypeCode,-27} " +
                $"DURATION {durationDirection,-7} " +
                $"[{string.Join(", ", durationGross.Select(x => x.ToString("0", CultureInfo.InvariantCulture)))}]  " +
                $"RPE {rpeDirection,-7} " +
                $"[{string.Join(", ", rpeGross.Select(x => x.ToString("0", CultureInfo.InvariantCulture)))}]");
        }

        report.AppendLine();
        report.AppendLine(
            "NOTE: Composite ADMIN planned block minutes are absolute by design. Composite duration sensitivity therefore varies whole-session ACTUAL duration, which proportionally resolves observed segment durations.");
        report.AppendLine(
            "RPE sensitivity supplies whole-session ActualRpe. Segment-specific actual RPE still has priority when present.");
    }

    private static string ClassifyMonotonic(
        IReadOnlyList<decimal> values)
    {
        var decreased = false;
        var increased = false;

        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] + 0.05m < values[i - 1])
                decreased = true;

            if (values[i] > values[i - 1] + 0.05m)
                increased = true;
        }

        if (decreased)
            return "RED";

        if (!increased)
            return "YELLOW";

        return "GREEN";
    }

    private static void AppendCheck(
        StringBuilder report,
        string label,
        bool passed,
        List<string> hardFailures,
        string failure)
    {
        report.AppendLine(
            $"{(passed ? "GREEN" : "RED"),-6}  {label}");

        if (!passed)
            hardFailures.Add(failure);
    }

    private static void AppendHeader(
        StringBuilder report,
        CalibrationRequest request,
        IReadOnlyList<WorkoutFixture> fixtures)
    {
        report.AppendLine(
            "12 WEEK RECOMP - ENGINE 3.0 PARAMETERIZED CALIBRATION REPORT");
        report.AppendLine(
            "======================================================================================================================");
        report.AppendLine(
            $"Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine(
            $"Configuration: CFG-3000.0");
        report.AppendLine(
            $"Active workout definitions: {fixtures.Count}");
        report.AppendLine(
            $"Subject: {request.Name}");
        report.AppendLine(
            $"Sex: {request.SexCode}");
        report.AppendLine(
            $"Age: {request.AgeYears}");
        report.AppendLine(
            $"Height: {request.HeightIn:0.0} in");
        report.AppendLine(
            $"Weight: {request.WeightLb:0.0} lb");
        report.AppendLine(
            $"Body fat: {request.BodyFatPct:0.0}%");
        report.AppendLine(
            $"Lifestyle: {request.LifestyleActivityCode}");
        report.AppendLine(
            $"Occupation: {request.OccupationName} / {request.OccupationActivityCode} / {request.OccupationHoursPerWeek:0.#} h/wk / fraction {request.OccupationActivityFraction:0.00}");
        report.AppendLine(
            $"Plan: {request.PlanCode}");
        report.AppendLine(
            $"Duration mode: {request.DurationMode}" +
            (request.DurationMode.Equals(
                "FIXED",
                StringComparison.OrdinalIgnoreCase)
                    ? $" ({request.FixedDurationMinutes:0.#} min)"
                    : ""));
        report.AppendLine(
            $"RPE override: {(request.RpeOverride.HasValue ? request.RpeOverride.Value.ToString("0.#") : "PROFILE DEFAULT")}");
        report.AppendLine();
        report.AppendLine(
            "IMPORTANT: GREEN means automated invariants/directional checks passed. It does NOT mean draft physiology coefficients are scientifically frozen.");
        report.AppendLine();
    }

    private static void AppendWorkoutTable(
        StringBuilder report,
        IReadOnlyList<CalibrationRow> rows)
    {
        report.AppendLine(
            "======================================================================================================================");
        report.AppendLine(
            "27-WORKOUT CALIBRATION SWEEP");
        report.AppendLine(
            "Each workout is placed on day 1 of an otherwise REST week so daily prescription effects are comparable.");
        report.AppendLine(
            "======================================================================================================================");
        report.AppendLine(
            $"{"WORKOUT",-25} {"MIN",5} {"RPE",5} {"BASE",6} {"HAB",6} {"GROSS",7} {"REST",6} {"NET",7} {"MAINT",7} {"GLY",6} {"LOAD",6} {"RX",6} {"P",4} {"C",4} {"F",4} {"MET",5} {"STATUS",7} REASON");
        report.AppendLine(
            new string('-', 150));

        foreach (var row in rows)
        {
            report.AppendLine(
                $"{row.WorkoutTypeCode,-25} " +
                $"{row.DurationMinutes,5:0} " +
                $"{row.Rpe,5:0.0} " +
                $"{row.BaseLivingKcal,6:0} " +
                $"{row.HabitualActivityKcal,6:0} " +
                $"{row.GrossWorkoutKcal,7:0} " +
                $"{row.RestingDuringWorkoutKcal,6:0} " +
                $"{row.NetWorkoutKcal,7:0} " +
                $"{row.MaintenanceKcal,7:0} " +
                $"{row.GlycogenDemandG,6:0} " +
                $"{row.CarbReloadG,6:0} " +
                $"{row.PrescribedCalories,6:0} " +
                $"{row.ProteinG,4:0} " +
                $"{row.CarbsG,4:0} " +
                $"{row.FatG,4:0} " +
                $"{row.EstimatedMet,5:0.0} " +
                $"{row.Status,7} " +
                $"{row.Reasons}");
        }
    }

    private static void AppendSummary(
        StringBuilder report,
        IReadOnlyList<string> hardFailures,
        IReadOnlyList<string> reviewFlags)
    {
        report.AppendLine();
        report.AppendLine(
            "======================================================================================================================");
        report.AppendLine(
            "FINAL CALIBRATION SUMMARY");
        report.AppendLine(
            "======================================================================================================================");
        report.AppendLine(
            $"RED / hard failures: {hardFailures.Count}");
        report.AppendLine(
            $"YELLOW / review flags: {reviewFlags.Distinct().Count()}");

        if (hardFailures.Count == 0)
            report.AppendLine("AUTOMATED GATE: PASS");
        else
            report.AppendLine("AUTOMATED GATE: FAIL");

        if (hardFailures.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("RED / HARD FAILURES");
            foreach (var item in hardFailures.Distinct())
                report.AppendLine($"  RED: {item}");
        }

        if (reviewFlags.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("YELLOW / REVIEW FLAGS");
            foreach (var item in reviewFlags.Distinct())
                report.AppendLine($"  YELLOW: {item}");
        }
    }

    private static void WriteOutputs(
        CalibrationRequest request,
        IReadOnlyList<CalibrationRow> rows,
        StringBuilder report)
    {
        var reportDirectory =
            Path.GetDirectoryName(
                request.ReportPath);

        if (!string.IsNullOrWhiteSpace(reportDirectory))
            Directory.CreateDirectory(reportDirectory);

        File.WriteAllText(
            request.ReportPath,
            report.ToString());

        var csvDirectory =
            Path.GetDirectoryName(
                request.CsvPath);

        if (!string.IsNullOrWhiteSpace(csvDirectory))
            Directory.CreateDirectory(csvDirectory);

        var csv =
            new StringBuilder();

        csv.AppendLine(
            "WorkoutTypeCode,DisplayName,Family,DurationMinutes,RPE,BaseLivingKcal,HabitualActivityKcal,GrossWorkoutKcal,RestingDuringWorkoutKcal,NetWorkoutKcal,MaintenanceKcal,GlycogenDemandG,CarbReloadG,PrescribedCalories,WeeklyMaintenanceKcal,WeeklyPrescribedKcal,WeeklyDeficitKcal,WeeklySurplusKcal,EffectiveWeeklyDeficitPct,ProteinG,CarbsG,FatG,MacroEnergyVarianceKcal,EstimatedMET,ProfileCode,Status,Reasons");

        foreach (var row in rows)
        {
            csv.AppendLine(
                string.Join(
                    ",",
                    Csv(row.WorkoutTypeCode),
                    Csv(row.DisplayName),
                    Csv(row.Family),
                    Num(row.DurationMinutes),
                    Num(row.Rpe),
                    Num(row.BaseLivingKcal),
                    Num(row.HabitualActivityKcal),
                    Num(row.GrossWorkoutKcal),
                    Num(row.RestingDuringWorkoutKcal),
                    Num(row.NetWorkoutKcal),
                    Num(row.MaintenanceKcal),
                    Num(row.GlycogenDemandG),
                    Num(row.CarbReloadG),
                    Num(row.PrescribedCalories),
                    Num(row.WeeklyMaintenanceKcal),
                    Num(row.WeeklyPrescribedKcal),
                    Num(row.WeeklyDeficitKcal),
                    Num(row.WeeklySurplusKcal),
                    Num(row.EffectiveWeeklyDeficitPct),
                    Num(row.ProteinG),
                    Num(row.CarbsG),
                    Num(row.FatG),
                    Num(row.MacroEnergyVarianceKcal),
                    Num(row.EstimatedMet),
                    Csv(row.ProfileCode),
                    Csv(row.Status),
                    Csv(row.Reasons)));
        }

        File.WriteAllText(
            request.CsvPath,
            csv.ToString());
    }

    private static string Num(decimal value) =>
        value.ToString(
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

    private static void ValidateRequest(
        CalibrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Name is required.");

        if (request.AgeYears < 18 || request.AgeYears > 100)
            throw new ArgumentOutOfRangeException(nameof(request.AgeYears));

        if (request.HeightIn < 48m || request.HeightIn > 90m)
            throw new ArgumentOutOfRangeException(nameof(request.HeightIn));

        if (request.WeightLb < 80m || request.WeightLb > 500m)
            throw new ArgumentOutOfRangeException(nameof(request.WeightLb));

        if (request.BodyFatPct <= 2m || request.BodyFatPct >= 70m)
            throw new ArgumentOutOfRangeException(nameof(request.BodyFatPct));

        if (!request.DurationMode.Equals(
                "FIXED",
                StringComparison.OrdinalIgnoreCase)
            && !request.DurationMode.Equals(
                "PROFILE_DEFAULT",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "DurationMode must be FIXED or PROFILE_DEFAULT.");
        }

        if (request.DurationMode.Equals(
                "FIXED",
                StringComparison.OrdinalIgnoreCase)
            && request.FixedDurationMinutes <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.FixedDurationMinutes));
        }

        if (request.RpeOverride.HasValue
            && (request.RpeOverride.Value < 0m
                || request.RpeOverride.Value > 10m))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.RpeOverride));
        }
    }

    private sealed record CalibrationRequest(
        string Name,
        DateOnly ActionDate,
        string SexCode,
        int AgeYears,
        decimal HeightIn,
        decimal WeightLb,
        decimal BodyFatPct,
        string LifestyleActivityCode,
        string PlanCode,
        string OccupationName,
        string OccupationActivityCode,
        decimal OccupationHoursPerWeek,
        decimal OccupationActivityFraction,
        string DurationMode,
        decimal FixedDurationMinutes,
        decimal? RpeOverride,
        string FixturePath,
        string ReportPath,
        string CsvPath);

    private sealed record WorkoutFixture(
        string WorkoutTypeCode,
        string DisplayName,
        string WorkoutCategoryCode,
        decimal DefaultMinutes,
        int DefaultRestSec,
        string? ProfileVersionCode,
        int? ProfileVersionNumber,
        string? ProfileJsonBase64);

    private sealed record CalibrationRow(
        string WorkoutTypeCode,
        string DisplayName,
        string Family,
        decimal DurationMinutes,
        decimal Rpe,
        decimal BaseLivingKcal,
        decimal HabitualActivityKcal,
        decimal GrossWorkoutKcal,
        decimal RestingDuringWorkoutKcal,
        decimal NetWorkoutKcal,
        decimal MaintenanceKcal,
        decimal GlycogenDemandG,
        decimal CarbReloadG,
        decimal PrescribedCalories,
        decimal WeeklyMaintenanceKcal,
        decimal WeeklyPrescribedKcal,
        decimal WeeklyDeficitKcal,
        decimal WeeklySurplusKcal,
        decimal EffectiveWeeklyDeficitPct,
        decimal ProteinG,
        decimal CarbsG,
        decimal FatG,
        decimal MacroEnergyVarianceKcal,
        decimal EstimatedMet,
        string ProfileCode,
        string Status,
        string Reasons)
    {
        public static CalibrationRow Error(
            string code,
            string display,
            Exception ex)
        {
            return new CalibrationRow(
                code,
                display,
                "ERROR",
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                "",
                "RED",
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

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
                    $"Calibration fixture does not contain workout '{workoutTypeCode}'.");
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
