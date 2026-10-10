using WebRecomp.Web.Models;
namespace WebRecomp.Web.Services.ExternalConnections;
public static class DeviceSyncPolicy
{
    public static bool ShouldCheckOnOpen(DeviceProviderSetting setting, DeviceConnection? connection, DateTime now) =>
        setting.Enabled && setting.AutomaticSyncEnabled && connection is not null
        && connection.AutomaticSync && connection.ProtectedCredential is not null
        && connection.StatusCode is not ("RECONNECT" or "DISCONNECTED")
        && (connection.SyncLeaseUntilUtc is null || connection.SyncLeaseUntilUtc < now)
        && (connection.LastAttemptAtUtc is null || connection.LastAttemptAtUtc <= now.AddMinutes(-setting.SyncIntervalMinutes));
}
