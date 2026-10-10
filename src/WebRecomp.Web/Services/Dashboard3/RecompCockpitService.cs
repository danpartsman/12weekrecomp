using System.Text.Json;
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
public sealed record RecompScoreSnapshot(string Version, DateOnly WeekStart, int Score,
    int EvidenceDays, int CompletedDays, IReadOnlyList<RecompDayEvidence> Days,
    IReadOnlyList<RecompTrainingGauge> Training, IReadOnlyList<RecompScoreComponent> Components,
    RecompGlycogenWork? Glycogen = null);
public sealed class RecompCockpit
{
    public DateOnly WeekStart { get; init; }
    public RecompScoreResult Score { get; init; } = new();
    public RecompGlycogenWork? Glycogen { get; init; }
    public List<RecompUpcomingWorkout> UpcomingWorkouts { get; init; } = [];
    public List<RecompTrainingGauge> Training { get; init; } = [];
    public List<RecompDayEvidence> Days { get; init; } = [];
    public List<RecompScoreHistory> History { get; init; } = [];
    public int? Change { get; init; }
    public int? TodayFlexibleItems { get; init; }
    public string? StorageNotice { get; set; }
    public string? DataNotice { get; init; }
    public string? HistoryNotice { get; init; }
    public string NextAction { get; init; } = "";
}

public sealed class RecompCockpitService(RecompDbContext db,
    Engine30DashboardProjectionService projection, Engine30WorkoutUiCatalogService catalog,
    ProgramPlanService planSelection, ILogger<RecompCockpitService> logger)
{
    private const string FlexVersion = "RECOMP_FLEX_V1";

    public async Task<RecompCockpit> BuildAsync(AppClientContext context, DashboardViewModel day,
        MacroDayPlan mealPlan, CancellationToken ct)
    {
        var client = context.Client!;
        var program = context.Program!;
        var today = day.ActionDate;
        var historical = today < AppUserContextService.GetClientToday(client);
        var start = PlanningWeek.Start(today);
        var end = start.AddDays(6);
        var activeStart = program.StartDate > start ? program.StartDate : start;
        var planCode = await planSelection.GetPlanCodeForDateAsync(program, today, ct);
        var targets = await projection.BuildPlanningWeekAsync(context, start, planCode, ct);
        var types = await catalog.GetActiveMapAsync(ct);
        var nutrition = await db.NutritionDays.AsNoTracking().Where(x => x.ClientId == client.ClientId
            && x.ProgramId == program.ProgramId && x.NutritionDate >= activeStart && x.NutritionDate <= today).ToListAsync(ct);
        var sessions = await db.WorkoutSessions.AsNoTracking().Where(x => x.ClientId == client.ClientId
            && x.ProgramId == program.ProgramId && x.WorkoutDate >= activeStart && x.WorkoutDate <= today
            && x.CompletedFlag).ToListAsync(ct);
        sessions = (await WebRecomp.Web.Services.WorkoutIntelligence.DetailedWorkoutEngine30Adapter
            .CanonicalCompletedSessionsAsync(db, sessions, ct)).ToList();
        var plans = await db.PlannedWorkouts.AsNoTracking().Where(x => x.ClientId == client.ClientId
            && x.ProgramId == program.ProgramId && x.PlannedDate >= activeStart && x.PlannedDate <= end).ToListAsync(ct);
        var checkins = await db.DailyCheckins.AsNoTracking().Where(x => x.ProgramId == program.ProgramId
            && x.CheckinDate >= activeStart && x.CheckinDate <= today).ToListAsync(ct);
        var records = await db.AnalysisRuns.AsNoTracking().Where(x => x.ClientId == client.ClientId
            && x.ProgramId == program.ProgramId && x.ActionDate >= activeStart && x.ActionDate <= today
            && (x.EngineVersion == RecompScoreResult.Version || x.EngineVersion == "RECOMP_SCORE_V1" || x.EngineVersion == FlexVersion))
            .OrderByDescending(x => x.StartedAtUtc).ThenByDescending(x => x.AnalysisRunId).ToListAsync(ct);
        var flex = records.Where(x => x.EngineVersion == FlexVersion).GroupBy(x => x.ActionDate)
            .ToDictionary(x => x.Key, x => Read<FlexibleItemEntry>(x.First().DiagnosticJson)?.Count);
        var snapshots = records.Where(x => x.EngineVersion == RecompScoreResult.Version || x.EngineVersion == "RECOMP_SCORE_V1")
            .Select(x => (Run: x, Snapshot: Read<RecompScoreSnapshot>(x.DiagnosticJson)))
            .Where(x => x.Snapshot is not null).ToArray();
        var days = new List<RecompDayEvidence>();
        for (var date = activeStart; date <= end; date = date.AddDays(1))
        {
            if (!targets.TryGetValue(date, out var target)) continue;
            // First captured same-day prescription is preserved for historical scoring.
            // Older days without captures explicitly use reconstructed targets.
            var saved = date < today ? snapshots.Where(x => x.Run.ActionDate == date)
                .OrderBy(x => x.Run.StartedAtUtc).SelectMany(x => x.Snapshot!.Days)
                .FirstOrDefault(x => x.Date == date) : null;
            var actual = nutrition.SingleOrDefault(x => x.NutritionDate == date);
            decimal? p = date == today ? mealPlan.Bands[0].Eaten : actual?.ActualProteinG;
            decimal? c = date == today ? mealPlan.Bands[1].Eaten : actual?.ActualCarbsG;
            decimal? f = date == today ? mealPlan.Bands[2].Eaten : actual?.ActualFatG;
            days.Add(new(date, date == today ? mealPlan.Bands[0].Target : saved?.ProteinTarget ?? target.ProteinG,
                date == today ? mealPlan.Bands[1].Target : saved?.CarbsTarget ?? target.RecommendedCarbsG,
                date == today ? mealPlan.Bands[2].Target : saved?.FatTarget ?? target.FatG,
                date == today ? mealPlan.EnergyTarget : saved?.EnergyTarget ?? target.PrescribedEnergyKcal, p, c, f,
                date == today ? mealPlan.EnergyEaten : actual?.ActualCalories,
                flex.GetValueOrDefault(date), saved is not null ? "Captured same-day prescription"
                    : date < today ? "Reconstructed historical prescription" : "Current engine prescription"));
        }
        var training = new List<RecompTrainingGauge>();
        foreach (var (key, label, weight) in new[] { ("resistance", "Resistance", 25m),
            ("cardio", "Cardio / mobility", 8m), ("recovery", "Recovery / Zone 2", 7m) })
        {
            var matching = plans.Where(x => Group(x.WorkoutTypeCode, types) == key).ToArray();
            // Session rows are the canonical completion boundary. Explicit REST completion
            // also counts; otherwise a planned rest day needs a recorded check-in and no hard session.
            var completed = sessions.Count(x => Group(x.WorkoutTypeCode, types) == key
                && (x.DurationMin > 0 || Engine30WorkoutUiCatalogService.NormalizeCode(x.WorkoutTypeCode) == "REST"));
            if (key == "recovery")
                completed += matching.Count(p => p.PlannedDate < today && p.WorkoutTypeCode == "REST"
                    && checkins.Any(c => c.CheckinDate == p.PlannedDate)
                    && !sessions.Any(s => s.WorkoutDate == p.PlannedDate && (Group(s.WorkoutTypeCode, types) == "resistance"
                        || Group(s.WorkoutTypeCode, types) == "cardio" || s.WorkoutTypeCode == "REST")));
            var todayCompleted = sessions.Any(s => s.WorkoutDate == today && Group(s.WorkoutTypeCode, types) == key);
            var remaining = matching.Count(x => x.PlannedDate > today || (x.PlannedDate == today && !todayCompleted));
            // Removing a missed plan row must not silently erase the weekly commitment.
            var capturedTarget = snapshots.SelectMany(x => x.Snapshot!.Training)
                .Where(x => x.Key == key).Select(x => x.Target).DefaultIfEmpty(0).Max();
            training.Add(new(key, label, completed, Math.Max(matching.Length, capturedTarget), remaining, weight));
        }
        decimal completedBurn = 0m;
        foreach (var date in sessions.Select(x => x.WorkoutDate).Distinct())
        {
            var actual = date == today && day.ActualWorkoutGlycogenG.HasValue
                ? day.ActualWorkoutGlycogenG.Value
                : (await projection.BuildActualWorkoutAsync(context, date, ct)).GlycogenDemandG;
            completedBurn += Math.Max(0m, actual);
        }
        var plannedBurn = targets.Values.Where(x => x.Date >= activeStart).Sum(x => Math.Max(0m, x.GlycogenDemandG));
        var capturedBurn = snapshots.Select(x => x.Snapshot!.Glycogen?.TargetG ?? 0m).DefaultIfEmpty().Max();
        var remainingBurn = targets.Values.Where(x => x.Date >= activeStart && (x.Date > today
            || (x.Date == today && !sessions.Any(s => s.WorkoutDate == today))))
            .Sum(x => Math.Max(0m, x.GlycogenDemandG));
        var glycogen = new RecompGlycogenWork(completedBurn, Math.Max(plannedBurn, capturedBurn), remainingBurn);
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
            WeekStart = start, Score = score, Training = training, Glycogen = glycogen, Days = days, History = history, UpcomingWorkouts = upcoming,
            Change = previous is null || previous.Version != RecompScoreResult.Version ? null : score.Score - previous.Score,
            TodayFlexibleItems = flex.GetValueOrDefault(today),
            HistoryNotice = !historical ? null : capture is not null
                ? $"Saved weekly projection · {today:MMM d} · {capture.Version}. Daily cards show the saved entries."
                : "Reconstructed weekly projection from saved entries and the available plan. No score was captured on this day; browsing does not create history.",
            DataNotice = plans.Count == 0 ? "Save a weekly training plan before judging training adherence."
                : training.Single(x => x.Key == "resistance").Target == 0
                ? "This week has no resistance goal. Review the plan before using the score to evaluate a recomp strategy."
                : score.KnownNutritionDays < score.CompletedNutritionDays
                ? "Some completed days have unknown intake. This is a provisional projection; unknown days have not been scored as failures." : null,
            NextAction = behind is not null ? $"{behind.Label} is behind the saved schedule. Review the remaining week and choose the next appropriate session."
                : mealPlan.Bands.Any(x => x.Eaten > x.High) ? "Review today's combined intake, then return to the next planned day."
                : mealPlan.Bands.Any(x => x.Remaining > 0) && mealPlan.CanPlan ? mealPlan.Advice
                : "Keep following your planned training, fueling and recovery. Extra sessions do not add score points."
        };
        var snapshot = new RecompScoreSnapshot(RecompScoreResult.Version, start, score.Score,
            score.KnownNutritionDays, score.CompletedNutritionDays, days, training, score.Components, glycogen);
        var json = JsonSerializer.Serialize(snapshot);
        if (!historical && snapshots.FirstOrDefault().Run?.DiagnosticJson != json)
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
        if (!historical)
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

    public async Task SaveFlexibleItemsAsync(AppClientContext context, DateOnly date, int count, CancellationToken ct)
    {
        var today = AppUserContextService.GetClientToday(context.Client!);
        if (count is < 0 or > 50 || date > today || date < PlanningWeek.Start(today) || date < context.Program!.StartDate)
            throw new ArgumentException("Choose a day in the current week and a count from 0 to 50.");
        db.AnalysisRuns.Add(NewRun(context, date, FlexVersion, JsonSerializer.Serialize(new FlexibleItemEntry(count)), "REPORTED"));
        await db.SaveChangesAsync(ct);
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
