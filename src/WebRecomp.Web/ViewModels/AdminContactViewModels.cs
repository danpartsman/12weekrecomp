using System.ComponentModel.DataAnnotations;

namespace WebRecomp.Web.ViewModels;

public sealed class AdminContactSettingsInput
{
    [EmailAddress]
    [StringLength(320)]
    [Display(Name = "Notification email")]
    public string? DestinationEmail { get; set; }

    public bool ContactEnabled { get; set; } = true;
}

public sealed class AdminContactMessageRow
{
    public Guid ContactMessageId { get; set; }
    public required string SenderName { get; set; }
    public required string SenderEmail { get; set; }
    public required string Subject { get; set; }
    public required string StatusCode { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
}

public sealed class AdminContactIndexViewModel
{
    public AdminContactSettingsInput Settings { get; set; } = new();

    public IReadOnlyList<AdminContactMessageRow> Messages { get; set; }
        = Array.Empty<AdminContactMessageRow>();

    public int UnreadCount { get; set; }
}

public sealed class AdminContactMessageDetailViewModel
{
    public Guid ContactMessageId { get; set; }

    public required string SenderName { get; set; }
    public required string SenderEmail { get; set; }
    public required string Subject { get; set; }
    public required string Message { get; set; }

    public required string StatusCode { get; set; }

    public DateTime SubmittedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }

    public required string EmailNotificationStatus { get; set; }
    public DateTime? EmailSentAtUtc { get; set; }
    public string? EmailError { get; set; }
}
