using System.ComponentModel.DataAnnotations;

namespace WebRecomp.Web.ViewModels;

public sealed class HabitualActivityViewModel
{
    [Required]
    public string LifestyleActivityCode { get; set; } = "MINIMAL";

    public List<HabitualOccupationRoleEditViewModel> Roles { get; set; } = [];
}

public sealed class HabitualOccupationRoleEditViewModel
{
    [StringLength(100)]
    public string? OccupationName { get; set; }

    [StringLength(40)]
    public string? OccupationActivityCode { get; set; }

    [Range(0, 168)]
    public decimal? TypicalHoursPerWeek { get; set; }

    public bool IsPrimary { get; set; }
}
