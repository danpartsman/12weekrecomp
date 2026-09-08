using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TwelveWeekRecomp.Live.Services;

namespace TwelveWeekRecomp.Live.Pages.Admin;

public sealed class SendModel : AdminPageModel
{
    private static readonly HashSet<string> Audiences = new(StringComparer.Ordinal)
    {
        "ALL_ACTIVE", "FREE_UPDATES", "FOUNDERS_RATE"
    };

    private readonly LaunchInterestRepository _repository;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendModel> _logger;

    public SendModel(
        StaffAccessService staffAccess,
        LaunchInterestRepository repository,
        IEmailService emailService,
        IConfiguration configuration,
        ILogger<SendModel> logger)
        : base(staffAccess)
    {
        _repository = repository;
        _emailService = emailService;
        _configuration = configuration;
        _logger = logger;
    }

    [BindProperty, Required, StringLength(32)]
    public string AudienceCode { get; set; } = "ALL_ACTIVE";

    [BindProperty, Required, StringLength(150)]
    public string Subject { get; set; } = "";

    [BindProperty, Required, StringLength(10000)]
    public string MessageText { get; set; } = "";

    public bool EmailConfigured => _emailService.IsConfigured;
    public bool ShowPreview { get; private set; }
    public int PreviewRecipientCount { get; private set; }
    public string PreviewHtml { get; private set; } = "";
    public string? ResultMessage { get; private set; }

    public IActionResult OnGet()
    {
        var denied = RequireAdmin("/admin/send");
        return denied ?? Page();
    }

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        var denied = RequireAdmin("/admin/send");
        if (denied is not null) return denied;
        ValidateAudience();
        if (!ModelState.IsValid) return Page();

        var recipients = await _repository.GetRecipientsAsync(AudienceCode, cancellationToken);
        PreviewRecipientCount = recipients.Count;
        ShowPreview = true;
        PreviewHtml = EmailTemplates.Broadcast(MessageText, "https://12weekrecomp.com/unsubscribe?token=PREVIEW");
        return Page();
    }

    public async Task<IActionResult> OnPostSendAsync(CancellationToken cancellationToken)
    {
        var denied = RequireAdmin("/admin/send");
        if (denied is not null) return denied;
        ValidateAudience();
        if (!_emailService.IsConfigured)
            ModelState.AddModelError(string.Empty, "Email is not configured on LIVE.");
        if (!ModelState.IsValid) return Page();

        var recipients = await _repository.GetRecipientsAsync(AudienceCode, cancellationToken);
        if (recipients.Count == 0)
        {
            ResultMessage = "No active recipients match that audience.";
            return Page();
        }

        var broadcastId = await _repository.CreateBroadcastAsync(
            Subject,
            MessageText,
            AudienceCode,
            recipients.Count,
            StaffIdentity?.Email,
            cancellationToken);

        var baseUrl = (_configuration["Site:PublicBaseUrl"] ?? "https://12weekrecomp.com").TrimEnd('/');
        var sent = 0;
        var failed = 0;

        foreach (var recipient in recipients)
        {
            try
            {
                var unsubscribeUrl = $"{baseUrl}/unsubscribe?token={recipient.UnsubscribeToken:N}";
                var html = EmailTemplates.Broadcast(MessageText, unsubscribeUrl);
                await _emailService.SendHtmlAsync(recipient.Email, Subject, html, cancellationToken: cancellationToken);
                sent++;
                await _repository.RecordDeliveryAsync(broadcastId, recipient, Subject, true, null, cancellationToken);
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Broadcast {BroadcastId} failed for launch interest {LaunchInterestId}.", broadcastId, recipient.LaunchInterestId);
                await _repository.RecordDeliveryAsync(broadcastId, recipient, Subject, false, ex.Message, cancellationToken);
            }
        }

        await _repository.CompleteBroadcastAsync(broadcastId, sent, failed, cancellationToken);
        ResultMessage = $"Broadcast {broadcastId}: sent {sent}, failed {failed}, total {recipients.Count}.";
        Subject = "";
        MessageText = "";
        return Page();
    }

    private void ValidateAudience()
    {
        if (!Audiences.Contains(AudienceCode))
            ModelState.AddModelError(nameof(AudienceCode), "Choose a valid audience.");
    }
}
