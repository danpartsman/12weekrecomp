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

        Assert.NotEmpty(choices);
        Assert.Equal(
            choices.Count,
            choices
                .Select(x => x.SystemCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        var cardio =
            choices.Single(
                x => x.SystemCode == CardioActivityCatalog.PresentationSystemCode);

        Assert.Equal("Cardio", cardio.DisplayName);
        Assert.Equal("GENERAL_CARDIO", cardio.EngineWorkoutTypeCode);
        Assert.DoesNotContain(
            choices,
            x => x.SystemCode != CardioActivityCatalog.PresentationSystemCode
                 && CardioActivityCatalog.IsCollapsedCardioEngineCode(
                     x.EngineWorkoutTypeCode));

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

        var expectedNonOtf = choices
            .Where(x => !x.GroupName.Equals("Orangetheory", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => groupOrder[x.GroupName])
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.SystemCode)
            .ToArray();

        Assert.Equal(
            expectedNonOtf,
            choices
                .Where(x => !x.GroupName.Equals("Orangetheory", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.SystemCode));

        var otf = choices
            .Where(x => x.GroupName.Equals("Orangetheory", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.SystemCode)
            .ToArray();

        Assert.Equal("OTF_STRENGTH60_2026_10_02", otf[0]);
        Assert.Equal("OTF_2G_2026_09_30", otf[1]);
        Assert.Equal("OTF_STRENGTH50_2026_09_30", otf[2]);
        Assert.Equal("OTF_2G_2026_09_29", otf[3]);
        Assert.Equal("OTF_2G_2026_09_28", otf[4]);
        Assert.Equal("OTF_2G_2026_09_27", otf[5]);
        Assert.Contains("ORANGETHEORY", otf.Skip(6));
        Assert.Contains("SYS_OTF_STRENGTH_50", otf.Skip(6));
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
        var strength = SystemWorkoutRecipeCatalog.PresentationRecipes.Single(x => x.SystemCode == "SYS_OTF_STRENGTH_50");
        Assert.Equal("Orangetheory", strength.GroupName);
        Assert.Equal("RESISTANCE_CIRCUIT", strength.EngineWorkoutTypeCode);
        Assert.Equal(50m, SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(strength, 45m));
        Assert.Equal("Standard Orangetheory 2G", recipe.DisplayName);
        Assert.Equal(60m, SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(recipe, 60m));
        Assert.Contains(SystemWorkoutRecipeCatalog.PresentationRecipes, x => x.SystemCode == "OTF_2G_2026_09_27");

        var dated =
            SystemWorkoutRecipeCatalog.PresentationRecipes
                .Single(x => x.SystemCode == "OTF_2G_2026_09_28");

        Assert.Equal("OTF 2G - 2026-09-28", dated.DisplayName);
        Assert.Equal("Orangetheory", dated.GroupName);
        Assert.Equal("ORANGETHEORY", dated.EngineWorkoutTypeCode);
        Assert.Equal(60m, SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(dated, 60m));
        Assert.Equal(16, dated.Slots.Count);
        Assert.Contains(
            dated.Slots,
            x => x.PreferredNameTerms.Single() == "Dumbbell Mid Band Squat"
                && x.Reps == 8);
        Assert.Contains(
            dated.Slots,
            x => x.PreferredNameTerms.Single() == "Treadmill Elevation Surge"
                && x.Sets == 5
                && x.WorkloadText!.Contains("40", StringComparison.Ordinal));

        var floorSquat = dated.Slots.Single(
            x => x.PreferredNameTerms.Single() == "Dumbbell Mid Band Squat");
        Assert.Equal("Floor Block 1", floorSquat.BlockName);
        Assert.Equal("Circuit", floorSquat.BlockType);
        Assert.Equal(540, floorSquat.DurationSeconds);
        Assert.Equal(3, floorSquat.Sets);
        Assert.Equal(3, floorSquat.EstimatedRounds);
        Assert.True(floorSquat.IsEstimated);

        var finisher = dated.Slots.Single(
            x => x.SlotCode == "OTF_0928_BICEP_FINISHER");
        Assert.Equal(30, finisher.DurationSeconds);
        Assert.Equal("Floor Finisher", finisher.BlockName);

        var september29 =
            SystemWorkoutRecipeCatalog.PresentationRecipes
                .Single(x => x.SystemCode == "OTF_2G_2026_09_29");

        Assert.Equal("OTF 2G - 2026-09-29", september29.DisplayName);
        Assert.Equal("Orangetheory", september29.GroupName);
        Assert.Equal("ORANGETHEORY", september29.EngineWorkoutTypeCode);
        Assert.Equal(60m, SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(september29, 60m));
        Assert.Equal(19, september29.Slots.Count);

        var requiredRows = september29.Slots.Single(
            x => x.SlotCode == "OTF_0929_ROW_500_REQUIRED");
        Assert.Equal(2, requiredRows.Sets);
        Assert.Equal(500m, requiredRows.DistanceMeters);
        Assert.Equal("Row", requiredRows.BlockType);

        var treadPush = september29.Slots.Single(
            x => x.SlotCode == "OTF_0929_TREAD_PUSH_400");
        Assert.Equal(400m, treadPush.DistanceMeters);
        Assert.Equal(2, treadPush.Sets);

        Assert.Equal(
            4,
            september29.Slots.Count(
                x => x.BlockType == "Member Choice"));
        Assert.All(
            september29.Slots.Where(x => x.BlockType == "Member Choice"),
            x => Assert.Equal(135, x.DurationSeconds));

        var september30 =
            SystemWorkoutRecipeCatalog.PresentationRecipes
                .Single(x => x.SystemCode == "OTF_2G_2026_09_30");

        Assert.Equal("OTF 2G - 2026-09-30", september30.DisplayName);
        Assert.Equal("Orangetheory", september30.GroupName);
        Assert.Equal("ORANGETHEORY", september30.EngineWorkoutTypeCode);
        Assert.Equal(60m, SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(september30, 60m));
        Assert.Equal(30, september30.Slots.Count);
        Assert.Equal(5, september30.Slots.Count(x => x.BlockName == "Floor Block 1" && x.DurationSeconds == 1110));
        Assert.Equal(4, september30.Slots.Count(x => x.BlockName == "Floor Block 2"));
        Assert.Contains(september30.Slots, x => x.PreferredNameTerms.Single() == "Two Handed Single Dumbbell Squat" && x.Reps == 5 && x.EstimatedRounds == 3 && x.IsEstimated);
        var september30Row = september30.Slots.Single(x => x.SlotCode == "OTF_0930_PUSH_ROW");
        Assert.Equal(300m, september30Row.DistanceMeters);
        Assert.Equal("Row", september30Row.BlockType);

        var september30Strength =
            SystemWorkoutRecipeCatalog.PresentationRecipes
                .Single(x => x.SystemCode == "OTF_STRENGTH50_2026_09_30");

        Assert.Equal("OTF Strength 50 Total - 2026-09-30", september30Strength.DisplayName);
        Assert.Equal("Orangetheory", september30Strength.GroupName);

        var datedOtfNames = SystemWorkoutRecipeCatalog.PresentationRecipes
            .Where(x => x.GroupName == "Orangetheory" && (x.SystemCode.StartsWith("OTF_2G_") || x.SystemCode.StartsWith("OTF_STRENGTH50_") || x.SystemCode.StartsWith("OTF_STRENGTH60_")))
            .Select(x => x.DisplayName)
            .ToArray();
        Assert.Contains("2026-10-02", datedOtfNames[0]);
        Assert.Contains("2026-09-30", datedOtfNames[1]);
        Assert.Contains("2026-09-30", datedOtfNames[2]);
        Assert.Equal("RESISTANCE_CIRCUIT", september30Strength.EngineWorkoutTypeCode);
        Assert.Equal(50m, SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(september30Strength, 45m));
        Assert.Equal(14, september30Strength.Slots.Count);
        Assert.Contains(september30Strength.Slots, x => x.PreferredNameTerms.Single() == "Dumbbell Split Squat With Leg On BOSU" && x.Reps == 8);
        Assert.Contains(september30Strength.Slots, x => x.PreferredNameTerms.Single() == "Alternating Single Arm Snatch" && x.WorkloadText == "4-8 reps");
        Assert.Equal(2, september30Strength.Slots.Count(x => x.BlockName == "Finisher" && x.DurationSeconds == 30));

        var october2HellWeek =
            SystemWorkoutRecipeCatalog.PresentationRecipes
                .Single(x => x.SystemCode == "OTF_STRENGTH60_2026_10_02");

        Assert.Equal("OTF Hell Week Strength 60 - 2026-10-02", october2HellWeek.DisplayName);
        Assert.Equal("Orangetheory", october2HellWeek.GroupName);
        Assert.Equal("RESISTANCE_CIRCUIT", october2HellWeek.EngineWorkoutTypeCode);
        Assert.Equal(60m, SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(october2HellWeek, 45m));
        Assert.Equal(11, october2HellWeek.Slots.Count);
        Assert.Contains(october2HellWeek.Slots, x => x.PreferredNameTerms.Single() == "Barbell Romanian Deadlift" && x.Sets == 3 && x.WorkloadText == "3 x 8-10; rest 75 sec");
        Assert.Contains(october2HellWeek.Slots, x => x.PreferredNameTerms.Single() == "Split Squat" && x.Sets == 2 && x.WorkloadText == "2 x 5/side; rest 60 sec");
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

        var cardio =
            SystemWorkoutRecipeCatalog.PresentationRecipes
                .Single(
                    x => x.SystemCode == CardioActivityCatalog.PresentationSystemCode);

        Assert.Equal("Cardio", cardio.DisplayName);

        foreach (var activityName in new[]
        {
            "Running",
            "Cycling / stationary bike",
            "Rowing",
            "Treadmill",
            "Swimming",
            "Stair climber / stepper"
        })
        {
            Assert.Contains(
                CardioActivityCatalog.All,
                x => x.DisplayName == activityName);
        }

        Assert.DoesNotContain(
            SystemWorkoutRecipeCatalog.PresentationRecipes,
            x => x.DisplayName.StartsWith(
                "Easy Running",
                StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            SystemWorkoutRecipeCatalog.PresentationRecipes,
            x => x.DisplayName.StartsWith(
                "Hard Cycling",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PresentationRecipesResolveEveryEngine30TypeThroughCanonicalUi()
    {
        var engineCodes =
            new Engine30WorkoutTypeCatalog()
                .GetAll()
                .Select(x => x.WorkoutTypeCode)
                .ToArray();

        foreach (var engineCode in engineCodes)
        {
            var presentation =
                SystemWorkoutRecipeCatalog
                    .GetDefaultPresentationForEngine(
                        engineCode);

            Assert.NotNull(presentation);

            if (CardioActivityCatalog.IsCollapsedCardioEngineCode(engineCode))
            {
                Assert.Equal(
                    CardioActivityCatalog.PresentationSystemCode,
                    presentation!.SystemCode);
            }
        }
    }

    [Fact]
    public void CardioActivitiesMapOnlyToExistingCalibratedEngine30Types()
    {
        var engine =
            new Engine30WorkoutTypeCatalog();

        Assert.NotEmpty(CardioActivityCatalog.All);

        foreach (var activity in CardioActivityCatalog.All)
        {
            Assert.True(
                engine.TryGet(
                    activity.EngineWorkoutTypeCode,
                    out var definition));

            Assert.NotNull(definition);
            Assert.Equal(
                activity.EngineWorkoutTypeCode,
                definition!.WorkoutTypeCode);
        }

        Assert.Equal(
            "CARDIO",
            SystemWorkoutRecipeCatalog
                .GetDefaultPresentationForEngine("RUNNING")!
                .SystemCode);

        Assert.Equal(
            "CARDIO",
            SystemWorkoutRecipeCatalog
                .GetDefaultPresentationForEngine("ZONE2_CARDIO")!
                .SystemCode);
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

    [Fact]
    public void PlanningRecipesKeepOnlyReusableOrangetheoryChoices()
    {
        var planningOrangetheory =
            SystemWorkoutRecipeCatalog
                .PlanningRecipes
                .Where(
                    x => x.GroupName.Equals(
                        "Orangetheory",
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

        Assert.Equal(
            new[]
            {
                "ORANGETHEORY",
                "SYS_OTF_STRENGTH_50"
            },
            planningOrangetheory
                .Select(x => x.SystemCode)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray());

        Assert.DoesNotContain(
            SystemWorkoutRecipeCatalog.PlanningRecipes,
            x =>
                SystemWorkoutRecipeCatalog
                    .IsDatedOrangetheorySystemCode(
                        x.SystemCode));

        Assert.Contains(
            SystemWorkoutRecipeCatalog.PresentationRecipes,
            x =>
                SystemWorkoutRecipeCatalog
                    .IsDatedOrangetheorySystemCode(
                        x.SystemCode));

        Assert.Equal(
            "ORANGETHEORY",
            SystemWorkoutRecipeCatalog
                .NormalizePlanningSystemCode(
                    "OTF_2G_2026_09_30"));

        Assert.Equal(
            "SYS_OTF_STRENGTH_50",
            SystemWorkoutRecipeCatalog
                .NormalizePlanningSystemCode(
                    "OTF_STRENGTH50_2026_09_30"));
    }

}
