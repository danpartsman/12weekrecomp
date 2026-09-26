using System.ComponentModel.DataAnnotations;

namespace WebRecomp.Web.ViewModels;

public sealed class OnboardingSetupViewModel
{
    [Required, StringLength(100)]
    public string ClientName { get; set; } = "";

    [Required]
    [Range(18, 120, ErrorMessage = "Enter an age from 18 to 120.")]
    public int? Age { get; set; }

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
    [RegularExpression("^(FAT_LOSS_RECOMP|FAT_LOSS|MUSCLE_BUILD)$", ErrorMessage = "Choose a primary goal.")]
    public string GoalCode { get; set; } = "";

    [Required]
    public string TrainingExperienceCode { get; set; } = "INTERMEDIATE";

    [Range(1, 7)]
    public byte AvailableTrainingDaysWeek { get; set; } = 4;

    [Required]
    public string TimeZone { get; set; } = "America/Los_Angeles";

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
}
