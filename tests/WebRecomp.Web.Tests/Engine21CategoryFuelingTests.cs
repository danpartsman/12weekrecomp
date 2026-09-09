using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine21CategoryFuelingTests
{
    private readonly WorkoutMathService _math = new();


    [Theory]
    [InlineData("MAJOR_STRENGTH")]
    [InlineData("ACCESSORY_STRENGTH")]
    [InlineData("CONDITIONING_CARDIO")]
    [InlineData("REST_RECOVERY_OTHER")]
    public void Neutral_Category_Multiplier_Preserves_Legacy_Target(
        string categoryCode)
    {
        var result = _math.CalculatePlannedTargets(
            210m,
            Plan(),
            Workout(categoryCode),
            Settings());

        // Legacy calculation:
        // glycogen burn = 40 g
        // carbs = 120 + (40 * .80) = 152
        // rounded to nearest 5 = 150 g
        Assert.Equal(40m, result.ScaledGlycogenBurnG);
        Assert.Equal(150m, result.CarbsG);
    }


    [Theory]
    [InlineData(
        "MAJOR_STRENGTH",
        "ENGINE21_CATEGORY_BASE_CARB_MULT_MAJOR",
        1.10,
        165)]
    [InlineData(
        "ACCESSORY_STRENGTH",
        "ENGINE21_CATEGORY_BASE_CARB_MULT_ACCESSORY",
        1.00,
        150)]
    [InlineData(
        "CONDITIONING_CARDIO",
        "ENGINE21_CATEGORY_BASE_CARB_MULT_CONDITIONING",
        0.95,
        145)]
    [InlineData(
        "REST_RECOVERY_OTHER",
        "ENGINE21_CATEGORY_BASE_CARB_MULT_REST",
        0.90,
        140)]
    public void Approved_Category_Multiplier_Produces_Expected_Target(
        string categoryCode,
        string settingCode,
        decimal multiplier,
        decimal expectedCarbsG)
    {
        var settings =
            new Dictionary<string, decimal>(
                Settings(),
                StringComparer.OrdinalIgnoreCase)
            {
                [settingCode] = multiplier
            };

        var result =
            _math.CalculatePlannedTargets(
                210m,
                Plan(),
                Workout(categoryCode),
                settings);

        Assert.Equal(
            40m,
            result.ScaledGlycogenBurnG);

        Assert.Equal(
            expectedCarbsG,
            result.CarbsG);
    }


    [Fact]
    public void NonNeutral_Category_Multiplier_Changes_Only_Base_Carb_Layer()
    {
        var settings =
            new Dictionary<string, decimal>(
                Settings(),
                StringComparer.OrdinalIgnoreCase)
            {
                ["ENGINE21_CATEGORY_BASE_CARB_MULT_MAJOR"] = 1.25m
            };

        var result = _math.CalculatePlannedTargets(
            210m,
            Plan(),
            Workout("MAJOR_STRENGTH"),
            settings);

        // Category base:
        // 120 * 1.25 = 150
        //
        // Workout restoration:
        // 40 * .80 = 32
        //
        // 150 + 32 = 182
        // rounded to nearest 5 = 180
        Assert.Equal(40m, result.ScaledGlycogenBurnG);
        Assert.Equal(180m, result.CarbsG);
    }


    [Fact]
    public void Unknown_Category_Is_Rejected()
    {
        Assert.Throws<InvalidOperationException>(
            () => _math.CalculatePlannedTargets(
                210m,
                Plan(),
                Workout("INVALID_CATEGORY"),
                Settings()));
    }


    private static PlanDefinition Plan() =>
        new()
        {
            PlanDefinitionId = Guid.NewGuid(),
            ConfigVersionId = Guid.NewGuid(),
            PlanCode = "TEST",
            DisplayName = "Test Plan",
            BaseRestCarbsG = 120m,
            PlanCategoryCode = "TEST",
            SortOrder = 1,
            IsActive = true
        };


    private static WorkoutTypeDefinition Workout(
        string categoryCode) =>
        new()
        {
            WorkoutTypeDefinitionId = Guid.NewGuid(),
            ConfigVersionId = Guid.NewGuid(),

            WorkoutTypeCode = "ENGINE21_TEST",
            DisplayName = "Engine 2.1 Test",

            WorkoutCategoryCode = categoryCode,

            WeeklyRoleCode = "HEAVY",
            RegionCode = "PUSH",

            CountsDedicatedHeavy = true,
            CountsLightStrength = false,
            CountsPrimaryConditioning = false,
            CountsRecovery = false,

            FuelPriorityCode = "HIGH",
            SchedulePrincipleCode = "TEST",

            MacroClassCode = "Major",
            PrimaryFocusCode = "Push",

            FuelScore = 3m,
            DefaultMinutes = 60m,

            RawGlycogenGMin = 1m,
            DefaultIntensityFactor = 1m,
            DefaultRestSec = 75,

            ReferenceWorkoutCalPerMin = 8m,
            GlycogenCarbShare = 1m,

            PushLoad = 3m,
            PullLoad = 0m,
            LegLoad = 0m,
            CardioLoad = 0m,

            StrengthStress = 3m,
            PlannedRestorationPct = 0.80m,

            ProteinAdjustmentG = 0m,
            TargetFatG = 50m,
            TypicalRecoveryHours = 48m,

            IsActive = true,
            IsSelectable = true,
            DisplayOrder = 100
        };


    private static IReadOnlyDictionary<string, decimal>
        Settings() =>
        new Dictionary<string, decimal>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["REFERENCE_WEIGHT_LB"] = 210m,
            ["BODY_WEIGHT_GLYCOGEN_EXPONENT"] = 0.50m,
            ["GLYCOGEN_ESTIMATE_MULTIPLIER"] = 0.65m,

            ["CARB_ROUNDING_G"] = 5m,

            ["PROTEIN_ROUNDING_G"] = 5m,
            ["PROTEIN_PER_LB"] = 0.88m,
            ["PROTEIN_MIN_G"] = 175m,
            ["PROTEIN_MAX_G"] = 195m,

            ["MINIMUM_FAT_G"] = 35m,

            ["ENGINE21_CATEGORY_BASE_CARB_MULT_MAJOR"] = 1.00m,
            ["ENGINE21_CATEGORY_BASE_CARB_MULT_ACCESSORY"] = 1.00m,
            ["ENGINE21_CATEGORY_BASE_CARB_MULT_CONDITIONING"] = 1.00m,
            ["ENGINE21_CATEGORY_BASE_CARB_MULT_REST"] = 1.00m
        };
}


