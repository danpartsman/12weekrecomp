using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using TwelveWeekRecomp.Live.Services;

namespace TwelveWeekRecomp.Live.Pages;

[EnableRateLimiting("launch-join")]
public sealed class JoinModel : PageModel
{
    private static readonly HashSet<string> AllowedInterestCodes =
        new(StringComparer.Ordinal)
        {
            "FREE_UPDATES",
            "FOUNDERS_RATE"
        };

    private readonly LaunchInterestRepository _repository;
    private readonly ILogger<JoinModel> _logger;

    public JoinModel(
        LaunchInterestRepository repository,
        ILogger<JoinModel> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [BindProperty]
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = "";

    [BindProperty]
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Choose one launch option.")]
    public string InterestChoice { get; set; } = "FREE_UPDATES";

    [BindProperty]
    public string? Company { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(Company))
            return RedirectToPage("/Thanks");

        if (!AllowedInterestCodes.Contains(InterestChoice))
        {
            ModelState.AddModelError(
                nameof(InterestChoice),
                "Choose a valid launch option.");
        }

        if (!ModelState.IsValid)
            return Page();

        try
        {
            await _repository.UpsertAsync(
                Name,
                Email,
                InterestChoice,
                cancellationToken);

            return RedirectToPage("/Thanks");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unable to save launch-interest submission.");

            ModelState.AddModelError(
                string.Empty,
                "We could not save your request right now. Please try again shortly.");

            return Page();
        }
    }
}
