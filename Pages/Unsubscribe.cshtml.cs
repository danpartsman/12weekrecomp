using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TwelveWeekRecomp.Live.Services;

namespace TwelveWeekRecomp.Live.Pages;

public sealed class UnsubscribeModel : PageModel
{
    private readonly LaunchInterestRepository _repository;

    public UnsubscribeModel(LaunchInterestRepository repository)
    {
        _repository = repository;
    }

    [BindProperty(SupportsGet = true)]
    public Guid? Token { get; set; }

    public bool ValidToken => Token.HasValue && Token.Value != Guid.Empty;
    public bool Completed { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ValidToken)
            return Page();

        await _repository.UnsubscribeAsync(Token!.Value, cancellationToken);
        Completed = true;
        return Page();
    }
}
