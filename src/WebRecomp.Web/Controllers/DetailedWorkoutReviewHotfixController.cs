using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutIntelligence;
using WebRecomp.WorkoutIntelligence;
using WebRecomp.WorkoutIntelligence.Persistence;

namespace WebRecomp.Web.Controllers;

// TEST hotfix: the legacy DetailedWorkoutController.Review action builds an
// unused dictionary keyed by ExerciseId. A valid workout may contain the same
// exercise more than once, which makes ToDictionary throw an ArgumentException.
// This literal GET route has higher route precedence than the conventional
// /{controller}/{action} route and renders the normal Review view without that
// duplicate-key projection. The POST action below completes the workout and
// returns directly to Dashboard; no intermediate Complete screen is required.
public sealed class DetailedWorkoutReviewHotfixController(
    RecompDbContext db,
    AppUserContextService userContext,
    IDetailedWorkoutStore detailedWorkoutStore,
    IWorkoutIntelligenceGateway workoutIntelligence) : Controller
{
    [HttpGet("/DetailedWorkout/Review")]
    public async Task<IActionResult> Review(
        Guid workoutSessionId,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!context.Program.DetailedWorkoutEntryEnabled)
            return StatusCode(403);

        var session =
            await db.WorkoutSessions
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutSessionId == workoutSessionId
                        && x.ClientId == context.Client.ClientId
                        && x.ProgramId == context.Program.ProgramId,
                    cancellationToken);

        if (session is null)
            return NotFound();

        if (session.CompletedFlag)
        {
            return RedirectToAction(
                "Index",
                "Dashboard");
        }

        var detailed =
            await detailedWorkoutStore.GetAsync(
                workoutSessionId,
                cancellationToken);

        if (detailed is null
            || detailed.Exercises.Count == 0)
        {
            return BadRequest(
                "Add at least one exercise before reviewing the workout.");
        }

        var analysis =
            await BuildReviewAnalysisAsync(
                detailed,
                session.DurationMin,
                cancellationToken);

        var reviewRpe =
            ResolveReviewRpe(session);

        ViewData["WorkoutSessionId"] =
            session.WorkoutSessionId;

        ViewData["WorkoutName"] =
            string.IsNullOrWhiteSpace(
                session.WorkoutName)
                ? "Workout"
                : session.WorkoutName;

        ViewData["DurationMin"] =
            session.DurationMin > 0m
                ? session.DurationMin
                : 1m;

        ViewData["SessionRpe"] =
            reviewRpe;

        var recordedWorkingSets =
            detailed.Exercises
                .SelectMany(x => x.Sets)
                .Count(
                    x =>
                        !x.IsWarmup
                        && ((x.Reps ?? 0) > 0
                            || (x.DurationSeconds ?? 0) > 0));

        var totalRecordedReps =
            detailed.Exercises
                .SelectMany(x => x.Sets)
                .Where(x => !x.IsWarmup)
                .Sum(x => x.Reps ?? 0);

        ViewData["RecordedSets"] =
            recordedWorkingSets;

        ViewData["TotalReps"] =
            totalRecordedReps;

        ViewData["ExerciseCount"] =
            analysis.ExerciseCount;

        ViewData["PrimaryMuscles"] =
            string.Join(
                ", ",
                analysis.MuscleDemand
                    .OrderByDescending(
                        x => x.WeightedSetEquivalents)
                    .Take(3)
                    .Select(
                        x =>
                            x.MuscleSlug
                                .Replace("-", " ")));

        ViewData["ReviewDeadlineUtc"] =
            DateTime.SpecifyKind(
                    session.UpdatedAtUtc,
                    DateTimeKind.Utc)
                .AddMinutes(10)
                .ToString(
                    "yyyy-MM-ddTHH:mm:ss.fff'Z'");

        return View(
            "~/Views/DetailedWorkout/Review.cshtml");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FinalizeWorkout(
        Guid workoutSessionId,
        decimal durationMin,
        decimal? sessionRpe,
        int extraSets,
        Guid? extraExerciseId,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!context.Program.DetailedWorkoutEntryEnabled)
            return StatusCode(403);

        if (durationMin is < 1m or > 360m)
            return BadRequest(
                "Workout duration must be between 1 and 360 minutes.");

        if (sessionRpe.HasValue
            && sessionRpe.Value is < 1m or > 10m)
        {
            return BadRequest(
                "Workout RPE must be between 1 and 10.");
        }

        if (extraSets is < 0 or > 50)
            return BadRequest(
                "Untracked sets must be between 0 and 50.");

        var session =
            await db.WorkoutSessions
                .SingleOrDefaultAsync(
                    x =>
                        x.WorkoutSessionId == workoutSessionId
                        && x.ClientId == context.Client.ClientId
                        && x.ProgramId == context.Program.ProgramId,
                    cancellationToken);

        if (session is null)
            return NotFound();

        if (!session.CompletedFlag)
        {
            await FinalizeWorkoutReviewAsync(
                session,
                durationMin,
                sessionRpe,
                extraSets,
                extraExerciseId,
                cancellationToken);
        }

        TempData["StatusMessage"] =
            "Workout saved and completed.";

        return RedirectToAction(
            "Index",
            "Dashboard");
    }

    private async Task FinalizeWorkoutReviewAsync(
        WorkoutSession session,
        decimal durationMin,
        decimal? sessionRpe,
        int extraSets,
        Guid? extraExerciseId,
        CancellationToken cancellationToken)
    {
        var detailed =
            await detailedWorkoutStore.GetAsync(
                session.WorkoutSessionId,
                cancellationToken);

        if (detailed is null
            || detailed.Exercises.Count == 0)
        {
            throw new InvalidOperationException(
                "A detailed workout is required before finalization.");
        }

        if (extraSets > 0)
        {
            var targetExercise =
                await ResolveExtraSetExerciseAsync(
                    detailed,
                    durationMin,
                    extraExerciseId,
                    cancellationToken);

            var representativeReps =
                targetExercise.Sets
                    .Where(
                        x =>
                            !x.IsWarmup
                            && (x.Reps ?? 0) > 0)
                    .OrderByDescending(x => x.SetOrder)
                    .Select(x => x.Reps!.Value)
                    .FirstOrDefault();

            if (representativeReps <= 0)
                representativeReps = 1;

            var nextSetOrder =
                targetExercise.Sets.Count == 0
                    ? 1
                    : targetExercise.Sets.Max(
                        x => x.SetOrder) + 1;

            var added =
                Enumerable.Range(
                        0,
                        extraSets)
                    .Select(
                        offset =>
                            new DetailedWorkoutSetEntry(
                                Guid.Empty,
                                nextSetOrder + offset,
                                representativeReps,
                                null,
                                targetExercise.LoadUnitCode,
                                null,
                                null,
                                null,
                                null,
                                false,
                                "Untracked set added during workout review."))
                    .ToArray();

            var revisedExercises =
                detailed.Exercises
                    .Select(
                        exercise =>
                            exercise.ExerciseId
                                == targetExercise.ExerciseId
                                ? exercise with
                                {
                                    Sets =
                                        exercise.Sets
                                            .Concat(added)
                                            .ToArray()
                                }
                                : exercise)
                    .ToArray();

            await detailedWorkoutStore.ReplaceAsync(
                new DetailedWorkoutSession(
                    detailed.WorkoutSessionId,
                    revisedExercises),
                cancellationToken);
        }

        session.DurationMin =
            Math.Clamp(
                Math.Round(
                    durationMin,
                    1,
                    MidpointRounding.AwayFromZero),
                1m,
                360m);

        session.SessionRpe =
            sessionRpe;

        if (session.TypicalRestSec.HasValue
            && session.TypicalRestSec.Value <= 0)
        {
            session.TypicalRestSec = null;
        }

        session.CompletedFlag = true;
        session.EntrySourceCode =
            RecompInputSourceCodes.DetailedWorkout;
        session.CalculationEngineVersion = "3.0";
        session.UpdatedAtUtc =
            DateTime.UtcNow;

        await db.SaveChangesAsync(
            cancellationToken);
    }

    private async Task<DetailedWorkoutExerciseEntry>
        ResolveExtraSetExerciseAsync(
            DetailedWorkoutSession detailed,
            decimal durationMin,
            Guid? requestedExerciseId,
            CancellationToken cancellationToken)
    {
        if (requestedExerciseId.HasValue)
        {
            var requested =
                detailed.Exercises
                    .FirstOrDefault(
                        x =>
                            x.ExerciseId
                                == requestedExerciseId.Value);

            if (requested is null)
            {
                throw new InvalidOperationException(
                    "The selected exercise is not part of this workout.");
            }

            return requested;
        }

        var analysis =
            await BuildReviewAnalysisAsync(
                detailed,
                durationMin,
                cancellationToken);

        var primaryMuscle =
            analysis.MuscleDemand
                .OrderByDescending(
                    x => x.WeightedSetEquivalents)
                .Select(x => x.MuscleSlug)
                .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(primaryMuscle))
        {
            var exerciseIds =
                detailed.Exercises
                    .Select(x => x.ExerciseId)
                    .Distinct()
                    .ToArray();

            var targets =
                await db.ExerciseMuscleTargets
                    .AsNoTracking()
                    .Where(
                        x =>
                            exerciseIds.Contains(
                                x.ExerciseCatalogItemId)
                            && x.MuscleSlug == primaryMuscle)
                    .OrderBy(x => x.DisplayOrder)
                    .ToListAsync(
                        cancellationToken);

            var primaryTargetIds =
                targets
                    .Where(
                        x =>
                            string.Equals(
                                x.RoleCode,
                                "PRIMARY",
                                StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.ExerciseCatalogItemId)
                    .ToHashSet();

            var primaryMatch =
                detailed.Exercises
                    .FirstOrDefault(
                        exercise =>
                            primaryTargetIds.Contains(
                                exercise.ExerciseId));

            if (primaryMatch is not null)
                return primaryMatch;

            var anyTargetIds =
                targets
                    .Select(x => x.ExerciseCatalogItemId)
                    .ToHashSet();

            var anyMatch =
                detailed.Exercises
                    .FirstOrDefault(
                        exercise =>
                            anyTargetIds.Contains(
                                exercise.ExerciseId));

            if (anyMatch is not null)
                return anyMatch;
        }

        return detailed.Exercises
            .OrderByDescending(
                exercise =>
                    exercise.Sets.Count(
                        set =>
                            !set.IsWarmup
                            && ((set.Reps ?? 0) > 0
                                || (set.DurationSeconds ?? 0) > 0)))
            .ThenBy(x => x.ExerciseOrder)
            .First();
    }

    private async Task<WorkoutIntelligenceHandoff>
        BuildReviewAnalysisAsync(
            DetailedWorkoutSession detailed,
            decimal durationMin,
            CancellationToken cancellationToken)
    {
        var exerciseIds =
            detailed.Exercises
                .Select(x => x.ExerciseId)
                .Distinct()
                .ToArray();

        var targets =
            await db.ExerciseMuscleTargets
                .AsNoTracking()
                .Where(
                    x =>
                        exerciseIds.Contains(
                            x.ExerciseCatalogItemId))
                .ToListAsync(
                    cancellationToken);

        var targetsByExercise =
            targets
                .GroupBy(
                    x => x.ExerciseCatalogItemId)
                .ToDictionary(
                    x => x.Key,
                    x =>
                        x.OrderBy(
                                target => target.DisplayOrder)
                            .Select(
                                target =>
                                    new MuscleTargetInput(
                                        target.MuscleSlug,
                                        target.RoleCode))
                            .ToArray());

        return workoutIntelligence.Analyze(
            new WorkoutIntelligenceRequest(
                SessionKey:
                    detailed.WorkoutSessionId
                        .ToString("D"),
                DurationMinutes:
                    Math.Max(
                        1,
                        (int)Math.Round(
                            durationMin,
                            0,
                            MidpointRounding.AwayFromZero)),
                Exercises:
                    detailed.Exercises
                        .Select(
                            exercise =>
                                new WorkoutExerciseInput(
                                    ExerciseId:
                                        exercise.ExerciseId,
                                    ExerciseName:
                                        exercise.ExerciseNameSnapshot,
                                    Mechanics:
                                        exercise.MechanicsSnapshot,
                                    MuscleTargets:
                                        targetsByExercise
                                            .TryGetValue(
                                                exercise.ExerciseId,
                                                out var exerciseTargets)
                                            ? exerciseTargets
                                            : Array.Empty<MuscleTargetInput>(),
                                    Sets:
                                        exercise.Sets
                                            .OrderBy(
                                                set => set.SetOrder)
                                            .Select(
                                                set =>
                                                    new WorkoutSetInput(
                                                        set.Reps,
                                                        set.Load,
                                                        set.Rpe,
                                                        set.Rir,
                                                        set.RestSeconds,
                                                        set.DurationSeconds,
                                                        set.IsWarmup))
                                            .ToArray()))
                        .ToArray()));
    }

    private static decimal ResolveReviewRpe(
        WorkoutSession session)
    {
        if (session.SessionRpe is >= 1m and <= 10m)
            return session.SessionRpe.Value;

        var baseRpe = 5m;
        var engineCatalog =
            new Engine30WorkoutTypeCatalog();

        if (engineCatalog.TryGet(
                session.WorkoutTypeCode,
                out var engineDefinition)
            && engineDefinition is not null)
        {
            baseRpe =
                engineDefinition.DefaultRpe;
        }

        if (!string.IsNullOrWhiteSpace(
                session.SystemWorkoutCode)
            && SystemWorkoutRecipeCatalog.TryGet(
                session.SystemWorkoutCode,
                out var recipe)
            && recipe is not null)
        {
            return Math.Clamp(
                SystemWorkoutRecipeCatalog.ResolveDefaultRpe(
                    recipe,
                    baseRpe),
                1m,
                10m);
        }

        return Math.Clamp(
            baseRpe,
            1m,
            10m);
    }
}
