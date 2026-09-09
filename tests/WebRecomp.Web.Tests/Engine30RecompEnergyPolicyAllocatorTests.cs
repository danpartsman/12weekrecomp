using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30RecompEnergyPolicyAllocatorTests
{
    private readonly Engine30RecompEnergyPolicyAllocator _allocator =
        new();

    [Fact]
    public void FatLossRecomp_ProtectsMajorBeforeLowPriorityDays()
    {
        var start =
            new DateOnly(2026, 9, 7);

        var baseline =
            Baseline(
                start,
                weeklyTarget: 13260m,
                [
                    Day(start, "MAJOR_STRENGTH", 2400m, 150m),
                    Day(start.AddDays(1), "REST_RECOVERY_OTHER", 2000m, 120m),
                    Day(start.AddDays(2), "MAJOR_STRENGTH", 2400m, 150m),
                    Day(start.AddDays(3), "ACCESSORY_STRENGTH", 2200m, 130m),
                    Day(start.AddDays(4), "REST_RECOVERY_OTHER", 2000m, 120m),
                    Day(start.AddDays(5), "MAJOR_STRENGTH", 2400m, 150m),
                    Day(start.AddDays(6), "CONDITIONING_CARDIO", 2200m, 130m)
                ]);

        var policy =
            FatLossPolicy();

        var result =
            _allocator.Apply(
                baseline,
                policy);

        var majors =
            result.Days
                .Where(x => x.WorkoutCategoryCode == "MAJOR_STRENGTH")
                .ToArray();

        Assert.All(
            majors,
            day =>
                Assert.True(
                    day.PrescribedEnergyKcal
                    >= day.MaintenanceEnergyKcal * 0.95m));

        Assert.Contains(
            result.Days,
            day =>
                day.WorkoutCategoryCode == "REST_RECOVERY_OTHER"
                && day.PrescribedEnergyKcal
                    < day.MaintenanceEnergyKcal * 0.90m);
    }

    [Fact]
    public void WeeklyBudget_IsPreserved_WhenPolicyFloorsFit()
    {
        var start =
            new DateOnly(2026, 9, 7);

        var baseline =
            Baseline(
                start,
                weeklyTarget: 14000m,
                Enumerable.Range(0, 7)
                    .Select(i =>
                        Day(
                            start.AddDays(i),
                            i % 2 == 0
                                ? "MAJOR_STRENGTH"
                                : "REST_RECOVERY_OTHER",
                            2200m,
                            130m))
                    .ToArray());

        var result =
            _allocator.Apply(
                baseline,
                FatLossPolicy());

        Assert.Equal(
            result.WeeklyPrescribedEnergyKcal,
            result.Days.Sum(x => x.PrescribedEnergyKcal));
    }

    [Fact]
    public void CategoryAndMacroFloor_CanReduceEffectiveDeficit()
    {
        var start =
            new DateOnly(2026, 9, 7);

        var baseline =
            Baseline(
                start,
                weeklyTarget: 10500m,
                Enumerable.Range(0, 7)
                    .Select(i =>
                        Day(
                            start.AddDays(i),
                            "MAJOR_STRENGTH",
                            2200m,
                            180m))
                    .ToArray());

        var result =
            _allocator.Apply(
                baseline,
                FatLossPolicy());

        Assert.True(
            result.WeeklyPrescribedEnergyKcal
            > baseline.WeeklyPrescribedEnergyKcal);

        Assert.Contains(
            "DEFICIT_REDUCED_TO_PROTECT_CATEGORY_ENERGY_FLOORS",
            result.ReasonCodes);
    }

    private static Engine30WeeklyPrescriptionResult Baseline(
        DateOnly start,
        decimal weeklyTarget,
        IReadOnlyCollection<Engine30PrescriptionDayResult> days)
    {
        var maintenance =
            days.Sum(x => x.MaintenanceEnergyKcal);

        return new Engine30WeeklyPrescriptionResult(
            PlanCode: "PLAN_2",
            WeightLb: 210m,
            Bmi: 27m,
            RequestedDeficitPct: 0.15m,
            EffectiveDeficitPct:
                (maintenance - weeklyTarget) / maintenance,
            WeeklyMaintenanceEnergyKcal: maintenance,
            WeeklyDeficitKcal: maintenance - weeklyTarget,
            WeeklyPrescribedEnergyKcal: weeklyTarget,
            AverageDailyPrescribedEnergyKcal: weeklyTarget / 7m,
            StableProteinG: 185m,
            StableFatG: 50m,
            BaseRestCarbsG: 90m,
            Days: days.ToArray(),
            ReasonCodes: ["PLAN_2_FAT_LOSS"]);
    }

    private static Engine30PrescriptionDayResult Day(
        DateOnly date,
        string category,
        decimal maintenance,
        decimal carbFloor)
    {
        var protein =
            185m;

        var fat =
            50m;

        var target =
            1900m;

        var energyCarbs =
            Math.Max(
                carbFloor,
                (target - protein * 4m - fat * 9m) / 4m);

        var carbs =
            Math.Round(
                energyCarbs / 5m,
                0,
                MidpointRounding.AwayFromZero)
            * 5m;

        var macro =
            protein * 4m
            + fat * 9m
            + carbs * 4m;

        return new Engine30PrescriptionDayResult(
            Date: date,
            WorkoutCategoryCode: category,
            CategoryWeight: 1m,
            MaintenanceEnergyKcal: maintenance,
            PrescribedEnergyKcal: target,
            ProteinG: protein,
            FatG: fat,
            PhysiologicalCarbFloorG: carbFloor,
            EnergyBalancedCarbsG: carbs,
            StandardCarbUpperBoundaryG: 475m,
            RecommendedCarbsG: carbs,
            MacroDerivedEnergyKcal: macro,
            MacroEnergyVarianceKcal: macro - target,
            CarbGoverningRuleCode: "ENERGY_BALANCE");
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
            ConfigVersionCode: "CFG-3000.0",
            PlanCode: "PLAN_2",
            PlanDisplayName: "Fat-Loss Dominant Recomp",
            SurplusRecoveryFraction: 0.75m,
            Categories: categories);
    }
}
