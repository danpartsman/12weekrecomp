namespace WebRecomp.Web.Models;

public sealed class DemoInvite
{
    public Guid DemoInviteId { get; set; }
    public required string InviteCode { get; set; }
    public required string InviteTypeCode { get; set; }
    public string? GuidedScenarioCode { get; set; }
    public string StatusCode { get; set; } = "ACTIVE";
    public DateTime? ExpiresAtUtc { get; set; }
    public int? MaxRedemptions { get; set; }
    public int RedemptionCount { get; set; }
    public int WorkspaceLifetimeDays { get; set; } = 60;
    public int InactivityTimeoutDays { get; set; } = 30;
    public int SessionLifetimeDays { get; set; } = 30;
    public bool AllowConversion { get; set; } = true;
    public Guid? CreatedByUserId { get; set; }
    public string? Notes { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class DemoWorkspace
{
    public Guid DemoWorkspaceId { get; set; }
    public Guid DemoInviteId { get; set; }
    public Guid UserId { get; set; }
    public required string DemoTypeCode { get; set; }
    public string WorkspaceStatusCode { get; set; } = "ACTIVE";
    public DateTime ExpiresAtUtc { get; set; }
    public int InactivityTimeoutDays { get; set; } = 30;
    public int SessionLifetimeDays { get; set; } = 30;
    public DateTime LastActivityAtUtc { get; set; }
    public string? RecoveryCodeHash { get; set; }
    public DateTime? RecoveryCodeCreatedAtUtc { get; set; }
    public Guid? ConvertedToUserId { get; set; }
    public DateTime? ConvertedAtUtc { get; set; }

    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmContent { get; set; }

    public string? ReferrerHost { get; set; }
    public string? ReferrerPath { get; set; }
    public string? LandingPath { get; set; }

    public string? CountryCode { get; set; }
    public string? RegionName { get; set; }
    public string? CityName { get; set; }

    public DateTime? DemoCompletedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class DemoSession
{
    public Guid DemoSessionId { get; set; }
    public Guid DemoWorkspaceId { get; set; }
    public required string SessionTokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public string? IpAddress { get; set; }

    public string? DeviceTypeCode { get; set; }
    public string? OperatingSystem { get; set; }
    public string? BrowserName { get; set; }
    public string? UserAgent { get; set; }

    public int PageViewCount { get; set; }
    public string? LastPagePath { get; set; }
}
