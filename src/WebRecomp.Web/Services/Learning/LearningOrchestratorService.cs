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
                    "/images/coach/cassie-avatar-v1015c.png",
                    "/images/coach/cassie-zone2-cardio-v1015c.png",
                    "/images/coach/cassie-running-intervals-v1015c.png",
                    "/images/coach/cassie-true-hiit-v1015c.png",
                    "/images/coach/cassie-mixed-conditioning-v1015c.png"
                }),
            new CoachProfile(
                "MARCUS",
                "Marcus",
                "Calm, technical, and methodical.",
                "Technique and progression coach",
                "coach-marcus",
                new string?[]
                {
                    "/images/coach/marcus-avatar-v1015c.png",
                    "/images/coach/marcus-technique-v1015c.png",
                    "/images/coach/marcus-strength-v1015c.png",
                    "/images/coach/marcus-progression-v1015c.png",
                    "/images/coach/marcus-recovery-v1015c.png"
                }),
            new CoachProfile(
                "NINA",
                "Nina",
                "Direct, focused, and disciplined.",
                "Consistency and habit coach",
                "coach-nina",
                new string?[]
                {
                    "/images/coach/nina-avatar-v1015c.png",
                    "/images/coach/nina-technique-v1015c.png",
                    "/images/coach/nina-strength-v1015c.png",
                    "/images/coach/nina-discipline-v1015c.png",
                    "/images/coach/nina-recovery-v1015c.png"
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
            "ATP (adenosine triphosphate) is the small energy-carrying molecule your cells use to do work. Your muscles break down ATP to power every contraction, from walking to lifting a weight. Because muscles store only a small amount, your body continually rebuilds it using phosphocreatine, carbohydrate, and fat, with oxygen supporting sustained energy production. Eating enough to fuel your planned activity, resting between hard efforts, and building aerobic fitness help keep that ATP supply flowing.",
            "Fuel and train the systems that continuously rebuild ATP."),
        new("AE-02","AEROBIC_ENERGY","Aerobic Energy",2,8,LearningSurfaceCodes.Dashboard,
            "Mitochondria are energy factories","The why",
            "Mitochondria are tiny structures inside your cells that help turn energy from food into ATP, the molecule that powers muscle contraction. They use oxygen to process energy from carbohydrate and fat, supplying much of the ATP needed during sustained activity. Developing their capacity helps your muscles keep working and can make a familiar walking, cycling, or running pace feel easier. Regular Zone 2 training, a steady aerobic effort where you can still comfortably speak in full sentences, gives this energy system repeated practice and helps build its capacity over time.",
            "Zone 2 gives your aerobic energy system regular practice."),
        new("AE-03","AEROBIC_ENERGY","Aerobic Energy",3,8,LearningSurfaceCodes.Dashboard,
            "What Zone 2 is training","The why",
            "Zone 2 is a steady aerobic effort where breathing is a little faster but you can still comfortably speak in full sentences. It trains your muscles' mitochondria to supply ATP using oxygen while also challenging the heart and circulation that deliver that oxygen. This matters because a stronger aerobic system helps you sustain activity and recover between harder efforts. Use walking, cycling, or jogging at a conversational pace, repeat manageable sessions consistently, and gradually build duration rather than pushing every session harder.",
            "Keep the effort conversational and build consistency before intensity."),
        new("AE-04","AEROBIC_ENERGY","Aerobic Energy",4,8,LearningSurfaceCodes.Dashboard,
            "Fuel is always a mixture","The why",
            "Your fuel mix is the combination of carbohydrate and fat your body uses to rebuild ATP during activity. Both contribute during most exercise, but carbohydrate usually supplies a larger share as intensity rises because it can support faster energy production. This matters because burning more fat during one workout does not automatically mean losing more body fat over time, which also depends on your overall energy balance. Build aerobic fitness with steady conversational exercise, include carbohydrate to support demanding sessions, and judge fat-loss progress from consistent trends rather than a workout's fuel label.",
            "Match fuel to the session and assess fat loss over time."),
        new("AE-05","AEROBIC_ENERGY","Aerobic Energy",5,8,LearningSurfaceCodes.Dashboard,
            "Long workouts change the fuel mix","The why",
            "Muscle glycogen is carbohydrate stored in your muscles, and its contribution to energy production can change as a long workout continues. Your body also uses blood glucose and fat, with the balance depending on exercise intensity, duration, fitness, and available fuel. This matters because the first few minutes of a workout do not accurately describe every minute of a much longer session, and low glycogen can make sustained hard work difficult. Build longer sessions gradually, start adequately fueled, and plan carbohydrate intake for demanding prolonged activity rather than guessing from total calories burned.",
            "Long sessions need a fueling plan that accounts for duration."),
        new("AE-06","AEROBIC_ENERGY","Aerobic Energy",6,8,LearningSurfaceCodes.Dashboard,
            "Intervals train a different problem","The why",
            "Intervals are repeated periods of harder exercise separated by easier movement or rest. Hard efforts demand ATP quickly, drawing heavily on stored phosphocreatine and carbohydrate while aerobic metabolism contributes during both work and recovery. This matters because intervals challenge your ability to produce high power and repeat it, adding a different stimulus from steady Zone 2 exercise. Begin with manageable efforts, allow enough recovery to preserve good movement and repeatable performance, and increase the challenge gradually within your training plan.",
            "Use hard efforts and recovery together to build repeatable performance."),
        new("AE-07","AEROBIC_ENERGY","Aerobic Energy",7,8,LearningSurfaceCodes.Dashboard,
            "VO2 max is a whole-system outcome","The why",
            "VO2 max is the highest rate at which your body can take in and use oxygen during intense exercise. Your lungs bring oxygen in, your heart and blood deliver it, and your muscles' mitochondria use it to help produce ATP. It matters because a higher aerobic capacity can support harder sustained exercise, although endurance also depends on pacing, efficiency, and how long you can maintain the effort. Build a consistent aerobic base with manageable steady sessions and add appropriately challenging intervals as your fitness and recovery allow.",
            "Train both oxygen delivery and your muscles' ability to use it."),
        new("AE-08","AEROBIC_ENERGY","Aerobic Energy",8,8,LearningSurfaceCodes.Dashboard,
            "How 12WR uses this","The why",
            "A training plan combines different types of exercise because each develops a different part of your fitness. 12WR uses easy aerobic work, harder conditioning, and resistance training to address endurance, performance, and muscle strength while accounting for their different fuel demands. This matters because making every workout equally hard can spend recovery without providing the balanced practice your goals need. Follow the purpose of each planned session, fuel it appropriately, and use trends in performance and recovery to guide gradual adjustments.",
            "Give each session a clear job in the overall plan."),

        new("NF-01","FOOD_AND_FUEL","Food & Fuel",1,8,LearningSurfaceCodes.Nutrition,
            "Calories and nutrients are different questions","The why",
            "Calories measure the energy in food, while nutrients are the substances your body uses to function and maintain its tissues. Carbohydrate and fat provide much of your fuel, protein supplies building material, and vitamins, minerals, and fiber support other essential processes. This matters because two meals with similar calories can differ greatly in how well they support fullness, training, recovery, and health. Improve your meals by combining a protein source, varied vegetables or fruit, fiber-rich carbohydrate, and appropriate fats while keeping portions aligned with your plan.",
            "Plan for both energy needs and nutrient quality."),
        new("NF-02","FOOD_AND_FUEL","Food & Fuel",2,8,LearningSurfaceCodes.Nutrition,
            "Protein is raw material","The why",
            "Protein is a nutrient made from amino acids, the building blocks your body uses to make and maintain tissues. After resistance training, those amino acids help repair and remodel muscle as your body responds to the exercise stimulus. This matters because hard training needs adequate building material, and protein also helps preserve muscle when you are reducing body fat. Include protein-rich foods such as fish, eggs, dairy, beans, tofu, or lean meat across your regular meals and follow your plan's daily target.",
            "Pair a useful training stimulus with adequate daily protein."),
        new("NF-03","FOOD_AND_FUEL","Food & Fuel",3,8,LearningSurfaceCodes.Nutrition,
            "Carbohydrate is not just workout calories","The why",
            "Carbohydrate is a nutrient your body breaks down into sugars that can be used for energy or stored as glycogen in muscles and the liver. It helps support blood glucose and provides readily available fuel for demanding exercise, with glycogen rebuilt as you eat during recovery. This matters because a workout uses a mixture of fuels, so its total calorie burn is not the same as the amount of carbohydrate that needs replacing. Match carbohydrate intake to your planned activity and recovery needs using foods such as oats, rice, potatoes, beans, and fruit rather than automatically eating back every exercise calorie.",
            "Replace needed fuel rather than automatically eating back exercise calories."),
        new("NF-04","FOOD_AND_FUEL","Food & Fuel",4,8,LearningSurfaceCodes.Nutrition,
            "Fat has structural jobs","The why",
            "Dietary fat is an energy-rich nutrient that includes fatty acids your body needs for normal function. It provides fuel, contributes to cell membranes, and helps you absorb vitamins A, D, E, and K. This matters because removing too much fat can make a diet less nutritionally complete, while large portions can add substantial energy very quickly. Include reasonable portions of foods such as nuts, seeds, olive oil, avocado, and oily fish, and keep your overall intake aligned with your nutrition plan.",
            "Include appropriate fats and pay attention to portions."),
        new("NF-05","FOOD_AND_FUEL","Food & Fuel",5,8,LearningSurfaceCodes.Nutrition,
            "Fiber changes the meal","The why",
            "Fiber is the part of plant foods that your digestive system does not fully break down. Different types help stool move through the bowel, feed gut microbes, and can slow digestion or increase fullness. This matters because fiber supports digestive health and can make meals more satisfying while helping moderate the rise in blood glucose after some meals. Build intake gradually with vegetables, fruit, beans, and whole grains, and drink enough fluid as you increase it.",
            "Add fiber-rich foods gradually and support them with fluids."),
        new("NF-06","FOOD_AND_FUEL","Food & Fuel",6,8,LearningSurfaceCodes.Nutrition,
            "Recovery happens over time","The why",
            "Recovery nutrition is the ongoing process of replacing fuel and supplying the materials your body needs after exercise. Carbohydrate helps rebuild glycogen, protein supplies amino acids for tissue repair, and fluids help replace what you lose through sweat. This matters because recovery continues across meals, and timely refueling becomes more important when another demanding session is coming soon. Eat regular meals that meet your plan's fuel and protein needs, replace fluids, and give yourself adequate time between hard sessions.",
            "Support recovery across meals and between sessions."),
        new("NF-07","FOOD_AND_FUEL","Food & Fuel",7,8,LearningSurfaceCodes.Nutrition,
            "Logging is an estimate","The why",
            "Food logging is an estimate of what you eat based on portions, labels, recipes, and food databases. It helps you compare your usual intake with your nutrition targets and identify habits that may need adjusting. This matters because portions and database entries are imperfect, and even a precise-looking calorie total can differ from what you actually consumed. Improve accuracy by measuring commonly misjudged portions, using consistent entries, and checking longer-term trends in weight, waist, hunger, and performance before changing the plan.",
            "Use consistent logging and longer-term outcomes together."),
        new("NF-08","FOOD_AND_FUEL","Food & Fuel",8,8,LearningSurfaceCodes.Nutrition,
            "The goal is a repeatable diet","The why",
            "A repeatable diet is an eating pattern you can maintain through ordinary workdays, weekends, and changes in routine. It supplies enough protein and other nutrients, supports activity, and keeps overall energy intake aligned with your goals. This matters because results depend on what you can do consistently over time, and an overly restrictive plan can be difficult to sustain. Build a small set of meals you enjoy, plan convenient food options, and make manageable adjustments based on your progress instead of repeatedly starting over.",
            "Build an eating pattern you can repeat in real life."),

        new("TR-01","TRAINING_ADAPTATION","Training Adaptation",1,8,LearningSurfaceCodes.Workout,
            "Exercise is a signal","The why",
            "A training stimulus is the physical challenge that tells your body it needs to become better prepared for similar work. Resistance exercise creates tension in muscle, while aerobic exercise challenges the systems that produce and deliver energy. This matters because lasting improvements develop as your body responds to repeated training and recovery, rather than from how exhausted one session makes you. Choose an appropriate challenge, repeat it consistently, and support the response with enough food, sleep, and recovery before adding more work.",
            "Repeat a useful challenge and give your body time to adapt."),
        new("TR-02","TRAINING_ADAPTATION","Training Adaptation",2,8,LearningSurfaceCodes.Workout,
            "Hard enough is different from exhausted","The why",
            "Training effort describes how challenging a set is relative to your ability, while fatigue is the tiredness that accumulates as you work. A demanding set can stimulate muscle growth and strength even when you stop before you are unable to complete another good repetition. This matters because excessive fatigue can reduce technique and the quality of later sets without guaranteeing better results. Follow the prescribed effort, keep your repetitions controlled, and adjust the load when you cannot maintain the intended movement.",
            "Aim for productive effort with controlled technique."),
        new("TR-03","TRAINING_ADAPTATION","Training Adaptation",3,8,LearningSurfaceCodes.Workout,
            "Progressive overload is evidence","The why",
            "Progressive overload means gradually increasing an appropriate training challenge as your body adapts. You can apply it by adding repetitions or weight, improving control, or performing a greater useful range of motion with sound technique. This matters because repeating a challenge that has become easy may provide less stimulus, but increasing too quickly can undermine movement quality and recovery. Record your sets and repetitions, progress in small steps when you meet the plan's goals, and compare performance under similar conditions.",
            "Progress gradually and compare like-for-like performance."),
        new("TR-04","TRAINING_ADAPTATION","Training Adaptation",4,8,LearningSurfaceCodes.Workout,
            "Muscle glycogen is regional","The why",
            "Muscle glycogen is carbohydrate stored inside muscle fibers for use as a local fuel supply. The muscles recruited during exercise draw on their own stores, so a demanding leg session does not use every muscle's glycogen equally. This matters when planning another workout because the muscles that worked hardest may still need fuel replacement and recovery even if other muscles feel fresh. Support those stores with carbohydrate that fits your activity needs and allow appropriate recovery before repeating demanding work for the same muscles.",
            "Refuel and recover the muscles that did the work."),
        new("TR-05","TRAINING_ADAPTATION","Training Adaptation",5,8,LearningSurfaceCodes.Workout,
            "Rest between sets has a job","The why",
            "A rest interval is the pause between sets that lets you prepare for the next effort. During that time, your muscles rebuild phosphocreatine, a reserve that helps rapidly recycle ATP, while breathing and other sources of fatigue also recover. This matters because starting the next set too soon can reduce the force, repetitions, or technique you can maintain. Use your plan's rest periods and take enough time to perform the next set well, especially during heavy or demanding exercises.",
            "Rest enough to preserve the quality of your next set."),
        new("TR-06","TRAINING_ADAPTATION","Training Adaptation",6,8,LearningSurfaceCodes.Workout,
            "Creatine supports rapid ATP recycling","The why",
            "Creatine is a compound your body makes and also gets from foods such as meat and fish, with much of it stored in muscle. It helps form phosphocreatine, which rapidly transfers energy to rebuild ATP during brief, demanding efforts. This matters for repeated lifting sets and sprints because a larger phosphocreatine supply can help you maintain performance and support productive training. Improve that performance with consistent training and adequate rest, and consider creatine monohydrate as an optional supplement that can increase muscle stores if it fits your needs.",
            "Creatine can support repeated hard efforts; training and recovery remain essential."),
        new("TR-07","TRAINING_ADAPTATION","Training Adaptation",7,8,LearningSurfaceCodes.Workout,
            "Mobility is usable range","The why",
            "Mobility is your ability to move a joint through a useful range with enough strength and control. You use it when squatting, reaching overhead, stepping, or performing other movements needed in training and daily life. This matters because having usable movement options helps you perform exercises with control and adapt them to your body. Practice comfortable ranges regularly, combine appropriate stretching with strength work, and gradually expand the movement without forcing painful positions.",
            "Build range, strength, and control together."),
        new("TR-08","TRAINING_ADAPTATION","Training Adaptation",8,8,LearningSurfaceCodes.Workout,
            "Recovery is part of programming","The why",
            "Recovery is the process of restoring your ability to train while your body adapts to previous exercise. Sleep, food, fluids, and appropriately spaced sessions help restore fuel, repair tissues, and manage accumulated fatigue. This matters because fatigue that keeps building can reduce performance and make your next useful session harder to complete well. Keep a consistent sleep routine, meet your nutrition needs, and adjust training load or rest when several sessions show worsening performance or unusual fatigue.",
            "Use recovery to make the next productive session possible.")
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
