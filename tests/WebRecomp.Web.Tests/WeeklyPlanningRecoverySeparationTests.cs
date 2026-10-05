using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class WeeklyPlanningRecoverySeparationTests
{
    [Fact]
    public void CurrentPlanningDoesNotDependOnMixedWeeklyStrategyService()
    {
        var root = FindRepoRoot();

        var schedule =
            Read(
                root,
                "Controllers",
                "ScheduleController.cs");

        var weeklyPlan =
            Read(
                root,
                "Services",
                "WeeklyPlanService.cs");

        Assert.Contains(
            "WeeklyPlanningAssessmentService planningAssessment",
            schedule,
            StringComparison.Ordinal);

        Assert.Contains(
            "PlanningWeek.Start",
            schedule,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "WeeklyStrategyService strategy",
            schedule,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "WeeklyStrategyService.WeekStart",
            schedule,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "WeeklyStrategyService.WeekStart",
            weeklyPlan,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyRegionalLoadsAreRemovedFromCurrentRuntime()
    {
        var root = FindRepoRoot();

        var weeklyStrategy =
            Read(
                root,
                "Services",
                "WeeklyStrategyService.cs");

        var planning =
            Read(
                root,
                "Services",
                "WeeklyPlanningAssessmentService.cs");

        var dashboard =
            Read(
                root,
                "Services",
                "DashboardService.cs");

        var recovery =
            Read(
                root,
                "Services",
                "Engine30",
                "Engine30TrainingLoadRecoveryService.cs");

        var legacyRecovery =
            Read(
                root,
                "Services",
                "LegacyEngine2RegionRecoveryService.cs");

        foreach (var source in new[]
                 {
                     weeklyStrategy,
                     planning,
                     dashboard,
                     recovery,
                     legacyRecovery
                 })
        {
            Assert.DoesNotContain(
                "CalculatedPushLoad",
                source,
                StringComparison.Ordinal);

            Assert.DoesNotContain(
                "CalculatedPullLoad",
                source,
                StringComparison.Ordinal);

            Assert.DoesNotContain(
                "CalculatedLegLoad",
                source,
                StringComparison.Ordinal);

            Assert.DoesNotContain(
                "CalculatedCardioLoad",
                source,
                StringComparison.Ordinal);
        }

        Assert.Contains(
            "v1014D",
            legacyRecovery,
            StringComparison.Ordinal);

        Assert.Contains(
            "obsolete compatibility/runtime service removed",
            legacyRecovery,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardUsesEngine30TrainingLoadRecoveryEvidence()
    {
        var root = FindRepoRoot();

        var dashboard =
            Read(
                root,
                "Services",
                "DashboardService.cs");

        Assert.Contains(
            "Engine30TrainingLoadRecoveryService trainingLoadRecovery",
            dashboard,
            StringComparison.Ordinal);

        Assert.Contains(
            "trainingLoadRecovery.Evaluate",
            dashboard,
            StringComparison.Ordinal);

        Assert.Contains(
            "recoveryEvidence.FourDayHardDays",
            dashboard,
            StringComparison.Ordinal);

        Assert.Contains(
            "recoveryEvidence.FourDayModerateHardDays",
            dashboard,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TrainingLoadRecovery_UsesCurrentEngineEvidenceAndCheckin()
    {
        var service =
            new Engine30TrainingLoadRecoveryService();

        var impact =
            BuildImpact(
                hardDays: 2,
                moderateHardDays: 3);

        var checkin =
            new DailyCheckin
            {
                CheckinId = Guid.NewGuid(),
                ClientId = Guid.NewGuid(),
                ProgramId = Guid.NewGuid(),
                CheckinDate = new DateOnly(2026, 10, 5),
                SleepHours = 6m,
                EnergyScore = 5,
                SorenessScore = 6,
                PainFlag = false,
                IllnessFlag = false,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

        var result =
            service.Evaluate(
                impact,
                checkin);

        Assert.Equal(
            Engine30TrainingLoadBandCodes.High,
            result.TrainingLoadBandCode);

        Assert.Equal(
            Engine30RecoveryBandCodes.Watch,
            result.RecoveryBandCode);

        Assert.Equal(
            2,
            result.FourDayHardDays);

        Assert.Equal(
            3,
            result.FourDayModerateHardDays);
    }

    [Fact]
    public void TrainingLoadRecovery_SafetyFlagsTakePriority()
    {
        var service =
            new Engine30TrainingLoadRecoveryService();

        var impact =
            BuildImpact(
                hardDays: 0,
                moderateHardDays: 0);

        var checkin =
            new DailyCheckin
            {
                CheckinId = Guid.NewGuid(),
                ClientId = Guid.NewGuid(),
                ProgramId = Guid.NewGuid(),
                CheckinDate = new DateOnly(2026, 10, 5),
                SleepHours = 8m,
                EnergyScore = 9,
                SorenessScore = 1,
                PainFlag = true,
                IllnessFlag = false,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

        var result =
            service.Evaluate(
                impact,
                checkin);

        Assert.Equal(
            Engine30RecoveryBandCodes.SafetyLimited,
            result.RecoveryBandCode);

        Assert.Contains(
            "PAIN_FLAG",
            result.ReasonCodes);
    }

    private static Engine30DashboardYesterdayImpactProjection BuildImpact(
        short hardDays,
        short moderateHardDays)
        => new(
            PreviousDate:
                new DateOnly(2026, 10, 4),
            PreviousTargetAvailable:
                true,
            PreviousTargetCalories:
                2000m,
            PreviousTargetProteinG:
                180m,
            PreviousTargetCarbsG:
                180m,
            PreviousTargetFatG:
                60m,
            PreviousNutritionActualLogged:
                true,
            PreviousActualCalories:
                2000m,
            PreviousActualProteinG:
                180m,
            PreviousActualCarbsG:
                180m,
            PreviousActualFatG:
                60m,
            PreviousCarbDifferenceG:
                0m,
            PreviousWorkoutActual:
                "Completed workout",
            PreviousGlycogenDemandG:
                80m,
            PreviousRestorationCreditG:
                75m,
            PreviousRestorationRatio:
                0.94m,
            PreviousFuelSummary:
                "test",
            PreviousEnergyVarianceKcal:
                0m,
            PreviousEligibleEnergyCarryoverKcal:
                0m,
            WeekEnergyOverageKcal:
                0m,
            WeekEligibleEnergyCarryoverKcal:
                0m,
            EnergyCorrectionAppliedBeforeTodayKcal:
                0m,
            EnergyCorrectionAppliedTodayKcal:
                0m,
            EnergyCorrectionAppliedFutureKcal:
                0m,
            EnergyCorrectionUnallocatedKcal:
                0m,
            SurplusRecoveryFraction:
                0.5m,
            TodayBaselinePrescribedEnergyKcal:
                2000m,
            TodayEnergyCarryoverCorrectionKcal:
                0m,
            EnergyCarryoverSummary:
                "test",
            FourDayGlycogenDemandG:
                250m,
            FourDayRestorationCreditG:
                225m,
            AbsoluteRestorationRatio:
                0.90m,
            FourDayModerateHardDays:
                moderateHardDays,
            FourDayHardDays:
                hardDays,
            FourDayCarbohydrateEvidenceDays:
                4,
            TodayPlannedGlycogenDemandG:
                75m,
            FunctionalTargetRatio:
                0.90m,
            IncomingTargetAchievementRatio:
                0.90m,
            IncomingReadinessBandCode:
                "ADEQUATE",
            HistoricalShortfallG:
                0m,
            RecoveryAllocationG:
                0m,
            PreWorkoutRescueG:
                0m,
            ProjectedTargetAchievementRatio:
                0.90m,
            ProjectedReadinessBandCode:
                "ADEQUATE",
            FuelActionLabel:
                "FOLLOW PLAN",
            ReasonCodes:
                Array.Empty<string>());

    private static string Read(
        string root,
        params string[] parts)
        => File.ReadAllText(
            Path.Combine(
                new[]
                {
                    root,
                    "src",
                    "WebRecomp.Web"
                }
                .Concat(parts)
                .ToArray()));

    private static string FindRepoRoot()
    {
        var current =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate =
                Path.Combine(
                    current.FullName,
                    "src",
                    "WebRecomp.Web",
                    "WebRecomp.Web.csproj");

            if (File.Exists(candidate))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the 12WR repository root.");
    }
}
