using System.Text.Json;
using WebRecomp.NutritionIntelligence.Persistence;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Dashboard2;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.ViewModels;
using WebRecomp.Web.Services.WorkoutIntelligence;

namespace WebRecomp.Web.Services.Dashboard3;

public sealed record RecompScoreHistory(DateOnly Date, int Score, DateTime RecordedAtUtc, string Version = "RECOMP_SCORE_V1");
public sealed record RecompUpcomingWorkout(DateOnly Date, string Name, string Description,
    decimal? Minutes, decimal? Rpe, string? SampleCode, string Tone, bool Planned);
public sealed record FlexibleItemEntry(int Count);
public sealed record RecompTrainingCommitment(DateOnly Date, string Key, decimal GlycogenG);
public sealed record RecompScoreSnapshot(string Version, DateOnly WeekStart, int Score,
    int EvidenceDays, int CompletedDays, IReadOnlyList<RecompDayEvidence> Days,
    IReadOnlyList<RecompTrainingGauge> Training, IReadOnlyList<RecompScoreComponent> Components,
    RecompGlycogenWork? Glycogen = null, IReadOnlyList<RecompTrainingCommitment>? Commitments = null, RecompDayEvidence? TargetCapture = null);
public sealed class RecompCockpit
{
    public DateOnly WeekStart { get; init; }
    public DateOnly ScoreEnd { get; init; }
    public bool RollingScore { get; init; } = true;
    public List<RecompDayEvidence> EditableDays { get; init; } = [];
    public RecompBodyCompositionTrends? BodyComposition { get; init; }
    public RecompWeeklyTrend? WeeklyTrend { get; init; }
    public RecompScoreResult Score { get; init; } = new();
    public RecompGlycogenWork? Glycogen { get; init; }
    public List<RecompUpcomingWorkout> UpcomingWorkouts { get; init; } = [];
    public List<RecompTrainingGauge> Training { get; init; } = [];
    public List<RecompDayEvidence> Days { get; init; } = [];
    public List<RecompScoreHistory> History { get; init; } = [];
    public int? Change { get; init; }
    public int? TodayFlexibleItems { get; init; }
    public int? DetectedTreatItems { get; init; }
    public Dictionary<DateOnly, string> DetectedTreatFoods { get; init; } = [];
    public HashSet<DateOnly> TreatCountOverrides { get; init; } = [];
    public string? StorageNotice { get; set; }
    public string? DataNotice { get; init; }
    public string? HistoryNotice { get; init; }
    public string NextAction { get; init; } = "";
}

public sealed class RecompCockpitService(RecompDbContext db,
    Engine30DashboardProjectionService projection, Engine30WorkoutUiCatalogService catalog,
    ProgramPlanService planSelection, INutritionFoodLogStore foodLog, ILogger<RecompCockpitService> logger)
{
    private const string FlexVersion = "RECOMP_FLEX_V1";

    public async Task<RecompCockpit> BuildAsync(AppClientContext context, DashboardViewModel day,
        MacroDayPlan mealPlan, CancellationToken ct)
    {
        var client = context.Client!;
        var program = context.Program!;
        var today = day.ActionDate;
        var historical = today < AppUserContextService.GetClientToday(client);
        var start = today.AddDays(-7);
        var end = today.AddDays(-1);
        var activeStart = RollingStart(today, program.StartDate);
        var targets = new Dictionary<DateOnly, Engine30DashboardDayProjection>();
        // A rolling window can cross a planning-week or program-phase boundary.
        // Resolve each week's plan independently rather than using today's code for both.
        for (var week = PlanningWeek.Start(activeStart); week <= end; week = week.AddDays(7))
        {
            var planDate = week < activeStart ? activeStart : week;
            var planCode = await planSelection.GetPlanCodeForDateAsync(program, planDate, ct);
            foreach (var target in await projection.BuildPlanningWeekAsync(context, week, planCode, ct))
                if (target.Key >= activeStart && target.Key <= end) targets[target.Key] = target.Value;
        }
        var types = await catalog.GetActiveMapAsync(ct);
        var nutrition = await db.NutritionDays.AsNoTracking().Where(x => x.ClientId == client.ClientId
            && x.ProgramId == program.ProgramId && x.NutritionDate >= activeStart && x.NutritionDate <= today).ToListAsync(ct);
        var sessions = await db.WorkoutSessions.AsNoTracking().Where(x => x.ClientId == client.ClientId
            && x.ProgramId == program.ProgramId && x.WorkoutDate >= activeStart && x.WorkoutDate <= end
            && x.CompletedFlag).ToListAsync(ct);
        sessions = (await WebRecomp.Web.Services.WorkoutIntelligence.DetailedWorkoutEngine30Adapter
            .CanonicalCompletedSessionsAsync(db, sessions, ct)).ToList();
        var plans = await db.PlannedWorkouts.AsNoTracking().Where(x => x.ClientId == client.ClientId
            && x.ProgramId == program.ProgramId && x.PlannedDate >= activeStart && x.PlannedDate <= end).ToListAsync(ct);
        var checkins = await db.DailyCheckins.AsNoTracking().Where(x => x.ProgramId == program.ProgramId
            && x.CheckinDate >= activeStart && x.CheckinDate <= today).ToListAsync(ct);
        var records = await db.AnalysisRuns.AsNoTracking().Where(x => x.ClientId == client.ClientId
            && x.ProgramId == program.ProgramId && x.ActionDate >= activeStart && x.ActionDate <= today
            && (x.EngineVersion == RecompScoreResult.Version || (x.EngineVersion == "RECOMP_SCORE_V1" || x.EngineVersion == "RECOMP_SCORE_V2" || x.EngineVersion == "RECOMP_SCORE_V3" || x.EngineVersion == "RECOMP_SCORE_V4" || x.EngineVersion == "RECOMP_SCORE_V5") || x.EngineVersion == FlexVersion))
            .OrderByDescending(x => x.StartedAtUtc).ThenByDescending(x => x.AnalysisRunId).ToListAsync(ct);
        var flex = records.Where(x => x.EngineVersion == FlexVersion).GroupBy(x => x.ActionDate)
            .ToDictionary(x => x.Key, x => Read<FlexibleItemEntry>(x.First().DiagnosticJson)?.Count);
        var snapshots = records.Where(x => x.EngineVersion == RecompScoreResult.Version || (x.EngineVersion == "RECOMP_SCORE_V1" || x.EngineVersion == "RECOMP_SCORE_V2" || x.EngineVersion == "RECOMP_SCORE_V3" || x.EngineVersion == "RECOMP_SCORE_V4" || x.EngineVersion == "RECOMP_SCORE_V5"))
            .Select(x => (Run: x, Snapshot: Read<RecompScoreSnapshot>(x.DiagnosticJson)))
            .Where(x => x.Snapshot is not null).ToArray();
        var logs = new List<NutritionDailyLogReadModel>();
        if (program.NutritionFoodLogEnabled)
        {
            try
            {
                for (var date = today.AddDays(-7); date <= today; date = date.AddDays(1))
                    logs.Add(await foodLog.GetDailyLogAsync(client.ClientId, date, ct));
                mealPlan.PredictedEnergy = RecompMealPrediction.Predict(today, logs, mealPlan.EnergyEaten,
                    nutrition.SingleOrDefault(x => x.NutritionDate == today)?.EntrySourceCode);
                mealPlan.PredictionNotice = mealPlan.PredictedEnergy.HasValue
                    ? "Logged calories plus the seven-day average for meal slots not yet entered. Days without a slot count as zero; incomplete days are excluded. This is an estimate, not a food recommendation."
                    : "Prediction needs complete meal history and food-log meal detail for today's intake.";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Command Center meal history unavailable");
                mealPlan.PredictionNotice = "Meal history is temporarily unavailable.";
            }
        }
        var eligibleLogs = logs.Where(RecompTreatClassifier.Complete).Where(x => x.Items.Count > 0)
            .Where(x => nutrition.SingleOrDefault(n => n.NutritionDate == x.LogDate)?.EntrySourceCode is null or "FOOD_LOG" or "MANUAL").ToArray();
        var detected = eligibleLogs.ToDictionary(x => x.LogDate, RecompTreatClassifier.Count);
        var detectedFoods = eligibleLogs.ToDictionary(x => x.LogDate, RecompTreatClassifier.FoodNames);
        int? Flex(DateOnly date) => flex.GetValueOrDefault(date) ?? (detected.TryGetValue(date, out var count) ? count : null);
        var days = new List<RecompDayEvidence>();
        for (var date = activeStart; date <= end; date = date.AddDays(1))
        {
            if (!targets.TryGetValue(date, out var target)) continue;
            // First captured same-day prescription is preserved for historical scoring.
            // Older days without captures explicitly use reconstructed targets.
            var saved = snapshots.Where(x => x.Run.ActionDate == date)
                .OrderBy(x => x.Run.StartedAtUtc).SelectMany(x => x.Snapshot!.TargetCapture is { } captured
                    ? x.Snapshot.Days.Append(captured) : x.Snapshot!.Days)
                .FirstOrDefault(x => x.Date == date);
            var actual = nutrition.SingleOrDefault(x => x.NutritionDate == date);
            days.Add(new(date, saved?.ProteinTarget ?? target.ProteinG,
                saved?.CarbsTarget ?? target.RecommendedCarbsG, saved?.FatTarget ?? target.FatG,
                saved?.EnergyTarget ?? target.PrescribedEnergyKcal, actual?.ActualProteinG,
                actual?.ActualCarbsG, actual?.ActualFatG, actual?.ActualCalories,
                Flex(date), saved is not null ? "Captured same-day prescription"
                    : "Reconstructed historical prescription"));
        }
        var commitments = plans.Select(x => new RecompTrainingCommitment(x.PlannedDate,
                Group(x.WorkoutTypeCode, types), targets.GetValueOrDefault(x.PlannedDate)?.GlycogenDemandG ?? 0m))
            .Concat(snapshots.SelectMany(x => x.Snapshot!.Commitments ?? [])
                .Where(x => x.Date >= activeStart && x.Date <= end))
            .GroupBy(x => (x.Date, x.Key))
            .Select(g => new RecompTrainingCommitment(g.Key.Date, g.Key.Key, g.Max(x => x.GlycogenG)))
            .OrderBy(x => x.Date).ThenBy(x => x.Key).ToList();
        var training = new List<RecompTrainingGauge>();
        foreach (var (key, label, weight) in new[] { ("resistance", "Resistance", 25m),
            ("cardio", "Cardio / mobility", 8m), ("recovery", "Recovery / Zone 2", 7m) })
        {
            var matching = commitments.Where(x => x.Key == key).ToArray();
            var completed = sessions.Where(x => Group(x.WorkoutTypeCode, types) == key
                && (x.DurationMin > 0 || Engine30WorkoutUiCatalogService.NormalizeCode(x.WorkoutTypeCode) == "REST")).Select(x => x.WorkoutDate).Distinct().Count();
            if (key == "recovery")
                completed += plans.Count(p => p.WorkoutTypeCode == "REST"
                    && checkins.Any(c => c.CheckinDate == p.PlannedDate)
                    && !sessions.Any(s => s.WorkoutDate == p.PlannedDate && (Group(s.WorkoutTypeCode, types) == "resistance"
                        || Group(s.WorkoutTypeCode, types) == "cardio" || s.WorkoutTypeCode == "REST")));
            // Every date in this window is complete. There are no future credits.
            training.Add(new(key, label, completed, matching.Select(x => x.Date).Distinct().Count(), 0, weight));
        }
        var burns = new Dictionary<DateOnly, decimal>();
        foreach (var date in sessions.Select(x => x.WorkoutDate).Distinct())
            burns[date] = Math.Max(0m, (await projection.BuildActualWorkoutAsync(context, date, ct)).GlycogenDemandG);
        var workDays = plans.GroupBy(x => x.PlannedDate).Select(g => new RecompGlycogenDay(g.Key,
            burns.GetValueOrDefault(g.Key), commitments.Where(x => x.Date == g.Key).Sum(x => x.GlycogenG),
            g.All(x => Engine30WorkoutUiCatalogService.NormalizeCode(x.WorkoutTypeCode) == "REST"),
            sessions.Any(x => x.WorkoutDate == g.Key && x.DurationMin > 0))).ToArray();
        var glycogen = new RecompGlycogenWork(burns.Values.Sum(), commitments.Sum(x => Math.Max(0m, x.GlycogenG)), 0m, workDays);
        var upcomingPlans = await db.PlannedWorkouts.AsNoTracking().Where(x => x.ClientId == client.ClientId
            && x.ProgramId == program.ProgramId && x.PlannedDate > today && x.PlannedDate <= today.AddDays(2)).ToListAsync(ct);
        var templateIds = upcomingPlans.Where(x => x.WorkoutTemplateId.HasValue).Select(x => x.WorkoutTemplateId!.Value).ToArray();
        var templates = await db.WorkoutTemplates.AsNoTracking().Where(x => x.ClientId == client.ClientId
            && templateIds.Contains(x.WorkoutTemplateId)).ToDictionaryAsync(x => x.WorkoutTemplateId, ct);
        var upcoming = new List<RecompUpcomingWorkout>();
        for (var offset = 1; offset <= 2; offset++)
        {
            var date = today.AddDays(offset);
            var scheduled = upcomingPlans.SingleOrDefault(x => x.PlannedDate == date);
            if (scheduled is null)
            {
                upcoming.Add(new(date, "Choose your next session", "No workout is saved for this day yet.",
                    null, null, null, "unplanned", false));
                continue;
            }
            var code = Engine30WorkoutUiCatalogService.NormalizeCode(scheduled.WorkoutTypeCode);
            types.TryGetValue(code, out var type);
            SystemWorkoutRecipeCatalog.TryGet(scheduled.SystemWorkoutCode, out var selected);
            var sample = selected ?? SystemWorkoutRecipeCatalog.GetDefaultPresentationForEngine(code);
            var name = scheduled.WorkoutTemplateId.HasValue && templates.TryGetValue(scheduled.WorkoutTemplateId.Value, out var template)
                ? template.TemplateName : selected?.DisplayName ?? type?.DisplayName ?? code.Replace('_', ' ');
            var tone = code == "REST" || code == "ZONE2_CARDIO" ? "recovery"
                : Group(code, types) == "resistance" ? type?.CountsDedicatedHeavy == true ? "hard" : "moderate" : "cardio";
            upcoming.Add(new(date, name, selected?.Description ?? type?.Description ?? "Follow your saved training plan.",
                scheduled.PlannedDurationMin ?? (selected is not null && type is not null
                    ? SystemWorkoutRecipeCatalog.ResolveDefaultMinutes(selected, type.DefaultMinutes) : type?.DefaultMinutes),
                scheduled.PlannedRpe, sample?.SystemCode, tone, true));
        }
        var score = RecompAdherenceScorer.Score(today, days, training, glycogen);
        var capture = historical ? snapshots.FirstOrDefault(x => x.Run.ActionDate == today).Snapshot : null;
        if (capture is not null)
        {
            days = capture.Days.ToList();
            training = capture.Training.ToList();
            glycogen = capture.Glycogen;
            score = RestoreCapturedScore(today, capture);
        }
        var previous = snapshots.FirstOrDefault().Snapshot;
        var history = snapshots.GroupBy(x => x.Run.ActionDate).Select(x => x.First())
            .Select(x => new RecompScoreHistory(x.Run.ActionDate, x.Snapshot!.Score, x.Run.StartedAtUtc, x.Snapshot.Version)).OrderBy(x => x.Date).ToList();
        var behind = training.FirstOrDefault(x => x.ProjectedFraction < 1);
        var result = new RecompCockpit
        {
            WeekStart = capture?.WeekStart ?? start,
            RollingScore = capture is null || capture.Version == RecompScoreResult.Version,
            ScoreEnd = capture is not null && capture.Version != RecompScoreResult.Version ? capture.WeekStart.AddDays(6) : end,
            EditableDays = days.Where(x => x.Date < today).Append(new RecompDayEvidence(today,
                mealPlan.Bands[0].Target, mealPlan.Bands[1].Target, mealPlan.Bands[2].Target,
                mealPlan.EnergyTarget, mealPlan.Bands[0].Eaten, mealPlan.Bands[1].Eaten, mealPlan.Bands[2].Eaten,
                mealPlan.EnergyEaten, Flex(today), "Current engine prescription")).ToList(),
            BodyComposition = await BuildBodyCompositionAsync(context, today, ct),
            WeeklyTrend = await BuildWeeklyTrendAsync(context, today, ct),
            Score = score, Training = training, Glycogen = glycogen, Days = days, History = history, UpcomingWorkouts = upcoming,
            Change = !score.HasEvidence || previous is null || previous.Version != RecompScoreResult.Version ? null : score.Score - previous.Score,
            TodayFlexibleItems = Flex(today),
            DetectedTreatItems = detected.TryGetValue(today, out var detectedCount) ? detectedCount : null,
            DetectedTreatFoods = detectedFoods,
            TreatCountOverrides = flex.Where(x => x.Value.HasValue && (!detected.TryGetValue(x.Key, out var n) || n != x.Value.Value))
                .Select(x => x.Key).ToHashSet(),
            HistoryNotice = !historical ? null : capture is not null
                ? $"Saved score · {today:MMM d} · {capture.Version}. Daily cards show the saved entries."
                : "Reconstructed rolling score from saved entries and the available plan. No score was captured on this day; browsing does not create history.",
            DataNotice = plans.Count == 0 ? "Save a weekly training plan before judging training adherence."
                : training.Single(x => x.Key == "resistance").Target == 0
                ? "The last seven completed days have no resistance goal. Review the plan before using the score to evaluate a recomp strategy."
                : score.KnownNutritionDays < score.CompletedNutritionDays
                ? "Some completed days have unknown intake. Unknown days are excluded from the score." : null,
            NextAction = behind is not null ? $"{behind.Label} is behind the saved schedule. Review the next planned session and continue the strategy."
                : mealPlan.Bands.Any(x => x.Eaten > x.High) ? "Review today's combined intake, then return to the next planned day."
                : mealPlan.Bands.Any(x => x.Remaining > 0) && mealPlan.CanPlan ? mealPlan.Advice
                : "Keep following your planned training, fueling and recovery. Extra sessions do not add score points."
        };
        var snapshot = new RecompScoreSnapshot(RecompScoreResult.Version, start, score.Score,
            score.KnownNutritionDays, score.CompletedNutritionDays, days, training, score.Components, glycogen, commitments, result.EditableDays.Last());
        var json = JsonSerializer.Serialize(snapshot);
        if (!historical && score.HasEvidence && snapshots.FirstOrDefault().Run?.DiagnosticJson != json)
        {
            try
            {
                db.AnalysisRuns.Add(NewRun(context, today, RecompScoreResult.Version, json,
                    score.KnownNutritionDays == score.CompletedNutritionDays ? "AVAILABLE" : "PROVISIONAL"));
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                foreach (var entry in db.ChangeTracker.Entries<AnalysisRun>().Where(x => x.State == EntityState.Added).ToArray()) entry.State = EntityState.Detached;
                logger.LogError(ex, "Dashboard3 score snapshot could not be stored");
                result.StorageNotice = "Your current score is shown, but its history could not be saved. Please try again later.";
            }
        }
        if (!historical && score.HasEvidence)
        {
            result.History.RemoveAll(x => x.Date == today);
            result.History.Add(new(today, score.Score, DateTime.UtcNow, RecompScoreResult.Version));
        }
        return result;
    }

    public static RecompScoreResult RestoreCapturedScore(DateOnly date, RecompScoreSnapshot capture)
    {
        var evidence = RecompAdherenceScorer.Score(date, capture.Days, capture.Training, capture.Glycogen);
        return new RecompScoreResult
        {
            Score = capture.Score, Components = capture.Components.ToList(),
            KnownNutritionDays = capture.EvidenceDays, CompletedNutritionDays = capture.CompletedDays,
            FlexibleItems = evidence.FlexibleItems, FlexibleConfirmedDays = evidence.FlexibleConfirmedDays,
            FlexibleDayOverMacros = evidence.FlexibleDayOverMacros
        };
    }

    public static DateOnly RollingStart(DateOnly date, DateOnly programStart) =>
        programStart > date.AddDays(-7) ? programStart : date.AddDays(-7);

    public async Task SaveFlexibleItemsAsync(AppClientContext context, DateOnly date, int count, CancellationToken ct)
    {
        var today = AppUserContextService.GetClientToday(context.Client!);
        if (count is < 0 or > 50 || date > today || date < today.AddDays(-7) || date < context.Program!.StartDate)
            throw new ArgumentException("Choose a day within the last seven completed days or today and a count from 0 to 50.");
        db.AnalysisRuns.Add(NewRun(context, date, FlexVersion, JsonSerializer.Serialize(new FlexibleItemEntry(count)), "REPORTED"));
        await db.SaveChangesAsync(ct);
    }

    private async Task<RecompBodyCompositionTrends> BuildBodyCompositionAsync(AppClientContext context,
        DateOnly date, CancellationToken ct)
    {
        // The 30-day comparison needs the seven-day baseline ending 30 days ago.
        var from = date.AddDays(-36);
        var rows = await db.Measurements.AsNoTracking().Where(x => x.ClientId == context.Client!.ClientId
            && x.ProgramId == context.Program!.ProgramId && !x.IsDeleted
            && x.LocalMeasurementDate >= from && x.LocalMeasurementDate <= date).ToListAsync(ct);
        return RecompBodyCompositionTrends.Build(date, rows);
    }

    private async Task<RecompWeeklyTrend> BuildWeeklyTrendAsync(AppClientContext context, DateOnly date, CancellationToken ct)
    {
        var clientId = context.Client!.ClientId; var programId = context.Program!.ProgramId;
        var from = date.AddDays(-50); var end = date.AddDays(-1);
        var measurements = await db.Measurements.AsNoTracking().Where(x => x.ClientId == clientId
            && x.ProgramId == programId && !x.IsDeleted && x.LocalMeasurementDate >= from && x.LocalMeasurementDate <= date).ToListAsync(ct);
        var foods = await db.NutritionDays.AsNoTracking().Where(x => x.ClientId == clientId && x.ProgramId == programId
            && x.NutritionDate >= from && x.NutritionDate <= end).ToListAsync(ct);
        var workouts = await db.WorkoutSessions.AsNoTracking().Where(x => x.ClientId == clientId && x.ProgramId == programId
            && x.CompletedFlag && x.WorkoutDate >= from && x.WorkoutDate <= end).ToListAsync(ct);
        workouts = (await DetailedWorkoutEngine30Adapter.CanonicalCompletedSessionsAsync(db, workouts, ct)).ToList();
        var plans = await db.PlannedWorkouts.AsNoTracking().Where(x => x.ClientId == clientId && x.ProgramId == programId
            && x.PlannedDate >= from && x.PlannedDate <= end).ToListAsync(ct);
        var records = await db.AnalysisRuns.AsNoTracking().Where(x => x.ClientId == clientId && x.ProgramId == programId
            && x.ActionDate >= from && x.ActionDate <= date && (x.EngineVersion.StartsWith("RECOMP_SCORE_") || x.EngineVersion == FlexVersion))
            .OrderByDescending(x => x.StartedAtUtc).ThenByDescending(x => x.AnalysisRunId).ToListAsync(ct);
        var snapshots = records.Where(x => x.EngineVersion.StartsWith("RECOMP_SCORE_"))
            .Select(x => (Run: x, Snapshot: Read<RecompScoreSnapshot>(x.DiagnosticJson))).Where(x => x.Snapshot is not null).ToArray();
        // Only original same-day captures are used as prescriptions, never reconstructed historical targets.
        var targets = snapshots.Where(x => x.Snapshot!.TargetCapture is not null)
            .OrderBy(x => x.Run.StartedAtUtc).ThenBy(x => x.Run.AnalysisRunId)
            .Select(x => x.Snapshot!.TargetCapture!).GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.First());
        var treats = snapshots.SelectMany(x => x.Snapshot!.Days).Where(x => x.FlexibleItems.HasValue)
            .GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.First().FlexibleItems);
        foreach (var group in records.Where(x => x.EngineVersion == FlexVersion).GroupBy(x => x.ActionDate))
            if (Read<FlexibleItemEntry>(group.First().DiagnosticJson) is { } flex) treats[group.Key] = flex.Count;
        return RecompWeeklyTrend.Build(date, context.Program.StartDate, measurements, foods, workouts, plans, targets, treats);
    }

    private static AnalysisRun NewRun(AppClientContext context, DateOnly date, string version, string json, string confidence) => new()
    {
        AnalysisRunId = Guid.NewGuid(), ClientId = context.Client!.ClientId, ProgramId = context.Program!.ProgramId,
        ConfigVersionId = context.Program.ConfigVersionId, ActionDate = date, EngineVersion = version,
        RunStatusCode = "COMPLETED", DataConfidenceCode = confidence, DiagnosticJson = json,
        InputCutoffAtUtc = DateTime.UtcNow, StartedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow
    };
    private static T? Read<T>(string? json) where T : class
    {
        try { return json is null ? null : JsonSerializer.Deserialize<T>(json); }
        catch (JsonException) { return null; }
    }
    public static string Group(string? raw, IReadOnlyDictionary<string, WorkoutTypeDefinition> types)
    {
        var code = Engine30WorkoutUiCatalogService.NormalizeCode(raw);
        if (code is "REST" or "ZONE2_CARDIO") return "recovery";
        if (code == "MOBILITY_RECOVERY") return "cardio";
        if (code == "MIXED_GYM_SESSION") return "resistance";
        if (!types.TryGetValue(code, out var type)) return "unclassified";
        if (type.WorkoutCategoryCode.Contains("STRENGTH", StringComparison.OrdinalIgnoreCase)) return "resistance";
        if (type.WorkoutCategoryCode.Contains("CARDIO", StringComparison.OrdinalIgnoreCase)
            || type.WorkoutCategoryCode.Contains("CONDITIONING", StringComparison.OrdinalIgnoreCase)) return "cardio";
        return "unclassified";
    }
}
