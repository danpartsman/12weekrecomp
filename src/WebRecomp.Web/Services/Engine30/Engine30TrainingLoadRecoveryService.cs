using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.Engine30;

public static class Engine30RecoveryBandCodes
{
    public const string Adequate = "ADEQUATE";
    public const string Watch = "WATCH";
    public const string Reduced = "REDUCED";
    public const string SafetyLimited = "SAFETY_LIMITED";
    public const string Unknown = "UNKNOWN";
}

public static class Engine30TrainingLoadBandCodes
{
    public const string Low = "LOW";
    public const string Moderate = "MODERATE";
    public const string High = "HIGH";
}

/// <summary>
/// v1014C Engine 3.0 recovery/training-load evidence synthesis.
///
/// This deliberately does NOT read the legacy Engine 2.x regional load columns.
/// It consumes current Engine 3.0 recent-demand evidence plus today's recovery
/// check-in and produces a small recovery contract for current decision logic.
/// </summary>
public sealed record Engine30TrainingLoadRecoveryResult(
    short FourDayHardDays,
    short FourDayModerateHardDays,
    string TrainingLoadBandCode,
    string RecoveryBandCode,
    IReadOnlyList<string> ReasonCodes);

public sealed class Engine30TrainingLoadRecoveryService
{
    public Engine30TrainingLoadRecoveryResult Evaluate(
        Engine30DashboardYesterdayImpactProjection impact,
        DailyCheckin? checkin)
    {
        ArgumentNullException.ThrowIfNull(impact);

        var reasons =
            new List<string>();

        var loadBand =
            impact.FourDayHardDays >= 2
            || impact.FourDayModerateHardDays >= 3
                ? Engine30TrainingLoadBandCodes.High
                : impact.FourDayHardDays >= 1
                  || impact.FourDayModerateHardDays >= 2
                    ? Engine30TrainingLoadBandCodes.Moderate
                    : Engine30TrainingLoadBandCodes.Low;

        string recoveryBand;

        if (checkin?.PainFlag == true
            || checkin?.IllnessFlag == true)
        {
            recoveryBand =
                Engine30RecoveryBandCodes.SafetyLimited;

            if (checkin.PainFlag)
                reasons.Add("PAIN_FLAG");

            if (checkin.IllnessFlag)
                reasons.Add("ILLNESS_FLAG");
        }
        else if (checkin?.SleepHours is < 5.5m
                 || checkin?.EnergyScore is <= 3
                 || checkin?.SorenessScore is >= 8)
        {
            recoveryBand =
                Engine30RecoveryBandCodes.Reduced;

            if (checkin?.SleepHours is < 5.5m)
                reasons.Add("VERY_LOW_SLEEP");

            if (checkin?.EnergyScore is <= 3)
                reasons.Add("LOW_ENERGY");

            if (checkin?.SorenessScore is >= 8)
                reasons.Add("HIGH_SORENESS");
        }
        else if (impact.FourDayHardDays >= 2
                 || impact.FourDayModerateHardDays >= 3
                 || checkin?.SleepHours is < 6.5m
                 || checkin?.EnergyScore is <= 5
                 || checkin?.SorenessScore is >= 6)
        {
            recoveryBand =
                Engine30RecoveryBandCodes.Watch;

            if (impact.FourDayHardDays >= 2)
                reasons.Add("RECENT_HARD_DAY_DENSITY");

            if (impact.FourDayModerateHardDays >= 3)
                reasons.Add("RECENT_MODERATE_HARD_DENSITY");

            if (checkin?.SleepHours is < 6.5m)
                reasons.Add("SHORT_SLEEP");

            if (checkin?.EnergyScore is <= 5)
                reasons.Add("MODERATE_LOW_ENERGY");

            if (checkin?.SorenessScore is >= 6)
                reasons.Add("ELEVATED_SORENESS");
        }
        else if (checkin is null)
        {
            recoveryBand =
                Engine30RecoveryBandCodes.Unknown;

            reasons.Add("NO_RECOVERY_CHECKIN");
        }
        else
        {
            recoveryBand =
                Engine30RecoveryBandCodes.Adequate;
        }

        return new Engine30TrainingLoadRecoveryResult(
            FourDayHardDays:
                impact.FourDayHardDays,
            FourDayModerateHardDays:
                impact.FourDayModerateHardDays,
            TrainingLoadBandCode:
                loadBand,
            RecoveryBandCode:
                recoveryBand,
            ReasonCodes:
                reasons);
    }
}
