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
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<JoinModel> _logger;

    public JoinModel(
        LaunchInterestRepository repository,
        IEmailService emailService,
        IConfiguration configuration,
        ILogger<JoinModel> logger)
    {
        _repository = repository;
        _emailService = emailService;
        _configuration = configuration;
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

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(Company))
            return RedirectToPage("/Thanks");

        if (!AllowedInterestCodes.Contains(InterestChoice))
            ModelState.AddModelError(nameof(InterestChoice), "Choose a valid launch option.");

        if (!ModelState.IsValid)
            return Page();

        try
        {
            var result = await _repository.UpsertAsync(
                Name,
                Email,
                InterestChoice,
                cancellationToken);

            if ((result.IsNew || result.Reactivated) && _emailService.IsConfigured)
            {
                try
                {
                    var baseUrl = (_configuration["Site:PublicBaseUrl"] ?? "https://12weekrecomp.com").TrimEnd('/');
                    var unsubscribeUrl = $"{baseUrl}/unsubscribe?token={result.UnsubscribeToken:N}";
                    var body = EmailTemplates.Welcome(Name, InterestChoice, unsubscribeUrl);

                    await _emailService.SendHtmlAsync(
                        Email,
                        "You're on the 12 Week Recomp list",
                        body,
                        cancellationToken: cancellationToken);

                    await _repository.MarkWelcomeEmailAsync(
                        result.LaunchInterestId,
                        true,
                        null,
                        cancellationToken);
                }
                catch (Exception emailEx)
                {
                    _logger.LogError(emailEx, "Launch welcome email failed for interest ID {Id}.", result.LaunchInterestId);
                    await _repository.MarkWelcomeEmailAsync(
                        result.LaunchInterestId,
                        false,
                        emailEx.Message,
                        cancellationToken);
                }
            }

            return RedirectToPage("/Thanks");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to save launch-interest submission.");
            ModelState.AddModelError(string.Empty, "We could not save your request right now. Please try again shortly.");
            return Page();
        }
    }
}
