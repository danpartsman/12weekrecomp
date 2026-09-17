using WebRecomp.CatalogSync;

namespace WebRecomp.CatalogSync.Tests;

public sealed class CatalogSyncPolicyTests
{
    [Fact]
    public void Auto_sync_off_is_never_due()
    {
        var settings = Settings(auto: false, lastChecked: null, days: 7);
        Assert.False(CatalogSyncPolicy.IsDue(settings, DateTime.UtcNow));
    }

    [Fact]
    public void Never_checked_auto_sync_is_due()
    {
        var settings = Settings(auto: true, lastChecked: null, days: 7);
        Assert.True(CatalogSyncPolicy.IsDue(settings, DateTime.UtcNow));
    }

    [Fact]
    public void Seven_day_schedule_is_due_after_seven_days()
    {
        var now = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
        var settings = Settings(auto: true, lastChecked: now.AddDays(-7), days: 7);
        Assert.True(CatalogSyncPolicy.IsDue(settings, now));
    }

    [Fact]
    public void Seven_day_schedule_is_not_due_after_six_days()
    {
        var now = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
        var settings = Settings(auto: true, lastChecked: now.AddDays(-6), days: 7);
        Assert.False(CatalogSyncPolicy.IsDue(settings, now));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(14)]
    [InlineData(30)]
    public void Supported_intervals_are_accepted(int days)
        => Assert.True(CatalogSyncConstants.IsSupportedRefreshInterval(days));

    private static CatalogSyncSettings Settings(bool auto, DateTime? lastChecked, int days)
        => new(
            CatalogSyncConstants.DefaultSettingsKey,
            CatalogSyncConstants.KineticProviderCode,
            CatalogSyncConstants.NoMediaProviderCode,
            auto,
            days,
            lastChecked,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTime.UtcNow);
}
