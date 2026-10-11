using System.Text.Json;
using WebRecomp.ExternalConnections.Fitdays;
using WebRecomp.Web.Models;
namespace WebRecomp.Web.Services.ExternalConnections;

// Connection metadata stays outside the fitness database. App Service HOME is durable
// across deployments. Exclusive file locks and atomic replacement protect concurrent hosts.
public sealed class DeviceConnectionStore(string directory)
{
    private sealed class State
    {
        public State() {}
        public DeviceProviderSetting Provider {get;set;}=new();
        public List<DeviceConnection> Connections {get;set;}=[];
    }
    private async Task<T> Access<T>(Func<State,T> operation,bool write,CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var deadline=DateTime.UtcNow.AddSeconds(10);
        FileStream? handle=null;
        while(handle==null)
        {
            ct.ThrowIfCancellationRequested();
            try {handle=new FileStream(Path.Combine(directory,"connections.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException) when(DateTime.UtcNow<deadline) {await Task.Delay(50,ct);}
        }
        await using var held=handle;
        var path=Path.Combine(directory,"connections.json");
        var state=File.Exists(path)?JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(path,ct))??throw new InvalidOperationException("Connection storage could not be read."):new State();
        var result=operation(state);
        if(write)
        {
            var temp=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(state),ct);
                File.Move(temp,path,true);
            }
            finally {if(File.Exists(temp))File.Delete(temp);}
        }
        return result;
    }
    public Task<DeviceProviderSetting> ProviderAsync(CancellationToken ct)=>Access(s=>s.Provider,false,ct);
    public Task<DeviceConnection?> ConnectionAsync(Guid user,Guid client,CancellationToken ct)=>Access(s=>s.Connections.SingleOrDefault(c=>c.UserId==user&&c.ClientId==client&&c.ProviderCode=="FITDAYS"),false,ct);
    public Task<DeviceConnection?> ByIdAsync(Guid id,CancellationToken ct)=>Access(s=>s.Connections.SingleOrDefault(c=>c.DeviceConnectionId==id),false,ct);
    public Task<List<DeviceConnection>> ListAsync(CancellationToken ct)=>Access(s=>s.Connections.ToList(),false,ct);
    public Task<bool> UpdateProviderAsync(DeviceProviderSetting provider,CancellationToken ct)=>Access(s=>{s.Provider=provider;return true;},true,ct);
    public Task<DeviceConnection> SaveAsync(DeviceConnection connection,CancellationToken ct)=>Access(s=>
    {
        if(!s.Provider.Enabled)throw new FitdaysPreviewException("Fitdays is disabled by the administrator.");
        var old=s.Connections.SingleOrDefault(c=>c.UserId==connection.UserId&&c.ClientId==connection.ClientId&&c.ProviderCode==connection.ProviderCode);
        if(old?.SyncLeaseUntilUtc>DateTime.UtcNow)throw new FitdaysPreviewException("A sync is in progress. Please wait before changing the connection.");
        if(old!=null)s.Connections.Remove(old);
        s.Connections.Add(connection);return connection;
    },true,ct);
    public Task<DeviceConnection?> ClaimAsync(Guid id,Guid lease,DateTime now,CancellationToken ct)=>Access(s=>
    {
        var c=s.Connections.SingleOrDefault(c=>c.DeviceConnectionId==id);
        if(!s.Provider.Enabled||c?.ProtectedCredential==null||c.StatusCode=="DISCONNECTED"||c.SyncLeaseUntilUtc>now)return null;
        c.SyncLeaseId=lease;c.SyncLeaseUntilUtc=now.AddMinutes(2);c.LastAttemptAtUtc=now;return c;
    },true,ct);
    public Task<bool> FinishAsync(Guid id,Guid lease,string status,string message,string? credential,bool succeeded,CancellationToken ct)=>Access(s=>
    {
        var c=s.Connections.SingleOrDefault(c=>c.DeviceConnectionId==id&&c.SyncLeaseId==lease);if(c==null)return false;
        c.StatusCode=status;c.LastMessage=message;c.UpdatedAtUtc=DateTime.UtcNow;
        if(succeeded)c.LastSuccessAtUtc=c.UpdatedAtUtc;if(credential!=null)c.ProtectedCredential=credential;
        c.SyncLeaseId=null;c.SyncLeaseUntilUtc=null;return true;
    },true,ct);
    public Task<bool> PreferenceAsync(Guid user,Guid client,bool automatic,CancellationToken ct)=>Access(s=>
    {var c=s.Connections.SingleOrDefault(c=>c.UserId==user&&c.ClientId==client&&c.ProviderCode=="FITDAYS");if(c==null)return false;c.AutomaticSync=automatic;c.UpdatedAtUtc=DateTime.UtcNow;return true;},true,ct);
    public Task<bool> DisconnectAsync(Guid user,Guid client,CancellationToken ct)=>Access(s=>
    {
        var c=s.Connections.SingleOrDefault(c=>c.UserId==user&&c.ClientId==client&&c.ProviderCode=="FITDAYS");
        if(c==null||c.SyncLeaseUntilUtc>DateTime.UtcNow)return false;
        c.ProtectedCredential=null;c.AutomaticSync=false;c.StatusCode="DISCONNECTED";c.UpdatedAtUtc=DateTime.UtcNow;c.LastMessage="Disconnected. Imported measurements retained.";return true;
    },true,ct);
    public Task<bool> PauseAsync(Guid id,CancellationToken ct)=>Access(s=>{var c=s.Connections.SingleOrDefault(c=>c.DeviceConnectionId==id);if(c==null)return false;c.AutomaticSync=false;c.UpdatedAtUtc=DateTime.UtcNow;return true;},true,ct);
}
