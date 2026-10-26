using System.Net;
using System.Text.Json;
using WebPush;

namespace WebRecomp.Web.Services.Notifications;

public interface IPhoneNotificationSender
{
    Task<bool> SendAsync(NotificationDevice device, string subject, string url, string tag, CancellationToken ct);
}
public sealed class WebPushDelivery(NotificationFiles files) : IPhoneNotificationSender
{
    public async Task<bool> SendAsync(NotificationDevice device, string subject, string url, string tag, CancellationToken ct)
    {
        NotificationFiles.ValidateDevice(device);
        var keys = files.Identity;
        using var sender = new WebPushClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await sender.SendNotificationAsync(new PushSubscription(device.Endpoint, device.P256dh, device.Auth),
                JsonSerializer.Serialize(new { title = "12 Week Recomp", body = subject, url, tag }),
                new VapidDetails("mailto:contact@12weekrecomp.com", keys.PublicKey, keys.PrivateKey), timeout.Token);
            return true;
        }
        catch (WebPushException e) when (e.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound) { return false; }
    }
}
