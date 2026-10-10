using System.ComponentModel.DataAnnotations;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Tests;

public sealed class OnboardingActivityInputTests
{
    [Fact]
    public void MissingWorkAndLifestyleCannotSilentlyBecomeSedentary()
    {
        var model = ValidModel();
        model.WorkStatusCode = "";
        model.LifestyleActivityCode = null;
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(model.WorkStatusCode)));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(model.LifestyleActivityCode)));
    }

    [Fact]
    public void WorkingRequiresRoleActivityAndPositiveHours()
    {
        var model = ValidModel();
        model.WorkStatusCode = "WORKING";
        Assert.NotEmpty(model.Validate(new ValidationContext(model)));
        model.OccupationalRoles[0].OccupationName = "Retail";
        var errors = model.Validate(new ValidationContext(model)).ToArray();
        Assert.Contains(errors, x => x.MemberNames.Contains("OccupationalRoles[0].TypicalHoursPerWeek"));
        Assert.Contains(errors, x => x.MemberNames.Contains("OccupationalRoles[0].OccupationActivityCode"));
    }

    [Fact]
    public void ActiveJobWithBlankOptionalRolesIsValid()
    {
        var model = ValidModel();
        model.WorkStatusCode = "WORKING";
        model.OccupationalRoles[0] = new()
        {
            OccupationName = "Retail", OccupationActivityCode = "ACTIVE_LIGHT", TypicalHoursPerWeek = 40m
        };
        Assert.Empty(model.Validate(new ValidationContext(model)));
    }

    [Fact]
    public void InvalidClassificationAndExcessCombinedHoursAreRejected()
    {
        var model = ValidModel();
        model.WorkStatusCode = "WORKING";
        model.OccupationalRoles[0] = new()
        {
            OccupationName = "Retail", OccupationActivityCode = "UNKNOWN", TypicalHoursPerWeek = 100m
        };
        model.OccupationalRoles[1] = new()
        {
            OccupationName = "Caregiving", OccupationActivityCode = "TYPICAL_MIXED", TypicalHoursPerWeek = 100m
        };
        var errors = model.Validate(new ValidationContext(model)).ToArray();
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(model.OccupationalRoles)));
        Assert.Contains(errors, x => x.MemberNames.Contains("OccupationalRoles[0].OccupationActivityCode"));
    }

    [Fact]
    public void ExplicitNoWorkNeedsNoInventedOccupation()
    {
        var model = ValidModel();
        Assert.Empty(model.Validate(new ValidationContext(model)));
    }

    [Fact]
    public void WalkingJobFeedsHabitualEnergyAboveRestingAndDeskWork()
    {
        // Hypothetical height: this is a wiring regression, not a user diagnosis.
        var date = new DateOnly(2026, 10, 8);
        var resting = new Engine30BaseLivingCalculator().Calculate(
            new Engine30BaseLivingInput(date, date.AddYears(-45), "FEMALE", 64m, 146m));
        var calculator = new Engine30HabitualActivityCalculator();
        var window = Engine30HabitualActivityWindowPolicy.Resolve(45).HoursPerWeek;
        Engine30HabitualActivityResult Calculate(string code)
        {
            var option = Engine30ActivityProfileCatalog.ResolveOccupation(code);
            return calculator.Calculate(resting.BaseLivingEnergyKcal,
                [new Engine30OccupationalRoleInput("Work", code, 40m, option.EffectiveOccupationalActivityFactor)],
                "LIGHT", habitualWindowHoursPerWeek: window);
        }
        var desk = Calculate("HIGHLY_SEDENTARY");
        var walking = Calculate("ACTIVE_LIGHT");
        Assert.True(walking.AverageDailyOccupationalActivityEnergyKcal > desk.AverageDailyOccupationalActivityEnergyKcal);
        Assert.True(walking.BasePlusHabitualEnergyKcal > resting.BaseLivingEnergyKcal);
        Assert.Equal(Math.Round(resting.BaseLivingEnergyKcal + walking.AverageDailyHabitualActivityEnergyKcal, 2),
            walking.BasePlusHabitualEnergyKcal);
    }

    private static OnboardingSetupViewModel ValidModel() => new()
    {
        ClientName = "Example", BirthYear = 1981, SexCode = "FEMALE", HeightIn = 64m,
        StartingWeightLb = 146m, GoalCode = "FAT_LOSS_RECOMP", WorkStatusCode = "NO_WORK",
        LifestyleActivityCode = "LIGHT", MedicalAcknowledged = true,
        TrainingAcknowledged = true, TrackingAcknowledged = true
    };
}
