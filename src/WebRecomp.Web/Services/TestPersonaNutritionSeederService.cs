using Microsoft.EntityFrameworkCore;
using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.Services;

/// <summary>
/// Adds item-level food-log history to the persistent synthetic TEST personas.
/// Core Engine nutrition remains authoritative in NutritionDays; these entries
/// make the Nutrition UI itself fully testable and are constructed to sum to
/// the same daily calories/macros already seeded for each persona.
/// </summary>
public sealed class TestPersonaNutritionSeederService(
    RecompDbContext db,
    INutritionFoodLogStore nutritionStore)
{
    private static readonly TestMealTemplate[] MealTemplates =
    [
        new(
            "BREAKFAST",
            "TEST Protein Oats & Berries",
            0.25m,
            450m,
            30m,
            55m,
            12m,
            8m),
        new(
            "LUNCH",
            "TEST Chicken Rice Bowl",
            0.30m,
            600m,
            45m,
            70m,
            15m,
            7m),
        new(
            "SNACK",
            "TEST Greek Yogurt Protein Snack",
            0.15m,
            300m,
            25m,
            30m,
            8m,
            4m),
        new(
            "DINNER",
            "TEST Salmon Potato Plate",
            0.30m,
            650m,
            45m,
            65m,
            22m,
            6m)
    ];

    public async Task SeedAllAsync(
        CancellationToken cancellationToken = default)
    {
        foreach (var spec in TestPersonaCatalog.All)
        {
            await SeedPersonaAsync(spec, cancellationToken);
        }
    }

    private async Task SeedPersonaAsync(
        TestPersonaSpec spec,
        CancellationToken cancellationToken)
    {
        var subject = TestPersonaCatalog.ExternalSubject(spec.Email);

        var userId = await db.UserAccounts
            .AsNoTracking()
            .Where(x =>
                x.ExternalSubject == subject
                && x.AccountStatusCode == "ACTIVE")
            .Select(x => (Guid?)x.UserId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!userId.HasValue)
            return;

        var clientId = await db.UserClientAccess
            .AsNoTracking()
            .Where(x =>
                x.UserId == userId.Value
                && x.RevokedAtUtc == null
                && x.AccessRoleCode == "OWNER")
            .Select(x => (Guid?)x.ClientId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!clientId.HasValue)
            return;

        var program = await db.Programs
            .AsNoTracking()
            .Where(x =>
                x.ClientId == clientId.Value
                && x.ProgramStatusCode == "ACTIVE")
            .OrderByDescending(x => x.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (program is null)
            return;

        var foods = await EnsureCustomFoodsAsync(
            clientId.Value,
            cancellationToken);

        var nutritionDays = await db.NutritionDays
            .AsNoTracking()
            .Where(x =>
                x.ClientId == clientId.Value
                && x.ProgramId == program.ProgramId
                && x.NutritionDate >= program.StartDate
                && x.NutritionDate < program.StartDate.AddDays(28)
                && x.ActualCalories.HasValue
                && x.ActualProteinG.HasValue
                && x.ActualCarbsG.HasValue
                && x.ActualFatG.HasValue)
            .OrderBy(x => x.NutritionDate)
            .ToListAsync(cancellationToken);

        foreach (var day in nutritionDays)
        {
            var existing = await nutritionStore.GetDailyLogAsync(
                clientId.Value,
                day.NutritionDate,
                cancellationToken);

            if (existing.Items.Count > 0)
                continue;

            for (var i = 0; i < MealTemplates.Length; i++)
            {
                var meal = MealTemplates[i];
                var isLast = i == MealTemplates.Length - 1;

                decimal Share(decimal total)
                {
                    if (!isLast)
                        return Round1(total * meal.Share);

                    var priorShare = MealTemplates
                        .Take(i)
                        .Sum(x => Round1(total * x.Share));

                    return Math.Max(0m, total - priorShare);
                }

                var energy = Share(day.ActualCalories!.Value);
                var protein = Share(day.ActualProteinG!.Value);
                var carbs = Share(day.ActualCarbsG!.Value);
                var fat = Share(day.ActualFatG!.Value);
                var fiber = day.FiberG.HasValue
                    ? Share(day.FiberG.Value)
                    : (decimal?)null;

                await nutritionStore.AddDailyLogItemAsync(
                    clientId.Value,
                    day.NutritionDate,
                    "America/Los_Angeles",
                    new DailyLogItemWriteModel(
                        MealSlotCode: meal.MealSlotCode,
                        ItemOrder: 1,
                        Food: new NutritionFoodReference(
                            CustomFoodId: foods[meal.FoodName]),
                        DescriptionSnapshot: meal.FoodName,
                        Quantity: 1m,
                        QuantityUnitCode: "SERVING",
                        GramWeight: null,
                        NutrientSnapshotPolicyCode:
                            NutritionSnapshotPolicyCodes.UserEntered,
                        EnergyKcal: energy,
                        ProteinG: protein,
                        CarbohydrateG: carbs,
                        FatG: fat,
                        FiberG: fiber,
                        SugarG: null,
                        SodiumMg: null,
                        SaturatedFatG: null),
                    cancellationToken);
            }
        }
    }

    private async Task<IReadOnlyDictionary<string, Guid>>
        EnsureCustomFoodsAsync(
            Guid clientId,
            CancellationToken cancellationToken)
    {
        var existing = await nutritionStore.GetCustomFoodsAsync(
            clientId,
            cancellationToken);

        var result = existing
            .Where(x => MealTemplates.Any(y =>
                string.Equals(
                    y.FoodName,
                    x.FoodName,
                    StringComparison.OrdinalIgnoreCase)))
            .GroupBy(x => x.FoodName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.First().CustomFoodId,
                StringComparer.OrdinalIgnoreCase);

        foreach (var meal in MealTemplates)
        {
            if (result.ContainsKey(meal.FoodName))
                continue;

            var id = await nutritionStore.SaveCustomFoodAsync(
                clientId,
                new CustomFoodWriteModel(
                    FoodName: meal.FoodName,
                    BrandName: "12WR TEST",
                    ServingSize: 1m,
                    ServingUnitCode: "SERVING",
                    HouseholdServingText: "1 synthetic test serving",
                    Nutrients:
                    [
                        new("ENERGY_KCAL", meal.EnergyKcal, "kcal"),
                        new("PROTEIN_G", meal.ProteinG, "g"),
                        new("CARBOHYDRATE_G", meal.CarbohydrateG, "g"),
                        new("FAT_G", meal.FatG, "g"),
                        new("FIBER_G", meal.FiberG, "g")
                    ],
                    Notes: "Synthetic TEST-only food used by persistent persona history."),
                cancellationToken);

            result[meal.FoodName] = id;
        }

        return result;
    }

    private static decimal Round1(decimal value)
        => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private sealed record TestMealTemplate(
        string MealSlotCode,
        string FoodName,
        decimal Share,
        decimal EnergyKcal,
        decimal ProteinG,
        decimal CarbohydrateG,
        decimal FatG,
        decimal FiberG);
}

public sealed class TestPersonaNutritionSeederHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<TestPersonaNutritionSeederHostedService> logger) : IHostedService
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
            return;

        try
        {
            await using var scope = services.CreateAsyncScope();
            var seeder = scope.ServiceProvider
                .GetRequiredService<TestPersonaNutritionSeederService>();

            await seeder.SeedAllAsync(cancellationToken);
            logger.LogInformation(
                "TEST persona item-level nutrition seed check completed.");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "TEST persona item-level nutrition seeding failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
