using WebRecomp.Web.Models;
namespace WebRecomp.Web.Services.ExternalConnections;
public static class DeviceSyncPolicy
{
    // External Fitdays traffic is initiated only by an explicit user action.
    // Existing automatic-sync preferences are intentionally ignored.
    public static bool ShouldCheckOnOpen(DeviceProviderSetting setting, DeviceConnection? connection, DateTime now) => false;
}
