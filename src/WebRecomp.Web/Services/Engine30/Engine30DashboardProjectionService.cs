using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.WorkoutIntelligence;
using WebRecomp.WorkoutIntelligence;

namespace WebRecomp.Web.Services.Engine30;

public sealed record Engine30DashboardDayProjection(
    DateOnly Date,
    bool HasPlannedWorkout,
    string? WorkoutTypeCode,
    string WorkoutDisplayName,
    string WorkoutCategoryCode,
    decimal PlannedDurationMinutes,
    decimal? PlannedRpe,
    decimal BaseLivingEnergyKcal,
    decimal HabitualActivityEnergyKcal,
    decimal GrossWorkoutEnergyKcal,
    decimal NetWorkoutActivityEnergyKcal,
    decimal MaintenanceEnergyKcal,
    decimal BaselinePrescribedEnergyKcal,
    decimal EnergyCarryoverCorrectionKcal,
    decimal PrescribedEnergyKcal,
    decimal ProteinG,
    decimal RecommendedCarbsG,
    decimal FatG,
    decimal PhysiologicalCarbFloorG,
    decimal BaseRestCarbsG,
    decimal EffectiveDeficitPct,
    string CarbGoverningRuleCode,
    decimal GlycogenDemandG,
    decimal GlycogenReloadRequirementG,
    string EngineVersionCode,
    string ConfigVersionCode,
    string? WorkoutProfileVersionCode,
    string? WorkoutProfileSourceCode);

public sealed record Engine30DashboardActualWorkoutProjection(
    bool HasActualWorkout,
    string? WorkoutSummary,
    int CompletedSessionCount,
    decimal TotalDurationMinutes,
    decimal? DurationWeightedRpe,
    decimal GrossWorkoutEnergyKcal,
    decimal NetWorkoutEnergyKcal,
    decimal GlycogenDemandG);
public sealed record Engine30DashboardTodayTomorrowProjection(
    Engine30DashboardDayProjection Today,
    Engine30DashboardDayProjection Tomorrow,
    Engine30EnergyCarryoverResult EnergyCarryover);

public sealed record Engine30DashboardYesterdayImpactProjection(
    DateOnly PreviousDate,
    bool PreviousTargetAvailable,
    decimal PreviousTargetCalories,
    decimal PreviousTargetProteinG,
    decimal PreviousTargetCarbsG,
    decimal PreviousTargetFatG,
    bool PreviousNutritionActualLogged,
    decimal? PreviousActualCalories,
    decimal? PreviousActualProteinG,
    decimal? PreviousActualCarbsG,
    decimal? PreviousActualFatG,
    decimal? PreviousCarbDifferenceG,
    string? PreviousWorkoutActual,
    decimal PreviousGlycogenDemandG,
    decimal? PreviousRestorationCreditG,
    decimal? PreviousRestorationRatio,
    string PreviousFuelSummary,
    decimal? PreviousEnergyVarianceKcal,
    decimal? PreviousEligibleEnergyCarryoverKcal,
    decimal WeekEnergyOverageKcal,
    decimal WeekEligibleEnergyCarryoverKcal,
    decimal EnergyCorrectionAppliedBeforeTodayKcal,
    decimal EnergyCorrectionAppliedTodayKcal,
    decimal EnergyCorrectionAppliedFutureKcal,
    decimal EnergyCorrectionUnallocatedKcal,
    decimal SurplusRecoveryFraction,
    decimal TodayBaselinePrescribedEnergyKcal,
    decimal TodayEnergyCarryoverCorrectionKcal,
    string EnergyCarryoverSummary,
    decimal FourDayGlycogenDemandG,
    decimal FourDayRestorationCreditG,
    decimal AbsoluteRestorationRatio,
    short FourDayModerateHardDays,
    short FourDayHardDays,
    short FourDayCarbohydrateEvidenceDays,
    decimal TodayPlannedGlycogenDemandG,
    decimal FunctionalTargetRatio,
    decimal IncomingTargetAchievementRatio,
    string IncomingReadinessBandCode,
    decimal HistoricalShortfallG,
    decimal RecoveryAllocationG,
    decimal PreWorkoutRescueG,
    decimal ProjectedTargetAchievementRatio,
    string ProjectedReadinessBandCode,
    string FuelActionLabel,
    IReadOnlyList<string> ReasonCodes);

internal sealed record Engine30DashboardWeekProjection(
    string PlanCode,
    DateOnly WeekStart,
    IReadOnlyDictionary<DateOnly, Engine30DashboardDayProjection> Days,
    Engine30EnergyCarryoverResult EnergyCarryover);

/// <summary>
/// Stable Dashboard-facing Engine 3.0 projection.
///
/// Update 1:
/// - Today + Tomorrow workout/profile and prescription outputs.
///
/// Update 2:
/// - Prior four REAL days of completed-workout glycogen demand.
/// - Prior four REAL days of carbohydrate evidence.
/// - Engine 3.0 restoration credit.
/// - Incoming readiness, historical shortfall, recovery allocation,
///   pre-workout rescue and projected readiness.
///
/// Important:
/// - Missing nutrition is missing evidence, not zero intake.
/// - Stored Engine 2.0 calculated workout calories/glycogen are not reused.
/// - Actual completed workout duration/RPE replace planned assumptions for
///   historical glycogen demand.
/// - Actual resistance rest is used when the completed session captured it.
/// - Rescue is timing inside today's already-prescribed carbohydrate.
/// - Glycogen reload kcal never become an energy-expenditure bucket.
/// </summary>
public sealed class Engine30DashboardProjectionService(
    RecompDbContext db,
    Engine30ClientActivityProfileService activityProfiles,
    Engine30DailyRuntimeService dailyRuntime,
    Engine30PrescriptionService prescriptionService,
    Engine30FuelStateService fuelStateService,
    IEngine30WorkoutRuntimeProfileProvider profileProvider,
    Engine30GoalEnergyPolicyService energyPolicies,
    Engine30RecompEnergyPolicyAllocator energyPolicyAllocator,
    Engine30EnergyCarryoverService energyCarryover,
    ProgramPlanService planSelection,
    IWorkoutIntelligenceGateway workoutIntelligence)
{
    public const string EngineVersionCode = "3000.0";
    public const string ConfigVersionCode = "CFG-3000.0";
    public const decimal DashboardPlannedRestorationFraction = 0.80m;

    public async Task<Engine30DashboardTodayTomorrowProjection> BuildTodayTomorrowAsync(
        AppClientContext context,
        DateOnly actionDate,
        CancellationToken cancellationToken = default)
    {
        if (context.Client is null || context.Program is null)
            throw new InvalidOperationException("Active client/program required.");

        var client = context.Client;
        var program = context.Program;

        ValidateClientProfile(client);

        var tomorrow = actionDate.AddDays(1);

        var todayPlanCode =
            await planSelection.GetPlanCodeForDateAsync(
                program,
                actionDate,
                cancellationToken);

        var tomorrowPlanCode =
            await planSelection.GetPlanCodeForDateAsync(
                program,
                tomorrow,
                cancellationToken);

        var definitions =
            await LoadEngine30DefinitionsAsync(cancellationToken);

        var measurements =
            await LoadMeasurementsAsync(
                client.ClientId,
                actionDate,
                cancellationToken);

        var todayWeekStart = WeekStart(actionDate);
        var tomorrowWeekStart = WeekStart(tomorrow);

        var todayWeek =
            await BuildWeekAsync(
                client,
                program,
                todayWeekStart,
                actionDate,
                todayPlanCode,
                definitions,
                measurements,
                cancellationToken);

        Engine30DashboardWeekProjection tomorrowWeek;

        if (tomorrowWeekStart == todayWeekStart
            && string.Equals(
                tomorrowPlanCode,
                todayPlanCode,
                StringComparison.OrdinalIgnoreCase))
        {
            tomorrowWeek = todayWeek;
        }
        else
        {
            tomorrowWeek =
                await BuildWeekAsync(
                    client,
                    program,
                    tomorrowWeekStart,
                    actionDate,
                    tomorrowPlanCode,
                    definitions,
                    measurements,
                    cancellationToken);
        }

        return new Engine30DashboardTodayTomorrowProjection(
            Today: todayWeek.Days[actionDate],
            Tomorrow: tomorrowWeek.Days[tomorrow],
            EnergyCarryover: todayWeek.EnergyCarryover);
    }

    public async Task<Engine30DashboardYesterdayImpactProjection>
        BuildYesterdayImpactAsync(
            AppClientContext context,
            DateOnly actionDate,
            Engine30DashboardTodayTomorrowProjection currentProjection,
            CancellationToken cancellationToken = default)
    {
        if (context.Client is null || context.Program is null)
            throw new InvalidOperationException("Active client/program required.");

        ArgumentNullException.ThrowIfNull(currentProjection);

        var client = context.Client;
        var program = context.Program;

        ValidateClientProfile(client);

        var firstHistoricalDate = actionDate.AddDays(-4);
        var previousDate = actionDate.AddDays(-1);

        var definitions =
            await LoadEngine30DefinitionsAsync(cancellationToken);

        var measurements =
            await LoadMeasurementsAsync(
                client.ClientId,
                actionDate,
                cancellationToken);

        var nutritionRows =
            await db.NutritionDays
                .AsNoTracking()
                .Where(x =>
                    x.ClientId == client.ClientId
                    && x.ProgramId == program.ProgramId
                    && x.NutritionDate >= firstHistoricalDate
                    && x.NutritionDate <= previousDate)
                .ToListAsync(cancellationToken);

        var nutritionByDate =
            nutritionRows.ToDictionary(
                x => x.NutritionDate,
                x => x);

        var actualSessions =
            await db.WorkoutSessions
                .AsNoTracking()
                .Where(x =>
                    x.ClientId == client.ClientId
                    && x.ProgramId == program.ProgramId
                    && x.WorkoutDate >= firstHistoricalDate
                    && x.WorkoutDate <= previousDate
                    && x.CompletedFlag)
                .OrderBy(x => x.WorkoutDate)
                .ThenBy(x => x.CreatedAtUtc)
                .ToListAsync(cancellationToken);

        actualSessions =
            (await DetailedWorkoutEngine30Adapter
                .PreferBuilderSessionsAsync(
                    db,
                    actualSessions,
                    cancellationToken))
            .ToList();

        var sessionsByDate =
            actualSessions
                .GroupBy(x => x.WorkoutDate)
                .ToDictionary(
                    x => x.Key,
                    x => (IReadOnlyList<WorkoutSession>)x.ToArray());

        var weekCache =
            new Dictionary<string, Engine30DashboardWeekProjection>(
                StringComparer.OrdinalIgnoreCase);

        async Task<Engine30DashboardDayProjection> GetPlannedProjectionAsync(
            DateOnly date)
        {
            var planCode =
                await planSelection.GetPlanCodeForDateAsync(
                    program,
                    date,
                    cancellationToken);

            var weekStart = WeekStart(date);
            var cacheKey =
                $"{weekStart:yyyyMMdd}|{planCode}|ASOF:{date:yyyyMMdd}";

            if (!weekCache.TryGetValue(
                    cacheKey,
                    out var week))
            {
                week =
                    await BuildWeekAsync(
                        client,
                        program,
                        weekStart,
                        date,
                        planCode,
                        definitions,
                        measurements,
                        cancellationToken);

                weekCache[cacheKey] = week;
            }

            return week.Days[date];
        }

        var observedDays =
            new List<Engine30ObservedFuelDayInput>(
                Engine30FuelStateDefaults.HistoricalWindowDays);

        var actualSummaries =
            new Dictionary<DateOnly, string?>();

        var actualDemandByDate =
            new Dictionary<DateOnly, decimal>();

        for (var i = 0; i < Engine30FuelStateDefaults.HistoricalWindowDays; i++)
        {
            var date =
                firstHistoricalDate.AddDays(i);

            var plannedProjection =
                await GetPlannedProjectionAsync(date);

            sessionsByDate.TryGetValue(
                date,
                out var sessions);

            var actual =
                await CalculateActualDayGlycogenAsync(
                    client,
                    date,
                    sessions ?? Array.Empty<WorkoutSession>(),
                    measurements,
                    cancellationToken);

            actualDemandByDate[date] =
                actual.DemandG;

            actualSummaries[date] =
                actual.Summary;

            nutritionByDate.TryGetValue(
                date,
                out var nutrition);

            observedDays.Add(
                new Engine30ObservedFuelDayInput(
                    Date: date,
                    GlycogenDemandG:
                        actual.DemandG,
                    BaseRestCarbohydrateG:
                        plannedProjection.BaseRestCarbsG,
                    ActualCarbohydrateG:
                        nutrition?.ActualCarbsG));
        }

        var historicalWindow =
            fuelStateService.BuildHistoricalWindow(
                observedDays);

        var readiness =
            fuelStateService.CalculateReadiness(
                historicalWindow,
                currentProjection.Today.GlycogenDemandG,
                currentProjection.Today.HasPlannedWorkout
                    ? DashboardPlannedRestorationFraction
                    : 0m,
                maxCarbohydrateAvailableBeforeWorkoutG:
                    currentProjection.Today.RecommendedCarbsG);

        var previousPlanned =
            await GetPlannedProjectionAsync(
                previousDate);

        nutritionByDate.TryGetValue(
            previousDate,
            out var previousNutrition);

        var previousLedger =
            historicalWindow.Days.Single(
                x => x.Date == previousDate);

        decimal? previousRestorationCredit =
            previousLedger.HasCarbohydrateEvidence
                ? previousLedger.RestorationCreditG
                : null;

        decimal? previousRestorationRatio =
            previousLedger.HasCarbohydrateEvidence
                ? previousLedger.GlycogenDemandG <= 0m
                    ? 1m
                    : Math.Min(
                        1m,
                        previousLedger.RestorationCreditG
                        / previousLedger.GlycogenDemandG)
                : null;

        var previousFuelSummary =
            BuildPreviousFuelSummary(
                previousLedger,
                previousRestorationRatio);

        var currentEnergy =
            currentProjection.EnergyCarryover;

        var previousEnergyDay =
            currentEnergy.Days.SingleOrDefault(
                x => x.Date == previousDate);

        decimal? previousEnergyVariance =
            previousNutrition?.ActualCalories is decimal actualEnergyCalories
                ? actualEnergyCalories
                  - previousPlanned.PrescribedEnergyKcal
                : null;

        decimal? previousEligibleEnergyCarryover =
            previousEnergyDay is null
                ? previousEnergyVariance.HasValue
                  && previousEnergyVariance.Value > 0m
                    ? 0m
                    : null
                : previousEnergyDay.EligibleCorrectionGeneratedKcal;

        var energyCarryoverSummary =
            BuildEnergyCarryoverSummary(
                actionDate,
                previousDate,
                previousEnergyVariance,
                previousEligibleEnergyCarryover,
                currentProjection.Today.WorkoutCategoryCode,
                currentEnergy);

        var actionLabel =
            FuelActionLabel(
                currentProjection.Today,
                readiness);

        return new Engine30DashboardYesterdayImpactProjection(
            PreviousDate:
                previousDate,
            PreviousTargetAvailable:
                true,
            PreviousTargetCalories:
                previousPlanned.PrescribedEnergyKcal,
            PreviousTargetProteinG:
                previousPlanned.ProteinG,
            PreviousTargetCarbsG:
                previousPlanned.RecommendedCarbsG,
            PreviousTargetFatG:
                previousPlanned.FatG,
            PreviousNutritionActualLogged:
                previousNutrition is not null,
            PreviousActualCalories:
                previousNutrition?.ActualCalories,
            PreviousActualProteinG:
                previousNutrition?.ActualProteinG,
            PreviousActualCarbsG:
                previousNutrition?.ActualCarbsG,
            PreviousActualFatG:
                previousNutrition?.ActualFatG,
            PreviousCarbDifferenceG:
                previousNutrition?.ActualCarbsG is decimal actualCarbs
                    ? actualCarbs
                      - previousPlanned.RecommendedCarbsG
                    : null,
            PreviousWorkoutActual:
                actualSummaries[previousDate],
            PreviousGlycogenDemandG:
                actualDemandByDate[previousDate],
            PreviousRestorationCreditG:
                previousRestorationCredit,
            PreviousRestorationRatio:
                previousRestorationRatio,
            PreviousFuelSummary:
                previousFuelSummary,
            PreviousEnergyVarianceKcal:
                previousEnergyVariance,
            PreviousEligibleEnergyCarryoverKcal:
                previousEligibleEnergyCarryover,
            WeekEnergyOverageKcal:
                currentEnergy.TotalHistoricalOverageKcal,
            WeekEligibleEnergyCarryoverKcal:
                currentEnergy.TotalEligibleCorrectionKcal,
            EnergyCorrectionAppliedBeforeTodayKcal:
                currentEnergy.CorrectionAppliedBeforeAsOfDateKcal,
            EnergyCorrectionAppliedTodayKcal:
                currentEnergy.CorrectionAppliedOnAsOfDateKcal,
            EnergyCorrectionAppliedFutureKcal:
                currentEnergy.CorrectionAppliedAfterAsOfDateKcal,
            EnergyCorrectionUnallocatedKcal:
                currentEnergy.UnallocatedCorrectionKcal,
            SurplusRecoveryFraction:
                currentEnergy.SurplusRecoveryFraction,
            TodayBaselinePrescribedEnergyKcal:
                currentProjection.Today.BaselinePrescribedEnergyKcal,
            TodayEnergyCarryoverCorrectionKcal:
                currentProjection.Today.EnergyCarryoverCorrectionKcal,
            EnergyCarryoverSummary:
                energyCarryoverSummary,
            FourDayGlycogenDemandG:
                historicalWindow.TotalGlycogenDemandG,
            FourDayRestorationCreditG:
                historicalWindow.TotalRestorationCreditG,
            AbsoluteRestorationRatio:
                historicalWindow.AbsoluteRestorationRatio,
            FourDayModerateHardDays:
                historicalWindow.ModerateHardDays,
            FourDayHardDays:
                historicalWindow.HardDays,
            FourDayCarbohydrateEvidenceDays:
                checked((short)historicalWindow.Days.Count(
                    x => x.HasCarbohydrateEvidence)),
            TodayPlannedGlycogenDemandG:
                readiness.PlannedGlycogenDemandG,
            FunctionalTargetRatio:
                readiness.FunctionalTargetFraction,
            IncomingTargetAchievementRatio:
                readiness.IncomingAchievementRatio,
            IncomingReadinessBandCode:
                readiness.IncomingReadinessBandCode,
            HistoricalShortfallG:
                readiness.HistoricalShortfallG,
            RecoveryAllocationG:
                readiness.RecoveryAllocationG,
            PreWorkoutRescueG:
                Math.Min(
                    currentProjection.Today.RecommendedCarbsG,
                    readiness.PreWorkoutRescueG),
            ProjectedTargetAchievementRatio:
                readiness.ProjectedAchievementRatio,
            ProjectedReadinessBandCode:
                readiness.ProjectedReadinessBandCode,
            FuelActionLabel:
                actionLabel,
            ReasonCodes:
                readiness.ReasonCodes);
    }

    // B2.35.5.2 HISTORICAL DURATION FALLBACK
    private static decimal EffectiveCompletedDurationMinutes(
        WorkoutSession session)
    {
        if (session.DurationMin > 0m)
            return session.DurationMin;

        if (session.CreatedAtUtc != default
            && session.UpdatedAtUtc > session.CreatedAtUtc)
        {
            var elapsed =
                (decimal)(
                    session.UpdatedAtUtc
                    - session.CreatedAtUtc)
                    .TotalMinutes;

            if (elapsed > 0m)
            {
                return Math.Max(
                    1m,
                    Math.Round(
                        elapsed,
                        1,
                        MidpointRounding.AwayFromZero));
            }
        }

        return 1m;
    }
    public async Task<Engine30DashboardActualWorkoutProjection>
        BuildActualWorkoutAsync(
            AppClientContext context,
            DateOnly actionDate,
            CancellationToken cancellationToken = default)
    {
        if (context.Client is null || context.Program is null)
            throw new InvalidOperationException("Active client/program required.");

        var client =
            context.Client;

        var program =
            context.Program;

        ValidateClientProfile(
            client);

        var sessions =
            await db.WorkoutSessions
                .AsNoTracking()
                .Where(x =>
                    x.ClientId == client.ClientId
                    && x.ProgramId == program.ProgramId
                    && x.WorkoutDate == actionDate
                    && x.CompletedFlag)
                .OrderBy(x => x.CreatedAtUtc)
                .ToListAsync(cancellationToken);

        sessions =
            (await DetailedWorkoutEngine30Adapter
                .PreferBuilderSessionsAsync(
                    db,
                    sessions,
                    cancellationToken))
            .ToList();

        if (sessions.Count == 0)
        {
            return new Engine30DashboardActualWorkoutProjection(
                HasActualWorkout:
                    false,
                WorkoutSummary:
                    null,
                CompletedSessionCount:
                    0,
                TotalDurationMinutes:
                    0m,
                DurationWeightedRpe:
                    null,
                GrossWorkoutEnergyKcal:
                    0m,
                NetWorkoutEnergyKcal:
                    0m,
                GlycogenDemandG:
                    0m);
        }

        var measurements =
            await LoadMeasurementsAsync(
                client.ClientId,
                actionDate,
                cancellationToken);

        var measurement =
            ResolveMeasurement(
                measurements,
                actionDate);

        var roles =
            await activityProfiles.BuildRuntimeRolesAsync(
                client.ClientId,
                actionDate,
                cancellationToken);

        var gross =
            0m;

        var net =
            0m;

        var glycogen =
            0m;

        var totalDuration =
            0m;

        var rpeWeightedMinutes =
            0m;

        var rpeMinutes =
            0m;

        var labels =
            new List<string>();

        foreach (var stored in sessions)
        {
            var effectiveDurationMinutes =
                EffectiveCompletedDurationMinutes(stored);
            var workoutCode =
                NormalizeWorkoutCode(
                    stored.WorkoutTypeCode);

            Engine30WorkoutRuntimeProfileResolution profileResolution;

            try
            {
                profileResolution =
                    await profileProvider.ResolveAsync(
                        workoutCode,
                        cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                labels.Add(
                    $"{stored.WorkoutTypeCode} (legacy/unmapped)");

                continue;
            }

            var session =
                await DetailedWorkoutEngine30Adapter
                    .BuildActualInputAsync(
                        db,
                        workoutIntelligence,
                        stored,
                        profileResolution.Profile,
                        workoutCode,
                        cancellationToken);

            var runtime =
                await dailyRuntime.CalculateAsync(
                    new Engine30DailyRuntimeRequest(
                        ActionDate:
                            actionDate,
                        DateOfBirth:
                            client.DateOfBirth!.Value,
                        SexCode:
                            client.SexCode!,
                        HeightIn:
                            client.HeightIn,
                        WeightLb:
                            measurement.WeightLb!.Value,
                        BodyFatFraction:
                            NormalizeBodyFatFraction(
                                measurement.BodyFatPct),
                        OccupationalRoles:
                            roles,
                        LifestyleActivityCode:
                            client.LifestyleActivityCode ?? "MINIMAL",
                        WorkoutSession:
                            session),
                    cancellationToken);

            if (runtime.Workout is null)
                continue;

            gross +=
                runtime.Workout.Energy.GrossWorkoutEnergyKcal;

            net +=
                runtime.Workout.Energy.EngineNetWorkoutEnergyKcal;

            glycogen +=
                runtime.Glycogen.EstimatedGlycogenDepletedG;

            totalDuration +=
                effectiveDurationMinutes;

            if (
                stored.SessionRpe.HasValue
                && effectiveDurationMinutes > 0m
            ) {
                rpeWeightedMinutes +=
                    stored.SessionRpe.Value
                    * effectiveDurationMinutes;

                rpeMinutes +=
                    effectiveDurationMinutes;
            }

            labels.Add(
                runtime.Workout.ProfileProvenance.WorkoutDisplayName);
        }

        var distinctLabels =
            labels
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var summary =
            distinctLabels.Length switch
            {
                0 => "Completed workout",
                1 => distinctLabels[0],
                _ => string.Join(" + ", distinctLabels)
            };

        decimal? weightedRpe =
            rpeMinutes > 0m
                ? Math.Round(
                    rpeWeightedMinutes / rpeMinutes,
                    1,
                    MidpointRounding.AwayFromZero)
                : null;

        return new Engine30DashboardActualWorkoutProjection(
            HasActualWorkout:
                true,
            WorkoutSummary:
                summary,
            CompletedSessionCount:
                sessions.Count,
            TotalDurationMinutes:
                Math.Round(
                    totalDuration,
                    1,
                    MidpointRounding.AwayFromZero),
            DurationWeightedRpe:
                weightedRpe,
            GrossWorkoutEnergyKcal:
                Math.Round(
                    gross,
                    0,
                    MidpointRounding.AwayFromZero),
            NetWorkoutEnergyKcal:
                Math.Round(
                    net,
                    0,
                    MidpointRounding.AwayFromZero),
            GlycogenDemandG:
                Math.Round(
                    glycogen,
                    0,
                    MidpointRounding.AwayFromZero));
    }
    public static DateOnly WeekStart(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-diff);
    }

    public static string NormalizeWorkoutCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Workout code is required.", nameof(code));

        var normalized = code.Trim().ToUpperInvariant();

        return normalized switch
        {
            // Frozen/current recovery + cardio codes.
            "RECOVERY_0_RPE2" => "REST",
            "MOBILITY__POSTURE" => "MOBILITY_RECOVERY",
            "ZONE_2" or "Z2_40_RPE4" => "ZONE2_CARDIO",
            "PURE_CARDIO" => "GENERAL_CARDIO",

            // Frozen/current major-strength codes.
            "MAJOR_60_RPE8" or "MAJOR_MIXED" => "FULL_BODY_STRENGTH",

            // Frozen/current accessory-strength codes.
            "ACCESSORY_45_RPE7"
                or "ACCESSORY_UPPER"
                or "ACCESSORY_LOWER"
                or "ACCESSORY_MIXED"
                => "ACCESSORY",

            // CFG-1008 classifies all OTF Strength variants as
            // ACCESSORY_STRENGTH. Preserve that meaning in Engine 3.0.
            "OTF_STRENGTH_UPPER"
                or "OTF_STRENGTH_LOWER"
                or "OTF_STRENGTH_TOTAL"
                => "ACCESSORY",

            // Mixed / composite legacy codes.
            "ACCESSORY_MIXED_PLUS_CARDIO" => "MIXED_GYM_SESSION",
            "ACC_PLUS_Z2_75_RPE6" => "CUSTOM_COMPOSITE",

            // Conditioning legacy codes.
            "HIIT_45_RPE9" => "TRUE_HIIT",
            "LONG_MIXED_75_RPE9" => "MIXED_CONDITIONING",
            "OTF_STANDARD" => "ORANGETHEORY",
            "PICKLEBALL_120_RPE6" => "MIXED_CONDITIONING",

            // Engine 3.0 codes (and any not-yet-translated historical code)
            // pass through. Unresolvable historical records are handled
            // safely in CalculateActualDayGlycogenAsync.
            _ => normalized
        };
    }
    public static decimal? NormalizeBodyFatFraction(decimal? storedBodyFat)
    {
        if (!storedBodyFat.HasValue)
            return null;

        var value = storedBodyFat.Value;

        if (value < 0m)
            throw new ArgumentOutOfRangeException(nameof(storedBodyFat));

        return value > 1m
            ? value / 100m
            : value;
    }

    public static string HumanizeReadiness(string code)
        => code switch
        {
            Engine30FuelReadinessBandCodes.Adequate => "Adequate",
            Engine30FuelReadinessBandCodes.SlightlyReduced => "Slightly reduced",
            Engine30FuelReadinessBandCodes.ModeratelyLow => "Moderately low",
            Engine30FuelReadinessBandCodes.Low => "Low",
            Engine30FuelReadinessBandCodes.VeryLow => "Very low",
            _ => string.IsNullOrWhiteSpace(code)
                ? "Unknown"
                : code.Replace("_", " ")
        };

    private async Task<Engine30DashboardWeekProjection> BuildWeekAsync(
        Client client,
        ProgramRecord program,
        DateOnly weekStart,
        DateOnly knowledgeCutoffDate,
        string planCode,
        IReadOnlyDictionary<string, WorkoutTypeDefinition> definitions,
        IReadOnlyList<Measurement> measurements,
        CancellationToken cancellationToken)
    {
        var weekEnd = weekStart.AddDays(6);

        var planned =
            await db.PlannedWorkouts
                .AsNoTracking()
                .Where(x =>
                    x.ProgramId == program.ProgramId
                    && x.PlannedDate >= weekStart
                    && x.PlannedDate <= weekEnd)
                .ToListAsync(cancellationToken);

        var plannedByDate =
            planned.ToDictionary(
                x => x.PlannedDate,
                x => x);

        var dailyResults =
            new List<(
                Engine30DailyRuntimeResult Runtime,
                string? WorkoutCode,
                string WorkoutCategoryCode)>(7);

        for (var i = 0; i < 7; i++)
        {
            var date = weekStart.AddDays(i);

            string? workoutCode = null;
            string workoutCategoryCode =
                Engine30PrescriptionCategoryCodes.RestRecoveryOther;

            if (plannedByDate.TryGetValue(date, out var plannedWorkout))
            {
                workoutCode =
                    NormalizeWorkoutCode(
                        plannedWorkout.WorkoutTypeCode);

                if (!definitions.TryGetValue(
                        workoutCode,
                        out var definition))
                {
                    throw new InvalidOperationException(
                        $"Planned workout '{plannedWorkout.WorkoutTypeCode}' on {date:yyyy-MM-dd} " +
                        $"does not resolve to an active {ConfigVersionCode} workout.");
                }

                workoutCategoryCode =
                    definition.WorkoutCategoryCode;
            }

            var measurementDate =
                date > knowledgeCutoffDate
                    ? knowledgeCutoffDate
                    : date;

            var measurement =
                ResolveMeasurement(
                    measurements,
                    measurementDate);

            var roles =
                await activityProfiles.BuildRuntimeRolesAsync(
                    client.ClientId,
                    date,
                    cancellationToken);

            Engine30WorkoutSessionInput? session =
                workoutCode is null
                    ? null
                    : new Engine30WorkoutSessionInput(
                        WorkoutTypeCode: workoutCode,
                        PlannedDurationMinutes:
                            plannedWorkout?.PlannedDurationMin
                            ?? 0m,
                        PlannedRpeOverride:
                            plannedWorkout?.PlannedRpe);

            var runtime =
                await dailyRuntime.CalculateAsync(
                    new Engine30DailyRuntimeRequest(
                        ActionDate: date,
                        DateOfBirth: client.DateOfBirth!.Value,
                        SexCode: client.SexCode!,
                        HeightIn: client.HeightIn,
                        WeightLb: measurement.WeightLb!.Value,
                        BodyFatFraction:
                            NormalizeBodyFatFraction(
                                measurement.BodyFatPct),
                        OccupationalRoles: roles,
                        LifestyleActivityCode:
                            client.LifestyleActivityCode ?? "MINIMAL",
                        WorkoutSession: session),
                    cancellationToken);

            dailyResults.Add(
                (
                    runtime,
                    workoutCode,
                    workoutCategoryCode
                ));
        }

        var prescriptionInputs =
            dailyResults
                .Select(x =>
                    new Engine30PrescriptionDayInput(
                        Date: x.Runtime.ActionDate,
                        MaintenanceEnergyKcal:
                            x.Runtime.Energy.MaintenanceEnergyKcal,
                        WorkoutCategoryCode:
                            x.WorkoutCategoryCode,
                        GlycogenReloadRequirementG:
                            x.Runtime.Glycogen.CarbohydrateReloadRequirementG))
                .ToArray();

        var anchorDate =
            knowledgeCutoffDate < weekStart
                ? knowledgeCutoffDate
                : knowledgeCutoffDate > weekEnd
                    ? weekEnd
                    : knowledgeCutoffDate;

        var anchorMeasurement =
            ResolveMeasurement(
                measurements,
                anchorDate);

        var baselinePrescription =
            prescriptionService.Calculate(
                planCode,
                anchorMeasurement.WeightLb!.Value,
                client.HeightIn,
                prescriptionInputs);

        var energyPolicy =
            await energyPolicies.LoadAsync(
                planCode,
                cancellationToken);

        var prescription =
            energyPolicyAllocator.Apply(
                baselinePrescription,
                energyPolicy);

        var actualCalories =
            await db.NutritionDays
                .AsNoTracking()
                .Where(x =>
                    x.ClientId == client.ClientId
                    && x.ProgramId == program.ProgramId
                    && x.NutritionDate >= weekStart
                    && x.NutritionDate <= weekEnd)
                .ToDictionaryAsync(
                    x => x.NutritionDate,
                    x => x.ActualCalories,
                    cancellationToken);

        var carryover =
            energyCarryover.Apply(
                prescription,
                energyPolicy,
                knowledgeCutoffDate,
                actualCalories);

        var result =
            new Dictionary<DateOnly, Engine30DashboardDayProjection>();

        foreach (var item in dailyResults)
        {
            var carryoverDay =
                carryover.Days.Single(
                    x => x.Date == item.Runtime.ActionDate);

            var day =
                carryoverDay.AdjustedPrescription;

            var workout =
                item.Runtime.Workout;

            result[item.Runtime.ActionDate] =
                new Engine30DashboardDayProjection(
                    Date: item.Runtime.ActionDate,
                    HasPlannedWorkout: item.WorkoutCode is not null,
                    WorkoutTypeCode: item.WorkoutCode,
                    WorkoutDisplayName:
                        workout?.ProfileProvenance.WorkoutDisplayName
                        ?? "No workout planned",
                    WorkoutCategoryCode:
                        item.WorkoutCategoryCode,
                    PlannedDurationMinutes:
                        workout?.ResolvedSession.AnalysisDuration.Minutes
                        ?? 0m,
                    PlannedRpe:
                        workout?.ResolvedSession.PlannedRpe.Value,
                    BaseLivingEnergyKcal:
                        item.Runtime.Energy.BaseLivingEnergyKcal,
                    HabitualActivityEnergyKcal:
                        item.Runtime.Energy.HabitualActivityEnergyKcal,
                    GrossWorkoutEnergyKcal:
                        item.Runtime.Workout?.Energy.GrossWorkoutEnergyKcal
                        ?? 0m,
                    NetWorkoutActivityEnergyKcal:
                        item.Runtime.Energy.NetWorkoutActivityEnergyKcal,
                    MaintenanceEnergyKcal:
                        item.Runtime.Energy.MaintenanceEnergyKcal,
                    BaselinePrescribedEnergyKcal:
                        carryoverDay.BaselinePrescription.PrescribedEnergyKcal,
                    EnergyCarryoverCorrectionKcal:
                        carryoverDay.TotalCorrectionAppliedKcal,
                    PrescribedEnergyKcal:
                        day.PrescribedEnergyKcal,
                    ProteinG:
                        day.ProteinG,
                    RecommendedCarbsG:
                        day.RecommendedCarbsG,
                    FatG:
                        day.FatG,
                    PhysiologicalCarbFloorG:
                        day.PhysiologicalCarbFloorG,
                    BaseRestCarbsG:
                        prescription.BaseRestCarbsG,
                    EffectiveDeficitPct:
                        prescription.EffectiveDeficitPct,
                    CarbGoverningRuleCode:
                        day.CarbGoverningRuleCode,
                    GlycogenDemandG:
                        item.Runtime.Glycogen.EstimatedGlycogenDepletedG,
                    GlycogenReloadRequirementG:
                        item.Runtime.Glycogen.CarbohydrateReloadRequirementG,
                    EngineVersionCode:
                        EngineVersionCode,
                    ConfigVersionCode:
                        workout?.ProfileProvenance.ConfigVersionCode
                        ?? ConfigVersionCode,
                    WorkoutProfileVersionCode:
                        workout?.ProfileProvenance.ProfileVersionCode,
                    WorkoutProfileSourceCode:
                        workout?.ProfileProvenance.SourceCode);
        }

        return new Engine30DashboardWeekProjection(
            PlanCode: planCode,
            WeekStart: weekStart,
            Days: result,
            EnergyCarryover: carryover);
    }

    private async Task<(decimal DemandG, string? Summary)>
        CalculateActualDayGlycogenAsync(
            Client client,
            DateOnly date,
            IReadOnlyList<WorkoutSession> sessions,
            IReadOnlyList<Measurement> measurements,
            CancellationToken cancellationToken)
    {
        if (sessions.Count == 0)
            return (0m, null);

        var measurement =
            ResolveMeasurement(
                measurements,
                date);

        var roles =
            await activityProfiles.BuildRuntimeRolesAsync(
                client.ClientId,
                date,
                cancellationToken);

        var demand = 0m;
        var summaries = new List<string>();

        foreach (var stored in sessions)
        {
            var effectiveDurationMinutes =
                EffectiveCompletedDurationMinutes(stored);
            var workoutCode =
                NormalizeWorkoutCode(
                    stored.WorkoutTypeCode);

            Engine30WorkoutRuntimeProfileResolution profileResolution;

            try
            {
                profileResolution =
                    await profileProvider.ResolveAsync(
                        workoutCode,
                        cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                summaries.Add(
                    $"{stored.WorkoutTypeCode}  -  {stored.DurationMin:0} min " +
                    "(legacy workout not mapped to an active Engine 3.0 profile; " +
                    "excluded from historical glycogen modeling)");

                continue;
            }

            var session =
                await DetailedWorkoutEngine30Adapter
                    .BuildActualInputAsync(
                        db,
                        workoutIntelligence,
                        stored,
                        profileResolution.Profile,
                        workoutCode,
                        cancellationToken);

            var runtime =
                await dailyRuntime.CalculateAsync(
                    new Engine30DailyRuntimeRequest(
                        ActionDate:
                            date,
                        DateOfBirth:
                            client.DateOfBirth!.Value,
                        SexCode:
                            client.SexCode!,
                        HeightIn:
                            client.HeightIn,
                        WeightLb:
                            measurement.WeightLb!.Value,
                        BodyFatFraction:
                            NormalizeBodyFatFraction(
                                measurement.BodyFatPct),
                        OccupationalRoles:
                            roles,
                        LifestyleActivityCode:
                            client.LifestyleActivityCode ?? "MINIMAL",
                        WorkoutSession:
                            session),
                    cancellationToken);

            demand +=
                runtime.Glycogen.EstimatedGlycogenDepletedG;

            var rpe =
                stored.SessionRpe.HasValue
                    ? $", RPE {stored.SessionRpe.Value:0.#}"
                    : "";

            summaries.Add(
                $"{runtime.Workout!.ProfileProvenance.WorkoutDisplayName}  -  " +
                $"{stored.DurationMin:0} min{rpe}");
        }

        return (
            Math.Round(
                demand,
                2,
                MidpointRounding.AwayFromZero),
            string.Join(" + ", summaries));
    }

    private static IReadOnlyCollection<Engine30WorkoutSegmentInput>?
        BuildActualResistanceSegment(
            Engine30WorkoutAdminProfile profile,
            WorkoutSession stored)
    {
        if (!stored.TypicalRestSec.HasValue)
            return null;

        if (!profile.WorkoutFamilyCode.Equals(
                Engine30WorkoutFamilyCodes.Resistance,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var plannedDuration =
            profile.DefaultDurationMinutes > 0m
                ? profile.DefaultDurationMinutes
                : Math.Max(1m, stored.DurationMin);

        return new[]
        {
            new Engine30WorkoutSegmentInput(
                SegmentCode:
                    "SESSION",
                ModalityCode:
                    profile.DefaultModalityCode,
                PlannedDurationMinutes:
                    plannedDuration,
                DefaultRpe:
                    profile.DefaultRpe,
                ActualDurationMinutes:
                    stored.DurationMin,
                ActualRpe:
                    stored.SessionRpe,
                Resistance:
                    new Engine30ResistanceSegmentDetails(
                        AverageRestSeconds:
                            stored.TypicalRestSec.Value))
        };
    }

    private static string BuildEnergyCarryoverSummary(
        DateOnly actionDate,
        DateOnly previousDate,
        decimal? previousEnergyVarianceKcal,
        decimal? previousEligibleEnergyCarryoverKcal,
        string todayCategoryCode,
        Engine30EnergyCarryoverResult current)
    {
        string prefix;

        if (!previousEnergyVarianceKcal.HasValue)
        {
            prefix =
                "No completed calorie actual was logged yesterday, so yesterday created no energy correction.";
        }
        else if (previousEnergyVarianceKcal.Value <= 0m)
        {
            prefix =
                $"Yesterday was {previousEnergyVarianceKcal.Value:+0;-0;0} kcal versus target. " +
                "Engine 3.0 does not bank under-target intake into extra future calories.";
        }
        else if (WeekStart(previousDate) != WeekStart(actionDate))
        {
            prefix =
                $"Yesterday was +{previousEnergyVarianceKcal.Value:0} kcal over target, " +
                "but it was in the prior prescription week. Energy carryover expires at the week boundary.";
        }
        else
        {
            prefix =
                $"Yesterday was +{previousEnergyVarianceKcal.Value:0} kcal over target. " +
                $"At the Admin recovery setting of {current.SurplusRecoveryFraction:P0}, " +
                $"{previousEligibleEnergyCarryoverKcal.GetValueOrDefault():0} kcal became eligible for same-week correction.";
        }

        string allocation;

        if (current.TotalEligibleCorrectionKcal <= 0m)
        {
            allocation =
                " No current-week calorie correction is active.";
        }
        else
        {
            var todayProtected =
                todayCategoryCode.Equals(
                    Engine30PrescriptionCategoryCodes.MajorStrength,
                    StringComparison.OrdinalIgnoreCase)
                && current.CorrectionAppliedOnAsOfDateKcal <= 0m;

            allocation =
                $" Current-week correction: {current.CorrectionAppliedOnAsOfDateKcal:0} kcal applied today, " +
                $"{current.CorrectionAppliedAfterAsOfDateKcal:0} kcal assigned to later days";

            if (todayProtected)
            {
                allocation +=
                    "; today's Major Strength prescription is protected";
            }

            if (current.UnallocatedCorrectionKcal > 0m)
            {
                allocation +=
                    $"; {current.UnallocatedCorrectionKcal:0} kcal could not be placed safely and remains unallocated";
            }

            allocation += ".";
        }

        return prefix + allocation;
    }
    private static string BuildPreviousFuelSummary(
        Engine30FuelLedgerDay previous,
        decimal? restorationRatio)
    {
        if (!previous.HasCarbohydrateEvidence)
        {
            return "No carbohydrate actual was logged yesterday. Engine 3.0 preserves that as missing evidence rather than pretending intake was zero.";
        }

        if (previous.GlycogenDemandG <= 0m)
        {
            return "No meaningful completed-workout glycogen demand was modeled yesterday, so yesterday contributes little demand to the four-day fuel ledger.";
        }

        if (!restorationRatio.HasValue)
        {
            return "Yesterday has carbohydrate evidence, but the refill ratio could not be resolved.";
        }

        if (restorationRatio.Value >= 0.90m)
        {
            return $"Yesterday supplied about {previous.RestorationCreditG:0} g of restoration credit against {previous.GlycogenDemandG:0} g of Engine 3.0 modeled demand - most of that day's demand was covered.";
        }

        if (restorationRatio.Value >= 0.60m)
        {
            return $"Yesterday supplied about {previous.RestorationCreditG:0} g of restoration credit against {previous.GlycogenDemandG:0} g of Engine 3.0 modeled demand. The remaining burden carries into today's four-day calculation.";
        }

        return $"Yesterday supplied about {previous.RestorationCreditG:0} g of restoration credit against {previous.GlycogenDemandG:0} g of Engine 3.0 modeled demand. Refill was limited, so today's fuel allocation gives recovery more attention.";
    }

    private static string FuelActionLabel(
        Engine30DashboardDayProjection today,
        Engine30FuelReadinessResult readiness)
    {
        if (!today.HasPlannedWorkout
            || readiness.PlannedGlycogenDemandG <= 0m)
        {
            return "NO FUEL RESCUE NEEDED";
        }

        if (!readiness.RescueRecommended)
        {
            return "FUEL READY  -  FOLLOW PLAN";
        }

        if (readiness.RescueCanReachAdequateReadiness)
        {
            return "FUEL BEFORE TRAINING  -  PLAN SUPPORTED";
        }

        return "FUEL LIMITED  -  REVIEW WITH COACH";
    }

    private async Task<IReadOnlyDictionary<string, WorkoutTypeDefinition>>
        LoadEngine30DefinitionsAsync(
            CancellationToken cancellationToken)
    {
        var config =
            await db.ConfigVersions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.VersionCode == ConfigVersionCode,
                    cancellationToken)
            ?? throw new InvalidOperationException(
                $"Engine 3.0 configuration '{ConfigVersionCode}' was not found.");

        return await db.WorkoutTypeDefinitions
            .AsNoTracking()
            .Where(x =>
                x.ConfigVersionId == config.ConfigVersionId
                && x.IsActive)
            .ToDictionaryAsync(
                x => x.WorkoutTypeCode,
                StringComparer.OrdinalIgnoreCase,
                cancellationToken);
    }

    private async Task<IReadOnlyList<Measurement>> LoadMeasurementsAsync(
        Guid clientId,
        DateOnly knowledgeCutoffDate,
        CancellationToken cancellationToken)
        =>
            await db.Measurements
                .AsNoTracking()
                .Where(x =>
                    x.ClientId == clientId
                    && !x.IsDeleted
                    && x.WeightLb != null
                    && x.LocalMeasurementDate <= knowledgeCutoffDate)
                .OrderByDescending(x => x.LocalMeasurementDate)
                .ThenByDescending(x => x.MeasurementAtUtc)
                .ToListAsync(cancellationToken);

    private static Measurement ResolveMeasurement(
        IEnumerable<Measurement> measurements,
        DateOnly date)
        =>
            measurements.FirstOrDefault(
                x => x.LocalMeasurementDate <= date)
            ?? throw new InvalidOperationException(
                $"Engine 3.0 Dashboard needs at least one valid weight on or before {date:yyyy-MM-dd}.");

    private static void ValidateClientProfile(Client client)
    {
        if (!client.DateOfBirth.HasValue)
            throw new InvalidOperationException(
                "Engine 3.0 Dashboard requires date of birth.");

        if (string.IsNullOrWhiteSpace(client.SexCode))
            throw new InvalidOperationException(
                "Engine 3.0 Dashboard requires sex.");

        if (client.HeightIn <= 0m)
            throw new InvalidOperationException(
                "Engine 3.0 Dashboard requires height.");
    }
}













