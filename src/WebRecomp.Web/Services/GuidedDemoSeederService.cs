using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record GuidedDemoSeedResult(
    Client Client,
    ProgramRecord Program,
    DateOnly ActionDate);

public sealed class GuidedDemoSeederService(
    RecompDbContext db,
    ConfigurationService configuration,
    WeeklyPlanService weeklyPlans,
    WebRecomp.Web.Services.Engine30.Engine30WorkoutUiCatalogService engine30WorkoutCatalog,
    WorkoutActualService workoutActuals)
{
    private const string SupportedScenario = "RECOMP_STANDARD_01";

    public async Task<GuidedDemoSeedResult> SeedAsync(
        DemoWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                workspace.DemoTypeCode,
                "GUIDED",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only Guided Demo workspaces can be preloaded.");
        }

        // Idempotency: if this workspace was already seeded, reuse its existing
        // Client/Program rather than creating a second customer profile.
        var existingAccess = await db.UserClientAccess
            .Where(x =>
                x.UserId == workspace.UserId
                && x.RevokedAtUtc == null
                && x.AccessRoleCode == "OWNER")
            .OrderBy(x => x.GrantedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingAccess is not null)
        {
            var existingClient = await db.Clients
                .SingleAsync(
                    x => x.ClientId == existingAccess.ClientId,
                    cancellationToken);

            var existingProgram = await db.Programs
                .Where(x =>
                    x.ClientId == existingClient.ClientId
                    && x.ProgramStatusCode == "ACTIVE")
                .OrderByDescending(x => x.StartDate)
                .FirstAsync(cancellationToken);

            // RECOMP_STANDARD_01 intentionally starts exactly four weeks
            // before its curated dashboard story date.
            var existingActionDate =
                existingProgram.StartDate.AddDays(28);

            return new GuidedDemoSeedResult(
                existingClient,
                existingProgram,
                existingActionDate);
        }

        var invite = await db.DemoInvites
            .SingleAsync(
                x => x.DemoInviteId == workspace.DemoInviteId,
                cancellationToken);

        if (!string.Equals(
                invite.GuidedScenarioCode,
                SupportedScenario,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported Guided Demo scenario: " +
                $"{invite.GuidedScenarioCode ?? "(none)"}");
        }

        var configVersion =
            await configuration.GetActiveVersionAsync(
                cancellationToken);

        var now = DateTime.UtcNow;

        // Use the most recent Friday as the scenario anchor. PLAN_2 with four
        // available training days gives the demo a clear Mon Legs / Wed Push /
        // Thu Conditioning / Fri Pull story, so the previous-day fuel panel and
        // the rolling four-day glycogen calculation are meaningful regardless
        // of which day a tester opens the invitation.
        var clientToday = DateOnly.FromDateTime(now);
        var actionDate = MostRecentFriday(clientToday);
        var programStart = actionDate.AddDays(-28);

        var client = new Client
        {
            ClientId = Guid.NewGuid(),
            ClientName = "Alex Demo",
            DateOfBirth = new DateOnly(1978, 5, 12),
            SexCode = "MALE",
            HeightIn = 71m,
            TimeZone = "America/Los_Angeles",
            UnitSystemCode = "US",
            TrainingExperienceCode = "INTERMEDIATE",
            BaselineStepsDay = 7000,
            BaselineSleepHours = 6.8m,
            ClientStatusCode = "ACTIVE",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        // Fat-loss Recomp is useful for this demonstration because its normal
        // four-day template includes three resistance sessions plus one
        // conditioning session while retaining recovery days.
        var program = new ProgramRecord
        {
            ProgramId = Guid.NewGuid(),
            ClientId = client.ClientId,
            ConfigVersionId = configVersion.ConfigVersionId,
            SelectedPlanCode = "PLAN_2",
            GoalCode = "FAT_LOSS_RECOMP",
            StartDate = programStart,
            DurationWeeks = 12,
            AvailableTrainingDaysWeek = 4,
            ProgramStatusCode = "ACTIVE",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var goal = new ProgramGoal
        {
            ProgramGoalId = Guid.NewGuid(),
            ProgramId = program.ProgramId,
            TargetWeightLowLb = 198m,
            TargetWeightHighLb = 202m,
            TargetWaistIn = 36.5m,
            GoalNotes =
                "Guided Demo: preserve muscle while steadily reducing body fat."
        };

        var access = new UserClientAccess
        {
            UserClientAccessId = Guid.NewGuid(),
            UserId = workspace.UserId,
            ClientId = client.ClientId,
            AccessRoleCode = "OWNER",
            GrantedAtUtc = now
        };

        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    cancellationToken);

            db.Clients.Add(client);
            await db.SaveChangesAsync(cancellationToken);

            db.UserClientAccess.Add(access);
            db.Programs.Add(program);
            await db.SaveChangesAsync(cancellationToken);

            db.ProgramGoals.Add(goal);
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        });

        // Seed both the prior and current weeks using the real WeeklyPlanService.
        // This keeps the four-day rolling window meaningful even if the scenario
        // anchor falls near the beginning of a week.
        var actionWeekStart =
            PlanningWeek.Start(actionDate);

        await weeklyPlans.SeedRecommendedWeekIfBlankAsync(
            client,
            program,
            actionWeekStart.AddDays(-7),
            cancellationToken);

        await weeklyPlans.SeedRecommendedWeekIfBlankAsync(
            client,
            program,
            actionWeekStart,
            cancellationToken);

        SeedMeasurements(
            client,
            program,
            programStart,
            actionDate,
            now);

        await db.SaveChangesAsync(cancellationToken);

        var workoutTypes =
            await engine30WorkoutCatalog.GetActiveMapAsync(
                cancellationToken);

        await SeedRecentTrainingAndNutritionAsync(
            workspace,
            client,
            program,
            actionDate,
            now,
            workoutTypes,
            cancellationToken);

        db.DailyCheckins.Add(
            new DailyCheckin
            {
                CheckinId = Guid.NewGuid(),
                ClientId = client.ClientId,
                ProgramId = program.ProgramId,
                CheckinDate = actionDate,
                SleepHours = 7.1m,
                RestingHr = 57,
                Steps = 3200,
                EnergyScore = 7,
                SorenessScore = 4,
                PainFlag = false,
                IllnessFlag = false,
                Notes =
                    "Guided Demo morning check-in: ready to train, with some normal accumulated fatigue.",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

        await db.SaveChangesAsync(cancellationToken);

        // v1014D: Dashboard and Engine 3.0 derive current recommendations directly
        // from canonical plan, nutrition, check-in, and completed-workout data.

        return new GuidedDemoSeedResult(
            client,
            program,
            actionDate);
    }

    private void SeedMeasurements(
        Client client,
        ProgramRecord program,
        DateOnly programStart,
        DateOnly actionDate,
        DateTime now)
    {
        var points = new[]
        {
            new
            {
                Date = programStart,
                Weight = 216.0m,
                BodyFat = 0.278m,
                Muscle = 154.0m,
                Water = 111.0m,
                Visceral = 11m,
                Waist = 41.0m
            },
            new
            {
                Date = actionDate.AddDays(-21),
                Weight = 214.5m,
                BodyFat = 0.272m,
                Muscle = 154.2m,
                Water = 111.5m,
                Visceral = 11m,
                Waist = 40.6m
            },
            new
            {
                Date = actionDate.AddDays(-14),
                Weight = 213.0m,
                BodyFat = 0.266m,
                Muscle = 154.5m,
                Water = 112.0m,
                Visceral = 10m,
                Waist = 40.2m
            },
            new
            {
                Date = actionDate.AddDays(-7),
                Weight = 211.5m,
                BodyFat = 0.260m,
                Muscle = 154.8m,
                Water = 112.2m,
                Visceral = 10m,
                Waist = 39.8m
            },
            new
            {
                Date = actionDate,
                Weight = 210.5m,
                BodyFat = 0.254m,
                Muscle = 155.0m,
                Water = 112.5m,
                Visceral = 10m,
                Waist = 39.5m
            }
        };

        foreach (var point in points)
        {
            db.Measurements.Add(
                new Measurement
                {
                    MeasurementId = Guid.NewGuid(),
                    ClientId = client.ClientId,
                    ProgramId = program.ProgramId,
                    MeasurementAtUtc =
                        now.AddDays(
                            point.Date.DayNumber
                            - actionDate.DayNumber),
                    LocalMeasurementDate = point.Date,
                    WeightLb = point.Weight,
                    BodyFatPct = point.BodyFat,
                    MuscleMassLb = point.Muscle,
                    BodyWaterLb = point.Water,
                    VisceralFatRating = point.Visceral,
                    WaistIn = point.Waist,
                    MeasurementSourceCode = "DEMO_SEED",
                    SourceRecordId =
                        $"DEMO:{program.ProgramId:D}:MEASUREMENT:{point.Date:yyyy-MM-dd}",
                    Notes =
                        "Preloaded Guided Demo body-composition history.",
                    CreatedAtUtc = now
                });
        }
    }

    private async Task SeedRecentTrainingAndNutritionAsync(
        DemoWorkspace workspace,
        Client client,
        ProgramRecord program,
        DateOnly actionDate,
        DateTime now,
        IReadOnlyDictionary<string, WorkoutTypeDefinition> workoutTypes,
        CancellationToken cancellationToken)
    {
        var historyStart = actionDate.AddDays(-4);
        var historyEnd = actionDate.AddDays(-1);

        var planned = await db.PlannedWorkouts
            .Where(x =>
                x.ProgramId == program.ProgramId
                && x.PlannedDate >= historyStart
                && x.PlannedDate <= historyEnd)
            .OrderBy(x => x.PlannedDate)
            .ToListAsync(cancellationToken);

        var plannedByDate =
            planned.ToDictionary(x => x.PlannedDate);

        for (var offset = -4; offset <= -1; offset++)
        {
            var date = actionDate.AddDays(offset);

            WorkoutTypeDefinition? type = null;

            if (plannedByDate.TryGetValue(date, out var plan)
                && workoutTypes.TryGetValue(
                    plan.WorkoutTypeCode,
                    out var plannedType))
            {
                type = plannedType;
            }

            // Recovery days remain legitimate "no completed workout" days.
            if (type is not null
                && !type.CountsRecovery
                && !string.Equals(
                    type.WeeklyRoleCode,
                    "RECOVERY",
                    StringComparison.OrdinalIgnoreCase))
            {
                var rpe = type.WeeklyRoleCode switch
                {
                    "HEAVY" or "HEAVY_GENERIC" => 8.0m,
                    "CONDITIONING" => 8.5m,
                    "LIGHT_STRENGTH" => 7.0m,
                    "RECOVERY_CARDIO" => 4.0m,
                    _ => 7.0m
                };

                var session = new WorkoutSession
                {
                    WorkoutSessionId = Guid.NewGuid(),
                    ClientId = client.ClientId,
                    ProgramId = program.ProgramId,
                    WorkoutDate = date,
                    WorkoutTypeCode = type.WorkoutTypeCode,
                    DurationMin = type.DefaultMinutes,
                    TypicalRestSec = type.DefaultRestSec,
                    SessionRpe = rpe,
                    CompletedFlag = true,
                    EntrySourceCode = "DEMO_SEED",
                    SourceRecordId =
                        $"DEMO:{workspace.DemoWorkspaceId:D}:WORKOUT:{date:yyyy-MM-dd}",
                    Notes =
                        "Preloaded Guided Demo completed workout.",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                workoutActuals.ApplyCurrentEngineState(
                    session,
                    completed: true);

                db.WorkoutSessions.Add(session);
            }

            // Deliberately adequate-but-not-perfect carbohydrate intake.
            // This creates a believable partial refill across the rolling
            // four-day window without fabricating the Recomp calculation.
            var carbs = type?.FuelScore switch
            {
                >= 4m => 175m,
                >= 3m => 170m,
                >= 2m => 160m,
                _ => 145m
            };

            const decimal protein = 185m;
            const decimal fat = 60m;

            var calories =
                protein * 4m
                + carbs * 4m
                + fat * 9m;

            db.NutritionDays.Add(
                new NutritionDay
                {
                    NutritionDayId = Guid.NewGuid(),
                    ClientId = client.ClientId,
                    ProgramId = program.ProgramId,
                    NutritionDate = date,
                    ActualCalories = calories,
                    ActualProteinG = protein,
                    ActualCarbsG = carbs,
                    ActualFatG = fat,
                    FiberG = 29m,
                    WaterOz = 92m,
                    EntrySourceCode = "DEMO_SEED",
                    SourceRecordId =
                        $"DEMO:{workspace.DemoWorkspaceId:D}:NUTRITION:{date:yyyy-MM-dd}",
                    Notes =
                        "Preloaded Guided Demo nutrition actuals.",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static DateOnly MostRecentFriday(DateOnly date)
    {
        var diff =
            ((int)date.DayOfWeek
             - (int)DayOfWeek.Friday
             + 7) % 7;

        return date.AddDays(-diff);
    }
}
