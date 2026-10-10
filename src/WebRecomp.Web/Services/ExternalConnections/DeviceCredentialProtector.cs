using WebRecomp.ExternalConnections.Fitdays;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using WebRecomp.Web.Models;
namespace WebRecomp.Web.Services.ExternalConnections;
public sealed record SavedFitdaysLogin(string Email, string PasswordHash, FitdaysSession Session);
public sealed class DeviceCredentialProtector(IDataProtectionProvider provider)
{
    private IDataProtector For(DeviceConnection c) => provider.CreateProtector("12WR.DeviceLogin.v1", c.UserId.ToString("N"), c.ClientId.ToString("N"), c.ProviderCode);
    public string Protect(DeviceConnection c, SavedFitdaysLogin login) => For(c).Protect(JsonSerializer.Serialize(login));
    public SavedFitdaysLogin Unprotect(DeviceConnection c) => JsonSerializer.Deserialize<SavedFitdaysLogin>(For(c).Unprotect(c.ProtectedCredential ?? "")) ?? throw new InvalidOperationException("Reconnect your scale account.");
}
