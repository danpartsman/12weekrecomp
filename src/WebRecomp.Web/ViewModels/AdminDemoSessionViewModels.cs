namespace WebRecomp.Web.ViewModels;

public sealed class AdminDemoSessionsViewModel
{
    public IReadOnlyList<AdminDemoSessionRow> Sessions { get; set; }
        = Array.Empty<AdminDemoSessionRow>();

    public int ActiveCount { get; set; }
}

public sealed class AdminDemoSessionRow
{
    public Guid DemoSessionId { get; set; }
    public Guid DemoWorkspaceId { get; set; }

    public required string InviteCode { get; set; }
    public required string DemoTypeCode { get; set; }

    public required string SessionStatus { get; set; }
    public required string WorkspaceStatus { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; }

    public DateTime SessionExpiresAtUtc { get; set; }
    public DateTime WorkspaceExpiresAtUtc { get; set; }
    public DateTime InactivityExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

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

    public string? DeviceTypeCode { get; set; }
    public string? OperatingSystem { get; set; }
    public string? BrowserName { get; set; }

    public int PageViewCount { get; set; }
    public string? LastPagePath { get; set; }
}
