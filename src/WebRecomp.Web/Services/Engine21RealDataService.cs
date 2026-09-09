using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

/// <summary>
/// Provenance around the real application data used by the
/// Engine 2.1 shadow calculation.
/// </summary>
public sealed record Engine21RealDataResult(
    string ConfigVersionCode,
    DateOnly ActionDate,
    DateOnly WeekStart,
    decimal WeightLb,
    DateOnly WeightMeasurementDate,
    DateOnly StepWindowStart,
    DateOnly StepWindowEnd,
    int StepCheckinCount,
    int ValidStepDays,
    IReadOnlyList<Engine21PlannedEnergyDay> PlannedDays,
    Engine21EnergyPipelineResult Pipeline,
    string ActionDateWorkoutTypeCode,
    Engine21FuelWindow ActionDateFuelWindow,
    Engine21DailyMacroResult ActionDateMacro,
    IReadOnlyList<Engine21WeeklyMacroDayResult> ProjectedMacroDays);

/// <summary>
/// Loads actual 12WR application data and composes it into the
/// Engine 2.1 energy pipeline.
///
/// IMPORTANT:
/// This is currently a SHADOW / DIAGNOSTIC calculation.
/// It explicitly loads CFG-1008.0 and does not modify the
/// Program's existing ConfigVersionId or authoritative
/// Engine 2.0 recommendation.
/// </summary>
public sealed class Engine21RealDataService(
    RecompDbContext db,
    ConfigurationService configuration,
    WorkoutMathService workoutMath,
    ProgramPlanService planSelection,
    Engine21PipelineService pipeline,
    Engine21FuelWindowService fuelWindow,
    Engine21DailyMacroService dailyMacro,
    Engine21WeeklyMacroProjectionService weeklyMacroProjection)
{
    private const string Engine21ConfigCode = "CFG-1008.0";

    public async Task<Engine21RealDataResult> CalculateAsync(
        Client client,
        ProgramRecord program,
        DateOnly actionDate,
        CancellationToken cancellationToken = default)
    {
        ValidateProfile(client);

        // ------------------------------------------------------------
        // 1. Load Engine 2.1 configuration explicitly.
        //
        // Do NOT use program.ConfigVersionId here yet. The real program
        // remains on its frozen Engine 2.0 configuration while v1008
        // is evaluated in shadow mode.
        // ------------------------------------------------------------

        var engine21Config =
            await db.ConfigVersions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.VersionCode == Engine21ConfigCode,
                    cancellationToken)
            ?? throw new InvalidOperationException(
                $"{Engine21ConfigCode} was not found.");

        var settings =
            await configuration.GetSettingsAsync(
                engine21Config.ConfigVersionId,
                cancellationToken);

        var workoutTypes =
            await configuration.GetWorkoutTypesAsync(
                engine21Config.ConfigVersionId,
                cancellationToken);

        if (workoutTypes.Count == 0)
        {
            throw new InvalidOperationException(
                $"{Engine21ConfigCode} contains no active workout definitions.");
        }

        // ------------------------------------------------------------
        // 2. Resolve the person's effective Recomp plan for this date.
        //
        // This preserves existing plan-change history semantics rather
        // than blindly reading Program.SelectedPlanCode.
        // ------------------------------------------------------------

        var planCode =
            await planSelection.GetPlanCodeForDateAsync(
                program,
                actionDate,
                cancellationToken);

        // ------------------------------------------------------------
        // 3. Most recent valid weight on or before actionDate.
        // ------------------------------------------------------------

        var latestMeasurement =
            await db.Measurements
                .AsNoTracking()
                .Where(
                    x =>
                        x.ClientId == client.ClientId
                        && !x.IsDeleted
                        && x.LocalMeasurementDate <= actionDate
                        && x.WeightLb != null)
                .OrderByDescending(
                    x => x.LocalMeasurementDate)
                .ThenByDescending(
                    x => x.MeasurementAtUtc)
                .FirstOrDefaultAsync(
                    cancellationToken)
            ?? throw new InvalidOperationException(
                "Engine 2.1 requires a current body weight.");

        var weightLb =
            latestMeasurement.WeightLb!.Value;

        // ------------------------------------------------------------
        // 4. Recent habitual activity.
        //
        // First real-data implementation uses the most recent
        // 14-calendar-day window ending on actionDate.
        //
        // Missing step entries remain missing. They are NOT converted
        // to zero.
        // ------------------------------------------------------------

        var stepWindowStart =
            actionDate.AddDays(-13);

        var stepRows =
            await db.DailyCheckins
                .AsNoTracking()
                .Where(
                    x =>
                        x.ClientId == client.ClientId
                        && x.ProgramId == program.ProgramId
                        && x.CheckinDate >= stepWindowStart
                        && x.CheckinDate <= actionDate)
                .OrderByDescending(
                    x => x.CheckinDate)
                .Select(
                    x => x.Steps)
                .ToListAsync(
                    cancellationToken);

        var validStepDays =
            stepRows.Count(
                x => x.HasValue
                     && x.Value >= 0);

        // ------------------------------------------------------------
        // 5. Load the actual saved seven-day workout plan.
        // ------------------------------------------------------------

        var weekStart =
            WeeklyStrategyService.WeekStart(
                actionDate);

        var weekEnd =
            weekStart.AddDays(6);

        var savedPlan =
            await db.PlannedWorkouts
                .AsNoTracking()
                .Where(
                    x =>
                        x.ProgramId == program.ProgramId
                        && x.PlannedDate >= weekStart
                        && x.PlannedDate <= weekEnd)
                .OrderBy(
                    x => x.PlannedDate)
                .ToListAsync(
                    cancellationToken);

        if (savedPlan.Count != 7)
        {
            throw new InvalidOperationException(
                $"Engine 2.1 requires seven saved planned-workout days. " +
                $"Week {weekStart:yyyy-MM-dd} contains {savedPlan.Count}.");
        }

        if (savedPlan
            .Select(x => x.PlannedDate)
            .Distinct()
            .Count() != 7)
        {
            throw new InvalidOperationException(
                "Engine 2.1 found duplicate dates in the saved weekly plan.");
        }

        // ------------------------------------------------------------
        // 6. Convert real planned workouts into Engine 2.1
        //    physiological energy inputs.
        // ------------------------------------------------------------

        var plannedEnergyDays =
            new List<Engine21PlannedEnergyDay>(
                capacity: 7);

        foreach (var planned in savedPlan)
        {
            if (!workoutTypes.TryGetValue(
                    planned.WorkoutTypeCode,
                    out var workout))
            {
                throw new InvalidOperationException(
                    $"{Engine21ConfigCode} does not contain saved workout " +
                    $"type '{planned.WorkoutTypeCode}' for " +
                    $"{planned.PlannedDate:yyyy-MM-dd}.");
            }

            if (string.IsNullOrWhiteSpace(
                    workout.WorkoutCategoryCode))
            {
                throw new InvalidOperationException(
                    $"Workout '{workout.WorkoutTypeCode}' does not have " +
                    "an Engine 2.1 workout category.");
            }

            var workoutCalories =
                workoutMath.CalculatePlannedWorkoutCalories(
                    weightLb,
                    workout,
                    settings);

            plannedEnergyDays.Add(
                new Engine21PlannedEnergyDay(
                    Date:
                        planned.PlannedDate,

                    WorkoutCategoryCode:
                        workout.WorkoutCategoryCode,

                    PlannedWorkoutCalories:
                        workoutCalories,

                    PlannedWorkoutMinutes:
                        workout.DefaultMinutes));
        }

        // ------------------------------------------------------------
        // 7. Compose the real person's Engine 2.1 input.
        // ------------------------------------------------------------

        var person =
            new Engine21PersonEnergyInput(
                ActionDate:
                    actionDate,

                DateOfBirth:
                    client.DateOfBirth!.Value,

                SexCode:
                    client.SexCode!,

                HeightIn:
                    client.HeightIn,

                WeightLb:
                    weightLb,

                BaselineStepsDay:
                    client.BaselineStepsDay,

                RecentDailySteps:
                    stepRows,

                PlanCode:
                    planCode);

        var result =
            pipeline.Calculate(
                person,
                plannedEnergyDays,
                settings);

        // ------------------------------------------------------------
        // 8. Build the action-date Engine 2.1 macro diagnostic.
        //
        // This is still SHADOW ONLY.
        //
        // Protein is intentionally stable here. We preserve the
        // established recomp protein model but do not apply the old
        // workout-specific ProteinAdjustmentG, because Engine 2.1
        // periodizes carbohydrate rather than protein.
        //
        // Fat comes from the stable weekly Engine 2.1 fat target.
        //
        // Carbohydrate is then determined from:
        //
        //   existing glycogen/readiness physiology
        //              +
        //   Engine 2.1 intended daily energy
        // ------------------------------------------------------------

        var actionPlanned =
            savedPlan.Single(
                x => x.PlannedDate == actionDate);

        if (!workoutTypes.TryGetValue(
                actionPlanned.WorkoutTypeCode,
                out var actionWorkout))
        {
            throw new InvalidOperationException(
                $"{Engine21ConfigCode} does not contain action-date workout " +
                $"'{actionPlanned.WorkoutTypeCode}'.");
        }

        var actionPlan =
            await configuration.GetPlanAsync(
                engine21Config.ConfigVersionId,
                planCode,
                cancellationToken);

        var actionEnergyDay =
            result.WeeklyBudget.Days.Single(
                x => x.Date == actionDate);

        var actionFuelWindow =
            await fuelWindow.CalculateAsync(
                program,
                actionDate,
                engine21Config.ConfigVersionId,
                workoutTypes,
                settings,
                cancellationToken);

        var proteinIncrement =
            ConfigurationService.Setting(
                settings,
                "PROTEIN_ROUNDING_G",
                5m);

        var proteinPerLb =
            ConfigurationService.Setting(
                settings,
                "PROTEIN_PER_LB",
                0.88m);

        var proteinMin =
            ConfigurationService.Setting(
                settings,
                "PROTEIN_MIN_G",
                175m);

        var proteinMax =
            ConfigurationService.Setting(
                settings,
                "PROTEIN_MAX_G",
                195m);

        if (proteinIncrement <= 0m)
        {
            throw new InvalidOperationException(
                "PROTEIN_ROUNDING_G must be greater than zero.");
        }

        var stableProteinG =
            WorkoutMathService.RoundToIncrement(
                Math.Clamp(
                    weightLb * proteinPerLb,
                    proteinMin,
                    proteinMax),
                proteinIncrement);

        var actionDateMacro =
            dailyMacro.Calculate(
                new Engine21DailyMacroInput(
                    WeightLb:
                        weightLb,

                    Plan:
                        actionPlan,

                    Workout:
                        actionWorkout,

                    FuelDemandG:
                        actionFuelWindow.DemandG,

                    FuelRestorationCreditG:
                        actionFuelWindow.RestorationCreditG,

                    FuelAbsoluteRestorationRatio:
                        actionFuelWindow.AbsoluteRestorationRatio,

                    ModerateHardDays:
                        actionFuelWindow.ModerateHardDays,

                    HardDays:
                        actionFuelWindow.HardDays,

                    ProteinG:
                        stableProteinG,

                    FatG:
                        result.FatTarget.RecommendedFatG,

                    IntendedEnergyTargetKcal:
                        actionEnergyDay.IntendedEnergyTargetKcal),
                settings);
        // ------------------------------------------------------------
        // 9. Sequential macro projection from actionDate through
        //    the end of the saved week.
        //
        // The action-date fuel window contains the real four historical
        // days immediately before actionDate. Each projected day's
        // prescribed carbohydrate then rolls into the next day's
        // modeled fuel state.
        //
        // SHADOW / DIAGNOSTIC ONLY.
        // ------------------------------------------------------------

        var projectedMacroInputs =
            new List<Engine21WeeklyMacroDayInput>();

        for (
            var projectedDate = actionDate;
            projectedDate <= weekEnd;
            projectedDate = projectedDate.AddDays(1))
        {
            var projectedPlanned =
                savedPlan.Single(
                    x => x.PlannedDate == projectedDate);

            if (!workoutTypes.TryGetValue(
                    projectedPlanned.WorkoutTypeCode,
                    out var projectedWorkout))
            {
                throw new InvalidOperationException(
                    $"{Engine21ConfigCode} does not contain projected workout " +
                    $"'{projectedPlanned.WorkoutTypeCode}' for " +
                    $"{projectedDate:yyyy-MM-dd}.");
            }

            var projectedPlanCode =
                await planSelection.GetPlanCodeForDateAsync(
                    program,
                    projectedDate,
                    cancellationToken);

            var projectedPlan =
                await configuration.GetPlanAsync(
                    engine21Config.ConfigVersionId,
                    projectedPlanCode,
                    cancellationToken);

            var projectedEnergyDay =
                result.WeeklyBudget.Days.Single(
                    x => x.Date == projectedDate);

            projectedMacroInputs.Add(
                new Engine21WeeklyMacroDayInput(
                    Date:
                        projectedDate,

                    Plan:
                        projectedPlan,

                    Workout:
                        projectedWorkout,

                    IntendedEnergyTargetKcal:
                        projectedEnergyDay.IntendedEnergyTargetKcal));
        }

        var projectedMacroDays =
            weeklyMacroProjection.Calculate(
                weightLb:
                    weightLb,

                proteinG:
                    stableProteinG,

                fatG:
                    result.FatTarget.RecommendedFatG,

                initialFuelWindow:
                    actionFuelWindow,

                plannedDays:
                    projectedMacroInputs,

                settings:
                    settings);

        //
        // The first sequential result must reproduce the independently
        // calculated action-date macro result. This protects against
        // the two shadow paths silently diverging.
        //
        var projectedActionDate =
            projectedMacroDays.Single(
                x => x.Date == actionDate);

        if (
            projectedActionDate.Macro.Reconciliation.RecommendedCarbsG
                != actionDateMacro.Reconciliation.RecommendedCarbsG
            ||
            projectedActionDate.Macro.CarbRequirement.PhysiologicalCarbFloorG
                != actionDateMacro.CarbRequirement.PhysiologicalCarbFloorG
            ||
            projectedActionDate.Macro.Reconciliation.DerivedCaloriesKcal
                != actionDateMacro.Reconciliation.DerivedCaloriesKcal)
        {
            throw new InvalidOperationException(
                "Engine 2.1 action-date macro calculation diverged from " +
                "the sequential weekly projection.");
        }
        return new Engine21RealDataResult(
            ConfigVersionCode:
                engine21Config.VersionCode,

            ActionDate:
                actionDate,

            WeekStart:
                weekStart,

            WeightLb:
                weightLb,

            WeightMeasurementDate:
                latestMeasurement.LocalMeasurementDate,

            StepWindowStart:
                stepWindowStart,

            StepWindowEnd:
                actionDate,

            StepCheckinCount:
                stepRows.Count,

            ValidStepDays:
                validStepDays,

            PlannedDays:
                plannedEnergyDays,

            Pipeline:
                result,

            ActionDateWorkoutTypeCode:
                actionWorkout.WorkoutTypeCode,

            ActionDateFuelWindow:
                actionFuelWindow,

            ActionDateMacro:
                actionDateMacro,

            ProjectedMacroDays:
                projectedMacroDays);
    }

    private static void ValidateProfile(
        Client client)
    {
        if (!client.DateOfBirth.HasValue)
        {
            throw new InvalidOperationException(
                "Engine 2.1 requires date of birth.");
        }

        if (string.IsNullOrWhiteSpace(
                client.SexCode))
        {
            throw new InvalidOperationException(
                "Engine 2.1 requires sex for the population energy equation.");
        }

        if (client.HeightIn <= 0m)
        {
            throw new InvalidOperationException(
                "Engine 2.1 requires height.");
        }
    }
}


