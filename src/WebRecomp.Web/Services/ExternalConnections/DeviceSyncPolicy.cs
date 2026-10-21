using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.ExternalConnections;

public static class DeviceSyncPolicy
{
    // Background Fitdays traffic is intentionally disabled. We allow a user-triggered
    // manual sync from the UI, but we never poll automatically from page open.
    // The policy still preserves the expected guard conditions for tests and future manual
    // re-enablement without triggering background requests.
    public static bool ShouldCheckOnOpen(DeviceProviderSetting setting, DeviceConnection? connection, DateTime now)
    {
        if (setting is null || !setting.Enabled || !setting.AutomaticSyncEnabled)
            return false;

        if (connection is null || !connection.AutomaticSync)
            return false;

        if (string.Equals(connection.StatusCode, "RECONNECT", StringComparison.OrdinalIgnoreCase))
            return false;

        if (connection.SyncLeaseUntilUtc is DateTime lease && lease > now)
            return false;

        if (connection.LastAttemptAtUtc is not DateTime lastAttempt)
            return true;

        var due = lastAttempt.AddMinutes(setting.SyncIntervalMinutes);
        return now >= due;
    }
}
