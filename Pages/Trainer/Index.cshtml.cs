using Microsoft.AspNetCore.Mvc;
using TwelveWeekRecomp.Live.Services;

namespace TwelveWeekRecomp.Live.Pages.Trainer;

public sealed class IndexModel : TrainerPageModel
{
    public IndexModel(StaffAccessService staffAccess)
        : base(staffAccess)
    {
    }

    public IActionResult OnGet()
    {
        var denied = RequireTrainer();
        return denied ?? Page();
    }
}
