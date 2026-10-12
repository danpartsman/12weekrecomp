using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using WebRecomp.ExternalConnections.Fitdays;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.ExternalConnections;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Tests;
public sealed class ExternalConnectionTests
{
    [Theory]
    [InlineData("recomp-test",true)] [InlineData("recomp-live",true)]
    [InlineData("other-site",false)] [InlineData(null,false)]
    public void SupportedEnvironmentsAreExplicit(string? site,bool expected)
        => Assert.Equal(expected,ExternalConnectionFeatures.IsSupportedSite(site));
    [Fact]
    public void PageOpenPollingWaitsForTheConfiguredInterval()
    {
        var now=DateTime.UtcNow;
        var setting=new DeviceProviderSetting {SyncIntervalMinutes=15};
        var connection=new DeviceConnection {ProtectedCredential="encrypted",LastAttemptAtUtc=now.AddMinutes(-14)};
        Assert.False(DeviceSyncPolicy.ShouldCheckOnOpen(setting,connection,now));
        connection.LastAttemptAtUtc=now.AddMinutes(-15);
        Assert.True(DeviceSyncPolicy.ShouldCheckOnOpen(setting,connection,now));
        connection.SyncLeaseUntilUtc=now.AddMinutes(1);
        Assert.False(DeviceSyncPolicy.ShouldCheckOnOpen(setting,connection,now));
    }
    [Fact]
    public void PageOpenPollingRespectsOptOutAndReconnectStatus()
    {
        var now=DateTime.UtcNow;var setting=new DeviceProviderSetting();
        var c=new DeviceConnection {ProtectedCredential="encrypted"};
        Assert.True(DeviceSyncPolicy.ShouldCheckOnOpen(setting,c,now));
        c.AutomaticSync=false;Assert.False(DeviceSyncPolicy.ShouldCheckOnOpen(setting,c,now));
        c.AutomaticSync=true;c.StatusCode="RECONNECT";Assert.False(DeviceSyncPolicy.ShouldCheckOnOpen(setting,c,now));
        c.StatusCode="CONNECTED";setting.AutomaticSyncEnabled=false;Assert.False(DeviceSyncPolicy.ShouldCheckOnOpen(setting,c,now));
        Assert.False(DeviceSyncPolicy.ShouldCheckOnOpen(setting,null,now));
    }
    [Fact]
    public async Task DurableConnectionStoreSurvivesRecreationAndDisconnectRetainsMetadata()
    {
        var path=Path.Combine(Path.GetTempPath(),"recomp-connections-"+Guid.NewGuid());
        try
        {
            var store=new DeviceConnectionStore(path);
            var c=new DeviceConnection {DeviceConnectionId=Guid.NewGuid(),UserId=Guid.NewGuid(),ClientId=Guid.NewGuid(),ProtectedCredential="encrypted-ciphertext",PersonId=12};
            await store.SaveAsync(c,default);
            var reopened=new DeviceConnectionStore(path);
            Assert.Equal("encrypted-ciphertext",(await reopened.ConnectionAsync(c.UserId,c.ClientId,default))!.ProtectedCredential);
            Assert.Null(await reopened.ConnectionAsync(Guid.NewGuid(),c.ClientId,default));
            await reopened.UpdateProviderAsync(new() {SyncIntervalMinutes=20},default);
            Assert.Equal(20,(await store.ProviderAsync(default)).SyncIntervalMinutes);
            Assert.True(await reopened.DisconnectAsync(c.UserId,c.ClientId,default));
            var disconnected=await store.ConnectionAsync(c.UserId,c.ClientId,default);
            Assert.Null(disconnected!.ProtectedCredential);Assert.Equal(12,disconnected.PersonId);Assert.False(disconnected.AutomaticSync);
        }
        finally {if(Directory.Exists(path))Directory.Delete(path,true);}
    }
    [Fact]
    public async Task DurableStoreAllowsOnlyOneConcurrentSyncLease()
    {
        var path=Path.Combine(Path.GetTempPath(),"recomp-leases-"+Guid.NewGuid());
        try
        {
            var first=new DeviceConnectionStore(path);var second=new DeviceConnectionStore(path);
            var c=new DeviceConnection {DeviceConnectionId=Guid.NewGuid(),UserId=Guid.NewGuid(),ClientId=Guid.NewGuid(),ProtectedCredential="ciphertext"};
            await first.SaveAsync(c,default);
            var lease=Guid.NewGuid();var now=DateTime.UtcNow;
            var results=await Task.WhenAll(first.ClaimAsync(c.DeviceConnectionId,lease,now,default),second.ClaimAsync(c.DeviceConnectionId,Guid.NewGuid(),now,default));
            Assert.Single(results.Where(x=>x!=null));
            var winner=results.Single(x=>x!=null)!;
            Assert.False(await first.FinishAsync(c.DeviceConnectionId,Guid.NewGuid(),"CONNECTED","",null,true,default));
            Assert.True(await second.FinishAsync(c.DeviceConnectionId,winner.SyncLeaseId!.Value,"CONNECTED","Synced",null,true,default));
            Assert.NotNull(await first.ClaimAsync(c.DeviceConnectionId,Guid.NewGuid(),DateTime.UtcNow,default));
        }
        finally {if(Directory.Exists(path))Directory.Delete(path,true);}
    }
    [Fact]
    public void StaleDailyFormDoesNotOverwriteAReadingThatArrivedDuringNutritionEdits()
    {
        var rendered=new Measurement {MeasurementSourceCode="FITDAYS",WeightLb=200};
        var latest=new Measurement {MeasurementSourceCode="FITDAYS",WeightLb=199};
        var entry=new DailyEntryViewModel {WeightLb=200,ProteinG=150};
        Assert.True(FitdaysMeasurementMapper.BodyValuesUnchanged(latest,rendered,entry));
        entry.WeightLb=198;Assert.False(FitdaysMeasurementMapper.BodyValuesUnchanged(latest,rendered,entry));
    }
    [Fact]
    public void SavedAccountDisplayReturnsOnlyEmailWithoutChangingCredentials()
    {
        var dir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "recomp-account-display-" + Guid.NewGuid())); dir.Create();
        try
        {
            var protector = new DeviceCredentialProtector(DataProtectionProvider.Create(dir));
            var connection = new DeviceConnection { UserId=Guid.NewGuid(), ClientId=Guid.NewGuid() };
            connection.ProtectedCredential = protector.Protect(connection, new("test@example.com", "synthetic-hash", new() { Token="synthetic-token", Uid=12 }));
            var stored = connection.ProtectedCredential;
            var service = new DeviceIntegrationService(null!, null!, protector, null!);
            Assert.Equal("test@example.com", service.SavedAccountEmail(connection));
            Assert.Equal(stored, connection.ProtectedCredential);
            connection.UserId = Guid.NewGuid();
            Assert.Null(service.SavedAccountEmail(connection));
            Assert.Equal(stored, connection.ProtectedCredential);
            Assert.Null(service.SavedAccountEmail(null));
        }
        finally { dir.Delete(true); }
    }
    private static FitdaysReading Reading() => new(12,new DateTimeOffset(2026,10,10,23,30,0,TimeSpan.FromHours(-7)),212.7m,"record-a",26.2m,68.8m,42.4m,54.1m,18.7m,10m);
    [Fact]
    public void MapsVerifiedPercentFieldsAndLocalDateWithoutGenericMuscle()
    {
        var c=new DeviceConnection {ClientId=Guid.NewGuid(),PersonId=12};
        var m=FitdaysMeasurementMapper.Create(Reading(),c,Guid.NewGuid(),new());
        Assert.Equal(new DateOnly(2026,10,10),m.LocalMeasurementDate);
        Assert.Equal(new DateTime(2026,10,11,6,30,0,DateTimeKind.Utc),m.MeasurementAtUtc);
        Assert.Equal(212.7m,m.WeightLb);Assert.Equal(.262m,m.BodyFatPct);
        Assert.Equal(90.2m,m.SkeletalMuscleMassLb);Assert.Equal(115.1m,m.BodyWaterLb);
        Assert.Null(m.MuscleMassLb);Assert.Equal("12:record-a",m.SourceRecordId);
    }
    [Fact]
    public void AdministratorCanDisableBodyComposition()
    {
        var m=FitdaysMeasurementMapper.Create(Reading(),new(),Guid.NewGuid(),new() {BodyCompositionEnabled=false});
        Assert.Equal(212.7m,m.WeightLb);Assert.Null(m.BodyFatPct);Assert.Null(m.SkeletalMuscleMassLb);Assert.Null(m.BodyWaterLb);
    }
    [Theory]
    [InlineData("PERCENT",50,100)] [InlineData("LB",100,100)] [InlineData("KG",45.359237,100)]
    public void ConfigurableMassUnitsAreConverted(string unit,double value,double expected) => Assert.Equal((decimal)expected,FitdaysMeasurementMapper.Mass(200,(decimal)value,unit));
    [Fact]
    public void ImplausibleBodyCompositionValuesAreDiscarded()
    {
        Assert.Null(FitdaysMeasurementMapper.Fraction(101));Assert.Null(FitdaysMeasurementMapper.Fraction(-2));
        Assert.Null(FitdaysMeasurementMapper.Mass(200,201,"LB"));Assert.Null(FitdaysMeasurementMapper.Mass(200,200,"KG"));
    }
    [Fact]
    public void ManualCorrectionsBlockImportsWhileDeletedManualRowsDoNot()
    {
        Assert.True(FitdaysMeasurementMapper.CanImportDay([new() {MeasurementSourceCode="FITDAYS"}]));
        Assert.False(FitdaysMeasurementMapper.CanImportDay([new() {MeasurementSourceCode="MANUAL"}]));
        Assert.True(FitdaysMeasurementMapper.CanImportDay([new() {MeasurementSourceCode="MANUAL",IsDeleted=true}]));
    }
    [Fact]
    public void SavingOtherDailyFieldsDoesNotTurnUnchangedScaleReadingIntoManualOverride()
    {
        var m=new Measurement {WeightLb=200,BodyFatPct=.25m,SkeletalMuscleMassLb=90,BodyWaterLb=110};
        var entry=new DailyEntryViewModel {WeightLb=200,BodyFatPercent=25,SkeletalMuscleMassLb=90,BodyWaterLb=110};
        Assert.True(FitdaysMeasurementMapper.MatchesEntry(m,entry));
        entry.WeightLb=199;Assert.False(FitdaysMeasurementMapper.MatchesEntry(m,entry));
        entry.WeightLb=null;Assert.False(FitdaysMeasurementMapper.MatchesEntry(m,entry));
    }
    [Fact]
    public void SavedCredentialsSurviveProviderRecreationAndAreBoundToOwner()
    {
        var dir=new DirectoryInfo(Path.Combine(Path.GetTempPath(),"recomp-device-"+Guid.NewGuid()));dir.Create();
        try
        {
            var c=new DeviceConnection {UserId=Guid.NewGuid(),ClientId=Guid.NewGuid()};
            var login=new SavedFitdaysLogin("test@example.com","hash-value",new() {Token="private-token",Uid=12});
            c.ProtectedCredential=new DeviceCredentialProtector(DataProtectionProvider.Create(dir)).Protect(c,login);
            Assert.DoesNotContain("private-token",c.ProtectedCredential);Assert.DoesNotContain("hash-value",c.ProtectedCredential);
            var second=new DeviceCredentialProtector(DataProtectionProvider.Create(dir));
            Assert.Equal("private-token",second.Unprotect(c).Session.Token);
            c.UserId=Guid.NewGuid();Assert.Throws<CryptographicException>(()=>second.Unprotect(c));
        }
        finally {dir.Delete(true);}
    }
}
