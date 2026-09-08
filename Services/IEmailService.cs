namespace TwelveWeekRecomp.Live.Services;

public interface IEmailService
{
    bool IsConfigured { get; }

    Task SendHtmlAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        string? replyTo = null,
        CancellationToken cancellationToken = default);
}
