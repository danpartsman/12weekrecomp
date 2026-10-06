using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services.Learning;

public static class LearningSurfaceCodes
{
    public const string Dashboard = "DASHBOARD";
    public const string Nutrition = "NUTRITION";
    public const string Workout = "WORKOUT";
}

public static class LearningInteractionCodes
{
    public const string Open = "OPEN";
    public const string Complete = "COMPLETE";
    public const string Dismiss = "DISMISS";
    public const string LinkClick = "LINK_CLICK";
}

internal static class LearningSchemaReadiness
{
    private static int _isReady;

    public static bool IsReady
    {
        get => Volatile.Read(ref _isReady) == 1;
        set => Interlocked.Exchange(ref _isReady, value ? 1 : 0);
    }
}

public sealed class LearningSchemaHostedService(
    IServiceProvider services,
    ILogger<LearningSchemaHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope =
            services.CreateAsyncScope();

        var db =
            scope.ServiceProvider.GetRequiredService<RecompDbContext>();

        var connection =
            db.Database.GetDbConnection();

        var closeWhenDone =
            connection.State != System.Data.ConnectionState.Open;

        if (closeWhenDone)
            await connection.OpenAsync(cancellationToken);

        try
        {
            if (!await IsReadyAsync(connection, cancellationToken))
            {
                try
                {
                    await using var command =
                        connection.CreateCommand();

                    command.CommandText =
                        """
                        SET XACT_ABORT ON;

                        IF OBJECT_ID('app.LearningCoachPreferences', 'U') IS NULL
                        BEGIN
                            CREATE TABLE app.LearningCoachPreferences
                            (
                                user_id uniqueidentifier NOT NULL,
                                preferred_coach_code varchar(30) NOT NULL
                                    CONSTRAINT DF_LearningCoachPreferences_coach DEFAULT ('MARISOL'),
                                created_at_utc datetime2 NOT NULL,
                                updated_at_utc datetime2 NOT NULL,
                                CONSTRAINT PK_LearningCoachPreferences PRIMARY KEY (user_id),
                                CONSTRAINT FK_LearningCoachPreferences_UserAccounts
                                    FOREIGN KEY (user_id) REFERENCES app.UserAccounts(user_id)
                                    ON DELETE CASCADE
                            );
                        END;

                        IF OBJECT_ID('app.LearningSeriesProgress', 'U') IS NULL
                        BEGIN
                            CREATE TABLE app.LearningSeriesProgress
                            (
                                learning_series_progress_id uniqueidentifier NOT NULL,
                                user_id uniqueidentifier NOT NULL,
                                series_code varchar(60) NOT NULL,
                                current_module_number int NOT NULL
                                    CONSTRAINT DF_LearningSeriesProgress_current DEFAULT (1),
                                highest_completed_module_number int NOT NULL
                                    CONSTRAINT DF_LearningSeriesProgress_highest DEFAULT (0),
                                status_code varchar(20) NOT NULL
                                    CONSTRAINT DF_LearningSeriesProgress_status DEFAULT ('ACTIVE'),
                                last_shown_at_utc datetime2 NULL,
                                last_completed_at_utc datetime2 NULL,
                                created_at_utc datetime2 NOT NULL,
                                updated_at_utc datetime2 NOT NULL,
                                CONSTRAINT PK_LearningSeriesProgress PRIMARY KEY (learning_series_progress_id),
                                CONSTRAINT FK_LearningSeriesProgress_UserAccounts
                                    FOREIGN KEY (user_id)
                                    REFERENCES app.UserAccounts(user_id)
                                    ON DELETE CASCADE,
                                CONSTRAINT UQ_LearningSeriesProgress_UserSeries
                                    UNIQUE (user_id, series_code)
                            );
                        END;

                        IF OBJECT_ID('app.LearningInteractions', 'U') IS NULL
                        BEGIN
                            CREATE TABLE app.LearningInteractions
                            (
                                learning_interaction_id uniqueidentifier NOT NULL,
                                user_id uniqueidentifier NOT NULL,
                                content_code varchar(100) NOT NULL,
                                series_code varchar(60) NOT NULL,
                                surface_code varchar(30) NOT NULL,
                                interaction_code varchar(30) NOT NULL,
                                module_number int NOT NULL,
                                target_url nvarchar(1000) NULL,
                                occurred_at_utc datetime2 NOT NULL,
                                CONSTRAINT PK_LearningInteractions PRIMARY KEY (learning_interaction_id),
                                CONSTRAINT FK_LearningInteractions_UserAccounts
                                    FOREIGN KEY (user_id)
                                    REFERENCES app.UserAccounts(user_id)
                                    ON DELETE CASCADE
                            );

                            CREATE INDEX IX_LearningInteractions_UserOccurred
                                ON app.LearningInteractions(user_id, occurred_at_utc);
                        END;
                        """;

                    await command.ExecuteNonQueryAsync(
                        cancellationToken);
                }
                catch (System.Data.Common.DbException exception)
                {
                    logger.LogWarning(
                        exception,
                        "v1015c learning schema is missing and the application identity could not provision it.");
                }
            }

            LearningSchemaReadiness.IsReady =
                await IsReadyAsync(
                    connection,
                    cancellationToken);

            if (LearningSchemaReadiness.IsReady)
            {
                logger.LogInformation(
                    "v1015c learning schema is ready.");
            }
            else
            {
                logger.LogWarning(
                    "v1015c learning schema is not ready; coach learning surfaces will stay disabled.");
            }
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    private static async Task<bool> IsReadyAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT CASE
                WHEN OBJECT_ID('app.LearningCoachPreferences', 'U') IS NULL THEN 0
                WHEN OBJECT_ID('app.LearningSeriesProgress', 'U') IS NULL THEN 0
                WHEN OBJECT_ID('app.LearningInteractions', 'U') IS NULL THEN 0
                ELSE 1
            END;
            """;

        var scalar =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return scalar is not null
            && scalar is not DBNull
            && Convert.ToInt32(scalar) == 1;
    }
}

public sealed record CoachProfile(
    string Code,
    string Name,
    string Tagline,
    string Style,
    string AccentClass,
    IReadOnlyList<string?> GalleryImages);

public sealed record LearningLesson(
    string ContentCode,
    string SeriesCode,
    string SeriesTitle,
    int ModuleNumber,
    int TotalModules,
    string SurfaceCode,
    string Title,
    string Why,
    string Body,
    string? Takeaway = null);

public sealed record LearningPresentation(
    CoachProfile Coach,
    LearningLesson Lesson,
    string ImagePath,
    string DisplayMode);

/// <summary>
/// Curated coach identities. v1015c reserves five image roles per coach so
/// asset creation can evolve independently from the learning mechanics.
/// Missing slots gracefully fall back to the coach's first available image.
/// </summary>
public static class CoachCatalog
{
    private static readonly IReadOnlyDictionary<string, CoachProfile> Coaches =
        new[]
        {
            new CoachProfile(
                "MARISOL",
                "Marisol",
                "Strong, warm, and consistent.",
                "Supportive strength coach",
                "coach-marisol",
                new string?[]
                {
                    "/images/coach/marisol-coach-avatar.webp",
                    "/images/coach/marisol-full-body-strength-guide.webp",
                    "/images/coach/marisol-major-push-guide.webp",
                    "/images/coach/marisol-major-pull-guide.webp",
                    "/images/coach/marisol-orangetheory-guide.webp"
                }),
            new CoachProfile(
                "CASSIE",
                "Cassie",
                "Energetic, athletic, and performance-minded.",
                "Conditioning and performance coach",
                "coach-cassie",
                new string?[]
                {
                    "/images/coach/cassie-coach-avatar.webp",
                    "/images/coach/cassie-zone2-cardio-guide.webp",
                    "/images/coach/cassie-running-intervals-guide.webp",
                    "/images/coach/cassie-true-hiit-guide.webp",
                    "/images/coach/cassie-mixed-conditioning-guide.webp"
                }),
            new CoachProfile(
                "MARCUS",
                "Marcus",
                "Calm, technical, and methodical.",
                "Technique and progression coach",
                "coach-marcus",
                new string?[]
                {
                    null, null, null, null, null
                }),
            new CoachProfile(
                "NINA",
                "Nina",
                "Direct, focused, and disciplined.",
                "Consistency and habit coach",
                "coach-nina",
                new string?[]
                {
                    null, null, null, null, null
                })
        }
        .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<CoachProfile> All =>
        Coaches.Values.ToArray();

    public static CoachProfile Resolve(string? code)
        => !string.IsNullOrWhiteSpace(code)
           && Coaches.TryGetValue(code.Trim(), out var coach)
            ? coach
            : Coaches["MARISOL"];

    public static bool IsValid(string? code)
        => !string.IsNullOrWhiteSpace(code)
           && Coaches.ContainsKey(code.Trim());

    public static string ResolveImage(
        CoachProfile coach,
        int moduleNumber)
    {
        var available =
            coach.GalleryImages
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToArray();

        if (available.Length == 0)
            return "";

        var index =
            Math.Max(0, moduleNumber - 1)
            % available.Length;

        return available[index];
    }
}

/// <summary>
/// High-school-readable teaching content. These are explanations, not engine
/// coefficients. The engine decides what to recommend; Learning explains why.
/// </summary>
public static class LearningContentCatalog
{
    private static readonly IReadOnlyList<LearningLesson> Lessons =
    [
        new("AE-01","AEROBIC_ENERGY","Aerobic Energy",1,8,LearningSurfaceCodes.Dashboard,
            "Your muscles need ATP","Why this matters",
            "Every muscle contraction spends ATP. Your body only stores a tiny amount, so it has to rebuild ATP continuously while you move.",
            "ATP is the spendable energy; the fuel systems are how you keep making it."),
        new("AE-02","AEROBIC_ENERGY","Aerobic Energy",2,8,LearningSurfaceCodes.Dashboard,
            "Mitochondria are energy factories","The why",
            "Mitochondria use oxygen to turn fat and carbohydrate into usable energy. Easy aerobic work gives them repeated demand without overwhelming the system.",
            "More capable mitochondria make the same pace cost less effort."),
        new("AE-03","AEROBIC_ENERGY","Aerobic Energy",3,8,LearningSurfaceCodes.Dashboard,
            "What Zone 2 is training","The why",
            "Zone 2 is not magic because of a single heart-rate number. It is useful because the effort is high enough to demand sustained aerobic ATP production while still being low enough to repeat for a long time.",
            "The adaptation comes from sustained aerobic demand, not from suffering."),
        new("AE-04","AEROBIC_ENERGY","Aerobic Energy",4,8,LearningSurfaceCodes.Dashboard,
            "Fuel is always a mixture","The why",
            "Muscle does not flip between fat and carbohydrate like a light switch. Both contribute. As intensity rises, carbohydrate usually supplies more of the energy because it can support ATP production faster.",
            "Intensity changes the fuel mix."),
        new("AE-05","AEROBIC_ENERGY","Aerobic Energy",5,8,LearningSurfaceCodes.Dashboard,
            "Long workouts change the fuel mix","The why",
            "As prolonged exercise continues, muscle glycogen contribution can fall while circulating fuel and fat make a larger contribution. That is why 12WR does not multiply the first ten minutes of glycogen use across a two-hour workout.",
            "Duration changes substrate use."),
        new("AE-06","AEROBIC_ENERGY","Aerobic Energy",6,8,LearningSurfaceCodes.Dashboard,
            "Intervals train a different problem","The why",
            "Hard intervals demand ATP faster. Glycolysis and phosphagen energy become much more important, while recovery periods let aerobic metabolism catch up.",
            "Hard intervals and Zone 2 are complementary tools."),
        new("AE-07","AEROBIC_ENERGY","Aerobic Energy",7,8,LearningSurfaceCodes.Dashboard,
            "VO2 max is a whole-system outcome","The why",
            "Your lungs, heart, blood vessels, mitochondria and working muscles all contribute to aerobic performance. Training can improve several links in that chain.",
            "Aerobic fitness is not only about the lungs."),
        new("AE-08","AEROBIC_ENERGY","Aerobic Energy",8,8,LearningSurfaceCodes.Dashboard,
            "How 12WR uses this","The why",
            "12WR separates easy aerobic work, hard conditioning and resistance training because they create different fuel demands and adaptations. The goal is enough stimulus to improve you without spending recovery where it does not help.",
            "Different training has different jobs."),

        new("NF-01","FOOD_AND_FUEL","Food & Fuel",1,8,LearningSurfaceCodes.Nutrition,
            "Calories and nutrients are different questions","The why",
            "Calories describe energy. Protein, carbohydrate, fat, fiber, vitamins and minerals describe what that food can do for recovery, muscle, hormones, digestion and health.",
            "Energy matters, but food quality still matters."),
        new("NF-02","FOOD_AND_FUEL","Food & Fuel",2,8,LearningSurfaceCodes.Nutrition,
            "Protein is raw material","The why",
            "Resistance training creates a reason to remodel muscle. Dietary protein supplies amino acids used to repair and build that tissue.",
            "Training is the signal; protein supplies building material."),
        new("NF-03","FOOD_AND_FUEL","Food & Fuel",3,8,LearningSurfaceCodes.Nutrition,
            "Carbohydrate is not just workout calories","The why",
            "Carbohydrate can support blood glucose and replenish glycogen, but not every calorie burned in exercise came from glycogen. 12WR separates the fuel sources before deciding how much carbohydrate recovery actually needs.",
            "Burned calories are not automatically carbs to eat back."),
        new("NF-04","FOOD_AND_FUEL","Food & Fuel",4,8,LearningSurfaceCodes.Nutrition,
            "Fat has structural jobs","The why",
            "Dietary fat is energy-dense, but it also supplies essential fatty acids and supports cell membranes and hormone production. Cutting fat extremely low is not a free way to create a deficit.",
            "Fat is more than stored energy."),
        new("NF-05","FOOD_AND_FUEL","Food & Fuel",5,8,LearningSurfaceCodes.Nutrition,
            "Fiber changes the meal","The why",
            "Fiber can increase fullness, support bowel function and change how quickly carbohydrate reaches the bloodstream. Whole-food carbohydrate often behaves differently from highly refined carbohydrate.",
            "Food structure matters."),
        new("NF-06","FOOD_AND_FUEL","Food & Fuel",6,8,LearningSurfaceCodes.Nutrition,
            "Recovery happens over time","The why",
            "A workout does not create a midnight deadline to replace every gram of glycogen. Recovery continues across meals and across the following day.",
            "Feed the recovery process; do not blindly eat back a workout."),
        new("NF-07","FOOD_AND_FUEL","Food & Fuel",7,8,LearningSurfaceCodes.Nutrition,
            "Logging is an estimate","The why",
            "Food labels, restaurant portions, cooking losses and serving estimates all contain error. Trends in body weight, waist and performance help tell us whether the estimate is working.",
            "Outcome data calibrates the plan."),
        new("NF-08","FOOD_AND_FUEL","Food & Fuel",8,8,LearningSurfaceCodes.Nutrition,
            "The goal is a repeatable diet","The why",
            "The best nutrition plan is not the one with the most rules. It is the one that supplies enough protein and micronutrients, supports training, creates the intended energy balance and can be followed consistently.",
            "Consistency beats theoretical perfection."),

        new("TR-01","TRAINING_ADAPTATION","Training Adaptation",1,8,LearningSurfaceCodes.Workout,
            "Exercise is a signal","The why",
            "A workout does not build muscle while you lift. It creates mechanical and metabolic signals that tell the body it needs to adapt during recovery.",
            "Training starts the adaptation; recovery builds it."),
        new("TR-02","TRAINING_ADAPTATION","Training Adaptation",2,8,LearningSurfaceCodes.Workout,
            "Hard enough is different from exhausted","The why",
            "A set can stimulate muscle without turning the whole workout into a maximal test. Repeated high-quality hard sets are usually more useful than chasing fatigue for its own sake.",
            "Stimulus matters more than theatrics."),
        new("TR-03","TRAINING_ADAPTATION","Training Adaptation",3,8,LearningSurfaceCodes.Workout,
            "Progressive overload is evidence","The why",
            "If you can gradually perform more useful work with good technique, that is evidence your body has adapted. More weight is one option; reps, control and range of motion also count.",
            "Progress has more than one measurement."),
        new("TR-04","TRAINING_ADAPTATION","Training Adaptation",4,8,LearningSurfaceCodes.Workout,
            "Muscle glycogen is regional","The why",
            "A hard leg workout mainly challenges glycogen in recruited leg muscles. It does not empty every muscle in your body equally. 12WR tracks regional workload so a second workout does not magically start with a fresh tank.",
            "The muscle that worked is the muscle that paid the bill."),
        new("TR-05","TRAINING_ADAPTATION","Training Adaptation",5,8,LearningSurfaceCodes.Workout,
            "Rest between sets has a job","The why",
            "Rest lets phosphocreatine recover and helps you produce more force on the next set. Short rest can be useful, but it changes the kind of limitation you are training against.",
            "Rest is part of the workout prescription."),
        new("TR-06","TRAINING_ADAPTATION","Training Adaptation",6,8,LearningSurfaceCodes.Workout,
            "Creatine supports rapid ATP recycling","The why",
            "Creatine increases the muscle's phosphocreatine pool. That can improve repeated high-intensity performance and, over time, help support greater training volume and strength gains.",
            "Creatine helps the energy system that bridges repeated hard efforts."),
        new("TR-07","TRAINING_ADAPTATION","Training Adaptation",7,8,LearningSurfaceCodes.Workout,
            "Mobility is usable range","The why",
            "Mobility is not simply being flexible. It is having enough range of motion, strength and control to move well in the positions your life and training require.",
            "Useful movement combines range and control."),
        new("TR-08","TRAINING_ADAPTATION","Training Adaptation",8,8,LearningSurfaceCodes.Workout,
            "Recovery is part of programming","The why",
            "Training creates fatigue and adaptation at the same time. Good programming gives you enough stimulus to improve while managing fatigue so the next useful session can still be productive.",
            "More training is not automatically more progress.")
    ];

    public static IReadOnlyList<LearningLesson> All => Lessons;

    public static IReadOnlyList<LearningLesson> ForSurface(string surfaceCode)
        => Lessons
            .Where(x => x.SurfaceCode.Equals(surfaceCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.SeriesCode)
            .ThenBy(x => x.ModuleNumber)
            .ToArray();

    public static LearningLesson? Find(string contentCode)
        => Lessons.FirstOrDefault(
            x => x.ContentCode.Equals(contentCode, StringComparison.OrdinalIgnoreCase));
}

public sealed class LearningOrchestratorService(
    RecompDbContext db)
{
    public async Task<string> GetPreferredCoachCodeAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!LearningSchemaReadiness.IsReady)
            return "MARISOL";

        var preference =
            await db.LearningCoachPreferences
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.UserId == userId,
                    cancellationToken);

        return CoachCatalog.Resolve(
            preference?.PreferredCoachCode).Code;
    }

    public async Task SetPreferredCoachAsync(
        Guid userId,
        string coachCode,
        CancellationToken cancellationToken = default)
    {
        if (!LearningSchemaReadiness.IsReady)
            throw new InvalidOperationException(
                "Learning preferences are temporarily unavailable.");

        var resolved =
            CoachCatalog.Resolve(coachCode);

        var now =
            DateTime.UtcNow;

        var preference =
            await db.LearningCoachPreferences
                .SingleOrDefaultAsync(
                    x => x.UserId == userId,
                    cancellationToken);

        if (preference is null)
        {
            preference =
                new LearningCoachPreference
                {
                    UserId = userId,
                    PreferredCoachCode = resolved.Code,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            db.LearningCoachPreferences.Add(
                preference);
        }
        else
        {
            preference.PreferredCoachCode =
                resolved.Code;

            preference.UpdatedAtUtc =
                now;
        }

        await db.SaveChangesAsync(
            cancellationToken);
    }

    public async Task<LearningPresentation?> ResolveAsync(
        UserAccount user,
        string surfaceCode,
        string displayMode,
        CancellationToken cancellationToken = default)
    {
        if (!LearningSchemaReadiness.IsReady)
            return null;

        var candidates =
            LearningContentCatalog.ForSurface(surfaceCode);

        if (candidates.Count == 0)
            return null;

        var seriesCode =
            candidates[0].SeriesCode;

        var progress =
            await db.LearningSeriesProgress
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.UserId == user.UserId
                        && x.SeriesCode == seriesCode,
                    cancellationToken);

        var module =
            progress?.CurrentModuleNumber ?? 1;

        var lesson =
            candidates.FirstOrDefault(x => x.ModuleNumber == module)
            ?? candidates.First();

        var coach =
            CoachCatalog.Resolve(
                await GetPreferredCoachCodeAsync(
                    user.UserId,
                    cancellationToken));

        return new LearningPresentation(
            coach,
            lesson,
            CoachCatalog.ResolveImage(coach, lesson.ModuleNumber),
            displayMode);
    }

    public async Task RecordInteractionAsync(
        UserAccount user,
        string contentCode,
        string surfaceCode,
        string interactionCode,
        string? targetUrl,
        CancellationToken cancellationToken = default)
    {
        if (!LearningSchemaReadiness.IsReady)
            return;

        var lesson =
            LearningContentCatalog.Find(contentCode)
            ?? throw new InvalidOperationException("Learning content was not found.");

        var now =
            DateTime.UtcNow;

        db.LearningInteractions.Add(
            new LearningInteraction
            {
                LearningInteractionId = Guid.NewGuid(),
                UserId = user.UserId,
                ContentCode = lesson.ContentCode,
                SeriesCode = lesson.SeriesCode,
                SurfaceCode = surfaceCode.Trim().ToUpperInvariant(),
                InteractionCode = interactionCode.Trim().ToUpperInvariant(),
                ModuleNumber = lesson.ModuleNumber,
                TargetUrl = string.IsNullOrWhiteSpace(targetUrl)
                    ? null
                    : targetUrl.Trim(),
                OccurredAtUtc = now
            });

        if (interactionCode.Equals(
                LearningInteractionCodes.Complete,
                StringComparison.OrdinalIgnoreCase))
        {
            var progress =
                await db.LearningSeriesProgress
                    .SingleOrDefaultAsync(
                        x =>
                            x.UserId == user.UserId
                            && x.SeriesCode == lesson.SeriesCode,
                        cancellationToken);

            if (progress is null)
            {
                progress =
                    new LearningSeriesProgress
                    {
                        LearningSeriesProgressId = Guid.NewGuid(),
                        UserId = user.UserId,
                        SeriesCode = lesson.SeriesCode,
                        CreatedAtUtc = now
                    };

                db.LearningSeriesProgress.Add(progress);
            }

            progress.HighestCompletedModuleNumber =
                Math.Max(
                    progress.HighestCompletedModuleNumber,
                    lesson.ModuleNumber);

            progress.CurrentModuleNumber =
                lesson.ModuleNumber >= lesson.TotalModules
                    ? 1
                    : lesson.ModuleNumber + 1;

            progress.StatusCode =
                lesson.ModuleNumber >= lesson.TotalModules
                    ? "COMPLETED"
                    : "ACTIVE";

            progress.LastCompletedAtUtc = now;
            progress.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
