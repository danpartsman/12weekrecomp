using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine21WeeklyMacroProjectionServiceTests
{
    private readonly Engine21WeeklyMacroProjectionService _service =
        new(
            new Engine21DailyMacroService(
                new Engine21CarbRequirementService(
                    new WorkoutMathService(),
                    new RecompEngineService()),
                new Engine21MacroReconciliationService()),
            new Engine21ProjectedFuelWindowService());

    private static readonly IReadOnlyDictionary<string, decimal> Settings =
        new Dictionary<string, decimal>
        {
            ["CARB_ROUNDING_G"] = 5m,
            ["REFERENCE_WEIGHT_LB"] = 210m,
            ["BODY_WEIGHT_GLYCOGEN_EXPONENT"] = 0.5m,
            ["GLYCOGEN_ESTIMATE_MULTIPLIER"] = 0.65m,
            ["BACKGROUND_BASE_CARB_CREDIT_PCT"] = 0.20m,
            ["CARB_STANDARD_RECOMP_MAX_G_PER_KG"] = 5m
        };

    [Fact]
    public void Day_one_prescription_changes_day_two_incoming_fuel_state()
    {
        var initialFuel =
            new Engine21FuelWindow(
                DemandG:
                    100m,

                RestorationCreditG:
                    55m,

                AbsoluteRestorationRatio:
                    0.55m,

                ModerateHardDays:
                    3,

                HardDays:
                    2,

                Days:
                    new[]
                    {
                        LedgerDay(
                            new DateOnly(2026, 8, 28),
                            demand: 10m,
                            credit: 10m,
                            fuelScore: 1m),

                        LedgerDay(
                            new DateOnly(2026, 8, 29),
                            demand: 20m,
                            credit: 10m,
                            fuelScore: 2m),

                        LedgerDay(
                            new DateOnly(2026, 8, 30),
                            demand: 30m,
                            credit: 15m,
                            fuelScore: 3m),

                        LedgerDay(
                            new DateOnly(2026, 8, 31),
                            demand: 40m,
                            credit: 20m,
                            fuelScore: 4m)
                    });

        var plan =
            new PlanDefinition
            {
                PlanCode = "PLAN_2",
                DisplayName = "Fat-loss Recomp",
                PlanCategoryCode = "RECOMP",
                BaseRestCarbsG = 130m,
                IsActive = true
            };

        var workout =
            new WorkoutTypeDefinition
            {
                WorkoutTypeCode = "TEST_MAJOR",
                DisplayName = "Test Major",
                WeeklyRoleCode = "HEAVY",
                FuelPriorityCode = "HIGH",
                SchedulePrincipleCode = "TEST",
                WorkoutCategoryCode = "MAJOR_STRENGTH",

                FuelScore = 3m,
                DefaultMinutes = 60m,
                RawGlycogenGMin = 2m,
                DefaultIntensityFactor = 1m,
                PlannedRestorationPct = 0.75m,
                IsActive = true
            };

        var days =
            new[]
            {
                new Engine21WeeklyMacroDayInput(
                    Date:
                        new DateOnly(2026, 9, 1),

                    Plan:
                        plan,

                    Workout:
                        workout,

                    IntendedEnergyTargetKcal:
                        2480m),

                new Engine21WeeklyMacroDayInput(
                    Date:
                        new DateOnly(2026, 9, 2),

                    Plan:
                        plan,

                    Workout:
                        workout,

                    IntendedEnergyTargetKcal:
                        2480m)
            };

        var result =
            _service.Calculate(
                weightLb:
                    210m,

                proteinG:
                    185m,

                fatG:
                    60m,

                initialFuelWindow:
                    initialFuel,

                plannedDays:
                    days,

                settings:
                    Settings);

        Assert.Equal(
            2,
            result.Count);

        var dayOne =
            result[0];

        var dayTwo =
            result[1];

        //
        // Day 1 starts from the original historical four-day window.
        //
        Assert.Equal(
            100m,
            dayOne.IncomingFuelWindow.DemandG);

        Assert.Equal(
            55m,
            dayOne.IncomingFuelWindow.RestorationCreditG);

        Assert.Equal(
            0.80m,
            dayOne.Macro.CarbRequirement.FunctionalTargetRatio);

        Assert.Equal(
            300m,
            dayOne.Macro.Reconciliation.RecommendedCarbsG);

        //
        // Day 1's 300 g prescription fully restores its modeled
        // 80 g glycogen demand.
        //
        Assert.Equal(
            80m,
            dayOne.ProjectedFuelDay.DemandG);

        Assert.Equal(
            80m,
            dayOne.ProjectedFuelDay.RestorationCreditG);

        //
        // Rolling forward drops Aug 28 (10 demand / 10 credit)
        // and adds Sep 1 (80 demand / 80 credit).
        //
        // New window:
        //
        // demand = 20 + 30 + 40 + 80 = 170
        // credit = 10 + 15 + 20 + 80 = 125
        //
        Assert.Equal(
            170m,
            dayOne.OutgoingFuelWindow.DemandG);

        Assert.Equal(
            125m,
            dayOne.OutgoingFuelWindow.RestorationCreditG);

        //
        // Day 2 MUST use Day 1's outgoing window.
        //
        Assert.Equal(
            170m,
            dayTwo.IncomingFuelWindow.DemandG);

        Assert.Equal(
            125m,
            dayTwo.IncomingFuelWindow.RestorationCreditG);

        Assert.Equal(
            4,
            dayTwo.IncomingFuelWindow.ModerateHardDays);

        Assert.Equal(
            3,
            dayTwo.IncomingFuelWindow.HardDays);

        //
        // Four moderate/hard days raises the functional restoration
        // target from 0.75 to 0.85.
        //
        Assert.Equal(
            0.85m,
            dayTwo.Macro.CarbRequirement.FunctionalTargetRatio);
    }

    private static Engine21FuelLedgerDay LedgerDay(
        DateOnly date,
        decimal demand,
        decimal credit,
        decimal fuelScore)
    {
        return new Engine21FuelLedgerDay(
            Date:
                date,

            DemandG:
                demand,

            RestorationCreditG:
                credit,

            MaxFuelScore:
                fuelScore,

            ModerateHard:
                fuelScore >= 2m,

            Hard:
                fuelScore >= 3m,

            HasCarbohydrateEvidence:
                true);
    }
}
