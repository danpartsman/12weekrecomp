using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;
using WebRecomp.WorkoutIntelligence.Persistence;

namespace WebRecomp.Web.Controllers;

public sealed class ExerciseLibraryController(
    AppUserContextService userContext,
    IExerciseCatalogStore catalogStore) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? q,
        CancellationToken cancellationToken = default)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var model =
            new ExerciseLibraryViewModel
            {
                FeatureEnabled =
                    context.Program.DetailedWorkoutEntryEnabled,
                Query = q?.Trim()
            };

        if (!model.FeatureEnabled)
            return View(model);

        var rows =
            await catalogStore.SearchAsync(
                model.Query,
                string.IsNullOrWhiteSpace(model.Query) ? 60 : 100,
                cancellationToken);

        model.Exercises =
            rows.Select(
                    x =>
                        new ExerciseCatalogRowViewModel
                        {
                            ExerciseId = x.ExerciseId,
                            Name = x.Name,
                            Mechanics = x.Mechanics,
                            Category = x.Category,
                            DifficultyLevel = x.DifficultyLevel,
                            PrimaryMuscles = x.PrimaryMuscles,
                            Equipment = x.Equipment
                        })
                .ToArray();

        return View(model);
    }
}
