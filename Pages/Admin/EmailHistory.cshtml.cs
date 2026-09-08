using Microsoft.AspNetCore.Mvc;
using TwelveWeekRecomp.Live.Services;

namespace TwelveWeekRecomp.Live.Pages.Admin;

public sealed class EmailHistoryModel : AdminPageModel
{
    private readonly LaunchInterestRepository _repository;

    public EmailHistoryModel(AdminAccessService adminAccess, LaunchInterestRepository repository)
        : base(adminAccess)
    {
        _repository = repository;
    }

    public IReadOnlyList<EmailBroadcastHistoryRow> Rows { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var denied = RequireAdmin("/admin/email-history");
        if (denied is not null) return denied;
        Rows = await _repository.GetBroadcastHistoryAsync(cancellationToken);
        return Page();
    }
}
