using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30EnergyCarryoverServiceTests
{
    private readonly Engine30EnergyCarryoverService _service =
        new();

    [Fact]
    public void PieDay_UsesConfiguredRecoveryFraction_AndProtectsMajorStrength()
    {
        var start =
            new DateOnly(2026, 9, 7);

        var weekly =
            Week(
                start,
                [
                    Day(start, "REST_RECOVERY_OTHER", 2000m, 1800m),
                    Day(start.AddDays(1), "REST_RECOVERY_OTHER", 2000m, 1800m),
                    Day(start.AddDays(2), "MAJOR_STRENGTH", 2400m, 2350m),
                    Day(start.AddDays(3), "REST_RECOVERY_OTHER", 2000m, 1800m),
                    Day(start.AddDays(4), "CONDITIONING_CARDIO", 2200m, 1900m),
                    Day(start.AddDays(5), "ACCESSORY_STRENGTH", 2200m, 2000m),
                    Day(start.AddDays(6), "REST_RECOVERY_OTHER", 2000m, 1800m)
                ]);

        var actual =
            new Dictionary<DateOnly, decimal?>
            {
                [start.AddDays(1)] = 2600m // +800 over target
            };

        var result =
            _service.Apply(
                weekly,
                Policy(),
                start.AddDays(2),
                actual);

        Assert.Equal(
            800m,
            result.TotalHistoricalOverageKcal);

        Assert.Equal(
            600m,
            result.TotalEligibleCorrectionKcal);

        var majorToday =
            result.Days.Single(
                x => x.Date == start.AddDays(2));

        Assert.Equal(
            0m,
            majorToday.TotalCorrectionAppliedKcal);

        Assert.Equal(
            0m,
            result.CorrectionAppliedOnAsOfDateKcal);

        Assert.True(
            result.CorrectionAppliedAfterAsOfDateKcal > 0m);
    }

    [Fact]
    public void UnderTargetCalories_AreNotBankedIntoFutureFood()
    {
        var start =
            new DateOnly(2026, 9, 7);

        var weekly =
            Week(
                start,
                Enumerable.Range(0, 7)
                    .Select(i =>
                        Day(
                            start.AddDays(i),
                            i == 2
                                ? "MAJOR_STRENGTH"
                                : "REST_RECOVERY_OTHER",
                            2000m,
                            1800m))
                    .ToArray());

        var actual =
            new Dictionary<DateOnly, decimal?>
            {
                [start] = 1400m
            };

        var result =
            _service.Apply(
                weekly,
                Policy(),
                start.AddDays(1),
                actual);

        Assert.Equal(
            0m,
            result.TotalHistoricalOverageKcal);

        Assert.Equal(
            0m,
            result.TotalEligibleCorrectionKcal);

        Assert.All(
            result.Days,
            x => Assert.Equal(
                0m,
                x.TotalCorrectionAppliedKcal));

        Assert.Contains(
            "UNDER_TARGET_INTAKE_NOT_BANKED",
            result.ReasonCodes);
    }

    [Fact]
    public void SameDayActual_DoesNotMoveSameDayTarget()
    {
        var start =
            new DateOnly(2026, 9, 7);

        var weekly =
            Week(
                start,
                Enumerable.Range(0, 7)
                    .Select(i =>
                        Day(
                            start.AddDays(i),
                            "REST_RECOVERY_OTHER",
                            2000m,
                            1800m))
                    .ToArray());

        var today =
            start.AddDays(2);

        var actual =
            new Dictionary<DateOnly, decimal?>
            {
                [today] = 3000m
            };

        var result =
            _service.Apply(
                weekly,
                Policy(),
                today,
                actual);

        Assert.Equal(
            0m,
            result.TotalHistoricalOverageKcal);

        Assert.Equal(
            1800m,
            result.Days.Single(x => x.Date == today)
                .AdjustedPrescription.PrescribedEnergyKcal);
    }

    [Fact]
    public void CorrectionNeverCrossesCategoryOrMacroFloor()
    {
        var start =
            new DateOnly(2026, 9, 7);

        var weekly =
            Week(
                start,
                [
                    Day(start, "REST_RECOVERY_OTHER", 2000m, 1800m),
                    Day(start.AddDays(1), "REST_RECOVERY_OTHER", 2000m, 1800m),
                    Day(start.AddDays(2), "MAJOR_STRENGTH", 2400m, 2350m),
                    Day(start.AddDays(3), "REST_RECOVERY_OTHER", 2000m, 1800m, carbFloor: 120m),
                    Day(start.AddDays(4), "MAJOR_STRENGTH", 2400m, 2350m),
                    Day(start.AddDays(5), "MAJOR_STRENGTH", 2400m, 2350m),
                    Day(start.AddDays(6), "MAJOR_STRENGTH", 2400m, 2350m)
                ]);

        var actual =
            new Dictionary<DateOnly, decimal?>
            {
                [start.AddDays(1)] = 3000m
            };

        var result =
            _service.Apply(
                weekly,
                Policy(),
                start.AddDays(2),
                actual);

        var rest =
            result.Days.Single(
                x => x.Date == start.AddDays(3));

        var macroFloor =
            weekly.StableProteinG * 4m
            + weekly.StableFatG * 9m
            + rest.AdjustedPrescription.PhysiologicalCarbFloorG * 4m;

        Assert.True(
            rest.AdjustedPrescription.PrescribedEnergyKcal
            >= macroFloor);

        Assert.True(
            result.UnallocatedCorrectionKcal > 0m);
    }

    [Fact]
    public void MondayDoesNotImportPriorWeekOverage()
    {
        var monday =
            new DateOnly(2026, 9, 7);

        var weekly =
            Week(
                monday,
                Enumerable.Range(0, 7)
                    .Select(i =>
                        Day(
                            monday.AddDays(i),
                            "REST_RECOVERY_OTHER",
                            2000m,
                            1800m))
                    .ToArray());

        var actual =
            new Dictionary<DateOnly, decimal?>
            {
                [monday.AddDays(-1)] = 3500m
            };

        var result =
            _service.Apply(
                weekly,
                Policy(),
                monday,
                actual);

        Assert.Equal(
            0m,
            result.TotalHistoricalOverageKcal);

        Assert.Equal(
            0m,
            result.TotalEligibleCorrectionKcal);
    }

    private static Engine30WeeklyPrescriptionResult Week(
        DateOnly start,
        IReadOnlyCollection<Engine30PrescriptionDayResult> days)
    {
        var maintenance =
            days.Sum(x => x.MaintenanceEnergyKcal);

        var prescribed =
            days.Sum(x => x.PrescribedEnergyKcal);

        return new Engine30WeeklyPrescriptionResult(
            PlanCode:
                "PLAN_2",
            WeightLb:
                210m,
            Bmi:
                27m,
            RequestedDeficitPct:
                0.15m,
            EffectiveDeficitPct:
                (maintenance - prescribed)
                / maintenance,
            WeeklyMaintenanceEnergyKcal:
                maintenance,
            WeeklyDeficitKcal:
                maintenance - prescribed,
            WeeklyPrescribedEnergyKcal:
                prescribed,
            AverageDailyPrescribedEnergyKcal:
                prescribed / 7m,
            StableProteinG:
                180m,
            StableFatG:
                45m,
            BaseRestCarbsG:
                90m,
            Days:
                days.ToArray(),
            ReasonCodes:
                ["TEST_BASELINE"]);
    }

    private static Engine30PrescriptionDayResult Day(
        DateOnly date,
        string category,
        decimal maintenance,
        decimal prescribed,
        decimal carbFloor = 50m)
    {
        var protein =
            180m;

        var fat =
            45m;

        var fixedKcal =
            protein * 4m
            + fat * 9m;

        var energyCarbs =
            Math.Max(
                carbFloor,
                (prescribed - fixedKcal) / 4m);

        var carbs =
            Math.Round(
                energyCarbs / 5m,
                0,
                MidpointRounding.AwayFromZero)
            * 5m;

        var macroKcal =
            protein * 4m
            + fat * 9m
            + carbs * 4m;

        return new Engine30PrescriptionDayResult(
            Date:
                date,
            WorkoutCategoryCode:
                category,
            CategoryWeight:
                1m,
            MaintenanceEnergyKcal:
                maintenance,
            PrescribedEnergyKcal:
                prescribed,
            ProteinG:
                protein,
            FatG:
                fat,
            PhysiologicalCarbFloorG:
                carbFloor,
            EnergyBalancedCarbsG:
                carbs,
            StandardCarbUpperBoundaryG:
                475m,
            RecommendedCarbsG:
                carbs,
            MacroDerivedEnergyKcal:
                macroKcal,
            MacroEnergyVarianceKcal:
                macroKcal - prescribed,
            CarbGoverningRuleCode:
                "ENERGY_BALANCE");
    }

    private static Engine30GoalEnergyPolicy Policy()
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
}

