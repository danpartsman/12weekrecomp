using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30RecompEnergyPolicyAllocatorTests
{
    private readonly Engine30RecompEnergyPolicyAllocator _allocator =
        new();

    [Fact]
    public void EnergyPolicy_DoesNotChangePhysiologyFirstMacrosOrCalories()
    {
        var baseline =
            new Engine30PrescriptionService()
                .Calculate(
                    "PLAN_2",
                    210m,
                    73m,
                    StandardWeek());

        var result =
            _allocator.Apply(
                baseline,
                FatLossPolicy());

        Assert.Equal(
            baseline.WeeklyPrescribedEnergyKcal,
            result.WeeklyPrescribedEnergyKcal);

        Assert.Equal(
            baseline.Days.Select(x => x.PrescribedEnergyKcal),
            result.Days.Select(x => x.PrescribedEnergyKcal));

        Assert.Equal(
            baseline.Days.Select(x => x.RecommendedCarbsG),
            result.Days.Select(x => x.RecommendedCarbsG));

        Assert.Contains(
            "ADMIN_GOAL_CATEGORY_ENERGY_POLICY_DIAGNOSTIC_ONLY",
            result.ReasonCodes);
    }

    [Fact]
    public void PolicyCanReportBelowMinimumWithoutBackfillingCarbs()
    {
        var baseline =
            new Engine30PrescriptionService()
                .Calculate(
                    "PLAN_2",
                    210m,
                    73m,
                    StandardWeek(equalMaintenance: true));

        var result =
            _allocator.Apply(
                baseline,
                FatLossPolicy());

        Assert.Contains(
            "PHYSIOLOGY_FIRST_BELOW_CATEGORY_MINIMUM_DIAGNOSTIC",
            result.ReasonCodes);

        Assert.All(
            result.Days,
            day =>
            {
                Assert.Equal(
                    day.PhysiologicalCarbFloorG,
                    day.RecommendedCarbsG);

                Assert.Equal(
                    day.MacroDerivedEnergyKcal,
                    day.PrescribedEnergyKcal);
            });
    }

    [Fact]
    public void PolicyPlanMustStillMatchPrescriptionPlan()
    {
        var baseline =
            new Engine30PrescriptionService()
                .Calculate(
                    "PLAN_3",
                    210m,
                    73m,
                    StandardWeek());

        Assert.Throws<ArgumentException>(
            () =>
                _allocator.Apply(
                    baseline,
                    FatLossPolicy()));
    }

    private static Engine30PrescriptionDayInput[] StandardWeek(
        bool equalMaintenance = false)
    {
        decimal M(int day)
            =>
                equalMaintenance
                    ? 2400m
                    : 2250m + day * 50m;

        return
        [
            new(
                new DateOnly(2026, 9, 7),
                M(1),
                Engine30PrescriptionCategoryCodes.MajorStrength,
                110m),
            new(
                new DateOnly(2026, 9, 8),
                M(2),
                Engine30PrescriptionCategoryCodes.AccessoryStrength,
                55m),
            new(
                new DateOnly(2026, 9, 9),
                M(3),
                Engine30PrescriptionCategoryCodes.ConditioningCardio,
                75m),
            new(
                new DateOnly(2026, 9, 10),
                M(4),
                Engine30PrescriptionCategoryCodes.MajorStrength,
                95m),
            new(
                new DateOnly(2026, 9, 11),
                M(5),
                Engine30PrescriptionCategoryCodes.ConditioningCardio,
                40m),
            new(
                new DateOnly(2026, 9, 12),
                M(6),
                Engine30PrescriptionCategoryCodes.AccessoryStrength,
                45m),
            new(
                new DateOnly(2026, 9, 13),
                M(7),
                Engine30PrescriptionCategoryCodes.RestRecoveryOther,
                0m)
        ];
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
