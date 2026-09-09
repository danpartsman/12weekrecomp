using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine21EnergyServiceTests
{
    private readonly Engine21EnergyService _service = new();

    [Fact]
    public void PopulationReference_CalculatesIndependentEnergyModels()
    {
        var result =
            _service.CalculateReference(
                actionDate:
                    new DateOnly(2026, 9, 1),

                dateOfBirth:
                    new DateOnly(1963, 1, 1),

                sexCode:
                    "M",

                heightIn:
                    73m,

                weightLb:
                    210m,

                baselineStepsDay:
                    6500,

                recentDailySteps:
                [
                    6000,
                    6500,
                    7000,
                    7500,
                    8000,
                    7000,
                    7000
                ]);

        Assert.Equal(
            63,
            result.AgeYears);

        Assert.Equal(
            95.25m,
            result.WeightKg);

        Assert.Equal(
            185.42m,
            result.HeightCm);

        // ten Haaf population RMR reference.
        Assert.Equal(
            1935m,
            result.RestingEnergyKcal);

        // 2023 DRI population TEE references.
        Assert.Equal(
            2619m,
            result.DriInactiveTeeKcal);

        Assert.Equal(
            2861m,
            result.DriLowActiveTeeKcal);

        Assert.Equal(
            7000m,
            result.HabitualStepsPerDay);

        Assert.Equal(
            7,
            result.ValidRecentStepDays);

        Assert.Equal(
            "RECENT_AVERAGE",
            result.StepSourceCode);

        Assert.Equal(
            "LOW_ACTIVE",
            result.HabitualStepBandCode);

        // Conservative midpoint:
        // (2619.097 + 2861.267) / 2 ~= 2740 kcal.
        Assert.Equal(
            2740m,
            result.ProvisionalNonWorkoutEnergyKcal);

        Assert.Equal(
            "DRI_INACTIVE_LOW_ACTIVE_MIDPOINT",
            result.NonWorkoutEnergyModelCode);

        Assert.Equal(
            "MODERATE",
            result.ConfidenceCode);
    }

    [Fact]
    public void HabitualSteps_UsesProfileBaseline_UntilEnoughRecentDaysExist()
    {
        var result =
            _service.CalculateReference(
                actionDate:
                    new DateOnly(2026, 9, 1),

                dateOfBirth:
                    new DateOnly(1985, 6, 15),

                sexCode:
                    "F",

                heightIn:
                    65m,

                weightLb:
                    150m,

                baselineStepsDay:
                    8000,

                recentDailySteps:
                [
                    4200,
                    11000,
                    6500
                ]);

        Assert.Equal(
            8000m,
            result.HabitualStepsPerDay);

        Assert.Equal(
            3,
            result.ValidRecentStepDays);

        Assert.Equal(
            "PROFILE_BASELINE",
            result.StepSourceCode);

        Assert.Equal(
            "SOMEWHAT_ACTIVE",
            result.HabitualStepBandCode);

        Assert.Equal(
            "DRI_INACTIVE_LOW_ACTIVE_MIDPOINT",
            result.NonWorkoutEnergyModelCode);

        Assert.Equal(
            "LOW",
            result.ConfidenceCode);
    }

    [Fact]
    public void SedentaryHabitualSteps_UseInactiveDriReference()
    {
        var result =
            _service.CalculateReference(
                actionDate:
                    new DateOnly(2026, 9, 1),

                dateOfBirth:
                    new DateOnly(1980, 1, 1),

                sexCode:
                    "M",

                heightIn:
                    70m,

                weightLb:
                    180m,

                baselineStepsDay:
                    4200,

                recentDailySteps:
                    []);

        Assert.Equal(
            "SEDENTARY",
            result.HabitualStepBandCode);

        Assert.Equal(
            result.DriInactiveTeeKcal,
            result.ProvisionalNonWorkoutEnergyKcal);

        Assert.Equal(
            "DRI_INACTIVE",
            result.NonWorkoutEnergyModelCode);
    }

    [Fact]
    public void HighStepCounts_DoNotAutomaticallyPromoteToActiveDriEnergy()
    {
        var result =
            _service.CalculateReference(
                actionDate:
                    new DateOnly(2026, 9, 1),

                dateOfBirth:
                    new DateOnly(1990, 1, 1),

                sexCode:
                    "F",

                heightIn:
                    66m,

                weightLb:
                    145m,

                baselineStepsDay:
                    13000,

                recentDailySteps:
                    []);

        Assert.Equal(
            "HIGHLY_ACTIVE",
            result.HabitualStepBandCode);

        // Steps classify activity context, but do not by themselves
        // authorize an Active/Very Active PAL equation because formal
        // exercise will be modeled separately.
        Assert.Equal(
            "DRI_INACTIVE_LOW_ACTIVE_MIDPOINT",
            result.NonWorkoutEnergyModelCode);

        Assert.True(
            result.ProvisionalNonWorkoutEnergyKcal
            > result.DriInactiveTeeKcal);

        Assert.True(
            result.ProvisionalNonWorkoutEnergyKcal
            < result.DriLowActiveTeeKcal);
    }

    [Fact]
    public void FatTarget_UsesBodyWeightFloor_WhenFloorIsHigher()
    {
        var result =
            _service.CalculateFatTarget(
                currentWeightLb:
                    210m,

                averagePrescribedEnergyKcal:
                    2000m,

                settings:
                    FatSettings());

        Assert.Equal(
            44.44m,
            result.FatFromEnergyG);

        Assert.Equal(
            47.63m,
            result.FatFloorG);

        Assert.Equal(
            50m,
            result.RecommendedFatG);

        Assert.Equal(
            "WEIGHT_FLOOR",
            result.GoverningRuleCode);
    }

    [Fact]
    public void FatTarget_UsesEnergyPercentage_WhenEnergyRuleIsHigher()
    {
        var result =
            _service.CalculateFatTarget(
                currentWeightLb:
                    210m,

                averagePrescribedEnergyKcal:
                    2400m,

                settings:
                    FatSettings());

        Assert.Equal(
            53.33m,
            result.FatFromEnergyG);

        Assert.Equal(
            47.63m,
            result.FatFloorG);

        Assert.Equal(
            55m,
            result.RecommendedFatG);

        Assert.Equal(
            "ENERGY_PERCENT",
            result.GoverningRuleCode);
    }

    [Fact]
    public void FatTarget_IsIndependentOfWorkoutType()
    {
        // There is deliberately no workout argument.
        // Fat is a stable person-level target.
        // Workout-specific nutrition variation belongs primarily in carbs.

        var result =
            _service.CalculateFatTarget(
                currentWeightLb:
                    210m,

                averagePrescribedEnergyKcal:
                    2200m,

                settings:
                    FatSettings());

        Assert.Equal(
            50m,
            result.RecommendedFatG);
    }

    [Fact]
    public void SevenDayAverage_UsesEntireNutritionWeek()
    {
        decimal[] daily =
        [
            1800m,
            2000m,
            2200m,
            1900m,
            2300m,
            1750m,
            1800m
        ];

        var average =
            _service.CalculateSevenDayAveragePrescribedEnergy(
                daily);

        Assert.Equal(
            1964.2857142857142857142857143m,
            average);
    }

    private static IReadOnlyDictionary<string, decimal>
        FatSettings()
        => new Dictionary<string, decimal>
        {
            ["FAT_TARGET_ENERGY_PCT"] = 0.20m,
            ["FAT_MIN_G_PER_KG"] = 0.50m,
            ["FAT_ROUNDING_G"] = 5m
        };
}
