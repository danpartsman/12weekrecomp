using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.NutritionIntelligence.Models;
using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.Nutrition;
using WebRecomp.Web.Services.Search;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

public sealed class NutritionController(
    RecompDbContext db,
    AppUserContextService userContext,
    NutritionFoodSearchService foodSearch,
    INutritionFoodLogStore nutritionStore,
    IConfiguration configuration,
    Engine30DashboardProjectionService engine30Projection) : Controller
{
    private const string ManualOverrideSourceCode = "MANUAL_OVERRIDE";

    private static readonly string[] MealSlots =
        ["BREAKFAST", "LUNCH", "DINNER", "SNACK"];

    [HttpGet]
    public async Task<IActionResult> Index(
        DateOnly? date,
        string? foodQuery,
        string? foodSort,
        string? selectedProvider,
        string? selectedFoodId,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        var targetDate =
            date
            ?? AppUserContextService.GetClientToday(
                context.Client);

        if (context.Program.NutritionFoodLogEnabled)
        {
            await SyncFoodLogToDailyEntryAsync(
                context,
                targetDate,
                cancellationToken);
        }

        var row =
            await db.NutritionDays
                .SingleOrDefaultAsync(
                    x =>
                        x.ProgramId == context.Program.ProgramId
                        && x.NutritionDate == targetDate,
                    cancellationToken);

        var manual =
            new NutritionViewModel
            {
                Date = targetDate,
                ActualCalories = row?.ActualCalories,
                ActualProteinG = row?.ActualProteinG,
                ActualCarbsG = row?.ActualCarbsG,
                ActualFatG = row?.ActualFatG,
                FiberG = row?.FiberG,
                WaterOz = row?.WaterOz,
                Notes = row?.Notes
            };

        return View(
            await BuildPageAsync(
                context,
                manual,
                foodQuery,
                foodSort,
                selectedProvider,
                selectedFoodId,
                cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> FoodMeasures(
        string providerCode,
        string sourceFoodId,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
        {
            return Unauthorized(
                new
                {
                    success = false,
                    message = "Client context is unavailable."
                });
        }

        if (!context.Program.NutritionFoodLogEnabled)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(providerCode)
            || string.IsNullOrWhiteSpace(sourceFoodId))
        {
            return BadRequest(
                new
                {
                    success = false,
                    message = "Food provider and food ID are required."
                });
        }

        try
        {
            var resolved =
                await foodSearch.GetFoodAsync(
                    providerCode,
                    sourceFoodId,
                    cancellationToken);

            var selected =
                BuildSelectedFood(
                    resolved.Food.Food);

            var completeMeasures =
                selected.Measures
                    .Where(HasCompleteCoreNutrition)
                    .ToList();

            if (completeMeasures.Count == 0)
            {
                return BadRequest(
                    new
                    {
                        success = false,
                        message =
                            "This provider food does not contain a complete calorie/protein/carbohydrate/fat set."
                    });
            }

            var defaultMeasureCode =
                completeMeasures.Any(x =>
                    string.Equals(
                        x.Code,
                        selected.DefaultMeasureCode,
                        StringComparison.OrdinalIgnoreCase))
                    ? selected.DefaultMeasureCode
                    : completeMeasures[0].Code;

            return Json(
                new
                {
                    success = true,
                    defaultMeasureCode,
                    measures =
                        completeMeasures.Select(
                            x => new
                            {
                                code = x.Code,
                                description = x.Description,
                                unitCode = x.UnitCode,
                                energyKcal = x.EnergyKcal,
                                proteinG = x.ProteinG,
                                carbohydrateG =
                                    x.CarbohydrateG,
                                fatG = x.FatG
                            })
                });
        }
        catch (Exception ex)
        {
            return BadRequest(
                new
                {
                    success = false,
                    message =
                        SafeUiError(
                            ex,
                            "Unable to load serving choices for that food.")
                });
        }
    }


    [HttpGet]
    public async Task<IActionResult> FoodPickerData(
        DateOnly date,
        string mealSlotCode,
        string? query,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
        {
            return Unauthorized(
                new
                {
                    success = false,
                    message = "Client context is unavailable."
                });
        }

        if (!context.Program.NutritionFoodLogEnabled)
            return Forbid();

        mealSlotCode = NormalizeMealSlot(mealSlotCode);

        var history =
            new NutritionFoodPickerHistoryStore(
                configuration);

        var pastMeals =
            await history.GetPastMealsAsync(
                context.Client.ClientId,
                date,
                mealSlotCode,
                take: 12,
                cancellationToken: cancellationToken);

        var historyFoods =
            await history.GetFrequentFoodsAsync(
                context.Client.ClientId,
                date,
                query: null,
                take: 80,
                cancellationToken: cancellationToken);

        var normalizedQuery =
            string.IsNullOrWhiteSpace(query)
                ? null
                : query.Trim();
        // ============================================================
        // B2.31.1 WORD-AWARE NUTRITION SEARCH
        // Words match independently of order and punctuation.
        // ============================================================
        static string[] FoodSearchWords(
            string value)
        {
            var normalized =
                new string(
                    value
                        .Select(ch =>
                            char.IsLetterOrDigit(ch)
                                ? char.ToLowerInvariant(ch)
                                : ' ')
                        .ToArray());

            return normalized
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .Where(x => x.Length >= 2)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        static int WordAwareFoodScore(
            string queryText,
            params string?[] candidateParts)
        {
            // Preserve the existing fuzzy behavior.
            var fuzzy =
                FuzzySearch.Score(
                    queryText,
                    candidateParts);

            var queryWords =
                FoodSearchWords(queryText);

            if (queryWords.Length == 0)
                return fuzzy;

            var candidateWords =
                candidateParts
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x))
                    .SelectMany(x =>
                        FoodSearchWords(x!))
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            var matched =
                queryWords.Count(
                    word =>
                        candidateWords.Contains(word));

            if (matched == 0)
                return fuzzy;

            var coverage =
                (decimal)matched
                / queryWords.Length;

            // ALL words in any order should rank very highly.
            // A partial word hit is allowed, but ranks lower.
            var wordScore =
                matched == queryWords.Length
                    ? 96
                    : 52
                      + (int)Math.Round(
                          38m * coverage,
                          MidpointRounding.AwayFromZero);

            return Math.Max(
                fuzzy,
                wordScore);
        }

        static IReadOnlyList<string>
            BuildWordAwareFoodQueries(
                string queryText)
        {
            var queries =
                new List<string>();

            void Add(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                var trimmed =
                    value.Trim();

                if (!queries.Contains(
                        trimmed,
                        StringComparer.OrdinalIgnoreCase))
                {
                    queries.Add(trimmed);
                }
            }

            // Full query first.
            Add(queryText);

            var words =
                FoodSearchWords(queryText);

            // Then reverse the word order.
            // Example: ground beef -> beef ground
            if (words.Length > 1)
            {
                Add(
                    string.Join(
                        " ",
                        words.Reverse()));
            }

            // Then search each meaningful word separately.
            foreach (var word in words)
                Add(word);

            // Finally retain the existing fuzzy variants.
            foreach (
                var variant in
                FuzzySearch.BuildQueryVariants(
                    queryText,
                    maxVariants: 4))
            {
                Add(variant);
            }

            return queries
                .Take(8)
                .ToArray();
        }


        var frequentFoods =
            normalizedQuery is null
                ? historyFoods
                    .Take(30)
                    .ToArray()
                : historyFoods
                    .Select(x => new
                    {
                        Item = x,
                        Score = WordAwareFoodScore(
                            normalizedQuery,
                            x.Description,
                            x.BrandName)
                    })
                    .Where(x => x.Score >= 48)
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.Item.UseCount)
                    .ThenBy(x => x.Item.Description)
                    .Take(30)
                    .Select(x => x.Item)
                    .ToArray();

        var foods = new List<object>();

        foreach (var item in frequentFoods)
        {
            foods.Add(
                new
                {
                    kind =
              !string.IsNullOrWhiteSpace(item.ProviderCode)
              && !string.IsNullOrWhiteSpace(item.SourceFoodId)
                  ? "provider"
                  : "pastFood",
                    sourceDailyLogItemId =
                        item.SourceDailyLogItemId,
                    description = item.Description,
                    brandName = item.BrandName,
                    usageCount = item.UseCount,
                    quantity = item.Quantity,
                    quantityUnitCode =
                        item.QuantityUnitCode,
                    energyKcal = item.EnergyKcal,
                    proteinG = item.ProteinG,
                    carbohydrateG =
                        item.CarbohydrateG,
                    fatG = item.FatG,
                    providerCode =
                        item.ProviderCode,
                    sourceFoodId =
                        item.SourceFoodId
                });
        }

        var customFoods =
            await nutritionStore.GetCustomFoodsAsync(
                context.Client.ClientId,
                cancellationToken);

        var frequentLabels =
            frequentFoods
                .Select(x =>
                    $"{x.Description}|{x.BrandName}")
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var rankedCustomFoods =
            customFoods
                .Where(x =>
                    !frequentLabels.Contains(
                        $"{x.FoodName}|{x.BrandName}"))
                .Select(x => new
                {
                    Food = x,
                    Score =
                        normalizedQuery is null
                            ? 100
                            : WordAwareFoodScore(
                                normalizedQuery,
                                x.FoodName,
                                x.BrandName)
                })
                .Where(x =>
                    normalizedQuery is null
                    || x.Score >= 48)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Food.FoodName)
                .Take(12)
                .Select(x => x.Food)
                .ToArray();

        foreach (var food in rankedCustomFoods)
        {
            foods.Add(
                new
                {
                    kind = "customFood",
                    customFoodId =
                        food.CustomFoodId,
                    description =
                        food.FoodName,
                    brandName =
                        food.BrandName,
                    servingSize =
                        food.ServingSize,
                    servingUnitCode =
                        food.ServingUnitCode,
                    usageCount = 0,
                    quantity =
                        (decimal?)null,
                    quantityUnitCode =
                        (string?)null,
                    energyKcal =
                        food.EnergyKcal,
                    proteinG =
                        food.ProteinG,
                    carbohydrateG =
                        food.CarbohydrateG,
                    fatG =
                        food.FatG,
                    providerCode =
                        (string?)null,
                    sourceFoodId =
                        (string?)null
                });
        }

        var warning = string.Empty;

        if (normalizedQuery is not null
            && normalizedQuery.Length >= 2)
        {
            var queryVariants =
                BuildWordAwareFoodQueries(
                    normalizedQuery);

            var search =
                await foodSearch.SearchAsync(
                    queryVariants[0],
                    NutritionFoodSortCodes.BestMatch,
                    cancellationToken);

            var providerCandidates =
                search.Results.ToList();

            var providerWarnings =
                search.Warnings.ToList();

            // If the exact USDA query is thin, use a few token/subphrase
            // fallbacks and fuzzy-rank the merged candidates against the
            // user's full query. This catches common typos and word-order
            // differences without fabricating nutrition records.
            if (queryVariants.Count > 1)
            {
                foreach (var variant in queryVariants.Skip(1))
                {
                    var fallback =
                        await foodSearch.SearchAsync(
                            variant,
                            NutritionFoodSortCodes.BestMatch,
                            cancellationToken);

                    providerCandidates.AddRange(
                        fallback.Results);

                    providerWarnings.AddRange(
                        fallback.Warnings);

                    if (providerCandidates.Count >= 36)
                        break;
                }
            }

            warning =
                providerWarnings.Count == 0
                    ? string.Empty
                    : string.Join(
                        " ",
                        providerWarnings.Distinct());

            var frequentExternalKeys =
                frequentFoods
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(
                            x.ProviderCode)
                        && !string.IsNullOrWhiteSpace(
                            x.SourceFoodId))
                    .Select(x =>
                        $"{x.ProviderCode}|{x.SourceFoodId}")
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            const int maxProviderCandidates = 24;
            const int maxValidatedProviderFoods = 12;

            var validatedProviderFoods = 0;

            foreach (
                var food in
                providerCandidates
                    .Where(x =>
                        !frequentExternalKeys.Contains(
                            $"{x.ProviderCode}|{x.SourceFoodId}"))
                    .GroupBy(
                        x =>
                            $"{x.ProviderCode}|{x.SourceFoodId}",
                        StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .GroupBy(
                        x =>
                            $"{x.Description}|{x.BrandOwner}|{x.DataType}",
                        StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .Select(x => new
                    {
                        Food = x,
                        Score = WordAwareFoodScore(
                            normalizedQuery,
                            x.Description,
                            x.BrandOwner,
                            x.DataType)
                    })
                    .Where(x => x.Score >= 48)
                    .OrderBy(x => x.Food.ProviderPriority)
                    .ThenByDescending(x => x.Score)
                    .ThenBy(x => x.Food.Description)
                    .Take(maxProviderCandidates)
                    .Select(x => x.Food))
            {
                if (validatedProviderFoods
                    >= maxValidatedProviderFoods)
                {
                    break;
                }

                try
                {
                    var resolved =
                        await foodSearch.GetFoodAsync(
                            food.ProviderCode,
                            food.SourceFoodId,
                            cancellationToken);

                    var selected =
                        BuildSelectedFood(
                            resolved.Food.Food);

                    var preview =
                        selected.Measures
                            .Where(HasCompleteCoreNutrition)
                            .FirstOrDefault(x =>
                                string.Equals(
                                    x.Code,
                                    selected.DefaultMeasureCode,
                                    StringComparison.OrdinalIgnoreCase))
                        ?? selected.Measures
                            .FirstOrDefault(
                                HasCompleteCoreNutrition);

                    if (preview is null)
                    {
                        continue;
                    }

                    foods.Add(
                        new
                        {
                            kind = "provider",
                            providerCode =
                                food.ProviderCode,
                            sourceFoodId =
                                food.SourceFoodId,
                            description =
                                food.Description,
                            brandName =
                                food.BrandOwner,
                            dataType =
                                food.DataType,
                            usageCount = 0,
                            quantity =
                                (decimal?)null,
                            quantityUnitCode =
                                (string?)null,
                            energyKcal =
                                preview.EnergyKcal,
                            proteinG =
                                preview.ProteinG,
                            carbohydrateG =
                                preview.CarbohydrateG,
                            fatG =
                                preview.FatG
                        });

                    validatedProviderFoods++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Picker quality rule:
                    // incomplete/unresolvable provider foods are hidden.
                }
            }

            if (validatedProviderFoods == 0
                && frequentFoods.Length == 0
                && string.IsNullOrWhiteSpace(warning))
            {
                warning =
                    "No foods with complete calories, protein, carbohydrates, and fat were found. Try a different search.";
            }
        }

        return Json(
            new
            {
                success = true,
                mealSlotCode,
                pastMeals =
                    pastMeals.Select(
                        x => new
                        {
                            dailyLogId =
                                x.DailyLogId,
                            logDate =
                                x.LogDate.ToString(
                                    "yyyy-MM-dd"),
                            mealSlotCode =
                                x.MealSlotCode,
                            itemCount =
                                x.ItemCount,
                            energyKcal =
                                x.EnergyKcal,
                            summary =
                                x.Summary
                        }),
                foods,
                warning
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPastFood(
        DateOnly date,
        Guid sourceDailyLogItemId,
        string mealSlotCode,
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

        mealSlotCode =
            NormalizeMealSlot(
                mealSlotCode);

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

            await nutritionStore.AddDailyLogItemAsync(
                context.Client.ClientId,
                date,
                context.Client.TimeZone,
                snapshot.ToWriteModel(
                    mealSlotCode),
                cancellationToken);

            await SyncFoodLogToDailyEntryAsync(
                context,
                date,
                cancellationToken);

            TempData["StatusMessage"] =
                $"{snapshot.Description} added to " +
                $"{MealSlotLabel(mealSlotCode)} from your food history.";
        }
        catch (Exception ex)
        {
            TempData["NutritionError"] =
                SafeUiError(
                    ex,
                    "Unable to add that past food.");
        }

        return RedirectToNutrition(date);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CopyYesterdayToToday(CancellationToken cancellationToken)
    {
        var context = await userContext.GetAsync(HttpContext, cancellationToken);
        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");
        if (!context.Program.NutritionFoodLogEnabled) return Forbid();
        var today = AppUserContextService.GetClientToday(context.Client);
        try
        {
            var result = await nutritionStore.CopyYesterdayToTodayAsync(
                context.Client.ClientId, today, context.Client.TimeZone, cancellationToken);
            if (result.SourceItems > 0)
                await SyncFoodLogToDailyEntryAsync(context, today, cancellationToken);
            TempData["StatusMessage"] = result.SourceItems == 0
                ? "No meals were logged yesterday. Add today's foods normally."
                : result.CopiedItems == 0
                ? "Yesterday's meals are already in today's log. No duplicate entries were added."
                : $"Copied {result.CopiedItems} food {(result.CopiedItems == 1 ? "entry" : "entries")} from yesterday to today. Meal slots and portions were kept; matching entries were skipped.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TempData["NutritionError"] = SafeUiError(ex, "Unable to copy yesterday's meals. Please try again.");
        }
        return RedirectToNutrition(today);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CopyPastMeal(
        DateOnly date,
        Guid sourceDailyLogId,
        string sourceMealSlotCode,
        string targetMealSlotCode,
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

        sourceMealSlotCode =
            NormalizeMealSlot(
                sourceMealSlotCode);

        targetMealSlotCode =
            NormalizeMealSlot(
                targetMealSlotCode);

        try
        {
            var history =
                new NutritionFoodPickerHistoryStore(
                    configuration);

            var items =
                await history.GetPastMealItemsAsync(
                    context.Client.ClientId,
                    sourceDailyLogId,
                    date,
                    sourceMealSlotCode,
                    cancellationToken);

            if (items.Count == 0)
            {
                throw new InvalidOperationException(
                    "That past meal is no longer available.");
            }

            foreach (var item in items)
            {
                await nutritionStore.AddDailyLogItemAsync(
                    context.Client.ClientId,
                    date,
                    context.Client.TimeZone,
                    item.ToWriteModel(
                        targetMealSlotCode),
                    cancellationToken);
            }

            await SyncFoodLogToDailyEntryAsync(
                context,
                date,
                cancellationToken);

            TempData["StatusMessage"] =
                $"{items.Count} food" +
                $"{(items.Count == 1 ? string.Empty : "s")} copied to " +
                $"{MealSlotLabel(targetMealSlotCode)}.";
        }
        catch (Exception ex)
        {
            TempData["NutritionError"] =
                SafeUiError(
                    ex,
                    "Unable to copy that past meal.");
        }

        return RedirectToNutrition(date);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDailyMealPrep(
        DateOnly date,
        string sourceMealSlotCode,
        string targetMealSlotCode,
        List<DateOnly> targetDates,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(HttpContext, cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!context.Program.NutritionFoodLogEnabled)
            return Forbid();

        if (!MealSlots.Contains(sourceMealSlotCode)
            || !MealSlots.Contains(targetMealSlotCode)
            || targetDates is null
            || targetDates.Count is < 1 or > 7
            || targetDates.Distinct().Count() != targetDates.Count
            || targetDates.Any(day =>
                day <= date || day > date.AddDays(7)))
        {
            TempData["NutritionError"] =
                "Choose a logged meal, a meal slot, and days within the next week.";
            return RedirectToNutrition(date);
        }

        try
        {
            var source =
                await nutritionStore.GetDailyLogAsync(
                    context.Client.ClientId,
                    date,
                    cancellationToken);

            if (source.DailyLogId is not Guid sourceId)
                throw new InvalidOperationException(
                    "Add the prepared meal to this day's Food Log first.");

            var history =
                new NutritionFoodPickerHistoryStore(configuration);
            var items =
                await history.GetPastMealItemsAsync(
                    context.Client.ClientId,
                    sourceId,
                    date.AddDays(1),
                    sourceMealSlotCode,
                    cancellationToken);

            if (items.Count == 0
                || source.Items.Count(item =>
                    item.MealSlotCode == sourceMealSlotCode) != items.Count)
                throw new InvalidOperationException(
                    "Add a complete meal with calories and macros before scheduling it.");

            // Validate every destination before making any changes.
            foreach (var day in targetDates)
            {
                var existing =
                    await nutritionStore.GetDailyLogAsync(
                        context.Client.ClientId,
                        day,
                        cancellationToken);

                if (existing.Items.Any(item =>
                    item.MealSlotCode == targetMealSlotCode))
                {
                    throw new InvalidOperationException(
                        $"{MealSlotLabel(targetMealSlotCode)} on {day:MMM d} already has food. Choose an empty meal slot.");
                }
            }

            foreach (var day in targetDates.Order())
            {
                foreach (var item in items)
                {
                    await nutritionStore.AddDailyLogItemAsync(
                        context.Client.ClientId,
                        day,
                        context.Client.TimeZone,
                        item.ToWriteModel(targetMealSlotCode),
                        cancellationToken);
                }

                await SyncFoodLogToDailyEntryAsync(
                    context,
                    day,
                    cancellationToken);
            }

            TempData["StatusMessage"] =
                $"Meal prep added to {targetDates.Count} day" +
                (targetDates.Count == 1 ? "." : "s.");
        }
        catch (Exception ex)
        {
            TempData["NutritionError"] =
                SafeUiError(ex, "Unable to schedule that meal.");
        }

        return RedirectToNutrition(date);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        NutritionViewModel model,
        bool nutritionManualOverride,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null || context.Program is null)
            return RedirectToAction("Index", "Intake");

        if (!ModelState.IsValid)
        {
            if (IsAjaxRequest())
            {
                return BadRequest(
                    new
                    {
                        success = false,
                        message = FirstModelError(
                            "Check the nutrition values.")
                    });
            }

            return View(
                await BuildPageAsync(
                    context,
                    model,
                    foodQuery: null,
                    foodSort: null,
                    selectedProvider: null,
                    selectedFoodId: null,
                    cancellationToken));
        }

        NutritionDailyLogReadModel? detailed = null;

        if (context.Program.NutritionFoodLogEnabled)
        {
            detailed =
                await nutritionStore.GetDailyLogAsync(
                    context.Client.ClientId,
                    model.Date,
                    cancellationToken);
        }

        var foodLogAvailable =
            detailed?.Totals is not null
            && detailed.Items.Count > 0;

        NutritionDay? row;

        if (context.Program.NutritionFoodLogEnabled
            && foodLogAvailable
            && !nutritionManualOverride)
        {
            await SyncFoodLogToDailyEntryAsync(
                context,
                model.Date,
                cancellationToken,
                replaceManualOverride: true);

            row =
                await db.NutritionDays
                    .SingleOrDefaultAsync(
                        x =>
                            x.ClientId == context.Client.ClientId
                            && x.ProgramId == context.Program.ProgramId
                            && x.NutritionDate == model.Date,
                        cancellationToken);

            if (row is not null)
            {
                row.WaterOz = model.WaterOz;
                row.Notes = model.Notes;
                row.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            row =
                await db.NutritionDays
                    .SingleOrDefaultAsync(
                        x =>
                            x.ClientId == context.Client.ClientId
                            && x.ProgramId == context.Program.ProgramId
                            && x.NutritionDate == model.Date,
                        cancellationToken);

            if (row is null)
            {
                row =
                    new NutritionDay
                    {
                        NutritionDayId = Guid.NewGuid(),
                        ClientId = context.Client.ClientId,
                        ProgramId = context.Program.ProgramId,
                        NutritionDate = model.Date,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                db.NutritionDays.Add(row);
            }

            row.ActualCalories = model.ActualCalories;
            row.ActualProteinG = model.ActualProteinG;
            row.ActualCarbsG = model.ActualCarbsG;
            row.ActualFatG = model.ActualFatG;
            row.FiberG = model.FiberG;
            row.WaterOz = model.WaterOz;
            row.Notes = model.Notes;
            row.EntrySourceCode =
                context.Program.NutritionFoodLogEnabled
                && foodLogAvailable
                    ? ManualOverrideSourceCode
                    : RecompInputSourceCodes.Manual;
            row.SourceRecordId = null;
            row.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
        }

        var requiredComplete =
            row is not null
            && row.ActualCalories.HasValue
            && row.ActualProteinG.HasValue
            && row.ActualCarbsG.HasValue
            && row.ActualFatG.HasValue;

        var sourceMessage =
            context.Program.NutritionFoodLogEnabled
            && foodLogAvailable
            && !nutritionManualOverride
                ? "Food Log totals are driving Daily Entry nutrition."
                : context.Program.NutritionFoodLogEnabled
                  && foodLogAvailable
                    ? "Manual nutrition override saved."
                    : "Daily Entry nutrition saved.";

        if (IsAjaxRequest())
        {
            return Json(
                new
                {
                    success = true,
                    requiredComplete,
                    message = sourceMessage
                });
        }

        TempData["StatusMessage"] = sourceMessage;
        return RedirectToNutrition(model.Date);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddExternalFood(
        DateOnly date,
        string providerCode,
        string sourceFoodId,
        decimal amount,
        string measureCode,
        string mealSlotCode,
        string? foodQuery,
        string? foodSort,
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

        if (amount is <= 0m or > 1000m)
        {
            TempData["NutritionError"] =
                "Enter an amount greater than 0 and no more than 1000.";

            return RedirectToNutrition(date, foodQuery, foodSort);
        }

        mealSlotCode = NormalizeMealSlot(mealSlotCode);

        try
        {
            var resolved =
                await foodSearch.GetFoodAsync(
                    providerCode,
                    sourceFoodId,
                    cancellationToken);

            var measure =
                (resolved.Food.Food.Measures
                    ?? Array.Empty<FoodMeasureOption>())
                .FirstOrDefault(
                    x => string.Equals(
                        x.Code,
                        measureCode,
                        StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    "Choose a valid serving or measure for this food.");

            var facts =
                NutritionFactsCalculator.ForMeasure(
                    resolved.Food.Food,
                    measure,
                    amount);

            if (!facts.EnergyKcal.HasValue
                || !facts.ProteinG.HasValue
                || !facts.CarbohydrateG.HasValue
                || !facts.FatG.HasValue)
            {
                throw new InvalidOperationException(
                    "This provider food does not contain a complete calorie/protein/carbohydrate/fat set.");
            }

            var externalFoodId =
                resolved.ExternalFoodId
                ?? await nutritionStore.UpsertExternalFoodAsync(
                    resolved.Food,
                    cancellationToken);

            var loggedQuantity =
                decimal.Round(
                    amount * measure.QuantityPerSelection,
                    4,
                    MidpointRounding.AwayFromZero);

            decimal? gramWeight =
                string.Equals(
                    measure.BasisUnit,
                    "g",
                    StringComparison.OrdinalIgnoreCase)
                    ? decimal.Round(
                        amount * measure.BasisQuantityPerSelection,
                        4,
                        MidpointRounding.AwayFromZero)
                    : null;

            await nutritionStore.AddDailyLogItemAsync(
                context.Client.ClientId,
                date,
                context.Client.TimeZone,
                new DailyLogItemWriteModel(
                    mealSlotCode,
                    ItemOrder: 0,
                    new NutritionFoodReference(
                        ExternalFoodId: externalFoodId),
                    resolved.Food.Food.Description,
                    loggedQuantity,
                    NormalizeUnit(measure.UnitCode),
                    gramWeight,
                    NutritionPersistencePolicy.SnapshotPolicyFor(
                        resolved.Policy.CachePolicyCode,
                        isUserEntered: false),
                    facts.EnergyKcal,
                    facts.ProteinG,
                    facts.CarbohydrateG,
                    facts.FatG,
                    facts.FiberG,
                    facts.SugarG,
                    facts.SodiumMg,
                    facts.SaturatedFatG,
                    SourceSavedMealId: null,
                    SourceServingId:
                        ExtractSourceServingId(
                            resolved.Food.Food.ProviderCode,
                            measure.Code)),
                cancellationToken);

            await SyncFoodLogToDailyEntryAsync(
                context,
                date,
                cancellationToken);

            TempData["StatusMessage"] =
                $"{resolved.Food.Food.Description} added to " +
                $"{MealSlotLabel(mealSlotCode)}. Daily Entry was synchronized " +
                "unless that date is using Manual Override.";
        }
        catch (Exception ex)
        {
            TempData["NutritionError"] =
                SafeUiError(
                    ex,
                    "Unable to add that provider food.");
        }

        return RedirectToNutrition(date, foodQuery, foodSort);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCustomFood(
        DateOnly date,
        string foodName,
        string? brandName,
        decimal servingSize,
        string servingUnitCode,
        decimal energyKcal,
        decimal proteinG,
        decimal carbohydrateG,
        decimal fatG,
        decimal? fiberG,
        bool addToToday,
        string mealSlotCode,
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

        if (string.IsNullOrWhiteSpace(foodName)
            || servingSize <= 0
            || energyKcal < 0
            || proteinG < 0
            || carbohydrateG < 0
            || fatG < 0
            || (fiberG.HasValue && fiberG.Value < 0))
        {
            TempData["NutritionError"] =
                "Check the custom food name, serving, and nutrition values.";

            return RedirectToNutrition(date);
        }

        var nutrients =
            new List<CanonicalNutrientWriteModel>
            {
                new(
                    CanonicalNutrientCodes.EnergyKcal,
                    energyKcal,
                    "kcal"),
                new(
                    CanonicalNutrientCodes.ProteinG,
                    proteinG,
                    "g"),
                new(
                    CanonicalNutrientCodes.CarbohydrateG,
                    carbohydrateG,
                    "g"),
                new(
                    CanonicalNutrientCodes.FatG,
                    fatG,
                    "g")
            };

        if (fiberG.HasValue)
        {
            nutrients.Add(
                new CanonicalNutrientWriteModel(
                    CanonicalNutrientCodes.FiberG,
                    fiberG.Value,
                    "g"));
        }

        try
        {
            var customFoodId =
                await nutritionStore.SaveCustomFoodAsync(
                    context.Client.ClientId,
                    new CustomFoodWriteModel(
                        foodName.Trim(),
                        string.IsNullOrWhiteSpace(brandName)
                            ? null
                            : brandName.Trim(),
                        servingSize,
                        NormalizeUnit(servingUnitCode),
                        HouseholdServingText: "1 serving",
                        Nutrients: nutrients),
                    cancellationToken);

            if (addToToday)
            {
                mealSlotCode = NormalizeMealSlot(mealSlotCode);

                await nutritionStore.AddDailyLogItemAsync(
                    context.Client.ClientId,
                    date,
                    context.Client.TimeZone,
                    new DailyLogItemWriteModel(
                        mealSlotCode,
                        ItemOrder: 0,
                        new NutritionFoodReference(
                            CustomFoodId: customFoodId),
                        foodName.Trim(),
                        1m,
                        "serving",
                        string.Equals(
                            servingUnitCode,
                            "g",
                            StringComparison.OrdinalIgnoreCase)
                                ? servingSize
                                : null,
                        NutritionSnapshotPolicyCodes.UserEntered,
                        energyKcal,
                        proteinG,
                        carbohydrateG,
                        fatG,
                        fiberG,
                        SugarG: null,
                        SodiumMg: null,
                        SaturatedFatG: null),
                    cancellationToken);

                await SyncFoodLogToDailyEntryAsync(
                    context,
                    date,
                    cancellationToken);
            }

            TempData["StatusMessage"] =
                addToToday
                    ? "Custom food created and added. Daily Entry was synchronized unless Manual Override is active."
                    : "Custom food created.";
        }
        catch (Exception ex)
        {
            TempData["NutritionError"] =
                SafeUiError(
                    ex,
                    "Unable to create the custom food.");
        }

        return RedirectToNutrition(date);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCustomFood(
        DateOnly date,
        Guid customFoodId,
        decimal servings,
        string mealSlotCode,
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

        if (servings is <= 0 or > 100)
        {
            TempData["NutritionError"] =
                "Enter a serving amount between 0 and 100.";

            return RedirectToNutrition(date);
        }

        var customFoods =
            await nutritionStore.GetCustomFoodsAsync(
                context.Client.ClientId,
                cancellationToken);

        var food =
            customFoods.FirstOrDefault(
                x => x.CustomFoodId == customFoodId);

        if (food is null)
        {
            TempData["NutritionError"] =
                "Custom food was not found.";

            return RedirectToNutrition(date);
        }

        mealSlotCode = NormalizeMealSlot(mealSlotCode);

        await nutritionStore.AddDailyLogItemAsync(
            context.Client.ClientId,
            date,
            context.Client.TimeZone,
            new DailyLogItemWriteModel(
                mealSlotCode,
                ItemOrder: 0,
                new NutritionFoodReference(
                    CustomFoodId: customFoodId),
                food.FoodName,
                servings,
                "serving",
                string.Equals(
                    food.ServingUnitCode,
                    "g",
                    StringComparison.OrdinalIgnoreCase)
                        ? food.ServingSize * servings
                        : null,
                NutritionSnapshotPolicyCodes.UserEntered,
                Scale(food.EnergyKcal, servings),
                Scale(food.ProteinG, servings),
                Scale(food.CarbohydrateG, servings),
                Scale(food.FatG, servings),
                Scale(food.FiberG, servings),
                Scale(food.SugarG, servings),
                Scale(food.SodiumMg, servings),
                Scale(food.SaturatedFatG, servings)),
            cancellationToken);

        await SyncFoodLogToDailyEntryAsync(
            context,
            date,
            cancellationToken);

        TempData["StatusMessage"] =
            $"{food.FoodName} added to {MealSlotLabel(mealSlotCode)}. " +
            "Daily Entry was synchronized unless Manual Override is active.";

        return RedirectToNutrition(date);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateLogItem(
        DateOnly date,
        Guid dailyLogItemId,
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

        mealSlotCode =
            NormalizeMealSlot(
                mealSlotCode);

        try
        {
            await nutritionStore.UpdateDailyLogItemAsync(
                context.Client.ClientId,
                dailyLogItemId,
                mealSlotCode,
                quantity,
                cancellationToken);

            await SyncFoodLogToDailyEntryAsync(
                context,
                date,
                cancellationToken);

            TempData["StatusMessage"] =
                "Food updated. Daily Entry was synchronized unless Manual Override is active.";
        }
        catch (Exception ex)
        {
            TempData["NutritionError"] =
                SafeUiError(
                    ex,
                    "Unable to update that food.");
        }

        return RedirectToNutrition(date);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLogItem(
        DateOnly date,
        Guid dailyLogItemId,
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

        try
        {
            await nutritionStore.DeleteDailyLogItemAsync(
                context.Client.ClientId,
                dailyLogItemId,
                cancellationToken);

            await SyncFoodLogToDailyEntryAsync(
                context,
                date,
                cancellationToken);

            TempData["StatusMessage"] =
                "Food removed. Daily Entry was synchronized unless Manual Override is active.";
        }
        catch (Exception ex)
        {
            TempData["NutritionError"] =
                SafeUiError(
                    ex,
                    "Unable to remove that food.");
        }

        return RedirectToNutrition(date);
    }

    private async Task<NutritionIntelligencePageViewModel> BuildPageAsync(
        AppClientContext context,
        NutritionViewModel manual,
        string? foodQuery,
        string? foodSort,
        string? selectedProvider,
        string? selectedFoodId,
        CancellationToken cancellationToken)
    {
        if (context.Client is not null)
            ViewData["NutritionToday"] = AppUserContextService.GetClientToday(context.Client);
        var normalizedSort =
            NutritionFoodSortCodes.Normalize(foodSort);

        var page =
            new NutritionIntelligencePageViewModel
            {
                Manual = manual,
                FoodLogEnabled =
                    context.Program?.NutritionFoodLogEnabled == true,
                FoodQuery = foodQuery,
                FoodSort = normalizedSort
            };

        if (!page.FoodLogEnabled
            || context.Client is null)
        {
            page.NutritionSourceCode = RecompInputSourceCodes.Manual;
            page.NutritionSourceLabel = "Manual entry";
            return page;
        }

        var engineNutrition =
            await db.NutritionDays
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.ClientId == context.Client.ClientId
                        && x.ProgramId == context.Program!.ProgramId
                        && x.NutritionDate == manual.Date,
                    cancellationToken);

        page.NutritionSourceCode =
            engineNutrition?.EntrySourceCode
            ?? RecompInputSourceCodes.Manual;
        page.NutritionSourceLabel =
            SourceLabel(page.NutritionSourceCode);
        page.NutritionManualOverride =
            string.Equals(
                page.NutritionSourceCode,
                ManualOverrideSourceCode,
                StringComparison.OrdinalIgnoreCase);

        try
        {
            var planProjection =
                await engine30Projection.BuildTodayTomorrowAsync(
                    context,
                    manual.Date,
                    cancellationToken);

            page.PlanTargets =
                new NutritionPlanTargetsViewModel
                {
                    EnergyKcal =
                        planProjection.Today.PrescribedEnergyKcal,
                    ProteinG =
                        planProjection.Today.ProteinG,
                    CarbohydrateG =
                        planProjection.Today.RecommendedCarbsG,
                    FatG =
                        planProjection.Today.FatG,
                    EngineVersionCode =
                        planProjection.Today.EngineVersionCode
                };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            page.PlanTargetWarning =
                SafeUiError(
                    ex,
                    "Plan targets are not available for this date.");
        }

        var dailyLog =
            await nutritionStore.GetDailyLogAsync(
                context.Client.ClientId,
                manual.Date,
                cancellationToken);

        page.FoodLogAvailable =
            dailyLog.Totals is not null
            && dailyLog.Items.Count > 0;

        page.LoggedItems =
            dailyLog.Items
                .Select(
                    x => new NutritionDailyLogItemViewModel
                    {
                        DailyLogItemId = x.DailyLogItemId,
                        MealSlotCode = x.MealSlotCode,
                        ItemOrder = x.ItemOrder,
                        Description = x.DescriptionSnapshot,
                        Quantity = x.Quantity,
                        QuantityUnitCode = x.QuantityUnitCode,
                        EnergyKcal = x.EnergyKcal,
                        ProteinG = x.ProteinG,
                        CarbohydrateG = x.CarbohydrateG,
                        FatG = x.FatG
                    })
                .ToList();

        if (dailyLog.Totals is not null)
        {
            page.LoggedTotals =
                new NutritionDailyTotalsViewModel
                {
                    EnergyKcal = dailyLog.Totals.EnergyKcal,
                    ProteinG = dailyLog.Totals.ProteinG,
                    CarbohydrateG =
                        dailyLog.Totals.CarbohydrateG,
                    FatG = dailyLog.Totals.FatG,
                    FiberG = dailyLog.Totals.FiberG,
                    CompletenessCode =
                        dailyLog.Totals.CompletenessCode
                };
        }

        var customFoods =
            await nutritionStore.GetCustomFoodsAsync(
                context.Client.ClientId,
                cancellationToken);

        page.CustomFoods =
            customFoods
                .Select(
                    x => new NutritionCustomFoodViewModel
                    {
                        CustomFoodId = x.CustomFoodId,
                        FoodName = x.FoodName,
                        BrandName = x.BrandName,
                        ServingSize = x.ServingSize,
                        ServingUnitCode = x.ServingUnitCode,
                        HouseholdServingText =
                            x.HouseholdServingText,
                        EnergyKcal = x.EnergyKcal,
                        ProteinG = x.ProteinG,
                        CarbohydrateG =
                            x.CarbohydrateG,
                        FatG = x.FatG,
                        FiberG = x.FiberG
                    })
                .ToList();

        if (!string.IsNullOrWhiteSpace(foodQuery))
        {
            var search =
                await foodSearch.SearchAsync(
                    foodQuery,
                    normalizedSort,
                    cancellationToken);

            page.SearchResults =
                search.Results.ToList();

            if (search.Warnings.Count > 0)
            {
                page.FoodSearchWarning =
                    string.Join(
                        " ",
                        search.Warnings);
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedProvider)
            && !string.IsNullOrWhiteSpace(selectedFoodId))
        {
            try
            {
                var selected =
                    await foodSearch.GetFoodAsync(
                        selectedProvider,
                        selectedFoodId,
                        cancellationToken);

                page.SelectedFood =
                    BuildSelectedFood(
                        selected.Food.Food);
            }
            catch (Exception ex)
            {
                page.FoodSearchWarning =
                    SafeUiError(
                        ex,
                        "Unable to load serving choices for that food.");
            }
        }

        return page;
    }

    private static bool HasCompleteCoreNutrition(
        NutritionFoodMeasureViewModel measure)
        => measure.EnergyKcal.HasValue
           && measure.ProteinG.HasValue
           && measure.CarbohydrateG.HasValue
           && measure.FatG.HasValue;

    private static NutritionSelectedFoodViewModel BuildSelectedFood(
        FoodCatalogItem food)
    {
        var measures =
            (food.Measures ?? Array.Empty<FoodMeasureOption>())
            .ToList();

        if (measures.Count == 0)
        {
            measures.Add(
                new FoodMeasureOption(
                    Code: "basis",
                    Description:
                        $"1 {food.NutrientBasisUnit}",
                    UnitCode: food.NutrientBasisUnit,
                    QuantityPerSelection: 1m,
                    BasisQuantityPerSelection: 1m,
                    BasisUnit: food.NutrientBasisUnit));
        }

        var result =
            new NutritionSelectedFoodViewModel
            {
                ProviderCode = food.ProviderCode,
                SourceFoodId = food.SourceFoodId,
                Description = food.Description,
                BrandOwner = food.BrandOwner,
                DataType = food.DataType,
                DefaultMeasureCode =
                    measures.FirstOrDefault(x =>
                        string.Equals(
                            x.Code,
                            "serving",
                            StringComparison.OrdinalIgnoreCase))?.Code
                    ?? measures.FirstOrDefault(x =>
                        x.Code.StartsWith(
                            "portion:",
                            StringComparison.OrdinalIgnoreCase))?.Code
                    ?? measures[0].Code
            };

        foreach (var measure in measures)
        {
            var facts =
                NutritionFactsCalculator.ForMeasure(
                    food,
                    measure,
                    1m);

            result.Measures.Add(
                new NutritionFoodMeasureViewModel
                {
                    Code = measure.Code,
                    Description = measure.Description,
                    UnitCode = measure.UnitCode,
                    QuantityPerSelection =
                        measure.QuantityPerSelection,
                    BasisQuantityPerSelection =
                        measure.BasisQuantityPerSelection,
                    BasisUnit = measure.BasisUnit,
                    EnergyKcal = facts.EnergyKcal,
                    ProteinG = facts.ProteinG,
                    CarbohydrateG =
                        facts.CarbohydrateG,
                    FatG = facts.FatG
                });
        }

        return result;
    }

    private async Task SyncFoodLogToDailyEntryAsync(
        AppClientContext context,
        DateOnly date,
        CancellationToken cancellationToken,
        bool replaceManualOverride = false)
    {
        if (context.Client is null
            || context.Program is null
            || !context.Program.NutritionFoodLogEnabled)
        {
            return;
        }

        var detailed =
            await nutritionStore.GetDailyLogAsync(
                context.Client.ClientId,
                date,
                cancellationToken);

        var row =
            await db.NutritionDays
                .SingleOrDefaultAsync(
                    x =>
                        x.ClientId == context.Client.ClientId
                        && x.ProgramId == context.Program.ProgramId
                        && x.NutritionDate == date,
                    cancellationToken);

        if (detailed.Totals is null
            || detailed.Items.Count == 0)
        {
            if (row is not null
                && string.Equals(
                    row.EntrySourceCode,
                    RecompInputSourceCodes.FoodLog,
                    StringComparison.OrdinalIgnoreCase))
            {
                row.ActualCalories = null;
                row.ActualProteinG = null;
                row.ActualCarbsG = null;
                row.ActualFatG = null;
                row.FiberG = null;
                row.EntrySourceCode =
                    RecompInputSourceCodes.Manual;
                row.SourceRecordId = null;
                row.UpdatedAtUtc = DateTime.UtcNow;

                await db.SaveChangesAsync(
                    cancellationToken);
            }

            return;
        }

        if (row is not null)
        {
            if (string.Equals(
                    row.EntrySourceCode,
                    ManualOverrideSourceCode,
                    StringComparison.OrdinalIgnoreCase)
                && !replaceManualOverride)
            {
                return;
            }

            if (!string.Equals(
                    row.EntrySourceCode,
                    RecompInputSourceCodes.Manual,
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    row.EntrySourceCode,
                    RecompInputSourceCodes.FoodLog,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        else
        {
            row =
                new NutritionDay
                {
                    NutritionDayId = Guid.NewGuid(),
                    ClientId = context.Client.ClientId,
                    ProgramId = context.Program.ProgramId,
                    NutritionDate = date,
                    EntrySourceCode =
                        RecompInputSourceCodes.FoodLog,
                    CreatedAtUtc = DateTime.UtcNow
                };

            db.NutritionDays.Add(row);
        }

        row.ActualCalories =
            detailed.Totals.EnergyKcal;
        row.ActualProteinG =
            detailed.Totals.ProteinG;
        row.ActualCarbsG =
            detailed.Totals.CarbohydrateG;
        row.ActualFatG =
            detailed.Totals.FatG;
        row.FiberG =
            detailed.Totals.FiberG;
        row.EntrySourceCode =
            RecompInputSourceCodes.FoodLog;
        row.SourceRecordId =
            detailed.DailyLogId?.ToString("D");
        row.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(
            cancellationToken);
    }

    private IActionResult RedirectToNutrition(
        DateOnly date,
        string? foodQuery = null,
        string? foodSort = null)
        => RedirectToAction(
            nameof(Index),
            new
            {
                date = date.ToString("yyyy-MM-dd"),
                foodQuery =
                    string.IsNullOrWhiteSpace(foodQuery)
                        ? null
                        : foodQuery,
                foodSort =
                    string.IsNullOrWhiteSpace(foodSort)
                        ? null
                        : NutritionFoodSortCodes.Normalize(foodSort)
            });

    private static string NormalizeMealSlot(string? value)
    {
        var normalized =
            string.IsNullOrWhiteSpace(value)
                ? "SNACK"
                : value.Trim().ToUpperInvariant();

        return MealSlots.Contains(
            normalized,
            StringComparer.Ordinal)
                ? normalized
                : "SNACK";
    }

    private static string MealSlotLabel(string code)
        => code switch
        {
            "BREAKFAST" => "breakfast",
            "LUNCH" => "lunch",
            "DINNER" => "dinner",
            _ => "snack"
        };

    private static string NormalizeUnit(string? value)
    {
        var unit =
            string.IsNullOrWhiteSpace(value)
                ? "serving"
                : value.Trim().ToLowerInvariant();

        return unit.Length <= 30
            ? unit
            : unit[..30];
    }

    private static decimal? Scale(
        decimal? value,
        decimal multiplier)
        => value.HasValue
            ? decimal.Round(
                value.Value * multiplier,
                4,
                MidpointRounding.AwayFromZero)
            : null;

    private static string SourceLabel(string? sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return "Manual entry";

        if (string.Equals(
                sourceCode,
                RecompInputSourceCodes.FoodLog,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Food Log";
        }

        if (string.Equals(
                sourceCode,
                ManualOverrideSourceCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Manual Override";
        }

        if (string.Equals(
                sourceCode,
                RecompInputSourceCodes.Manual,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Manual entry";
        }

        return sourceCode
            .Replace("_", " ")
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x =>
                char.ToUpperInvariant(x[0])
                + x[1..])
            .Aggregate((a, b) => $"{a} {b}");
    }

    private static string? ExtractSourceServingId(
        string providerCode,
        string measureCode)
    {
        if (!string.Equals(
                providerCode,
                NutritionProviderCodes.FatSecret,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        const string prefix = "serving:";

        return measureCode.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase)
            ? measureCode[prefix.Length..]
            : null;
    }

    private static string SafeUiError(
        Exception exception,
        string fallback)
    {
        var message = exception.Message;

        if (string.IsNullOrWhiteSpace(message))
            return fallback;

        if (message.Contains(
                "API key",
                StringComparison.OrdinalIgnoreCase))
        {
            return "A nutrition provider is not configured yet. Check the TEST provider credentials in Administration.";
        }

        return message.Length <= 220
            ? message
            : fallback;
    }

    private bool IsAjaxRequest()
        => string.Equals(
            Request.Headers["X-Requested-With"].ToString(),
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);

    private string FirstModelError(string fallback)
        => ModelState.Values
               .SelectMany(x => x.Errors)
               .Select(x => x.ErrorMessage)
               .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
           ?? fallback;
}
