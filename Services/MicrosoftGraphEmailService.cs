using System.Net.Http.Headers;
using System.Net.Http.Json;
using Azure.Core;
using Azure.Identity;

namespace TwelveWeekRecomp.Live.Services;

public sealed class MicrosoftGraphEmailService : IEmailService
{
    private static readonly string[] GraphScopes =
        ["https://graph.microsoft.com/.default"];

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ManagedIdentityCredential _credential = new();

    public MicrosoftGraphEmailService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    public bool IsConfigured =>
        _configuration.GetValue<bool>("Email:Enabled") &&
        !string.IsNullOrWhiteSpace(_configuration["Email:SenderMailbox"]);

    public async Task SendHtmlAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        string? replyTo = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Email is not configured.");

        var sender = _configuration["Email:SenderMailbox"]!.Trim();

        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(GraphScopes),
            cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(sender)}/sendMail");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Token);

        var message = new Dictionary<string, object?>
        {
            ["subject"] = subject,
            ["body"] = new
            {
                contentType = "HTML",
                content = htmlBody
            },
            ["toRecipients"] = new[]
            {
                new
                {
                    emailAddress = new
                    {
                        address = recipientEmail.Trim()
                    }
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            message["replyTo"] = new[]
            {
                new
                {
                    emailAddress = new
                    {
                        address = replyTo.Trim()
                    }
                }
            };
        }

        request.Content = JsonContent.Create(new
        {
            message,
            saveToSentItems = true
        });

        var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            if (detail.Length > 1200)
                detail = detail[..1200];

            throw new InvalidOperationException(
                $"Microsoft Graph sendMail failed: {(int)response.StatusCode} {response.ReasonPhrase}. {detail}");
        }
    }
}
