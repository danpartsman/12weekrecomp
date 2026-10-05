using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutIntelligence;
using WebRecomp.WorkoutIntelligence.Persistence;

namespace WebRecomp.Web.Services;

public sealed class TestPersonaSeederService(
    RecompDbContext db,
    ConfigurationService configuration,
    WeeklyPlanService weeklyPlans,
    Engine30WorkoutUiCatalogService engine30WorkoutCatalog,
    Engine30DashboardProjectionService engine30Projection,
    WorkoutActualService workoutActuals,
    IExerciseCatalogStore exerciseCatalog,
    IDetailedWorkoutStore detailedWorkoutStore)
{
    private const int HistoryDays = 28;

    public async Task SeedAllAsync(CancellationToken cancellationToken = default)
    {
        var configVersion =
            await configuration.GetActiveVersionAsync(cancellationToken);

        foreach (var spec in TestPersonaCatalog.All)
        {
            await SeedPersonaAsync(
                spec,
                configVersion,
                cancellationToken);
        }
    }

    private async Task SeedPersonaAsync(
        TestPersonaSpec spec,
        ConfigVersion configVersion,
        CancellationToken cancellationToken)
    {
        var externalSubject =
            TestPersonaCatalog.ExternalSubject(spec.Email);

        var existingUser = await db.UserAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ExternalSubject == externalSubject,
                cancellationToken);

        // Idempotent by design. Once a tester starts changing a persona,
        // future TEST deployments must not overwrite their experiment.
        if (existingUser is not null)
            return;

        var now = DateTime.UtcNow;
        var today = PacificToday(now);
        var programStart = today.AddDays(-HistoryDays);

        var user = new UserAccount
        {
            UserId = Guid.NewGuid(),
            ExternalSubject = externalSubject,
            EmailNormalized = spec.Email,
            DisplayName = spec.DisplayName,
            PreferredDisplayName = spec.DisplayName,
            ContactEmail = spec.Email,
            ThemeCode = "SYSTEM",
            AccountStatusCode = "ACTIVE",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var client = new Client
        {
            ClientId = Guid.NewGuid(),
            ClientName = spec.DisplayName,
            DateOfBirth = spec.DateOfBirth,
            SexCode = spec.SexCode,
            HeightIn = spec.HeightIn,
            TimeZone = "America/Los_Angeles",
            UnitSystemCode = "US",
            TrainingExperienceCode = spec.TrainingExperienceCode,
            LifestyleActivityCode = spec.LifestyleActivityCode,
            BaselineStepsDay = spec.BaselineStepsDay,
            BaselineSleepHours = spec.BaselineSleepHours,
            ClientStatusCode = "ACTIVE",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var program = new ProgramRecord
        {
            ProgramId = Guid.NewGuid(),
            ClientId = client.ClientId,
            ConfigVersionId = configVersion.ConfigVersionId,
            SelectedPlanCode = spec.PlanCode,
            GoalCode = spec.GoalCode,
            StartDate = programStart,
            DurationWeeks = 12,
            AvailableTrainingDaysWeek = spec.AvailableTrainingDaysWeek,
            DetailedWorkoutEntryEnabled = true,
            NutritionFoodLogEnabled = true,
            ProgramStatusCode = "ACTIVE",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var goal = new ProgramGoal
        {
            ProgramGoalId = Guid.NewGuid(),
            ProgramId = program.ProgramId,
            TargetWeightLowLb = spec.TargetWeightLowLb,
            TargetWeightHighLb = spec.TargetWeightHighLb,
            TargetWaistIn = spec.TargetWaistIn,
            GoalNotes = "Synthetic TEST persona goal generated for Engine analysis."
        };

        var access = new UserClientAccess
        {
            UserClientAccessId = Guid.NewGuid(),
            UserId = user.UserId,
            ClientId = client.ClientId,
            AccessRoleCode = "OWNER",
            GrantedAtUtc = now
        };

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(cancellationToken);

            db.UserAccounts.Add(user);
            db.Clients.Add(client);
            await db.SaveChangesAsync(cancellationToken);

            db.UserClientAccess.Add(access);
            db.Programs.Add(program);
            await db.SaveChangesAsync(cancellationToken);

            db.ProgramGoals.Add(goal);
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        });

        var firstWeek = PlanningWeek.Start(programStart);
        for (var week = 0; week < program.DurationWeeks; week++)
        {
            await weeklyPlans.SeedRecommendedWeekIfBlankAsync(
                client,
                program,
                firstWeek.AddDays(week * 7),
                cancellationToken);
        }

        var workoutTypes =
            await engine30WorkoutCatalog.GetActiveMapAsync(
                cancellationToken);

        var engineContext =
            new AppClientContext(
                user,
                client,
                program);

        var recoveryType =
            workoutTypes.Values.FirstOrDefault(x => x.CountsRecovery)
            ?? workoutTypes.Values
                .OrderBy(x => x.DisplayOrder)
                .First();

        var plannedRows = await db.PlannedWorkouts
            .Where(x =>
                x.ProgramId == program.ProgramId
                && x.PlannedDate >= programStart
                && x.PlannedDate < today)
            .ToListAsync(cancellationToken);

        var plannedByDate = plannedRows
            .GroupBy(x => x.PlannedDate)
            .ToDictionary(x => x.Key, x => x.First());

        decimal trendWeight = spec.StartingWeightLb;
        decimal fatMass = spec.StartingWeightLb * spec.StartingBodyFatFraction;
        decimal skeletalMuscle = spec.StartingSkeletalMuscleLb;
        var startFatMass = fatMass;

        var detailedSeeds = new List<(WorkoutSession Session, SystemWorkoutRecipe Recipe, int Seed)>();

        for (var dayIndex = 0; dayIndex < HistoryDays; dayIndex++)
        {
            var date = programStart.AddDays(dayIndex);
            var random = new Random(StableSeed(spec.Email, date));

            var scaleNoise = dayIndex == 0
                ? 0m
                : RandomBetween(random, -0.35m, 0.35m);

            var scaleWeight = Math.Max(90m, trendWeight + scaleNoise);
            var bodyFatFraction = Math.Clamp(
                fatMass / Math.Max(1m, trendWeight),
                0.08m,
                0.50m);

            AddMeasurement(
                client,
                program,
                spec,
                date,
                scaleWeight,
                bodyFatFraction,
                skeletalMuscle,
                startFatMass,
                fatMass,
                now);

            var checkin = BuildCheckin(
                client,
                program,
                spec,
                date,
                random,
                now);

            db.DailyCheckins.Add(checkin);

            WorkoutTypeDefinition workoutType = recoveryType;
            PlannedWorkout? planned = null;

            if (plannedByDate.TryGetValue(date, out var plannedRow))
            {
                planned = plannedRow;

                if (workoutTypes.TryGetValue(
                        plannedRow.WorkoutTypeCode,
                        out var resolvedType))
                {
                    workoutType = resolvedType;
                }
            }

            var trained =
                planned is not null
                && !workoutType.CountsRecovery
                && !string.Equals(
                    workoutType.WeeklyRoleCode,
                    "RECOVERY",
                    StringComparison.OrdinalIgnoreCase);

            if (trained)
            {
                var sessionRpe = Math.Clamp(
                    DefaultRpe(workoutType.WeeklyRoleCode)
                    * RandomBetween(random, 0.90m, 1.10m),
                    2m,
                    10m);

                var duration = Math.Max(
                    10m,
                    workoutType.DefaultMinutes
                    * RandomBetween(random, 0.80m, 1.20m));

                var recipe =
                    SystemWorkoutRecipeCatalog
                        .GetDefaultPresentationForEngine(
                            workoutType.WorkoutTypeCode);

                var session = new WorkoutSession
                {
                    WorkoutSessionId = Guid.NewGuid(),
                    ClientId = client.ClientId,
                    ProgramId = program.ProgramId,
                    WorkoutDate = date,
                    WorkoutName = recipe?.DisplayName ?? workoutType.DisplayName,
                    SystemWorkoutCode = recipe?.SystemCode,
                    WorkoutTypeCode = workoutType.WorkoutTypeCode,
                    DurationMin = Round1(duration),
                    TypicalRestSec = workoutType.DefaultRestSec,
                    SessionRpe = Round1(sessionRpe),
                    EntrySourceCode = "TEST_PERSONA_SEED",
                    SourceRecordId = $"TESTPERSONA:{spec.Email}:WORKOUT:{date:yyyy-MM-dd}",
                    Notes = "Synthetic completed workout generated from the Weekly Plan.",
                    CreatedAtUtc = now.AddDays(date.DayNumber - today.DayNumber),
                    UpdatedAtUtc = now.AddDays(date.DayNumber - today.DayNumber)
                };

                workoutActuals.ApplyCurrentEngineState(
                    session,
                    completed: true);

                db.WorkoutSessions.Add(session);

                if (recipe is not null && recipe.Slots.Count > 0)
                {
                    detailedSeeds.Add(
                        (
                            session,
                            recipe,
                            StableSeed(
                                spec.Email + ":DETAIL",
                                date)));
                }
            }

            // Persist the day's canonical actuals before asking Engine 3.0 for
            // the prescription used to synthesize nutrition behavior.
            await db.SaveChangesAsync(cancellationToken);

            var projectedWeek =
                await engine30Projection.BuildPlanningWeekAsync(
                    engineContext,
                    PlanningWeek.Start(date),
                    spec.PlanCode,
                    cancellationToken);

            var engineDay =
                projectedWeek[date];

            var targets = new
            {
                Calories = engineDay.PrescribedEnergyKcal,
                ProteinG = engineDay.ProteinG,
                CarbsG = engineDay.RecommendedCarbsG,
                FatG = engineDay.FatG
            };

            var protein = Round1(
                targets.ProteinG * RandomBetween(random, 0.80m, 1.20m));

            var carbs = Round1(
                targets.CarbsG * RandomBetween(random, 0.80m, 1.20m));

            var fat = Round1(
                targets.FatG * RandomBetween(random, 0.80m, 1.20m));

            var calories = Math.Round(
                protein * 4m + carbs * 4m + fat * 9m,
                0,
                MidpointRounding.AwayFromZero);

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
                    FiberG = Round1(RandomBetween(random, 22m, 38m)),
                    WaterOz = Round1(RandomBetween(random, 70m, 120m)),
                    EntrySourceCode = "TEST_PERSONA_SEED",
                    SourceRecordId = $"TESTPERSONA:{spec.Email}:NUTRITION:{date:yyyy-MM-dd}",
                    Notes = "Synthetic nutrition actual randomized within +/-20% of the Engine 3.0 prescription.",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

            await db.SaveChangesAsync(cancellationToken);

            var targetDailyWeightChange =
                spec.TargetWeeklyWeightFraction
                * trendWeight
                / 7m;

            var calorieDeviation =
                (calories - targets.Calories) / 3500m;

            var dailyWeightChange =
                targetDailyWeightChange + calorieDeviation;

            var proteinRatio =
                targets.ProteinG <= 0m
                    ? 1m
                    : Math.Clamp(protein / targets.ProteinG, 0.80m, 1.20m);

            var dailyMuscleGain =
                spec.TargetWeeklyMuscleGainLb
                / 7m
                * proteinRatio
                * (trained ? 1.05m : 0.85m);

            skeletalMuscle += dailyMuscleGain;

            if (dailyWeightChange < 0m)
                fatMass += dailyWeightChange * 0.80m;
            else
                fatMass += dailyWeightChange * spec.GainFatFraction;

            fatMass = Math.Max(8m, fatMass);
            trendWeight = Math.Max(90m, trendWeight + dailyWeightChange);
        }

        // Today's morning scale/check-in are present so Dashboard analysis can
        // compare yesterday's behavior against a real current-day response.
        var todayRandom = new Random(StableSeed(spec.Email, today));
        AddMeasurement(
            client,
            program,
            spec,
            today,
            trendWeight + RandomBetween(todayRandom, -0.25m, 0.25m),
            Math.Clamp(fatMass / Math.Max(1m, trendWeight), 0.08m, 0.50m),
            skeletalMuscle,
            startFatMass,
            fatMass,
            now);

        db.DailyCheckins.Add(
            BuildCheckin(
                client,
                program,
                spec,
                today,
                todayRandom,
                now));

        await db.SaveChangesAsync(cancellationToken);

        foreach (var item in detailedSeeds)
        {
            var detailed =
                await BuildDetailedWorkoutAsync(
                    spec,
                    item.Session,
                    item.Recipe,
                    item.Seed,
                    cancellationToken);

            if (detailed.Exercises.Count > 0)
            {
                await detailedWorkoutStore.ReplaceAsync(
                    detailed,
                    cancellationToken);
            }
        }
    }

    private async Task<DetailedWorkoutSession> BuildDetailedWorkoutAsync(
        TestPersonaSpec spec,
        WorkoutSession session,
        SystemWorkoutRecipe recipe,
        int seed,
        CancellationToken cancellationToken)
    {
        var random = new Random(seed);
        var exercises = new List<DetailedWorkoutExerciseEntry>();
        var used = new HashSet<Guid>();
        var progression =
            1m + Math.Max(0, session.WorkoutDate.DayNumber - DateOnly.FromDateTime(session.CreatedAtUtc).DayNumber) * 0.001m;

        foreach (var slot in recipe.Slots.Take(6))
        {
            ExerciseCatalogSearchItem? selected = null;

            foreach (var term in slot.PreferredNameTerms)
            {
                var matches = await exerciseCatalog.SearchAsync(
                    term,
                    12,
                    cancellationToken);

                selected = matches.FirstOrDefault(x =>
                    !used.Contains(x.ExerciseId)
                    && x.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                    ?? matches.FirstOrDefault(x => !used.Contains(x.ExerciseId));

                if (selected is not null)
                    break;
            }

            if (selected is null)
                continue;

            used.Add(selected.ExerciseId);

            var loadFactor = slot.PreferCompound
                ? 0.45m
                : slot.PreferIsolation
                    ? 0.16m
                    : 0.28m;

            var baseLoad = RoundTo5(
                spec.StartingWeightLb
                * loadFactor
                * progression
                * RandomBetween(random, 0.85m, 1.15m));

            var sets = new List<DetailedWorkoutSetEntry>();
            for (var setIndex = 0; setIndex < slot.Sets; setIndex++)
            {
                var reps = Math.Max(
                    1,
                    (int)Math.Round(
                        slot.Reps * (double)RandomBetween(random, 0.90m, 1.10m),
                        MidpointRounding.AwayFromZero));

                sets.Add(
                    new DetailedWorkoutSetEntry(
                        DetailedSetId: Guid.NewGuid(),
                        SetOrder: setIndex + 1,
                        Reps: reps,
                        Load: baseLoad,
                        LoadUnitCode: "LB",
                        Rpe: session.SessionRpe,
                        Rir: session.SessionRpe.HasValue
                            ? Math.Max(0m, 10m - session.SessionRpe.Value)
                            : null,
                        RestSeconds: session.TypicalRestSec,
                        DurationSeconds: null,
                        IsWarmup: false,
                        Notes: "Synthetic TEST set"));
            }

            exercises.Add(
                new DetailedWorkoutExerciseEntry(
                    DetailedExerciseId: Guid.NewGuid(),
                    ExerciseId: selected.ExerciseId,
                    ExerciseOrder: exercises.Count + 1,
                    ExerciseNameSnapshot: selected.Name,
                    MechanicsSnapshot: selected.Mechanics,
                    Notes: "Synthetic TEST exercise",
                    Sets: sets,
                    MaxWeight: baseLoad,
                    LoadUnitCode: "LB"));
        }

        return new DetailedWorkoutSession(
            session.WorkoutSessionId,
            exercises);
    }

    private static DailyCheckin BuildCheckin(
        Client client,
        ProgramRecord program,
        TestPersonaSpec spec,
        DateOnly date,
        Random random,
        DateTime now)
        => new()
        {
            CheckinId = Guid.NewGuid(),
            ClientId = client.ClientId,
            ProgramId = program.ProgramId,
            CheckinDate = date,
            SleepHours = Round1(Math.Clamp(
                spec.BaselineSleepHours + RandomBetween(random, -0.8m, 0.8m),
                5.5m,
                9.0m)),
            RestingHr = (short)Math.Clamp(
                (spec.SexCode == "MALE" ? 58 : 62) + random.Next(-5, 6),
                45,
                85),
            Steps = Math.Max(2500, spec.BaselineStepsDay + random.Next(-2200, 3000)),
            DailyActivityHours = Round1(RandomBetween(random, 0.5m, 2.5m)),
            DailyActivityEffortCode = "MODERATE",
            EnergyScore = (byte)random.Next(6, 10),
            SorenessScore = (byte)random.Next(2, 7),
            PainFlag = false,
            IllnessFlag = false,
            Notes = "Synthetic TEST daily check-in.",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

    private void AddMeasurement(
        Client client,
        ProgramRecord program,
        TestPersonaSpec spec,
        DateOnly date,
        decimal weight,
        decimal bodyFatFraction,
        decimal skeletalMuscle,
        decimal startFatMass,
        decimal fatMass,
        DateTime now)
    {
        var fatChange = fatMass - startFatMass;
        var waist = Math.Max(
            22m,
            spec.StartingWaistIn + fatChange * 0.12m);

        var waterFraction = spec.SexCode == "MALE" ? 0.55m : 0.52m;

        db.Measurements.Add(
            new Measurement
            {
                MeasurementId = Guid.NewGuid(),
                ClientId = client.ClientId,
                ProgramId = program.ProgramId,
                MeasurementAtUtc = now.AddDays(date.DayNumber - PacificToday(now).DayNumber),
                LocalMeasurementDate = date,
                WeightLb = Round1(weight),
                BodyFatPct = Math.Round(bodyFatFraction, 4),
                SkeletalMuscleMassLb = Round1(skeletalMuscle),
                MuscleMassLb = Round1(skeletalMuscle * 1.28m),
                BodyWaterLb = Round1(weight * waterFraction),
                VisceralFatRating = Math.Max(
                    1m,
                    Math.Round(
                        spec.StartingVisceralFat + fatChange / 5m,
                        1,
                        MidpointRounding.AwayFromZero)),
                SubcutaneousFatPct = Math.Round(
                    Math.Max(0.05m, bodyFatFraction * 0.82m),
                    4),
                WaistIn = Round1(waist),
                HipsIn = spec.SexCode == "FEMALE"
                    ? Round1(waist + 8m)
                    : Round1(waist + 3m),
                MeasurementSourceCode = "TEST_PERSONA_SEED",
                SourceRecordId = $"TESTPERSONA:{spec.Email}:MEASUREMENT:{date:yyyy-MM-dd}",
                Notes = "Synthetic modeled scale trend based on plan direction and nutrition variance.",
                CreatedAtUtc = now
            });
    }

    private static decimal DefaultRpe(string weeklyRoleCode)
        => weeklyRoleCode.ToUpperInvariant() switch
        {
            "HEAVY" or "HEAVY_GENERIC" => 8.0m,
            "CONDITIONING" => 8.0m,
            "LIGHT_STRENGTH" => 7.0m,
            "RECOVERY_CARDIO" => 4.5m,
            _ => 6.5m
        };

    private static DateOnly PacificToday(DateTime utcNow)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
            return DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone));
        }
        catch
        {
            return DateOnly.FromDateTime(utcNow);
        }
    }

    private static int StableSeed(string value, DateOnly date)
    {
        unchecked
        {
            var seed = 17;
            foreach (var ch in value.ToLowerInvariant())
                seed = seed * 31 + ch;
            seed = seed * 31 + date.DayNumber;
            return seed;
        }
    }

    private static decimal RandomBetween(Random random, decimal min, decimal max)
        => min + (max - min) * (decimal)random.NextDouble();

    private static decimal Round1(decimal value)
        => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static decimal RoundTo5(decimal value)
        => Math.Max(
            5m,
            Math.Round(value / 5m, 0, MidpointRounding.AwayFromZero) * 5m);
}

public sealed class TestPersonaSeederHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<TestPersonaSeederHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>(
                "WebRecomp:TestPersonaAccountsEnabled"))
        {
            return;
        }

        var siteName =
            Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")
            ?? string.Empty;

        var isTest =
            environment.IsDevelopment()
            || string.Equals(
                siteName,
                "recomp-test",
                StringComparison.OrdinalIgnoreCase)
            || siteName.Contains(
                "recomp-test",
                StringComparison.OrdinalIgnoreCase);

        if (!isTest)
        {
            logger.LogWarning(
                "TEST persona seeding was configured but blocked because this is not recomp-test.");
            return;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var seeder = scope.ServiceProvider
                .GetRequiredService<TestPersonaSeederService>();

            await seeder.SeedAllAsync(cancellationToken);
            logger.LogInformation("TEST persona seed check completed.");
        }
        catch (Exception ex)
        {
            // TEST personas must never stop the application from starting.
            logger.LogError(ex, "TEST persona seeding failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
