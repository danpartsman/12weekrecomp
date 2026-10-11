using WebRecomp.ExternalConnections.Fitdays;
using WebRecomp.Web.Models;
namespace WebRecomp.Web.Services.ExternalConnections;
public static class FitdaysMeasurementMapper
{
    public static decimal? Fraction(decimal? value) => value is > 0 and <= 100 ? value / 100m : null;
    public static decimal? Mass(decimal weightLb, decimal? value, string unit) => unit switch
    {
        "PERCENT" => Fraction(value) is decimal f ? decimal.Round(weightLb * f, 1) : null,
        "LB" => value is > 0 && value <= weightLb ? decimal.Round(value.Value,1) : null,
        "KG" => value is > 0 && value * 2.20462262185m <= weightLb ? decimal.Round(value.Value * 2.20462262185m,1) : null,
        _ => null
    };
    public static Measurement Create(FitdaysReading reading, DeviceConnection connection, Guid programId, DeviceProviderSetting provider)
    {
        var m = new Measurement { MeasurementId=Guid.NewGuid(), ClientId=connection.ClientId, ProgramId=programId, LocalMeasurementDate=DateOnly.FromDateTime(reading.MeasuredAt.DateTime), MeasurementAtUtc=reading.MeasuredAt.UtcDateTime, WeightLb=decimal.Round(reading.WeightLb,1), MeasurementSourceCode="FITDAYS", SourceRecordId=$"{connection.PersonId}:{reading.RecordId}", CreatedAtUtc=DateTime.UtcNow };
        if (provider.BodyCompositionEnabled)
        {
            m.BodyFatPct=Fraction(reading.Bfr);
            m.SubcutaneousFatPct=Fraction(reading.Sfr);
            m.SkeletalMuscleMassLb=Mass(reading.WeightLb,reading.Rosm,provider.SkeletalMuscleUnit);
            m.BodyWaterLb=Mass(reading.WeightLb,reading.Vwc,provider.BodyWaterUnit);
            m.VisceralFatRating=reading.Uvi is > 0 and <= 100 ? reading.Uvi : null;
        }
        return m;
    }
    public static bool BodyValuesUnchanged(Measurement current, Measurement? rendered, WebRecomp.Web.ViewModels.DailyEntryViewModel entry) =>
        MatchesEntry(current,entry) || (rendered?.MeasurementSourceCode == "FITDAYS" && MatchesEntry(rendered,entry));
    public static bool MatchesEntry(Measurement m, WebRecomp.Web.ViewModels.DailyEntryViewModel entry) =>
        m.WeightLb == entry.WeightLb && m.BodyFatPct == entry.BodyFatPercent / 100m
        && m.SkeletalMuscleMassLb == entry.SkeletalMuscleMassLb
        && m.SubcutaneousFatPct == entry.SubcutaneousFatPercent / 100m
        && m.BodyWaterLb == entry.BodyWaterLb && m.VisceralFatRating == entry.VisceralFatRating
        && m.WaistIn == entry.WaistIn && m.HipsIn == entry.HipsIn;
    public static bool CanImportDay(IEnumerable<Measurement> existing) => !existing.Any(m=>!m.IsDeleted && m.MeasurementSourceCode!="FITDAYS");
}
