namespace WebRecomp.Web.Models;

public sealed class DeviceProviderSetting
{
    public string ProviderCode { get; set; } = "FITDAYS";
    public bool Enabled { get; set; } = true;
    public bool AutomaticSyncEnabled { get; set; } = true;
    public int SyncIntervalMinutes { get; set; } = 60;
    public int HistoryDays { get; set; } = 30;
    public string AppVersion { get; set; } = "1.21.0";
    public bool BodyCompositionEnabled { get; set; } = true;
    public string SkeletalMuscleUnit { get; set; } = "PERCENT";
    public string BodyWaterUnit { get; set; } = "PERCENT";
    public DateTime UpdatedAtUtc { get; set; }
}
public sealed class DeviceConnection
{
    public Guid DeviceConnectionId { get; set; }
    public Guid UserId { get; set; }
    public Guid ClientId { get; set; }
    public string ProviderCode { get; set; } = "FITDAYS";
    public string Region { get; set; } = "us";
    public long PersonId { get; set; }
    public string PersonName { get; set; } = "";
    public string? ProtectedCredential { get; set; }
    public bool AutomaticSync { get; set; } = true;
    public string StatusCode { get; set; } = "CONNECTED";
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? LastSuccessAtUtc { get; set; }
    public DateTime? SyncLeaseUntilUtc { get; set; }
    public Guid? SyncLeaseId { get; set; }
    public string? LastMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
