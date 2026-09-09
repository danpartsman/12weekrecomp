using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30PrescriptionServiceTests
{
    [Fact]
    public void WeeklyPeriodization_PreservesAuthoritativeWeeklyEnergy()
    {
        var result =
            new Engine30PrescriptionService()
                .Calculate(
                    "PLAN_2",
                    weightLb: 200m,
                    heightIn: 72m,
                    days: StandardWeek());

        Assert.Equal(
            7,
            result.Days.Count);

        Assert.Equal(
            result.WeeklyPrescribedEnergyKcal,
            result.Days.Sum(
                x => x.PrescribedEnergyKcal));

        Assert.True(
            result.WeeklyPrescribedEnergyKcal
            <= result.WeeklyMaintenanceEnergyKcal);
    }

    [Fact]
    public void EqualMaintenance_MajorStrengthReceivesMoreEnergyThanRest_WhenFloorsDoNotBindBothDays()
    {
        var result =
            new Engine30PrescriptionService()
                .Calculate(
                    "PLAN_2",
                    200m,
                    72m,
                    StandardWeek(
                        equalMaintenance: true));

        var major =
            result.Days.Single(
                x => x.Date
                     == new DateOnly(
                         2026,
                         9,
                         7));

        var rest =
            result.Days.Single(
                x => x.Date
                     == new DateOnly(
                         2026,
                         9,
                         13));

        Assert.True(
            major.PrescribedEnergyKcal
            > rest.PrescribedEnergyKcal);

        Assert.Equal(
            1.10m,
            major.CategoryWeight);

        Assert.Equal(
            0.80m,
            rest.CategoryWeight);
    }

    [Fact]
    public void ProteinAndFatRemainStable_CarbohydratePeriodizes()
    {
        var result =
            new Engine30PrescriptionService()
                .Calculate(
                    "PLAN_3",
                    200m,
                    72m,
                    StandardWeek(
                        equalMaintenance: true));

        Assert.Single(
            result.Days
                .Select(x => x.ProteinG)
                .Distinct());

        Assert.Single(
            result.Days
                .Select(x => x.FatG)
                .Distinct());

        var highGlycogen =
            result.Days.Single(
                x => x.Date
                     == new DateOnly(
                         2026,
                         9,
                         7));

        var rest =
            result.Days.Single(
                x => x.Date
                     == new DateOnly(
                         2026,
                         9,
                         13));

        Assert.True(
            highGlycogen
                .PhysiologicalCarbFloorG
            > rest
                .PhysiologicalCarbFloorG);

        Assert.True(
            highGlycogen
                .RecommendedCarbsG
            >= rest
                .RecommendedCarbsG);
    }

    [Fact]
    public void FatFloor_IsNeverRoundedBelowHalfGramPerKg()
    {
        var service =
            new Engine30PrescriptionService();

        foreach (var sample in new[]
        {
            new { WeightLb = 155m, HeightIn = 68m },
            new { WeightLb = 135m, HeightIn = 64m },
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
                result.StableFatG
                >= floor,
                $"{sample.WeightLb} lb: " +
                $"fat {result.StableFatG}g " +
                $"was below floor {floor:0.00}g.");
        }
    }

    [Fact]
    public void MacroFloors_ReduceDeficitInsteadOfCreatingLargeDailyVariance()
    {
        var days =
            new[]
            {
                new Engine30PrescriptionDayInput(
                    new DateOnly(2026, 9, 7),
                    1601m,
                    Engine30PrescriptionCategoryCodes.MajorStrength,
                    59.5m),

                new Engine30PrescriptionDayInput(
                    new DateOnly(2026, 9, 8),
                    1502m,
                    Engine30PrescriptionCategoryCodes.AccessoryStrength,
                    33.2m),

                new Engine30PrescriptionDayInput(
                    new DateOnly(2026, 9, 9),
                    1518m,
                    Engine30PrescriptionCategoryCodes.ConditioningCardio,
                    16.2m),

                new Engine30PrescriptionDayInput(
                    new DateOnly(2026, 9, 10),
                    1576m,
                    Engine30PrescriptionCategoryCodes.MajorStrength,
                    22.4m),

                new Engine30PrescriptionDayInput(
                    new DateOnly(2026, 9, 11),
                    1606m,
                    Engine30PrescriptionCategoryCodes.ConditioningCardio,
                    41.2m),

                new Engine30PrescriptionDayInput(
                    new DateOnly(2026, 9, 12),
                    1614m,
                    Engine30PrescriptionCategoryCodes.ConditioningCardio,
                    91.5m),

                new Engine30PrescriptionDayInput(
                    new DateOnly(2026, 9, 13),
                    1372m,
                    Engine30PrescriptionCategoryCodes.RestRecoveryOther,
                    0m)
            };

        var result =
            new Engine30PrescriptionService()
                .Calculate(
                    "PLAN_2",
                    135m,
                    64m,
                    days);

        Assert.Contains(
            "DEFICIT_REDUCED_TO_PROTECT_MACRO_FLOORS",
            result.ReasonCodes);

        Assert.True(
            result.EffectiveDeficitPct
            < result.RequestedDeficitPct);

        Assert.True(
            result.Days.All(
                x =>
                    Math.Abs(
                        x.MacroEnergyVarianceKcal)
                    <= 10m));

        Assert.True(
            result.Days.All(
                x =>
                    x.PrescribedEnergyKcal
                    >=
                    x.ProteinG * 4m
                    + x.FatG * 9m
                    + x.PhysiologicalCarbFloorG * 4m
                    - 0.01m));
    }

    [Fact]
    public void GlycogenReload_DoesNotChangeMaintenance_AndReconcilesInsidePrescription()
    {
        var service =
            new Engine30PrescriptionService();

        var original =
            StandardWeek(
                equalMaintenance: true);

        var highReload =
            original
                .Select(
                    (x, i) =>
                        i == 0
                            ? x with
                            {
                                GlycogenReloadRequirementG =
                                    200m
                            }
                            : x)
                .ToArray();

        var a =
            service.Calculate(
                "PLAN_2",
                200m,
                72m,
                original);

        var b =
            service.Calculate(
                "PLAN_2",
                200m,
                72m,
                highReload);

        Assert.Equal(
            a.WeeklyMaintenanceEnergyKcal,
            b.WeeklyMaintenanceEnergyKcal);

        Assert.True(
            b.Days[0]
                .PhysiologicalCarbFloorG
            >
            a.Days[0]
                .PhysiologicalCarbFloorG);

        Assert.True(
            b.Days.All(
                x =>
                    Math.Abs(
                        x.MacroEnergyVarianceKcal)
                    <= 10m));
    }

    private static Engine30PrescriptionDayInput[] StandardWeek(
        bool equalMaintenance = false)
    {
        decimal M(int day)
            =>
                equalMaintenance
                    ? 2400m
                    : 2250m
                      + day * 50m;

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
