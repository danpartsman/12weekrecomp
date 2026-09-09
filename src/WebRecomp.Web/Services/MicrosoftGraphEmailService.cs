using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace WebRecomp.Web.Services;

public sealed class MicrosoftGraphEmailService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<MicrosoftGraphEmailService> logger)
    : IEmailService
{
    private static readonly string[] GraphScopes =
        ["https://graph.microsoft.com/.default"];

    private readonly ManagedIdentityCredential _credential = new(ManagedIdentityId.SystemAssigned);

    public async Task<EmailSendResult> SendAsync(
        OutboundEmail email,
        CancellationToken cancellationToken = default)
    {
        var senderAddress =
            configuration["Email:SenderAddress"];

        if (string.IsNullOrWhiteSpace(senderAddress))
        {
            return new EmailSendResult(
                false,
                "Email:SenderAddress is not configured.");
        }

        try
        {
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(GraphScopes),
                cancellationToken);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(senderAddress)}/sendMail");

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token.Token);

            var message = new Dictionary<string, object?>
            {
                ["subject"] = email.Subject,
                ["body"] = new
                {
                    contentType = email.IsHtml ? "HTML" : "Text",
                    content = email.Body
                },
                ["toRecipients"] = new[]
                {
                    new
                    {
                        emailAddress = new
                        {
                            address = email.ToAddress
                        }
                    }
                }
            };

            if (!string.IsNullOrWhiteSpace(email.ReplyToAddress))
            {
                message["replyTo"] = new[]
                {
                    new
                    {
                        emailAddress = new
                        {
                            address = email.ReplyToAddress,
                            name = email.ReplyToName
                        }
                    }
                };
            }

            var payload = new
            {
                message,
                saveToSentItems = true
            };

            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var response =
                await httpClient.SendAsync(
                    request,
                    cancellationToken);

            if (response.IsSuccessStatusCode)
                return new EmailSendResult(true);

            var responseBody =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            logger.LogError(
                "Microsoft Graph email send failed. Status {StatusCode}. Response: {Response}",
                (int)response.StatusCode,
                responseBody);

            return new EmailSendResult(
                false,
                $"Microsoft Graph returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Microsoft Graph email send failed.");

            return new EmailSendResult(
                false,
                ex.Message);
        }
    }
}
