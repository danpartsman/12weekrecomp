using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Nutrition;

namespace WebRecomp.Web.Controllers;

[Authorize]
[Route("NutritionAmount")]
public sealed class NutritionAmountController(
    AppUserContextService userContext,
    INutritionFoodLogStore nutritionStore,
    IConfiguration configuration) : Controller
{
    [HttpPost("AddPastFood")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPastFood(
        DateOnly date,
        Guid sourceDailyLogItemId,
        string mealSlotCode,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!context.Program.NutritionFoodLogEnabled)
            return Forbid();

        if (quantity is <= 0m or > 1000m)
        {
            TempData["NutritionError"] =
                "Enter an amount greater than 0 and no more than 1000.";

            return RedirectToNutrition(date);
        }

        mealSlotCode = NormalizeMealSlot(mealSlotCode);

        try
        {
            var history =
                new NutritionFoodPickerHistoryStore(
                    configuration);

            var snapshot =
                await history.GetFoodSnapshotAsync(
                    context.Client.ClientId,
                    sourceDailyLogItemId,
                    cancellationToken);

            if (snapshot is null)
            {
                throw new InvalidOperationException(
                    "That past food is no longer available.");
            }

            if (snapshot.Quantity <= 0m)
            {
                throw new InvalidOperationException(
                    "That past food has an invalid saved quantity.");
            }

            var factor =
                quantity / snapshot.Quantity;

            await nutritionStore.AddDailyLogItemAsync(
                context.Client.ClientId,
                date,
                context.Client.TimeZone,
                new DailyLogItemWriteModel(
                    mealSlotCode,
                    ItemOrder: 0,
                    snapshot.Food,
                    snapshot.Description,
                    quantity,
                    snapshot.QuantityUnitCode,
                    Scale(snapshot.GramWeight, factor),
                    snapshot.NutrientSnapshotPolicyCode,
                    Scale(snapshot.EnergyKcal, factor),
                    Scale(snapshot.ProteinG, factor),
                    Scale(snapshot.CarbohydrateG, factor),
                    Scale(snapshot.FatG, factor),
                    Scale(snapshot.FiberG, factor),
                    Scale(snapshot.SugarG, factor),
                    Scale(snapshot.SodiumMg, factor),
                    Scale(snapshot.SaturatedFatG, factor),
                    SourceSavedMealId: null,
                    SourceServingId: snapshot.SourceServingId),
                cancellationToken);

            TempData["StatusMessage"] =
                $"{snapshot.Description} added to " +
                $"{MealSlotLabel(mealSlotCode)} at " +
                $"{quantity:0.####} {snapshot.QuantityUnitCode}.";
        }
        catch
        {
            TempData["NutritionError"] =
                "Unable to add that food from history with the requested amount.";
        }

        return RedirectToNutrition(date);
    }

    private IActionResult RedirectToNutrition(DateOnly date)
        => RedirectToAction(
            "Index",
            "Nutrition",
            new
            {
                date = date.ToString("yyyy-MM-dd")
            });

    private static string NormalizeMealSlot(string? value)
    {
        var normalized =
            (value ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

        return normalized switch
        {
            "BREAKFAST" => "BREAKFAST",
            "LUNCH" => "LUNCH",
            "DINNER" => "DINNER",
            "SNACK" => "SNACK",
            _ => "BREAKFAST"
        };
    }

    private static string MealSlotLabel(string code)
        => code switch
        {
            "BREAKFAST" => "Breakfast",
            "LUNCH" => "Lunch",
            "DINNER" => "Dinner",
            _ => "Snack"
        };

    private static decimal Scale(
        decimal value,
        decimal factor)
        => decimal.Round(
            value * factor,
            4,
            MidpointRounding.AwayFromZero);

    private static decimal? Scale(
        decimal? value,
        decimal factor)
        => value.HasValue
            ? Scale(value.Value, factor)
            : null;
}
