using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;

namespace WebRecomp.Web.Services.Notifications;

public interface IPhoneNotificationSender
{
    Task<bool> SendAsync(NotificationDevice device, string subject, string url, string tag, CancellationToken ct);
}
public sealed class WebPushDelivery(NotificationFiles files, IHttpClientFactory clients) : IPhoneNotificationSender
{
    public async Task<bool> SendAsync(NotificationDevice device, string subject, string url, string tag, CancellationToken ct)
    {
        NotificationFiles.ValidateDevice(device);
        var keys = files.Identity;
        using var http = clients.CreateClient("CoachWebPush");
        var sender = new PushServiceClient(http) { AutoRetryAfter = false, DefaultTimeToLive = 86400 };
        using var authentication = new VapidAuthentication(keys.PublicKey, keys.PrivateKey) { Subject = "mailto:contact@12weekrecomp.com" };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var subscription = new PushSubscription { Endpoint = device.Endpoint, Keys = new Dictionary<string, string> { ["p256dh"] = device.P256dh, ["auth"] = device.Auth } };
            await sender.RequestPushMessageDeliveryAsync(subscription,
                new PushMessage(JsonSerializer.Serialize(new { title = "12 Week Recomp", body = subject, url, tag })), authentication, timeout.Token);
            return true;
        }
        catch (PushServiceClientException e) when (e.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound) { return false; }
    }
}
