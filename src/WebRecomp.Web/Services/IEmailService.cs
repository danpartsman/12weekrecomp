namespace WebRecomp.Web.Services;

public sealed record OutboundEmail(
    string ToAddress,
    string Subject,
    string Body,
    bool IsHtml = false,
    string? ReplyToAddress = null,
    string? ReplyToName = null);

public sealed record EmailSendResult(
    bool Success,
    string? Error = null);

public interface IEmailService
{
    Task<EmailSendResult> SendAsync(
        OutboundEmail email,
        CancellationToken cancellationToken = default);
}
