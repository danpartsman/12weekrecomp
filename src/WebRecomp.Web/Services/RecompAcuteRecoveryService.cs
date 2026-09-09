using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record RecompAcuteRecoveryState(
    decimal? SleepHours,
    decimal? BaselineSleepHours,
    decimal? SleepRatio,
    string SleepCode,
    byte? EnergyScore,
    string EnergyCode,
    byte? SorenessScore,
    string SorenessCode,
    short? RestingHr,
    string SystemicRecoveryCode,
    bool PainFlag,
    bool IllnessFlag,
    IReadOnlyList<string> LimitingFactors);

public static class RecompAcuteRecoveryService
{
    public static RecompAcuteRecoveryState Evaluate(
        DailyCheckin? checkin,
        decimal? baselineSleepHours,
        IReadOnlyDictionary<string, decimal> settings)
    {
        var sleepReference = baselineSleepHours is > 0
            ? baselineSleepHours.Value
            : ConfigurationService.Setting(
                settings,
                RecompModelCalibrationCatalog.SleepReferenceHours,
                RecompModelCalibrationCatalog.Default(
                    RecompModelCalibrationCatalog.SleepReferenceHours));

        var sleepReducedRatio =
            ConfigurationService.Setting(
                settings,
                RecompModelCalibrationCatalog.SleepReducedRatio,
                RecompModelCalibrationCatalog.Default(
                    RecompModelCalibrationCatalog.SleepReducedRatio));

        var sleepSevereRatio =
            ConfigurationService.Setting(
                settings,
                RecompModelCalibrationCatalog.SleepSevereRatio,
                RecompModelCalibrationCatalog.Default(
                    RecompModelCalibrationCatalog.SleepSevereRatio));

        var energyReducedMax =
            ConfigurationService.Setting(
                settings,
                RecompModelCalibrationCatalog.EnergyReducedMax,
                RecompModelCalibrationCatalog.Default(
                    RecompModelCalibrationCatalog.EnergyReducedMax));

        var energySevereMax =
            ConfigurationService.Setting(
                settings,
                RecompModelCalibrationCatalog.EnergySevereMax,
                RecompModelCalibrationCatalog.Default(
                    RecompModelCalibrationCatalog.EnergySevereMax));

        var sorenessReducedMin =
            ConfigurationService.Setting(
                settings,
                RecompModelCalibrationCatalog.SorenessReducedMin,
                RecompModelCalibrationCatalog.Default(
                    RecompModelCalibrationCatalog.SorenessReducedMin));

        var sorenessSevereMin =
            ConfigurationService.Setting(
                settings,
                RecompModelCalibrationCatalog.SorenessSevereMin,
                RecompModelCalibrationCatalog.Default(
                    RecompModelCalibrationCatalog.SorenessSevereMin));

        decimal? sleepRatio =
            checkin?.SleepHours is decimal sleep
            && sleepReference > 0
                ? sleep / sleepReference
                : null;

        var sleepCode =
            sleepRatio is null
                ? "UNKNOWN"
                : sleepRatio < sleepSevereRatio
                    ? "SEVERELY_REDUCED"
                    : sleepRatio < sleepReducedRatio
                        ? "REDUCED"
                        : "ADEQUATE";

        var energyCode =
            checkin?.EnergyScore is not byte energy
                ? "UNKNOWN"
                : energy <= energySevereMax
                    ? "SEVERELY_REDUCED"
                    : energy <= energyReducedMax
                        ? "REDUCED"
                        : "ADEQUATE";

        var sorenessCode =
            checkin?.SorenessScore is not byte soreness
                ? "UNKNOWN"
                : soreness >= sorenessSevereMin
                    ? "SEVERELY_REDUCED"
                    : soreness >= sorenessReducedMin
                        ? "REDUCED"
                        : "ADEQUATE";

        var limitingFactors = new List<string>();

        if (sleepCode is not "ADEQUATE" and not "UNKNOWN")
            limitingFactors.Add($"Sleep: {sleepCode}");

        if (energyCode is not "ADEQUATE" and not "UNKNOWN")
            limitingFactors.Add($"Energy: {energyCode}");

        if (sorenessCode is not "ADEQUATE" and not "UNKNOWN")
            limitingFactors.Add($"Soreness: {sorenessCode}");

        var knownCount =
            new[] { sleepCode, energyCode, sorenessCode }
                .Count(x => x != "UNKNOWN");

        string systemicRecoveryCode;

        if (knownCount == 0)
        {
            systemicRecoveryCode = "UNKNOWN";
        }
        else if (sleepCode == "SEVERELY_REDUCED"
                 || energyCode == "SEVERELY_REDUCED"
                 || sorenessCode == "SEVERELY_REDUCED")
        {
            systemicRecoveryCode = "SEVERELY_REDUCED";
        }
        else if (sleepCode == "REDUCED"
                 || energyCode == "REDUCED"
                 || sorenessCode == "REDUCED")
        {
            systemicRecoveryCode = "REDUCED";
        }
        else
        {
            systemicRecoveryCode = "ADEQUATE";
        }

        return new RecompAcuteRecoveryState(
            checkin?.SleepHours,
            baselineSleepHours,
            sleepRatio,
            sleepCode,
            checkin?.EnergyScore,
            energyCode,
            checkin?.SorenessScore,
            sorenessCode,
            checkin?.RestingHr,
            systemicRecoveryCode,
            checkin?.PainFlag == true,
            checkin?.IllnessFlag == true,
            limitingFactors);
    }
}
