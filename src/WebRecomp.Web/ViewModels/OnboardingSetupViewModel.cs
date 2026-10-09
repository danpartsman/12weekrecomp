using System.ComponentModel.DataAnnotations;

namespace WebRecomp.Web.ViewModels;

public sealed class OnboardingSetupViewModel : IValidatableObject
{
    [Required, StringLength(100)]
    public string ClientName { get; set; } = "";

    [Required]
    [Range(1900, 2100, ErrorMessage = "Enter a valid birth year.")]
    public int? BirthYear { get; set; }

    [Range(1, 12)]
    public int? BirthMonth { get; set; }

    [Range(1, 31)]
    public int? BirthDay { get; set; }

    [Required]
    [RegularExpression("^(MALE|FEMALE)$", ErrorMessage = "Choose the sex used for metabolic calculations.")]
    public string SexCode { get; set; } = "";

    [Required]
    [Range(48, 90)]
    public decimal? HeightIn { get; set; }

    [Required]
    [Range(75, 700)]
    public decimal? StartingWeightLb { get; set; }

    [Required]
    [RegularExpression("^(FAT_LOSS_RECOMP|FAT_LOSS|MUSCLE_BUILD|ATHLETIC_RECOMP|HEALTH_RESET|PAIN_LIMITED)$", ErrorMessage = "Choose a primary goal.")]
    public string GoalCode { get; set; } = "";

    [Required]
    public string TrainingExperienceCode { get; set; } = "INTERMEDIATE";

    [Range(1, 6)]
    public byte AvailableTrainingDaysWeek { get; set; } = 4;

    // Monday = 0 through Sunday = 6; no selection means a flexible day.
    public int[] PreferredTrainingDays { get; set; } = [];
    public int[] PreferredRestDays { get; set; } = [];

    [Required]
    public string TimeZone { get; set; } = "America/Los_Angeles";

    [Required]
    [WebRecomp.Web.Services.Learning.CoachCode]
    public string PreferredCoachCode { get; set; } = "MARISOL";

    [Range(typeof(bool), "true", "true", ErrorMessage = "Please acknowledge the medical guidance.")]
    public bool MedicalAcknowledged { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Please acknowledge the training guidance.")]
    public bool TrainingAcknowledged { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Please acknowledge the tracking limitations.")]
    public bool TrackingAcknowledged { get; set; }

    // Helpful, deliberately optional. Unknown is better than invented precision.
    [Range(2, 70)]
    public decimal? StartingBodyFatPercent { get; set; }

    [Range(0, 500)]
    public decimal? StartingMuscleMassLb { get; set; }

    [Range(0, 500)]
    public decimal? StartingBodyWaterLb { get; set; }

    [Range(0, 100)]
    public decimal? StartingWaistIn { get; set; }

    [Range(0, 100)]
    public decimal? StartingHipsIn { get; set; }

    [Range(0, 100)]
    public decimal? StartingVisceralFatRating { get; set; }

    public string? LifestyleActivityCode { get; set; }

    [Range(0, 100000)]
    public int? BaselineStepsDay { get; set; }

    [Range(0, 16)]
    public decimal? BaselineSleepHours { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BirthMonth.HasValue != BirthDay.HasValue)
            yield return new ValidationResult("Enter both month and day, or leave both blank.", [nameof(BirthMonth), nameof(BirthDay)]);

        if (BirthYear is int year && year is >= 1 and <= 9999
            && BirthMonth is int month && BirthDay is int day
            && month is >= 1 and <= 12 && day is >= 1 and <= 31
            && day > DateTime.DaysInMonth(year, month))
            yield return new ValidationResult("That month and day are not a valid date.", [nameof(BirthDay)]);

        var training = PreferredTrainingDays ?? [];
        var rest = PreferredRestDays ?? [];
        if (training.Any(x => x is < 0 or > 6) || rest.Any(x => x is < 0 or > 6)
            || training.Distinct().Count() != training.Length || rest.Distinct().Count() != rest.Length
            || training.Intersect(rest).Any())
            yield return new ValidationResult("Choose at most one preference per weekday.", [nameof(PreferredTrainingDays), nameof(PreferredRestDays)]);

        if (training.Length > AvailableTrainingDaysWeek || rest.Length > 7 - AvailableTrainingDaysWeek)
            yield return new ValidationResult("Your preferred training and rest days must fit the weekly training-day count.", [nameof(AvailableTrainingDaysWeek)]);
    }
}
