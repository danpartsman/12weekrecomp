using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

/// <summary>
/// Engine 2.1 shadow representation of the rolling four-day
/// glycogen demand / carbohydrate restoration window.
///
/// This deliberately preserves the frozen Engine 2.0 methodology
/// while evaluating the person's history against CFG-1008.0
/// configuration and plan definitions.
///
/// It does not write data and does not alter the authoritative
/// Engine 2.0 recommendation path.
/// </summary>
public sealed record Engine21FuelLedgerDay(
    DateOnly Date,
    decimal DemandG,
    decimal RestorationCreditG,
    decimal MaxFuelScore,
    bool ModerateHard,
    bool Hard,
    bool HasCarbohydrateEvidence);

public sealed record Engine21FuelWindow(
    decimal DemandG,
    decimal RestorationCreditG,
    decimal AbsoluteRestorationRatio,
    short ModerateHardDays,
    short HardDays,
    IReadOnlyList<Engine21FuelLedgerDay> Days);

public sealed class Engine21FuelWindowService(
    RecompDbContext db,
    ConfigurationService configuration,
    ProgramPlanService planSelection)
{
    public async Task<Engine21FuelWindow> CalculateAsync(
        ProgramRecord program,
        DateOnly actionDate,
        Guid engine21ConfigVersionId,
        IReadOnlyDictionary<string, WorkoutTypeDefinition> workoutTypes,
        IReadOnlyDictionary<string, decimal> settings,
        CancellationToken cancellationToken = default)
    {
        var start =
            actionDate.AddDays(-4);

        var end =
            actionDate.AddDays(-1);

        //
        // Preserve the existing Engine 2.0 chronology:
        // only the four completed calendar days immediately
        // preceding the action date contribute to this window.
        //
        var sessions =
            await db.WorkoutSessions
                .AsNoTracking()
                .Where(
                    x =>
                        x.ProgramId == program.ProgramId
                        && x.CompletedFlag
                        && x.WorkoutDate >= start
                        && x.WorkoutDate <= end)
                .ToListAsync(
                    cancellationToken);

        var nutrition =
            await db.NutritionDays
                .AsNoTracking()
                .Where(
                    x =>
                        x.ProgramId == program.ProgramId
                        && x.NutritionDate >= start
                        && x.NutritionDate <= end)
                .ToDictionaryAsync(
                    x => x.NutritionDate,
                    cancellationToken);

        var backgroundPct =
            ConfigurationService.Setting(
                settings,
                "BACKGROUND_BASE_CARB_CREDIT_PCT",
                0.20m);

        var carbRounding =
            ConfigurationService.Setting(
                settings,
                "CARB_ROUNDING_G",
                5m);

        if (backgroundPct < 0m)
        {
            throw new InvalidOperationException(
                "BACKGROUND_BASE_CARB_CREDIT_PCT cannot be negative.");
        }

        if (carbRounding <= 0m)
        {
            throw new InvalidOperationException(
                "CARB_ROUNDING_G must be greater than zero.");
        }

        decimal totalDemand =
            0m;

        decimal totalCredit =
            0m;

        short moderateHard =
            0;

        short hard =
            0;

        var days =
            new List<Engine21FuelLedgerDay>(
                capacity: 4);

        foreach (
            var date in
            Enumerable.Range(0, 4)
                .Select(i => start.AddDays(i)))
        {
            var daySessions =
                sessions
                    .Where(
                        x => x.WorkoutDate == date)
                    .ToList();

            //
            // Preserve the actual-session glycogen result already stored
            // by Engine 2.0. Engine 2.1 has not changed the glycogen
            // expenditure equation, so we should not manufacture a
            // second historical actual-workout result here.
            //
            var dayDemand =
                daySessions.Sum(
                    x =>
                        x.CalculatedScaledGlycogenBurnG
                        ?? 0m);

            totalDemand +=
                dayDemand;

            //
            // FuelScore classification is evaluated against the
            // CFG-1008.0 workout catalog passed into this service.
            //
            var maxFuel =
                daySessions
                    .Select(
                        x =>
                            workoutTypes.TryGetValue(
                                x.WorkoutTypeCode,
                                out var workout)
                                ? workout.FuelScore
                                : 0m)
                    .DefaultIfEmpty(0m)
                    .Max();

            if (maxFuel >= 2m)
            {
                moderateHard++;
            }

            if (maxFuel >= 3m)
            {
                hard++;
            }

            //
            // Retain whether real carbohydrate intake exists separately
            // from whether the day had modeled glycogen demand.
            //
            var hasCarbohydrateEvidence =
                nutrition.TryGetValue(
                    date,
                    out var dayNutrition)
                && dayNutrition.ActualCarbsG.HasValue;

            decimal dayCredit =
                0m;

            //
            // No modeled demand means there is nothing to restore in the
            // glycogen ledger. Missing carbohydrate intake remains missing
            // evidence rather than being converted to zero.
            //
            if (dayDemand > 0m &&
                hasCarbohydrateEvidence)
            {
                var carbs =
                    dayNutrition!.ActualCarbsG!.Value;

                var dayPlanCode =
                    await planSelection.GetPlanCodeForDateAsync(
                        program,
                        date,
                        cancellationToken);

                //
                // IMPORTANT:
                // Resolve the historical plan against CFG-1008.0 rather
                // than program.ConfigVersionId.
                //
                var dayPlan =
                    await configuration.GetPlanAsync(
                        engine21ConfigVersionId,
                        dayPlanCode,
                        cancellationToken);

                //
                // Preserve Engine 2.0 restoration-credit semantics.
                //
                var credit =
                    Math.Min(
                        dayDemand,

                        Math.Max(
                            0m,
                            carbs - dayPlan.BaseRestCarbsG)

                        + Math.Min(
                            carbs,
                            dayPlan.BaseRestCarbsG)
                          * backgroundPct);

                dayCredit =
                    WorkoutMathService.RoundToIncrement(
                        credit,
                        carbRounding);

                totalCredit +=
                    dayCredit;
            }

            days.Add(
                new Engine21FuelLedgerDay(
                    Date:
                        date,

                    DemandG:
                        dayDemand,

                    RestorationCreditG:
                        dayCredit,

                    MaxFuelScore:
                        maxFuel,

                    ModerateHard:
                        maxFuel >= 2m,

                    Hard:
                        maxFuel >= 3m,

                    HasCarbohydrateEvidence:
                        hasCarbohydrateEvidence));
        }

        var ratio =
            totalDemand <= 0m
                ? 1m
                : Math.Min(
                    1m,
                    totalCredit / totalDemand);

        return new Engine21FuelWindow(
            DemandG:
                totalDemand,

            RestorationCreditG:
                totalCredit,

            AbsoluteRestorationRatio:
                ratio,

            ModerateHardDays:
                moderateHard,

            HardDays:
                hard,

            Days:
                days);
    }
}


