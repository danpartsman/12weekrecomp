using Microsoft.AspNetCore.Mvc;
using TwelveWeekRecomp.Live.Services;

namespace TwelveWeekRecomp.Live.Pages.Admin;

public sealed class IndexModel : AdminPageModel
{
    private readonly LaunchInterestRepository _repository;

    public IndexModel(StaffAccessService staffAccess, LaunchInterestRepository repository)
        : base(staffAccess)
    {
        _repository = repository;
    }

    public LaunchInterestSummary Summary { get; private set; } = new(0, 0, 0, 0, 0);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var denied = RequireAdmin();
        if (denied is not null) return denied;
        Summary = await _repository.GetSummaryAsync(cancellationToken);
        return Page();
    }
}
