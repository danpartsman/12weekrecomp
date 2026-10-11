using WebRecomp.ExternalConnections.Fitdays;
using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
namespace WebRecomp.Web.Services.ExternalConnections;

public sealed class DeviceIntegrationService(RecompDbContext db, FitdaysClient client, DeviceCredentialProtector protector, DeviceConnectionStore connections)
{
    public int LastImportedCount { get; private set; }
    public Task<DeviceProviderSetting> ProviderAsync(CancellationToken ct) => connections.ProviderAsync(ct);
    public Task<DeviceConnection?> ConnectionAsync(Guid user, Guid owner, CancellationToken ct) => connections.ConnectionAsync(user,owner,ct);
    public async Task<DeviceConnection> SaveAsync(AppClientContext context, SavedFitdaysLogin login, FitdaysPerson person, bool automatic, CancellationToken ct)
    {
        var setting=await ProviderAsync(ct);
        if (!setting.Enabled || !ExternalConnectionFeatures.IsEnabled) throw new FitdaysPreviewException("Fitdays connections are currently disabled.");
        if (!await OwnsAsync(context.User.UserId,context.Client!.ClientId,ct)) throw new FitdaysPreviewException("Only the profile owner can connect a scale.");
        var c=await ConnectionAsync(context.User.UserId,context.Client.ClientId,ct);
        if (c?.SyncLeaseUntilUtc > DateTime.UtcNow) throw new FitdaysPreviewException("A sync is in progress. Please wait before changing the connection.");
        if(c==null) {c=new DeviceConnection {DeviceConnectionId=Guid.NewGuid(),UserId=context.User.UserId,ClientId=context.Client.ClientId,CreatedAtUtc=DateTime.UtcNow};}
        c.Region=login.Session.Region; c.PersonId=person.Id; c.PersonName=person.Name.Length>200?person.Name[..200]:person.Name;
        c.AutomaticSync=automatic;c.StatusCode="CONNECTED";c.LastAttemptAtUtc=null;c.LastSuccessAtUtc=null;c.LastMessage="Connected. Ready to sync.";c.UpdatedAtUtc=DateTime.UtcNow;
        c.ProtectedCredential=protector.Protect(c,login);
        return await connections.SaveAsync(c,ct);
    }
    public Task<bool> OwnsAsync(Guid user,Guid owner,CancellationToken ct) => db.UserClientAccess.AnyAsync(a=>a.UserId==user&&a.ClientId==owner&&a.AccessRoleCode=="OWNER"&&a.RevokedAtUtc==null,ct);

    public async Task<string> SyncAsync(Guid id,CancellationToken ct,bool diagnose=false)
    {
        if(!ExternalConnectionFeatures.IsEnabled)return "Device syncing is unavailable in this environment.";
        var setting=await ProviderAsync(ct);if(!setting.Enabled)return "Fitdays is disabled by the administrator.";
        var now=DateTime.UtcNow;var lease=Guid.NewGuid();
        var connection=await connections.ClaimAsync(id,lease,now,ct);
        if(connection==null)return "A sync is already running or the connection is disconnected.";
        string message;string status="CONNECTED";string? updatedSecret=null;bool succeeded=false;
        try
        {
            if(!await OwnsAsync(connection.UserId,connection.ClientId,ct)||!await db.UserAccounts.AnyAsync(u=>u.UserId==connection.UserId&&u.AccountStatusCode=="ACTIVE",ct))throw new FitdaysPreviewException("The account no longer owns an active client profile.");
            var person=await db.Clients.SingleAsync(c=>c.ClientId==connection.ClientId&&c.ClientStatusCode=="ACTIVE",ct);
            var program=await db.Programs.Where(p=>p.ClientId==connection.ClientId&&p.ProgramStatusCode=="ACTIVE").OrderByDescending(p=>p.StartDate).FirstOrDefaultAsync(ct);
            if(program==null)throw new FitdaysPreviewException("An active Recomp program is required to import weigh-ins.");
            var login=protector.Unprotect(connection);login.Session.AppVersion=setting.AppVersion;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(55));
            FitdaysSnapshot snapshot;bool renewedSession=false;
            try { snapshot=await client.ReadAsync(login.Session,person.TimeZone,timeout.Token,setting.HistoryDays); }
            catch(FitdaysPreviewException ex) when(ex.SessionRejected)
            { renewedSession=true;login=login with { Session=await client.LoginHashedAsync(login.Email,login.PasswordHash,connection.Region,timeout.Token,setting.AppVersion) };snapshot=await client.ReadAsync(login.Session,person.TimeZone,timeout.Token,setting.HistoryDays); }
            if(!snapshot.People.Any(p=>p.Id==connection.PersonId))throw new FitdaysPreviewException("The selected Fitdays profile is no longer available. Reconnect and select your profile.");
            var readings=snapshot.Readings.Where(r=>r.PersonId==connection.PersonId&&r.RecordId!="Unavailable"&&r.RecordId.Length<=70&&r.MeasuredAt.Date>=program.StartDate.ToDateTime(TimeOnly.MinValue).Date).OrderBy(r=>r.MeasuredAt).ToList();
            var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
            db.ChangeTracker.Clear();
            var inserted=0;var duplicates=0;var manualBlocked=0;
            await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var earliest=DateOnly.FromDateTime(DateTime.Today.AddDays(-setting.HistoryDays-2));
            var existing=await db.Measurements.Where(m=>m.ClientId==connection.ClientId&&m.ProgramId==program.ProgramId&&m.LocalMeasurementDate>=earliest).ToListAsync(ct);
            foreach(var reading in readings)
            {
                var mapped=FitdaysMeasurementMapper.Create(reading,connection,program.ProgramId,setting);
                if(!FitdaysMeasurementMapper.CanImportDay(existing.Where(m=>m.LocalMeasurementDate==mapped.LocalMeasurementDate))) {manualBlocked++;continue;}
                if(existing.Any(m=>m.MeasurementSourceCode=="FITDAYS"&&m.SourceRecordId==mapped.SourceRecordId)) {duplicates++;continue;}
                db.Measurements.Add(mapped);existing.Add(mapped);inserted++;
            }
            await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);
            return (inserted, duplicates, manualBlocked);
            });
            LastImportedCount=result.inserted;
            var selected=snapshot.Readings.Where(r=>r.PersonId==connection.PersonId).OrderByDescending(r=>r.MeasuredAt).ToList();
            var zone=TimeZoneInfo.FindSystemTimeZoneById(person.TimeZone);
            var localToday=TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,zone).Date;
            var todayCount=selected.Count(r=>r.MeasuredAt.Date==localToday);
            var latest=selected.FirstOrDefault();
            var latestText=latest==null?"none":latest.MeasuredAt.ToString("yyyy-MM-dd HH:mm zzz");
            var missingIds=selected.Count(r=>r.RecordId=="Unavailable"||r.RecordId.Length>70);
            var beforeProgram=selected.Count(r=>r.MeasuredAt.Date<program.StartDate.ToDateTime(TimeOnly.MinValue).Date);
            var rawTimes=snapshot.RawMeasurementTimes.GetValueOrDefault(connection.PersonId) ?? [];
            string FormatRawTime(long timestamp)
            {
                try { return TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(timestamp),zone).ToString("yyyy-MM-dd HH:mm zzz"); }
                catch(ArgumentOutOfRangeException) { return $"unsupported timestamp {timestamp}"; }
            }
            var rawLatest=rawTimes.Count==0?"none":FormatRawTime(rawTimes.Max());
            var widerWindow="";
            if(diagnose && string.Equals(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"),"recomp-test",StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var expanded=await client.ReadAsync(login.Session,person.TimeZone,timeout.Token,90,24);
                    var expandedTimes=expanded.RawMeasurementTimes.GetValueOrDefault(connection.PersonId) ?? [];
                    widerWindow=$" Wider diagnostic request (90 days back, 24 hours ahead): {expanded.RawRecordCount} raw rows, selected-profile newest {(expandedTimes.Count==0?"none":FormatRawTime(expandedTimes.Max()))}; all-profile newest {(expanded.RawLatestTimestamp is long expandedNewest?FormatRawTime(expandedNewest):"none")}.";
                }
                catch(Exception ex) when(ex is FitdaysPreviewException or HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
                { widerWindow=" Wider diagnostic request failed; normal sync result retained."; }
            }
            var profileSummary=string.Join("; ",snapshot.People.Take(10).Select(p=>
            {
                var times=snapshot.RawMeasurementTimes.GetValueOrDefault(p.Id) ?? [];
                return $"{p.Name}: latest raw {(times.Count==0?"none":FormatRawTime(times.Max()))}";
            }));
            updatedSecret=protector.Protect(connection,login);succeeded=true;
            message=$"Fitdays responded at {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. Returned {snapshot.Readings.Count} supported readings across profiles; {selected.Count} match your selected profile. Latest selected reading: {latestText}. For local today ({localToday:yyyy-MM-dd}, {person.TimeZone}): {todayCount} returned. Imported {result.inserted}; duplicates {result.duplicates}; blocked by manual days {result.manualBlocked}; unusable record IDs {missingIds}; before program start {beforeProgram}. Raw selected-profile records: {rawTimes.Count}; newest raw timestamp: {rawLatest}. Account profiles: {profileSummary}. Authentication: {(renewedSession?"saved session rejected; automatic login renewed it":"saved session accepted; no login performed")}. Raw response rows {snapshot.RawRecordCount}; deleted {snapshot.RawDeletedCount}; malformed profile/timestamp {snapshot.RawMalformedCount}; newest raw across all profile IDs {(snapshot.RawLatestTimestamp is long rawNewest?FormatRawTime(rawNewest):"none")}.{widerWindow}";
        }
        catch(FitdaysPreviewException ex) {message=ex.Message;status="ATTENTION";}
        catch(CryptographicException) {message="Saved sign-in could not be opened. Reconnect Fitdays once to restore automatic sync.";status="RECONNECT";}
        catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException or DbUpdateException or System.Data.Common.DbException or KeyNotFoundException)
        {message="Sync could not finish. Your existing measurements were preserved. Try Sync now or reconnect if the issue continues.";status="ATTENTION";}
        await connections.FinishAsync(id,lease,status,message,updatedSecret,succeeded,CancellationToken.None);
        return message;
    }
}
