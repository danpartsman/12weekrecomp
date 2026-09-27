using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutIntelligence;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class SystemWorkoutRecipeCatalogTests
{
    [Fact]
    public void SystemWorkoutPresentationUsesNamedGroupsAndUniqueCodes()
    {
        Assert.Equal(27, SystemWorkoutRecipeCatalog.CoreRecipes.Count);
        Assert.Equal(3, SystemWorkoutRecipeCatalog.SignatureRecipes.Count);

        var choices =
            SystemWorkoutRecipeCatalog.PresentationRecipes;

        Assert.Equal(54, choices.Count);
        Assert.Equal(
            choices.Count,
            choices
                .Select(x => x.SystemCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        Assert.Contains(choices, x => x.DisplayName == "Set A - Back + Biceps + Quads" && x.GroupName == "Set A");
        Assert.Contains(choices, x => x.DisplayName == "Set B - Hard Legs" && x.GroupName == "Set B");
        Assert.Contains(choices, x => x.DisplayName == "Easy Accessory" && x.GroupName == "Easy");
        Assert.Contains(choices, x => x.DisplayName == "Standard Accessory" && x.GroupName == "Standard");
        Assert.Contains(choices, x => x.DisplayName == "Hard Accessory" && x.GroupName == "Hard");

        var groupOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Set A"] = 0,
            ["Set B"] = 1,
            ["Orangetheory"] = 2,
            ["Easy"] = 3,
            ["Standard"] = 4,
            ["Hard"] = 5,
            ["Recovery"] = 6
        };

        var expected = choices
            .OrderBy(x => groupOrder[x.GroupName])
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.SystemCode)
            .ToArray();

        Assert.Equal(expected, choices.Select(x => x.SystemCode));
    }

    [Theory]
    [InlineData("MAJOR_PUSH", "Resistance")]
    [InlineData("ACCESSORY", "Resistance")]
    [InlineData("RUNNING", "Cardio")]
    [InlineData("ZONE2_CARDIO", "Cardio")]
    [InlineData("TRUE_HIIT", "Conditioning")]
    [InlineData("ORANGETHEORY", "Orangetheory")]
    [InlineData("RESISTANCE_CIRCUIT", "Conditioning")]
    [InlineData("MIXED_GYM_SESSION", "Mixed / Composite")]
    [InlineData("CUSTOM_COMPOSITE", "Mixed / Composite")]
    [InlineData("REST", "Recovery")]
    [InlineData("MOBILITY_RECOVERY", "Recovery")]
    public void PresentationCategoryPreservesTrainingTypeGrouping(
        string engineCode,
        string expectedCategory)
    {
        Assert.Equal(
            expectedCategory,
            SystemWorkoutRecipeCatalog
                .PresentationCategoryForEngineWorkoutType(
                    engineCode));
    }

    [Fact]
    public void PresentationCategoryOrderIsStable()
    {
        Assert.Equal(
            new[]
            {
                "Resistance",
                "Orangetheory",
                "Conditioning",
                "Cardio",
                "Mixed / Composite",
                "Recovery"
            },
            SystemWorkoutRecipeCatalog
                .PresentationCategoryOrder);
    }

    [Fact]
    public void OrangetheoryHasDedicatedSystemWorkoutCategory()
    {
        var recipe = SystemWorkoutRecipeCatalog.PresentationRecipes.Single(x => x.SystemCode == "ORANGETHEORY");
        Assert.Equal("Orangetheory", recipe.GroupName);
        Assert.Equal("ORANGETHEORY", recipe.EngineWorkoutTypeCode);
        Assert.Contains("Orangetheory", SystemWorkoutRecipeCatalog.PresentationCategoryOrder);
    }

    [Fact]
    public void TieredAccessoryAndCardioPresetsScaleTimeAndRpe()
    {
        var engine =
            new Engine30WorkoutTypeCatalog();

        var accessory =
            engine.GetRequired("ACCESSORY");

        var easyAccessory =
            SystemWorkoutRecipeCatalog.PresentationRecipes
                .Single(x => x.DisplayName == "Easy Accessory");

        var standardAccessory =
            SystemWorkoutRecipeCatalog.PresentationRecipes
                .Single(x => x.DisplayName == "Standard Accessory");

        var hardAccessory =
            SystemWorkoutRecipeCatalog.PresentationRecipes
                .Single(x => x.DisplayName == "Hard Accessory");

        Assert.Equal("ACCESSORY", easyAccessory.EngineWorkoutTypeCode);
        Assert.Equal("ACCESSORY", standardAccessory.EngineWorkoutTypeCode);
        Assert.Equal("ACCESSORY", hardAccessory.EngineWorkoutTypeCode);

        Assert.Equal(
            accessory.DefaultRpe,
            SystemWorkoutRecipeCatalog.ResolveDefaultRpe(
                standardAccessory,
                accessory.DefaultRpe));

        Assert.True(
            SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(
                easyAccessory,
                45m)
            < SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(
                standardAccessory,
                45m));

        Assert.True(
            SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(
                standardAccessory,
                45m)
            < SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(
                hardAccessory,
                45m));

        Assert.True(
            SystemWorkoutRecipeCatalog.ResolveDefaultRpe(
                easyAccessory,
                accessory.DefaultRpe)
            < SystemWorkoutRecipeCatalog.ResolveDefaultRpe(
                standardAccessory,
                accessory.DefaultRpe));

        Assert.True(
            SystemWorkoutRecipeCatalog.ResolveDefaultRpe(
                standardAccessory,
                accessory.DefaultRpe)
            < SystemWorkoutRecipeCatalog.ResolveDefaultRpe(
                hardAccessory,
                accessory.DefaultRpe));

        foreach (var cardioName in new[]
        {
            "Running",
            "Cycling",
            "Rowing",
            "Treadmill",
            "Swimming"
        })
        {
            Assert.Contains(
                SystemWorkoutRecipeCatalog.PresentationRecipes,
                x => x.DisplayName == $"Easy {cardioName}");

            Assert.Contains(
                SystemWorkoutRecipeCatalog.PresentationRecipes,
                x => x.DisplayName == $"Standard {cardioName}");

            Assert.Contains(
                SystemWorkoutRecipeCatalog.PresentationRecipes,
                x => x.DisplayName == $"Hard {cardioName}");
        }
    }

    [Fact]
    public void PresentationRecipesStillCoverEveryEngine30Type()
    {
        var engineCodes =
            new Engine30WorkoutTypeCatalog()
                .GetAll()
                .Select(x => x.WorkoutTypeCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var presentedEngineCodes =
            SystemWorkoutRecipeCatalog.PresentationRecipes
                .Select(x => x.EngineWorkoutTypeCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Empty(
            engineCodes.Except(
                presentedEngineCodes,
                StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void CoreRecipesCoverAllTwentySevenEngine30WorkoutTypes()
    {
        var engineCodes =
            new Engine30WorkoutTypeCatalog()
                .GetAll()
                .Select(x => x.WorkoutTypeCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var recipeCodes =
            SystemWorkoutRecipeCatalog.CoreRecipes
                .Select(x => x.SystemCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(27, engineCodes.Count);
        Assert.Equal(27, recipeCodes.Count);
        Assert.Empty(engineCodes.Except(recipeCodes, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(recipeCodes.Except(engineCodes, StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(SystemWorkoutRecipeCatalog.ChestTricepsGlutesHamstringsCode, "FULL_BODY_STRENGTH")]
    [InlineData(SystemWorkoutRecipeCatalog.BackBicepsQuadsCode, "FULL_BODY_STRENGTH")]
    [InlineData(SystemWorkoutRecipeCatalog.ShouldersAbsCalvesCode, "ACCESSORY")]
    public void SignatureRecipesMapToExistingCalibratedEngineTypes(
        string systemCode,
        string expectedEngineCode)
    {
        Assert.True(SystemWorkoutRecipeCatalog.TryGet(systemCode, out var recipe));
        Assert.NotNull(recipe);
        Assert.True(recipe!.IsSignature);
        Assert.Equal(expectedEngineCode, recipe.EngineWorkoutTypeCode);

        var resolved =
            new Engine30WorkoutTypeCatalog()
                .GetRequired(recipe.EngineWorkoutTypeCode);

        Assert.Equal(expectedEngineCode, resolved.WorkoutTypeCode);
    }

    [Fact]
    public void MobilityRecipeContainsCoreControlAndPostureSupportSlots()
    {
        Assert.True(SystemWorkoutRecipeCatalog.TryGet("MOBILITY_RECOVERY", out var recipe));
        Assert.NotNull(recipe);

        var allNames =
            recipe!.Slots
                .SelectMany(x => x.PreferredNameTerms)
                .ToArray();

        foreach (var expected in new[]
        {
            "glute bridge",
            "posterior pelvic tilt",
            "dead bug",
            "bird dog",
            "pallof press",
            "hip flexor stretch",
            "90/90",
            "thoracic rotation",
            "wall slide"
        })
        {
            Assert.Contains(
                allNames,
                x => x.Contains(expected, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TrainerResearchSeedTermsHaveIntentionalPriorityOrder()
    {
        Assert.True(
            SystemWorkoutRecipeCatalog.TryGet(
                SystemWorkoutRecipeCatalog.ChestTricepsGlutesHamstringsCode,
                out var chestRecipe));

        Assert.NotNull(chestRecipe);

        var upperChest =
            chestRecipe!.Slots.Single(
                x => x.SlotCode == "UPPER_CHEST");

        Assert.Equal(
            "low incline dumbbell press",
            upperChest.PreferredNameTerms[0]);

        Assert.True(
            SystemWorkoutRecipeCatalog.TryGet(
                SystemWorkoutRecipeCatalog.BackBicepsQuadsCode,
                out var backRecipe));

        var quads =
            backRecipe!.Slots.Single(
                x => x.SlotCode == "QUAD_COMPOUND");

        Assert.Equal(
            "hack squat",
            quads.PreferredNameTerms[0]);

        Assert.True(
            SystemWorkoutRecipeCatalog.TryGet(
                SystemWorkoutRecipeCatalog.ShouldersAbsCalvesCode,
                out var shoulderRecipe));

        var lateral =
            shoulderRecipe!.Slots.Single(
                x => x.SlotCode == "LATERAL_DELT");

        Assert.Equal(
            "cable lateral raise",
            lateral.PreferredNameTerms[0]);
    }

    [Fact]
    public void SignatureRecipesHaveDistinctSystemCodesAndUsefulVolume()
    {
        var recipes = SystemWorkoutRecipeCatalog.SignatureRecipes;

        Assert.Equal(3, recipes.Count);
        Assert.Equal(
            3,
            recipes
                .Select(x => x.SystemCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        Assert.All(recipes, recipe => Assert.True(recipe.Slots.Count >= 8));
    }
}
