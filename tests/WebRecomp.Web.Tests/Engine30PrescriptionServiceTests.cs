using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30PrescriptionServiceTests
{
    [Fact]
    public void BackgroundCarbohydrate_IsPhysiologicalAndPlanIndependent()
    {
        var service = new Engine30PrescriptionService();

        foreach (var plan in new[]
        {
            "PLAN_1", "PLAN_2", "PLAN_3", "PLAN_4", "PLAN_5"
        })
        {
            var result =
                service.Calculate(
                    plan,
                    weightLb: 210m,
                    heightIn: 73m,
                    days: StandardWeek());

            Assert.Equal(100m, result.BaseRestCarbsG);
            Assert.Contains(
                "BACKGROUND_CARB_DRI_ANCHOR_100G",
                result.ReasonCodes);
        }
    }

    [Fact]
    public void Carbohydrate_UsesRecoveryHorizonInsteadOfFullSameDayReload()
    {
        var result =
            new Engine30PrescriptionService()
                .Calculate(
                    "PLAN_2",
                    weightLb: 210m,
                    heightIn: 73m,
                    days: StandardWeek());

        var first = result.Days[0];
        var rest = result.Days[^1];

        Assert.Equal(155m, first.RecommendedCarbsG);
        Assert.Equal(125m, rest.RecommendedCarbsG);

        Assert.Equal(
            first.PhysiologicalCarbFloorG,
            first.RecommendedCarbsG);

        Assert.Equal(
            "BACKGROUND_PLUS_24H_GLYCOGEN_RECOVERY",
            first.CarbGoverningRuleCode);
    }

    [Fact]
    public void Calories_AreDerivedFromMacros_NotUsedToBackfillCarbohydrate()
    {
        var result =
            new Engine30PrescriptionService()
                .Calculate(
                    "PLAN_2",
                    weightLb: 210m,
                    heightIn: 73m,
                    days: StandardWeek(equalMaintenance: true));

        foreach (var day in result.Days)
        {
            var expected =
                day.ProteinG * 4m
                + day.FatG * 9m
                + day.RecommendedCarbsG * 4m;

            Assert.Equal(expected, day.PrescribedEnergyKcal);
            Assert.Equal(expected, day.MacroDerivedEnergyKcal);
            Assert.Equal(0m, day.MacroEnergyVarianceKcal);
            Assert.Equal(
                day.RecommendedCarbsG,
                day.EnergyBalancedCarbsG);
        }

        Assert.Equal(
            result.WeeklyPrescribedEnergyKcal,
            result.Days.Sum(x => x.PrescribedEnergyKcal));

        Assert.Contains(
            "CALORIES_DERIVED_FROM_MACROS_NOT_CARB_BACKFILLED",
            result.ReasonCodes);
    }

    [Fact]
    public void EqualMaintenance_CategoryWeightDoesNotManufactureExtraCalories()
    {
        var result =
            new Engine30PrescriptionService()
                .Calculate(
                    "PLAN_2",
                    210m,
                    73m,
                    StandardWeek(equalMaintenance: true));

        var major = result.Days[0];
        var rest = result.Days[^1];

        Assert.Equal(1.10m, major.CategoryWeight);
        Assert.Equal(0.80m, rest.CategoryWeight);

        Assert.Equal(
            30m,
            major.RecommendedCarbsG - rest.RecommendedCarbsG);

        Assert.Equal(
            120m,
            major.PrescribedEnergyKcal - rest.PrescribedEnergyKcal);
    }

    [Fact]
    public void ProteinAndFatRemainStable_CarbohydrateTracksGlycogenDemand()
    {
        var result =
            new Engine30PrescriptionService()
                .Calculate(
                    "PLAN_3",
                    210m,
                    73m,
                    StandardWeek(equalMaintenance: true));

        Assert.Single(
            result.Days
                .Select(x => x.ProteinG)
                .Distinct());

        Assert.Single(
            result.Days
                .Select(x => x.FatG)
                .Distinct());

        Assert.True(
            result.Days[0].RecommendedCarbsG
            > result.Days[^1].RecommendedCarbsG);
    }

    [Fact]
    public void FatTarget_IsNeverRoundedBelowHalfGramPerKg()
    {
        var service = new Engine30PrescriptionService();

        foreach (var sample in new[]
        {
            new { WeightLb = 155m, HeightIn = 68m },
            new { WeightLb = 210m, HeightIn = 73m },
            new { WeightLb = 250m, HeightIn = 76m }
        })
        {
            var result =
                service.Calculate(
                    "PLAN_2",
                    sample.WeightLb,
                    sample.HeightIn,
                    StandardWeek());

            var floor =
                sample.WeightLb
                / 2.2046226218m
                * 0.50m;

            Assert.True(
                result.StableFatG >= floor,
                $"{sample.WeightLb} lb: fat {result.StableFatG}g was below floor {floor:0.00}g.");
        }
    }

    [Fact]
    public void GlycogenReload_DoesNotChangeMaintenance_ButChangesMacrosAndCalories()
    {
        var service = new Engine30PrescriptionService();

        var original =
            StandardWeek(equalMaintenance: true);

        var highReload =
            original
                .Select(
                    (x, i) =>
                        i == 0
                            ? x with
                            {
                                GlycogenReloadRequirementG = 200m
                            }
                            : x)
                .ToArray();

        var a =
            service.Calculate(
                "PLAN_2",
                210m,
                73m,
                original);

        var b =
            service.Calculate(
                "PLAN_2",
                210m,
                73m,
                highReload);

        Assert.Equal(
            a.WeeklyMaintenanceEnergyKcal,
            b.WeeklyMaintenanceEnergyKcal);

        Assert.Equal(155m, a.Days[0].RecommendedCarbsG);
        Assert.Equal(200m, b.Days[0].RecommendedCarbsG);

        // The additional day-0 depletion is split across the workout date and
        // following date rather than prescribed almost entirely at once.
        Assert.Equal(
            45m,
            b.Days[0].RecommendedCarbsG
            - a.Days[0].RecommendedCarbsG);

        Assert.Equal(
            45m,
            b.Days[1].RecommendedCarbsG
            - a.Days[1].RecommendedCarbsG);

        // Calories always reconcile from the macros. If the weekly energy
        // safety guardrail is active, additional carbohydrate can legitimately
        // reduce the amount of guardrail fat required; therefore the calorie
        // delta is not assumed to equal carbohydrate delta x 4.
        Assert.Equal(
            b.Days[0].ProteinG * 4m
            + b.Days[0].FatG * 9m
            + b.Days[0].RecommendedCarbsG * 4m,
            b.Days[0].PrescribedEnergyKcal);
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
}
