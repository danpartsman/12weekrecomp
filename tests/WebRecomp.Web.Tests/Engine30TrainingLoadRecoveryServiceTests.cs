using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;

namespace WebRecomp.Web.Tests;

public sealed class Engine30TrainingLoadRecoveryServiceTests
{
    private readonly Engine30TrainingLoadRecoveryService _service = new();

    [Fact]
    public void PainOrIllness_IsSafetyLimited_BeforeLoadIsConsidered()
    {
        var result =
            _service.Evaluate(
                Impact(hardDays: 2, moderateHardDays: 3),
                Checkin(pain: true, illness: true, sleepHours: 8m, energy: 9, soreness: 1));

        Assert.Equal(Engine30RecoveryBandCodes.SafetyLimited, result.RecoveryBandCode);
        Assert.Contains("PAIN_FLAG", result.ReasonCodes);
        Assert.Contains("ILLNESS_FLAG", result.ReasonCodes);
        Assert.Equal(Engine30TrainingLoadBandCodes.High, result.TrainingLoadBandCode);
    }

    [Fact]
    public void VeryLowSleep_LowEnergy_OrHighSoreness_IsReduced()
    {
        var lowSleep =
            _service.Evaluate(
                Impact(hardDays: 0, moderateHardDays: 0),
                Checkin(sleepHours: 5.4m, energy: 7, soreness: 2));

        var lowEnergy =
            _service.Evaluate(
                Impact(hardDays: 0, moderateHardDays: 0),
                Checkin(sleepHours: 8m, energy: 3, soreness: 2));

        var highSoreness =
            _service.Evaluate(
                Impact(hardDays: 0, moderateHardDays: 0),
                Checkin(sleepHours: 8m, energy: 7, soreness: 8));

        Assert.Equal(Engine30RecoveryBandCodes.Reduced, lowSleep.RecoveryBandCode);
        Assert.Contains("VERY_LOW_SLEEP", lowSleep.ReasonCodes);
        Assert.Equal(Engine30RecoveryBandCodes.Reduced, lowEnergy.RecoveryBandCode);
        Assert.Contains("LOW_ENERGY", lowEnergy.ReasonCodes);
        Assert.Equal(Engine30RecoveryBandCodes.Reduced, highSoreness.RecoveryBandCode);
        Assert.Contains("HIGH_SORENESS", highSoreness.ReasonCodes);
    }

    [Fact]
    public void RecentHardDensity_OrModerateCheckin_IsWatch()
    {
        var hardDensity =
            _service.Evaluate(
                Impact(hardDays: 2, moderateHardDays: 0),
                Checkin(sleepHours: 8m, energy: 7, soreness: 2));

        var moderateDensity =
            _service.Evaluate(
                Impact(hardDays: 0, moderateHardDays: 3),
                Checkin(sleepHours: 8m, energy: 7, soreness: 2));

        var shortSleep =
            _service.Evaluate(
                Impact(hardDays: 0, moderateHardDays: 0),
                Checkin(sleepHours: 6.4m, energy: 7, soreness: 2));

        Assert.Equal(Engine30RecoveryBandCodes.Watch, hardDensity.RecoveryBandCode);
        Assert.Contains("RECENT_HARD_DAY_DENSITY", hardDensity.ReasonCodes);
        Assert.Equal(Engine30RecoveryBandCodes.Watch, moderateDensity.RecoveryBandCode);
        Assert.Contains("RECENT_MODERATE_HARD_DENSITY", moderateDensity.ReasonCodes);
        Assert.Equal(Engine30RecoveryBandCodes.Watch, shortSleep.RecoveryBandCode);
        Assert.Contains("SHORT_SLEEP", shortSleep.ReasonCodes);
        Assert.Equal(Engine30TrainingLoadBandCodes.High, hardDensity.TrainingLoadBandCode);
        Assert.Equal(Engine30TrainingLoadBandCodes.High, moderateDensity.TrainingLoadBandCode);
    }

    [Fact]
    public void MissingCheckin_WithoutLoadPressure_IsUnknown()
    {
        var result =
            _service.Evaluate(
                Impact(hardDays: 0, moderateHardDays: 1),
                checkin: null);

        Assert.Equal(Engine30RecoveryBandCodes.Unknown, result.RecoveryBandCode);
        Assert.Contains("NO_RECOVERY_CHECKIN", result.ReasonCodes);
        Assert.Equal(Engine30TrainingLoadBandCodes.Low, result.TrainingLoadBandCode);
    }

    [Fact]
    public void RestedCheckin_IsAdequate_AndOneHardDay_IsModerateLoad()
    {
        var result =
            _service.Evaluate(
                Impact(hardDays: 1, moderateHardDays: 1),
                Checkin(sleepHours: 7.5m, energy: 7, soreness: 3));

        Assert.Equal(Engine30RecoveryBandCodes.Adequate, result.RecoveryBandCode);
        Assert.Empty(result.ReasonCodes);
        Assert.Equal(Engine30TrainingLoadBandCodes.Moderate, result.TrainingLoadBandCode);
        Assert.Equal((short)1, result.FourDayHardDays);
        Assert.Equal((short)1, result.FourDayModerateHardDays);
    }

    private static DailyCheckin Checkin(
        bool pain = false,
        bool illness = false,
        decimal? sleepHours = null,
        byte? energy = null,
        byte? soreness = null)
    {
        return new DailyCheckin
        {
            PainFlag = pain,
            IllnessFlag = illness,
            SleepHours = sleepHours,
            EnergyScore = energy,
            SorenessScore = soreness
        };
    }

    private static Engine30DashboardYesterdayImpactProjection Impact(
        short hardDays,
        short moderateHardDays)
    {
        return new Engine30DashboardYesterdayImpactProjection(
            PreviousDate: new DateOnly(2026, 10, 10),
            PreviousTargetAvailable: false,
            PreviousTargetCalories: 0m,
            PreviousTargetProteinG: 0m,
            PreviousTargetCarbsG: 0m,
            PreviousTargetFatG: 0m,
            PreviousNutritionActualLogged: false,
            PreviousActualCalories: null,
            PreviousActualProteinG: null,
            PreviousActualCarbsG: null,
            PreviousActualFatG: null,
            PreviousCarbDifferenceG: null,
            PreviousWorkoutActual: null,
            PreviousGlycogenDemandG: 0m,
            PreviousRestorationCreditG: null,
            PreviousRestorationRatio: null,
            PreviousFuelSummary: "",
            PreviousEnergyVarianceKcal: null,
            PreviousEligibleEnergyCarryoverKcal: null,
            WeekEnergyOverageKcal: 0m,
            WeekEligibleEnergyCarryoverKcal: 0m,
            EnergyCorrectionAppliedBeforeTodayKcal: 0m,
            EnergyCorrectionAppliedTodayKcal: 0m,
            EnergyCorrectionAppliedFutureKcal: 0m,
            EnergyCorrectionUnallocatedKcal: 0m,
            SurplusRecoveryFraction: 0m,
            TodayBaselinePrescribedEnergyKcal: 0m,
            TodayEnergyCarryoverCorrectionKcal: 0m,
            EnergyCarryoverSummary: "",
            FourDayGlycogenDemandG: 0m,
            FourDayRestorationCreditG: 0m,
            AbsoluteRestorationRatio: 0m,
            FourDayModerateHardDays: moderateHardDays,
            FourDayHardDays: hardDays,
            FourDayCarbohydrateEvidenceDays: 0,
            TodayPlannedGlycogenDemandG: 0m,
            FunctionalTargetRatio: 0m,
            IncomingTargetAchievementRatio: 0m,
            IncomingReadinessBandCode: "",
            HistoricalShortfallG: 0m,
            RecoveryAllocationG: 0m,
            PreWorkoutRescueG: 0m,
            ProjectedTargetAchievementRatio: 0m,
            ProjectedReadinessBandCode: "",
            FuelActionLabel: "",
            ReasonCodes: []);
    }
}
