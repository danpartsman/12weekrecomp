using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace WebRecomp.Web.Services.Notifications;

public sealed class NotificationPreferences
{
    public bool Email { get; set; } = true;
    public bool DailyReview { get; set; } = true;
    public string ReviewTime { get; set; } = "20:00";
    public List<NotificationDevice> Devices { get; set; } = [];
}
public sealed record NotificationDevice(string Endpoint, string P256dh, string Auth);
public sealed record NotificationIdentity(DateTime ActiveSinceUtc, string PublicKey, string PrivateKey, string JobKey);
public sealed class NotificationDelivery
{
    public required string Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid CoachUserId { get; set; }
    public Guid UserId { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }
    public required string Url { get; set; }
    public bool Email { get; set; } = true;
    public bool EmailDone { get; set; }
    public bool PushDone { get; set; }
    public List<string> DeliveredDevices { get; set; } = [];
    public int Attempts { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptUtc { get; set; }
    public bool Finished => EmailDone && PushDone;
}

// Private App Service data survives package deployments and idle/restart cycles.
// Audit events remain the recoverable source; no notification DDL or new SQL service is needed.
public sealed class NotificationFiles
{
    private readonly IDataProtector protector;
    private readonly Lazy<NotificationIdentity> identity;
    public string Root { get; }
    public bool DeliveryEnabled { get; }
    public string BaseUrl { get; }
    public NotificationIdentity Identity => identity.Value;
    public NotificationFiles(IWebHostEnvironment environment, IConfiguration config, IDataProtectionProvider protection)
    {
        var site = config["WEBSITE_SITE_NAME"];
        BaseUrl = site == "recomp-test" ? "https://test.12weekrecomp.com" : "https://12weekrecomp.com";
        DeliveryEnabled = site == "recomp-live" || config.GetValue<bool>("Notifications:DeliveryEnabled");
        Root = config["Notifications:DataPath"] ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? environment.ContentRootPath, "data", "coach-notifications");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root, "users"));
        Directory.CreateDirectory(Path.Combine(Root, "queue"));
        Directory.CreateDirectory(Path.Combine(Root, "devices"));
        protector = protection.CreateProtector("12WR.CoachNotifications.v1");
        identity = new Lazy<NotificationIdentity>(Initialize);
    }
    private NotificationIdentity Initialize()
    {
        using var lease = Lease("identity");
        var result = Read<NotificationIdentity>("identity") ?? CreateIdentity();
        // Only the private Kudu scheduler reads this token. It is never returned to a browser.
        if (!File.Exists(Path.Combine(Root, "job-key.txt"))) File.WriteAllText(Path.Combine(Root, "job-key.txt"), result.JobKey);
        return result;
    }
    private NotificationIdentity CreateIdentity()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        static string B64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var publicKey = B64(new byte[] { 4 }.Concat(parameters.Q.X!).Concat(parameters.Q.Y!).ToArray());
        var result = new NotificationIdentity(System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(), publicKey, B64(parameters.D!), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        Write("identity", result); return result;
    }
    public static string DeviceId(string endpoint) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));
    public static void ValidateDevice(NotificationDevice device)
    {
        if (device.Endpoint.Length > 4096 || !Uri.TryCreate(device.Endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !(uri.Host == "fcm.googleapis.com" || uri.Host == "updates.push.services.mozilla.com"
                 || uri.Host == "web.push.apple.com" || uri.Host.EndsWith(".push.apple.com", StringComparison.Ordinal)
                 || uri.Host.EndsWith(".notify.windows.com", StringComparison.Ordinal)))
            throw new ArgumentException("Use a subscription created by this browser's notification service.");
        static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
        try {
            var key = Decode(device.P256dh); if (key.Length != 65 || key[0] != 4 || Decode(device.Auth).Length != 16) throw new FormatException();
            using var point = ECDsa.Create();
            point.ImportParameters(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = key[1..33], Y = key[33..65] } });
        }
        catch (Exception e) when (e is FormatException or CryptographicException) { throw new ArgumentException("The browser notification subscription is incomplete."); }
    }
    public NotificationPreferences User(Guid user) => Read<NotificationPreferences>($"users/{user:N}") ?? new();
    public void SaveUser(Guid user, NotificationPreferences settings) => Write($"users/{user:N}", settings);
    public bool OwnsDevice(Guid user, string endpoint) => Read<Guid?>($"devices/{DeviceId(endpoint)}") == user;
    public void BindDevice(Guid user, NotificationDevice device)
    {
        ValidateDevice(device);
        using var userLease = Lease($"users/{user:N}");
        using var deviceLease = Lease($"devices/{DeviceId(device.Endpoint)}");
        var settings = User(user);
        settings.Devices.RemoveAll(x => x.Endpoint == device.Endpoint);
        if (settings.Devices.Count >= 10) throw new ArgumentException("Remove an old device before adding another.");
        settings.Devices.Add(device);
        Write($"devices/{DeviceId(device.Endpoint)}", (Guid?)user);
        SaveUser(user, settings);
    }
    public void RemoveDevice(Guid user, string endpoint)
    {
        using var lease = Lease($"users/{user:N}");
        var settings = User(user); settings.Devices.RemoveAll(x => x.Endpoint == endpoint); SaveUser(user, settings);
    }
    public bool Enqueue(NotificationDelivery delivery)
    {
        using var lease = Lease($"queue/{delivery.Id}");
        if (Read<NotificationDelivery>($"queue/{delivery.Id}") is not null) return false;
        Write($"queue/{delivery.Id}", delivery); return true;
    }
    public IEnumerable<string> QueueIds() => Directory.EnumerateFiles(Path.Combine(Root, "queue"), "*.json").Select(Path.GetFileNameWithoutExtension)!;
    public T? Read<T>(string name)
    {
        var path = Path.Combine(Root, name + ".json");
        if (!File.Exists(path)) return default;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<T>(protector.Unprotect(reader.ReadToEnd()));
    }
    public void Write<T>(string name, T value)
    {
        var path = Path.Combine(Root, name + ".json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, protector.Protect(JsonSerializer.Serialize(value)));
        File.Move(temporary, path, true);
    }
    public FileStream Lease(string name) => new(Path.Combine(Root, name + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
}
