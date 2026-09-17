using System.Data;
using Microsoft.Data.SqlClient;
using WebRecomp.NutritionIntelligence.Persistence;

namespace WebRecomp.Web.Services.Nutrition;

public sealed class NutritionFoodPickerHistoryStore(
    IConfiguration configuration)
{
    private readonly string connectionString =
        configuration.GetConnectionString("RecompDatabase")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:RecompDatabase is not configured.");

    public async Task<IReadOnlyList<NutritionPickerPastMeal>> GetPastMealsAsync(
        Guid clientId,
        DateOnly beforeDate,
        string mealSlotCode,
        int take = 12,
        CancellationToken cancellationToken = default)
    {
        var result = new List<NutritionPickerPastMeal>();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT TOP (@take)
                dl.daily_log_id,
                dl.log_date,
                li.meal_slot_code,
                COUNT(*) AS item_count,
                SUM(li.energy_kcal) AS energy_kcal,
                STRING_AGG(
                    CONVERT(nvarchar(max), li.description_snapshot),
                    N', ') AS meal_summary
            FROM nutrition.DailyLogs dl
            INNER JOIN nutrition.DailyLogItems li
                ON li.daily_log_id = dl.daily_log_id
            WHERE dl.client_id = @client_id
              AND dl.log_date < @before_date
              AND li.meal_slot_code = @meal_slot_code
            GROUP BY
                dl.daily_log_id,
                dl.log_date,
                li.meal_slot_code
            HAVING SUM(
                CASE
                    WHEN li.energy_kcal IS NULL
                      OR li.protein_g IS NULL
                      OR li.carbohydrate_g IS NULL
                      OR li.fat_g IS NULL
                    THEN 1
                    ELSE 0
                END) = 0
            ORDER BY dl.log_date DESC;
            """;

        Add(command, "@take", SqlDbType.Int, Math.Clamp(take, 1, 50));
        Add(command, "@client_id", SqlDbType.UniqueIdentifier, clientId);
        Add(
            command,
            "@before_date",
            SqlDbType.Date,
            beforeDate.ToDateTime(TimeOnly.MinValue));
        Add(command, "@meal_slot_code", SqlDbType.VarChar, mealSlotCode, 30);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(
                new NutritionPickerPastMeal(
                    reader.GetGuid(0),
                    DateOnly.FromDateTime(reader.GetDateTime(1)),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                    reader.IsDBNull(5)
                        ? string.Empty
                        : reader.GetString(5)));
        }

        return result;
    }

    public async Task<IReadOnlyList<NutritionPickerFrequentFood>> GetFrequentFoodsAsync(
        Guid clientId,
        DateOnly beforeDate,
        string? query,
        int take = 30,
        CancellationToken cancellationToken = default)
    {
        var result = new List<NutritionPickerFrequentFood>();
        var normalizedQuery =
            string.IsNullOrWhiteSpace(query)
                ? null
                : query.Trim();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            WITH history AS
            (
                SELECT
                    li.daily_log_item_id,
                    li.description_snapshot,
                    li.quantity,
                    li.quantity_unit_code,
                    li.energy_kcal,
                    li.protein_g,
                    li.carbohydrate_g,
                    li.fat_g,
                    li.created_at_utc,
                    ef.brand_owner AS external_brand,
                    cf.brand_name AS custom_brand,
                    p.provider_code,
                    ef.source_food_id,
                    COUNT(*) OVER
                    (
                        PARTITION BY
                            li.external_food_id,
                            li.custom_food_id,
                            li.recipe_id,
                            li.description_snapshot
                    ) AS usage_count,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY
                            li.external_food_id,
                            li.custom_food_id,
                            li.recipe_id,
                            li.description_snapshot
                        ORDER BY li.created_at_utc DESC
                    ) AS row_num
                FROM nutrition.DailyLogItems li
                INNER JOIN nutrition.DailyLogs dl
                    ON dl.daily_log_id = li.daily_log_id
                LEFT JOIN nutrition.ExternalFoods ef
                    ON ef.external_food_id = li.external_food_id
                LEFT JOIN nutrition.Providers p
                    ON p.provider_id = ef.provider_id
                LEFT JOIN nutrition.CustomFoods cf
                    ON cf.custom_food_id = li.custom_food_id
                WHERE dl.client_id = @client_id
                  AND dl.log_date < @before_date
                  AND li.energy_kcal IS NOT NULL
                  AND li.protein_g IS NOT NULL
                  AND li.carbohydrate_g IS NOT NULL
                  AND li.fat_g IS NOT NULL
                  AND
                  (
                      @query IS NULL
                      OR li.description_snapshot LIKE @like
                      OR ef.brand_owner LIKE @like
                      OR cf.brand_name LIKE @like
                  )
            )
            SELECT TOP (@take)
                daily_log_item_id,
                description_snapshot,
                COALESCE(external_brand, custom_brand) AS brand_name,
                usage_count,
                quantity,
                quantity_unit_code,
                energy_kcal,
                protein_g,
                carbohydrate_g,
                fat_g,
                provider_code,
                source_food_id
            FROM history
            WHERE row_num = 1
            ORDER BY
                usage_count DESC,
                created_at_utc DESC,
                description_snapshot;
            """;

        Add(command, "@take", SqlDbType.Int, Math.Clamp(take, 1, 100));
        Add(command, "@client_id", SqlDbType.UniqueIdentifier, clientId);
        Add(
            command,
            "@before_date",
            SqlDbType.Date,
            beforeDate.ToDateTime(TimeOnly.MinValue));
        Add(command, "@query", SqlDbType.NVarChar, normalizedQuery, 200);
        Add(
            command,
            "@like",
            SqlDbType.NVarChar,
            normalizedQuery is null
                ? null
                : $"%{normalizedQuery}%",
            210);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(
                new NutritionPickerFrequentFood(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetDecimal(4),
                    reader.GetString(5),
                    reader.GetDecimal(6),
                    reader.GetDecimal(7),
                    reader.GetDecimal(8),
                    reader.GetDecimal(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return result;
    }

    public async Task<NutritionPickerFoodSnapshot?> GetFoodSnapshotAsync(
        Guid clientId,
        Guid sourceDailyLogItemId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                li.external_food_id,
                li.custom_food_id,
                li.recipe_id,
                li.description_snapshot,
                li.quantity,
                li.quantity_unit_code,
                li.gram_weight,
                li.nutrient_snapshot_policy_code,
                li.energy_kcal,
                li.protein_g,
                li.carbohydrate_g,
                li.fat_g,
                li.fiber_g,
                li.sugar_g,
                li.sodium_mg,
                li.saturated_fat_g
            FROM nutrition.DailyLogItems li
            INNER JOIN nutrition.DailyLogs dl
                ON dl.daily_log_id = li.daily_log_id
            WHERE dl.client_id = @client_id
              AND li.daily_log_item_id = @item_id
              AND li.energy_kcal IS NOT NULL
              AND li.protein_g IS NOT NULL
              AND li.carbohydrate_g IS NOT NULL
              AND li.fat_g IS NOT NULL;
            """;

        Add(command, "@client_id", SqlDbType.UniqueIdentifier, clientId);
        Add(command, "@item_id", SqlDbType.UniqueIdentifier, sourceDailyLogItemId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadSnapshot(reader);
    }

    public async Task<IReadOnlyList<NutritionPickerFoodSnapshot>> GetPastMealItemsAsync(
        Guid clientId,
        Guid sourceDailyLogId,
        DateOnly targetDate,
        string sourceMealSlotCode,
        CancellationToken cancellationToken = default)
    {
        var result = new List<NutritionPickerFoodSnapshot>();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                li.external_food_id,
                li.custom_food_id,
                li.recipe_id,
                li.description_snapshot,
                li.quantity,
                li.quantity_unit_code,
                li.gram_weight,
                li.nutrient_snapshot_policy_code,
                li.energy_kcal,
                li.protein_g,
                li.carbohydrate_g,
                li.fat_g,
                li.fiber_g,
                li.sugar_g,
                li.sodium_mg,
                li.saturated_fat_g
            FROM nutrition.DailyLogItems li
            INNER JOIN nutrition.DailyLogs dl
                ON dl.daily_log_id = li.daily_log_id
            WHERE dl.client_id = @client_id
              AND dl.daily_log_id = @daily_log_id
              AND dl.log_date < @target_date
              AND li.meal_slot_code = @meal_slot_code
              AND li.energy_kcal IS NOT NULL
              AND li.protein_g IS NOT NULL
              AND li.carbohydrate_g IS NOT NULL
              AND li.fat_g IS NOT NULL
            ORDER BY li.item_order;
            """;

        Add(command, "@client_id", SqlDbType.UniqueIdentifier, clientId);
        Add(command, "@daily_log_id", SqlDbType.UniqueIdentifier, sourceDailyLogId);
        Add(
            command,
            "@target_date",
            SqlDbType.Date,
            targetDate.ToDateTime(TimeOnly.MinValue));
        Add(command, "@meal_slot_code", SqlDbType.VarChar, sourceMealSlotCode, 30);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadSnapshot(reader));

        return result;
    }

    private async Task<SqlConnection> OpenAsync(
        CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static NutritionPickerFoodSnapshot ReadSnapshot(
        SqlDataReader reader)
        => new(
            new NutritionFoodReference(
                ExternalFoodId:
                    reader.IsDBNull(0) ? null : reader.GetGuid(0),
                CustomFoodId:
                    reader.IsDBNull(1) ? null : reader.GetGuid(1),
                RecipeId:
                    reader.IsDBNull(2) ? null : reader.GetGuid(2)),
            reader.GetString(3),
            reader.GetDecimal(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetDecimal(6),
            reader.GetString(7),
            reader.GetDecimal(8),
            reader.GetDecimal(9),
            reader.GetDecimal(10),
            reader.GetDecimal(11),
            reader.IsDBNull(12) ? null : reader.GetDecimal(12),
            reader.IsDBNull(13) ? null : reader.GetDecimal(13),
            reader.IsDBNull(14) ? null : reader.GetDecimal(14),
            reader.IsDBNull(15) ? null : reader.GetDecimal(15));

    private static void Add(
        SqlCommand command,
        string name,
        SqlDbType type,
        object? value,
        int? size = null)
    {
        var parameter = command.Parameters.Add(name, type);

        if (size.HasValue)
            parameter.Size = size.Value;

        parameter.Value = value ?? DBNull.Value;
    }
}

public sealed record NutritionPickerPastMeal(
    Guid DailyLogId,
    DateOnly LogDate,
    string MealSlotCode,
    int ItemCount,
    decimal? EnergyKcal,
    string Summary);

public sealed record NutritionPickerFrequentFood(
    Guid SourceDailyLogItemId,
    string Description,
    string? BrandName,
    int UseCount,
    decimal Quantity,
    string QuantityUnitCode,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbohydrateG,
    decimal FatG,
    string? ProviderCode,
    string? SourceFoodId);

public sealed record NutritionPickerFoodSnapshot(
    NutritionFoodReference Food,
    string Description,
    decimal Quantity,
    string QuantityUnitCode,
    decimal? GramWeight,
    string NutrientSnapshotPolicyCode,
    decimal EnergyKcal,
    decimal ProteinG,
    decimal CarbohydrateG,
    decimal FatG,
    decimal? FiberG,
    decimal? SugarG,
    decimal? SodiumMg,
    decimal? SaturatedFatG)
{
    public DailyLogItemWriteModel ToWriteModel(
        string mealSlotCode)
        => new(
            mealSlotCode,
            ItemOrder: 0,
            Food,
            Description,
            Quantity,
            QuantityUnitCode,
            GramWeight,
            NutrientSnapshotPolicyCode,
            EnergyKcal,
            ProteinG,
            CarbohydrateG,
            FatG,
            FiberG,
            SugarG,
            SodiumMg,
            SaturatedFatG);
}
