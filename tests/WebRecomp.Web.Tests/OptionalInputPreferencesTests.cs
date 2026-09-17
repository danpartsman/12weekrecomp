using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Tests;

public sealed class OptionalInputPreferencesTests
{
    [Fact]
    public void ProgramPreferences_DefaultOffOff()
    {
        var program = new ProgramRecord
        {
            ProgramId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            ConfigVersionId = Guid.NewGuid(),
            SelectedPlanCode = "TEST",
            GoalCode = "RECOMP",
            StartDate = new DateOnly(2026, 9, 11),
            DurationWeeks = 12,
            AvailableTrainingDaysWeek = 4
        };

        Assert.False(program.DetailedWorkoutEntryEnabled);
        Assert.False(program.NutritionFoodLogEnabled);
    }

    [Fact]
    public void SettingsPreferences_DefaultOffOff()
    {
        var model = new SettingsViewModel();

        Assert.False(model.DetailedWorkoutEntryEnabled);
        Assert.False(model.NutritionFoodLogEnabled);
    }

    [Theory]
    [InlineData("MANUAL")]
    [InlineData("DETAILED_WORKOUT")]
    [InlineData("FOOD_LOG")]
    public void InputSourceCodes_AreStable(string expected)
    {
        var actual = expected switch
        {
            "MANUAL" => RecompInputSourceCodes.Manual,
            "DETAILED_WORKOUT" => RecompInputSourceCodes.DetailedWorkout,
            "FOOD_LOG" => RecompInputSourceCodes.FoodLog,
            _ => throw new InvalidOperationException()
        };

        Assert.Equal(expected, actual);
    }
}
