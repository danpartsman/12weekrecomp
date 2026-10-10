using WebRecomp.ExternalConnections.Fitdays;
using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
namespace WebRecomp.Web.Services.ExternalConnections;

public sealed class DeviceIntegrationService(RecompDbContext db, FitdaysClient client, DeviceCredentialProtector protector)
{
    public Task<DeviceProviderSetting> ProviderAsync(CancellationToken ct) => db.DeviceProviderSettings.SingleAsync(p=>p.ProviderCode=="FITDAYS",ct);
    public Task<DeviceConnection?> ConnectionAsync(Guid user, Guid owner, CancellationToken ct) => db.DeviceConnections.SingleOrDefaultAsync(c=>c.UserId==user && c.ClientId==owner && c.ProviderCode=="FITDAYS",ct);
    public async Task<DeviceConnection> SaveAsync(AppClientContext context, SavedFitdaysLogin login, FitdaysPerson person, bool automatic, CancellationToken ct)
    {
        var setting=await ProviderAsync(ct);
        if (!setting.Enabled || !ExternalConnectionFeatures.IsTestSite) throw new FitdaysPreviewException("Fitdays connections are currently disabled.");
        if (!await OwnsAsync(context.User.UserId,context.Client!.ClientId,ct)) throw new FitdaysPreviewException("Only the profile owner can connect a scale.");
        var c=await ConnectionAsync(context.User.UserId,context.Client.ClientId,ct);
        if (c?.SyncLeaseUntilUtc > DateTime.UtcNow) throw new FitdaysPreviewException("A sync is in progress. Please wait before changing the connection.");
        if(c==null) {c=new DeviceConnection {DeviceConnectionId=Guid.NewGuid(),UserId=context.User.UserId,ClientId=context.Client.ClientId,CreatedAtUtc=DateTime.UtcNow};db.DeviceConnections.Add(c);}
        c.Region=login.Session.Region; c.PersonId=person.Id; c.PersonName=person.Name.Length>200?person.Name[..200]:person.Name;
        c.AutomaticSync=automatic;c.StatusCode="CONNECTED";c.LastAttemptAtUtc=null;c.LastSuccessAtUtc=null;c.LastMessage="Connected. Ready to sync.";c.UpdatedAtUtc=DateTime.UtcNow;
        c.ProtectedCredential=protector.Protect(c,login);
        await db.SaveChangesAsync(ct); return c;
    }
    public Task<bool> OwnsAsync(Guid user,Guid owner,CancellationToken ct) => db.UserClientAccess.AnyAsync(a=>a.UserId==user&&a.ClientId==owner&&a.AccessRoleCode=="OWNER"&&a.RevokedAtUtc==null,ct);

    public async Task<string> SyncAsync(Guid id,CancellationToken ct)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return "Device syncing is unavailable in this environment.";
        var setting=await ProviderAsync(ct);if(!setting.Enabled)return "Fitdays is disabled by the administrator.";
        var now=DateTime.UtcNow;var lease=Guid.NewGuid();
        var claimed=await db.DeviceConnections.Where(c=>c.DeviceConnectionId==id&&c.ProtectedCredential!=null&&c.StatusCode!="DISCONNECTED"&&(c.SyncLeaseUntilUtc==null||c.SyncLeaseUntilUtc<now))
            .ExecuteUpdateAsync(s=>s.SetProperty(c=>c.SyncLeaseUntilUtc,now.AddMinutes(2)).SetProperty(c=>c.SyncLeaseId,lease).SetProperty(c=>c.LastAttemptAtUtc,now),ct);
        if(claimed==0)return "A sync is already running or the connection is disconnected.";
        var connection=await db.DeviceConnections.AsNoTracking().SingleAsync(c=>c.DeviceConnectionId==id,ct);
        string message;string status="CONNECTED";string? updatedSecret=null;bool succeeded=false;
        try
        {
            if(!await OwnsAsync(connection.UserId,connection.ClientId,ct)||!await db.UserAccounts.AnyAsync(u=>u.UserId==connection.UserId&&u.AccountStatusCode=="ACTIVE",ct))throw new FitdaysPreviewException("The account no longer owns an active client profile.");
            var person=await db.Clients.SingleAsync(c=>c.ClientId==connection.ClientId&&c.ClientStatusCode=="ACTIVE",ct);
            var program=await db.Programs.Where(p=>p.ClientId==connection.ClientId&&p.ProgramStatusCode=="ACTIVE").OrderByDescending(p=>p.StartDate).FirstOrDefaultAsync(ct);
            if(program==null)throw new FitdaysPreviewException("An active Recomp program is required to import weigh-ins.");
            var login=protector.Unprotect(connection);login.Session.AppVersion=setting.AppVersion;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(55));
            FitdaysSnapshot snapshot;
            try { snapshot=await client.ReadAsync(login.Session,person.TimeZone,timeout.Token,setting.HistoryDays); }
            catch(FitdaysPreviewException ex) when(ex.SessionRejected)
            { login=login with { Session=await client.LoginHashedAsync(login.Email,login.PasswordHash,connection.Region,timeout.Token,setting.AppVersion) };snapshot=await client.ReadAsync(login.Session,person.TimeZone,timeout.Token,setting.HistoryDays); }
            if(!snapshot.People.Any(p=>p.Id==connection.PersonId))throw new FitdaysPreviewException("The selected Fitdays profile is no longer available. Reconnect and select your profile.");
            var readings=snapshot.Readings.Where(r=>r.PersonId==connection.PersonId&&r.RecordId!="Unavailable"&&r.RecordId.Length<=70&&r.MeasuredAt.Date>=program.StartDate.ToDateTime(TimeOnly.MinValue).Date).OrderBy(r=>r.MeasuredAt).ToList();
            var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
            db.ChangeTracker.Clear();
            var inserted=0;var skipped=0;
            await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var earliest=DateOnly.FromDateTime(DateTime.Today.AddDays(-setting.HistoryDays-2));
            var existing=await db.Measurements.Where(m=>m.ClientId==connection.ClientId&&m.ProgramId==program.ProgramId&&m.LocalMeasurementDate>=earliest).ToListAsync(ct);
            foreach(var reading in readings)
            {
                var mapped=FitdaysMeasurementMapper.Create(reading,connection,program.ProgramId,setting);
                if(!FitdaysMeasurementMapper.CanImportDay(existing.Where(m=>m.LocalMeasurementDate==mapped.LocalMeasurementDate))||existing.Any(m=>m.MeasurementSourceCode=="FITDAYS"&&m.SourceRecordId==mapped.SourceRecordId)) {skipped++;continue;}
                db.Measurements.Add(mapped);existing.Add(mapped);inserted++;
            }
            await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);
            return (inserted, skipped);
            });
            updatedSecret=protector.Protect(connection,login);succeeded=true;message=$"Synced {result.inserted} new readings. {result.skipped} existing readings or manually recorded days kept unchanged.";
        }
        catch(FitdaysPreviewException ex) {message=ex.Message;status="ATTENTION";}
        catch(CryptographicException) {message="Saved sign-in could not be opened. Reconnect Fitdays once to restore automatic sync.";status="RECONNECT";}
        catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException or DbUpdateException or System.Data.Common.DbException or KeyNotFoundException)
        {message="Sync could not finish. Your existing measurements were preserved. Try Sync now or reconnect if the issue continues.";status="ATTENTION";}
        var finished=DateTime.UtcNow;
        await db.DeviceConnections.Where(c=>c.DeviceConnectionId==id&&c.SyncLeaseId==lease)
            .ExecuteUpdateAsync(s=>s.SetProperty(c=>c.StatusCode,status).SetProperty(c=>c.LastMessage,message).SetProperty(c=>c.LastSuccessAtUtc,succeeded?finished:connection.LastSuccessAtUtc).SetProperty(c=>c.ProtectedCredential,updatedSecret??connection.ProtectedCredential).SetProperty(c=>c.SyncLeaseUntilUtc,(DateTime?)null).SetProperty(c=>c.SyncLeaseId,(Guid?)null).SetProperty(c=>c.UpdatedAtUtc,finished),CancellationToken.None);
        return message;
    }
}
public sealed class DeviceSyncWorker(IServiceScopeFactory scopes,ILogger<DeviceSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!ExternalConnectionFeatures.IsTestSite)return;
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
        while(await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<RecompDbContext>();var service=scope.ServiceProvider.GetRequiredService<DeviceIntegrationService>();
                var provider=await service.ProviderAsync(stoppingToken);if(!provider.Enabled||!provider.AutomaticSyncEnabled)continue;
                var due=DateTime.UtcNow.AddMinutes(-provider.SyncIntervalMinutes);
                var ids=await db.DeviceConnections.AsNoTracking().Where(c=>c.ProviderCode=="FITDAYS"&&c.AutomaticSync&&c.ProtectedCredential!=null&&c.StatusCode!="DISCONNECTED"&&c.StatusCode!="RECONNECT"&&(c.LastAttemptAtUtc==null||c.LastAttemptAtUtc<due)).Select(c=>c.DeviceConnectionId).Take(50).ToListAsync(stoppingToken);
                foreach(var id in ids) { using var syncScope=scopes.CreateScope(); await syncScope.ServiceProvider.GetRequiredService<DeviceIntegrationService>().SyncAsync(id,stoppingToken); }
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {break;}
            catch(Exception) {logger.LogWarning("External device sync could not complete this cycle. See the device connection status page.");}
        }
    }
}
