using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using WebRecomp.ExternalConnections.Fitdays;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.ExternalConnections;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Tests;
public sealed class ExternalConnectionTests
{
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
