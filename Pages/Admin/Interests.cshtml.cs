using System.Text;
using Microsoft.AspNetCore.Mvc;
using TwelveWeekRecomp.Live.Services;

namespace TwelveWeekRecomp.Live.Pages.Admin;

public sealed class InterestsModel : AdminPageModel
{
    private readonly LaunchInterestRepository _repository;

    public InterestsModel(StaffAccessService staffAccess, LaunchInterestRepository repository)
        : base(staffAccess)
    {
        _repository = repository;
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? InterestCode { get; set; }
    [BindProperty(SupportsGet = true)] public string? EmailStatus { get; set; }

    public IReadOnlyList<LaunchInterestRow> Rows { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var denied = RequireAdmin("/admin/interests");
        if (denied is not null) return denied;
        Rows = await _repository.ListAsync(Search, InterestCode, EmailStatus, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        var denied = RequireAdmin("/admin/interests");
        if (denied is not null) return denied;
        var rows = await _repository.ListAsync(Search, InterestCode, EmailStatus, cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine("Name,Email,InterestCode,EmailStatus,SubmissionCount,FirstSubmittedUtc,LastSubmittedUtc,WelcomeEmailStatus,WelcomeEmailSentUtc");
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(',', new[]
            {
                Csv(row.Name), Csv(row.Email), Csv(row.InterestCode), Csv(row.EmailStatus),
                row.SubmissionCount.ToString(), Csv(row.FirstSubmittedUtc.ToString("O")),
                Csv(row.LastSubmittedUtc.ToString("O")), Csv(row.WelcomeEmailStatus),
                Csv(row.WelcomeEmailSentUtc?.ToString("O") ?? "")
            }));
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", $"12wr-signups-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
