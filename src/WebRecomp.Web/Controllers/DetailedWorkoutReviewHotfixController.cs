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
// This literal route has higher route precedence than the conventional
// /{controller}/{action} route and renders the same Review view without that
// duplicate-key projection. Finalization continues through the existing
// DetailedWorkoutController.FinalizeWorkout action.
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
                "Complete",
                "DetailedWorkout",
                new
                {
                    workoutSessionId
                });
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

        // Preserve the existing 10-minute review deadline. If the deadline is
        // already past, Review.cshtml immediately submits the existing finalize
        // form; no duplicate-key dictionary is required.
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
