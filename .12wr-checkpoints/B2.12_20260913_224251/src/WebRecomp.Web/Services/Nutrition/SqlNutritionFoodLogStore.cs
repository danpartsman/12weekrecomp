using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using WebRecomp.NutritionIntelligence.Models;
using WebRecomp.NutritionIntelligence.Persistence;

namespace WebRecomp.Web.Services.Nutrition;

public sealed class SqlNutritionFoodLogStore(
    IConfiguration configuration)
    : INutritionFoodLogStore
{
    private readonly string connectionString =
        configuration.GetConnectionString("RecompDatabase")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:RecompDatabase is not configured.");

    public async Task<NutritionProviderPolicy?> GetProviderPolicyAsync(
        string providerCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                provider_code,
                cache_policy_code,
                is_enabled,
                requires_attribution,
                terms_review_required
            FROM nutrition.Providers
            WHERE provider_code = @provider_code;
            """;

        Add(command, "@provider_code", SqlDbType.VarChar, providerCode, 40);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new NutritionProviderPolicy(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4));
    }

    public async Task<Guid> UpsertExternalFoodAsync(
        FoodComposition food,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(food);

        var policy =
            await GetProviderPolicyAsync(
                food.Food.ProviderCode,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Provider '{food.Food.ProviderCode}' is not configured.");

        if (!policy.IsEnabled)
            throw new InvalidOperationException(
                $"Provider '{policy.ProviderCode}' is disabled.");

        if (!NutritionPersistencePolicy.MayPersistProviderPayload(
                policy.CachePolicyCode))
        {
            throw new InvalidOperationException(
                $"Provider '{policy.ProviderCode}' is not configured for " +
                "persistent provider-data caching.");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            var providerId =
                await GetProviderIdAsync(
                    connection,
                    transaction,
                    food.Food.ProviderCode,
                    cancellationToken);

            var externalFoodId =
                await FindExternalFoodIdAsync(
                    connection,
                    transaction,
                    providerId,
                    food.Food.SourceFoodId,
                    cancellationToken)
                ?? Guid.NewGuid();

            var rawJson = food.RawSourceJson ?? string.Empty;
            var payloadHash =
                Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(rawJson)));

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    IF EXISTS (
                        SELECT 1
                        FROM nutrition.ExternalFoods
                        WHERE external_food_id = @external_food_id
                    )
                    BEGIN
                        UPDATE nutrition.ExternalFoods
                        SET
                            data_type = @data_type,
                            description = @description,
                            brand_owner = @brand_owner,
                            gtin_upc = @gtin_upc,
                            serving_size = @serving_size,
                            serving_size_unit = @serving_size_unit,
                            household_serving_text = @household_serving_text,
                            nutrient_basis_amount = @nutrient_basis_amount,
                            nutrient_basis_unit = @nutrient_basis_unit,
                            cache_policy_snapshot_code = @cache_policy,
                            cache_status_code = 'CACHED',
                            source_payload_sha256 = @payload_sha256,
                            cached_at_utc = SYSUTCDATETIME(),
                            last_accessed_at_utc = SYSUTCDATETIME(),
                            raw_source_json = @raw_source_json,
                            is_active = 1,
                            updated_at_utc = SYSUTCDATETIME()
                        WHERE external_food_id = @external_food_id;
                    END
                    ELSE
                    BEGIN
                        INSERT nutrition.ExternalFoods
                        (
                            external_food_id,
                            provider_id,
                            source_food_id,
                            data_type,
                            description,
                            brand_owner,
                            gtin_upc,
                            serving_size,
                            serving_size_unit,
                            household_serving_text,
                            nutrient_basis_amount,
                            nutrient_basis_unit,
                            cache_policy_snapshot_code,
                            cache_status_code,
                            source_payload_sha256,
                            cached_at_utc,
                            last_accessed_at_utc,
                            raw_source_json,
                            is_active
                        )
                        VALUES
                        (
                            @external_food_id,
                            @provider_id,
                            @source_food_id,
                            @data_type,
                            @description,
                            @brand_owner,
                            @gtin_upc,
                            @serving_size,
                            @serving_size_unit,
                            @household_serving_text,
                            @nutrient_basis_amount,
                            @nutrient_basis_unit,
                            @cache_policy,
                            'CACHED',
                            @payload_sha256,
                            SYSUTCDATETIME(),
                            SYSUTCDATETIME(),
                            @raw_source_json,
                            1
                        );
                    END;
                    """;

                Add(command, "@external_food_id", SqlDbType.UniqueIdentifier, externalFoodId);
                Add(command, "@provider_id", SqlDbType.UniqueIdentifier, providerId);
                Add(command, "@source_food_id", SqlDbType.NVarChar, food.Food.SourceFoodId, 120);
                Add(command, "@data_type", SqlDbType.NVarChar, food.Food.DataType, 80);
                Add(command, "@description", SqlDbType.NVarChar, food.Food.Description, 300);
                Add(command, "@brand_owner", SqlDbType.NVarChar, food.Food.BrandOwner, 200);
                Add(command, "@gtin_upc", SqlDbType.VarChar, food.Food.GtinUpc, 32);
                AddDecimal(command, "@serving_size", food.Food.ServingSize, 12, 4);
                Add(command, "@serving_size_unit", SqlDbType.VarChar, food.Food.ServingSizeUnit, 30);
                Add(command, "@household_serving_text", SqlDbType.NVarChar, food.Food.HouseholdServingText, 200);
                AddDecimal(command, "@nutrient_basis_amount", food.Food.NutrientBasisAmount, 12, 4);
                Add(command, "@nutrient_basis_unit", SqlDbType.VarChar, food.Food.NutrientBasisUnit, 30);
                Add(command, "@cache_policy", SqlDbType.VarChar, policy.CachePolicyCode, 30);
                Add(command, "@payload_sha256", SqlDbType.Char, payloadHash, 64);
                Add(command, "@raw_source_json", SqlDbType.NVarChar, rawJson, -1);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = """
                    DELETE FROM nutrition.ExternalFoodNutrients
                    WHERE external_food_id = @external_food_id;
                    """;

                Add(delete, "@external_food_id", SqlDbType.UniqueIdentifier, externalFoodId);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            var nutrientIndex = 0;

            foreach (var nutrient in food.Food.Nutrients)
            {
                nutrientIndex++;

                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT nutrition.ExternalFoodNutrients
                    (
                        external_food_nutrient_id,
                        external_food_id,
                        source_nutrient_key,
                        source_nutrient_id,
                        source_nutrient_name,
                        source_nutrient_number,
                        canonical_code,
                        amount,
                        unit_name
                    )
                    VALUES
                    (
                        @id,
                        @external_food_id,
                        @source_key,
                        @source_id,
                        @source_name,
                        @source_number,
                        @canonical_code,
                        @amount,
                        @unit_name
                    );
                    """;

                Add(insert, "@id", SqlDbType.UniqueIdentifier, Guid.NewGuid());
                Add(insert, "@external_food_id", SqlDbType.UniqueIdentifier, externalFoodId);
                Add(insert, "@source_key", SqlDbType.NVarChar, BuildNutrientKey(nutrient, nutrientIndex), 100);
                Add(insert, "@source_id", SqlDbType.NVarChar, nutrient.SourceNutrientId?.ToString(), 60);
                Add(insert, "@source_name", SqlDbType.NVarChar, nutrient.SourceNutrientName, 150);
                Add(insert, "@source_number", SqlDbType.NVarChar, nutrient.SourceNutrientNumber, 40);
                Add(insert, "@canonical_code", SqlDbType.VarChar, nutrient.CanonicalCode, 40);
                AddDecimal(insert, "@amount", nutrient.Amount, 18, 6);
                Add(insert, "@unit_name", SqlDbType.VarChar, nutrient.UnitName, 30);

                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return externalFoodId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<Guid> SaveCustomFoodAsync(
        Guid clientId,
        CustomFoodWriteModel food,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(food);

        if (string.IsNullOrWhiteSpace(food.FoodName))
            throw new ArgumentException("Custom food name is required.", nameof(food));

        if (food.ServingSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(food.ServingSize));

        if (food.Nutrients.Count == 0)
            throw new ArgumentException("At least one nutrient is required.", nameof(food));

        var duplicate =
            food.Nutrients
                .GroupBy(x => x.CanonicalCode, StringComparer.Ordinal)
                .FirstOrDefault(x => x.Count() > 1);

        if (duplicate is not null)
            throw new ArgumentException(
                $"Duplicate nutrient code '{duplicate.Key}'.",
                nameof(food));

        var customFoodId = Guid.NewGuid();

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT nutrition.CustomFoods
                    (
                        custom_food_id,
                        client_id,
                        food_name,
                        brand_name,
                        serving_size,
                        serving_unit_code,
                        household_serving_text,
                        notes,
                        is_active
                    )
                    VALUES
                    (
                        @id,
                        @client_id,
                        @food_name,
                        @brand_name,
                        @serving_size,
                        @serving_unit_code,
                        @household_serving_text,
                        @notes,
                        1
                    );
                    """;

                Add(command, "@id", SqlDbType.UniqueIdentifier, customFoodId);
                Add(command, "@client_id", SqlDbType.UniqueIdentifier, clientId);
                Add(command, "@food_name", SqlDbType.NVarChar, food.FoodName.Trim(), 200);
                Add(command, "@brand_name", SqlDbType.NVarChar, food.BrandName, 160);
                AddDecimal(command, "@serving_size", food.ServingSize, 12, 4);
                Add(command, "@serving_unit_code", SqlDbType.VarChar, food.ServingUnitCode.Trim(), 30);
                Add(command, "@household_serving_text", SqlDbType.NVarChar, food.HouseholdServingText, 200);
                Add(command, "@notes", SqlDbType.NVarChar, food.Notes, 1000);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var nutrient in food.Nutrients)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT nutrition.CustomFoodNutrients
                    (
                        custom_food_nutrient_id,
                        custom_food_id,
                        canonical_code,
                        amount,
                        unit_name
                    )
                    VALUES
                    (
                        @id,
                        @custom_food_id,
                        @canonical_code,
                        @amount,
                        @unit_name
                    );
                    """;

                Add(insert, "@id", SqlDbType.UniqueIdentifier, Guid.NewGuid());
                Add(insert, "@custom_food_id", SqlDbType.UniqueIdentifier, customFoodId);
                Add(insert, "@canonical_code", SqlDbType.VarChar, nutrient.CanonicalCode, 40);
                AddDecimal(insert, "@amount", nutrient.Amount, 18, 6);
                Add(insert, "@unit_name", SqlDbType.VarChar, nutrient.UnitName, 30);

                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return customFoodId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<NutritionCustomFoodReadModel>> GetCustomFoodsAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        var result = new List<NutritionCustomFoodReadModel>();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                cf.custom_food_id,
                cf.food_name,
                cf.brand_name,
                cf.serving_size,
                cf.serving_unit_code,
                cf.household_serving_text,
                MAX(CASE WHEN n.canonical_code = 'ENERGY_KCAL' THEN n.amount END),
                MAX(CASE WHEN n.canonical_code = 'PROTEIN_G' THEN n.amount END),
                MAX(CASE WHEN n.canonical_code = 'CARBOHYDRATE_G' THEN n.amount END),
                MAX(CASE WHEN n.canonical_code = 'FAT_G' THEN n.amount END),
                MAX(CASE WHEN n.canonical_code = 'FIBER_G' THEN n.amount END),
                MAX(CASE WHEN n.canonical_code = 'SUGAR_G' THEN n.amount END),
                MAX(CASE WHEN n.canonical_code = 'SODIUM_MG' THEN n.amount END),
                MAX(CASE WHEN n.canonical_code = 'SATURATED_FAT_G' THEN n.amount END)
            FROM nutrition.CustomFoods cf
            LEFT JOIN nutrition.CustomFoodNutrients n
                ON n.custom_food_id = cf.custom_food_id
            WHERE cf.client_id = @client_id
              AND cf.is_active = 1
            GROUP BY
                cf.custom_food_id,
                cf.food_name,
                cf.brand_name,
                cf.serving_size,
                cf.serving_unit_code,
                cf.household_serving_text
            ORDER BY cf.food_name;
            """;

        Add(command, "@client_id", SqlDbType.UniqueIdentifier, clientId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(
                new NutritionCustomFoodReadModel(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetDecimal(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    GetNullableDecimal(reader, 6),
                    GetNullableDecimal(reader, 7),
                    GetNullableDecimal(reader, 8),
                    GetNullableDecimal(reader, 9),
                    GetNullableDecimal(reader, 10),
                    GetNullableDecimal(reader, 11),
                    GetNullableDecimal(reader, 12),
                    GetNullableDecimal(reader, 13)));
        }

        return result;
    }

    public async Task<NutritionDailyLogReadModel> GetDailyLogAsync(
        Guid clientId,
        DateOnly logDate,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);

        Guid? dailyLogId = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT daily_log_id
                FROM nutrition.DailyLogs
                WHERE client_id = @client_id
                  AND log_date = @log_date;
                """;

            Add(command, "@client_id", SqlDbType.UniqueIdentifier, clientId);
            Add(command, "@log_date", SqlDbType.Date, logDate.ToDateTime(TimeOnly.MinValue));

            var value =
                await command.ExecuteScalarAsync(cancellationToken);

            if (value is Guid id)
                dailyLogId = id;
        }

        if (!dailyLogId.HasValue)
            return new NutritionDailyLogReadModel(null, logDate, [], null);

        var items = new List<NutritionDailyLogItemReadModel>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    daily_log_item_id,
                    meal_slot_code,
                    item_order,
                    description_snapshot,
                    quantity,
                    quantity_unit_code,
                    gram_weight,
                    energy_kcal,
                    protein_g,
                    carbohydrate_g,
                    fat_g,
                    fiber_g,
                    sugar_g,
                    sodium_mg,
                    saturated_fat_g
                FROM nutrition.DailyLogItems
                WHERE daily_log_id = @daily_log_id
                ORDER BY
                    CASE meal_slot_code
                        WHEN 'BREAKFAST' THEN 1
                        WHEN 'LUNCH' THEN 2
                        WHEN 'DINNER' THEN 3
                        WHEN 'SNACK' THEN 4
                        ELSE 5
                    END,
                    item_order;
                """;

            Add(command, "@daily_log_id", SqlDbType.UniqueIdentifier, dailyLogId.Value);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(
                    new NutritionDailyLogItemReadModel(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.GetInt16(2),
                        reader.GetString(3),
                        reader.GetDecimal(4),
                        reader.GetString(5),
                        GetNullableDecimal(reader, 6),
                        GetNullableDecimal(reader, 7),
                        GetNullableDecimal(reader, 8),
                        GetNullableDecimal(reader, 9),
                        GetNullableDecimal(reader, 10),
                        GetNullableDecimal(reader, 11),
                        GetNullableDecimal(reader, 12),
                        GetNullableDecimal(reader, 13),
                        GetNullableDecimal(reader, 14)));
            }
        }

        NutritionDailyTotalsReadModel? totals = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    energy_kcal,
                    protein_g,
                    carbohydrate_g,
                    fat_g,
                    fiber_g,
                    sugar_g,
                    sodium_mg,
                    saturated_fat_g,
                    completeness_code
                FROM nutrition.DailyTotals
                WHERE daily_log_id = @daily_log_id;
                """;

            Add(command, "@daily_log_id", SqlDbType.UniqueIdentifier, dailyLogId.Value);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                totals =
                    new NutritionDailyTotalsReadModel(
                        reader.GetDecimal(0),
                        reader.GetDecimal(1),
                        reader.GetDecimal(2),
                        reader.GetDecimal(3),
                        GetNullableDecimal(reader, 4),
                        GetNullableDecimal(reader, 5),
                        GetNullableDecimal(reader, 6),
                        GetNullableDecimal(reader, 7),
                        reader.GetString(8));
            }
        }

        return new NutritionDailyLogReadModel(
            dailyLogId,
            logDate,
            items,
            totals);
    }

    public async Task<Guid> AddDailyLogItemAsync(
        Guid clientId,
        DateOnly logDate,
        string? timeZone,
        DailyLogItemWriteModel item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Food.Validate();

        if (item.Quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(item.Quantity));

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await ValidateOwnedReferenceAsync(
                connection,
                transaction,
                clientId,
                item.Food,
                cancellationToken);

            var dailyLogId =
                await GetOrCreateDailyLogIdAsync(
                    connection,
                    transaction,
                    clientId,
                    logDate,
                    timeZone,
                    cancellationToken);

            var itemOrder =
                await GetNextItemOrderAsync(
                    connection,
                    transaction,
                    dailyLogId,
                    item.MealSlotCode,
                    cancellationToken);

            var dailyLogItemId = Guid.NewGuid();

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT nutrition.DailyLogItems
                    (
                        daily_log_item_id,
                        daily_log_id,
                        meal_slot_code,
                        item_order,
                        external_food_id,
                        custom_food_id,
                        recipe_id,
                        source_saved_meal_id,
                        description_snapshot,
                        quantity,
                        quantity_unit_code,
                        gram_weight,
                        nutrient_snapshot_policy_code,
                        energy_kcal,
                        protein_g,
                        carbohydrate_g,
                        fat_g,
                        fiber_g,
                        sugar_g,
                        sodium_mg,
                        saturated_fat_g
                    )
                    VALUES
                    (
                        @id,
                        @daily_log_id,
                        @meal_slot_code,
                        @item_order,
                        @external_food_id,
                        @custom_food_id,
                        @recipe_id,
                        @source_saved_meal_id,
                        @description_snapshot,
                        @quantity,
                        @quantity_unit_code,
                        @gram_weight,
                        @snapshot_policy,
                        @energy_kcal,
                        @protein_g,
                        @carbohydrate_g,
                        @fat_g,
                        @fiber_g,
                        @sugar_g,
                        @sodium_mg,
                        @saturated_fat_g
                    );
                    """;

                Add(command, "@id", SqlDbType.UniqueIdentifier, dailyLogItemId);
                Add(command, "@daily_log_id", SqlDbType.UniqueIdentifier, dailyLogId);
                Add(command, "@meal_slot_code", SqlDbType.VarChar, item.MealSlotCode, 30);
                Add(command, "@item_order", SqlDbType.SmallInt, checked((short)itemOrder));
                Add(command, "@external_food_id", SqlDbType.UniqueIdentifier, item.Food.ExternalFoodId);
                Add(command, "@custom_food_id", SqlDbType.UniqueIdentifier, item.Food.CustomFoodId);
                Add(command, "@recipe_id", SqlDbType.UniqueIdentifier, item.Food.RecipeId);
                Add(command, "@source_saved_meal_id", SqlDbType.UniqueIdentifier, item.SourceSavedMealId);
                Add(command, "@description_snapshot", SqlDbType.NVarChar, item.DescriptionSnapshot, 300);
                AddDecimal(command, "@quantity", item.Quantity, 12, 4);
                Add(command, "@quantity_unit_code", SqlDbType.VarChar, item.QuantityUnitCode, 30);
                AddDecimal(command, "@gram_weight", item.GramWeight, 12, 4);
                Add(command, "@snapshot_policy", SqlDbType.VarChar, item.NutrientSnapshotPolicyCode, 30);
                AddDecimal(command, "@energy_kcal", item.EnergyKcal, 12, 4);
                AddDecimal(command, "@protein_g", item.ProteinG, 12, 4);
                AddDecimal(command, "@carbohydrate_g", item.CarbohydrateG, 12, 4);
                AddDecimal(command, "@fat_g", item.FatG, 12, 4);
                AddDecimal(command, "@fiber_g", item.FiberG, 12, 4);
                AddDecimal(command, "@sugar_g", item.SugarG, 12, 4);
                AddDecimal(command, "@sodium_mg", item.SodiumMg, 12, 4);
                AddDecimal(command, "@saturated_fat_g", item.SaturatedFatG, 12, 4);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await RecalculateDailyTotalsAsync(
                connection,
                transaction,
                dailyLogId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return dailyLogItemId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteDailyLogItemAsync(
        Guid clientId,
        Guid dailyLogItemId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            Guid? dailyLogId = null;

            await using (var lookup = connection.CreateCommand())
            {
                lookup.Transaction = transaction;
                lookup.CommandText = """
                    SELECT li.daily_log_id
                    FROM nutrition.DailyLogItems li
                    INNER JOIN nutrition.DailyLogs dl
                        ON dl.daily_log_id = li.daily_log_id
                    WHERE li.daily_log_item_id = @item_id
                      AND dl.client_id = @client_id;
                    """;

                Add(lookup, "@item_id", SqlDbType.UniqueIdentifier, dailyLogItemId);
                Add(lookup, "@client_id", SqlDbType.UniqueIdentifier, clientId);

                var value = await lookup.ExecuteScalarAsync(cancellationToken);
                if (value is Guid id)
                    dailyLogId = id;
            }

            if (!dailyLogId.HasValue)
                throw new InvalidOperationException(
                    "Nutrition log item was not found for this client.");

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = """
                    DELETE FROM nutrition.DailyLogItems
                    WHERE daily_log_item_id = @item_id;
                    """;

                Add(delete, "@item_id", SqlDbType.UniqueIdentifier, dailyLogItemId);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            await RecalculateDailyTotalsAsync(
                connection,
                transaction,
                dailyLogId.Value,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<SqlConnection> OpenAsync(
        CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<Guid> GetProviderIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string providerCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT provider_id
            FROM nutrition.Providers
            WHERE provider_code = @provider_code;
            """;

        Add(command, "@provider_code", SqlDbType.VarChar, providerCode, 40);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return value is Guid id
            ? id
            : throw new InvalidOperationException(
                $"Nutrition provider '{providerCode}' does not exist.");
    }

    private static async Task<Guid?> FindExternalFoodIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid providerId,
        string sourceFoodId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT external_food_id
            FROM nutrition.ExternalFoods
            WHERE provider_id = @provider_id
              AND source_food_id = @source_food_id;
            """;

        Add(command, "@provider_id", SqlDbType.UniqueIdentifier, providerId);
        Add(command, "@source_food_id", SqlDbType.NVarChar, sourceFoodId, 120);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : null;
    }

    private static async Task<Guid> GetOrCreateDailyLogIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid clientId,
        DateOnly logDate,
        string? timeZone,
        CancellationToken cancellationToken)
    {
        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText = """
                SELECT daily_log_id
                FROM nutrition.DailyLogs
                WHERE client_id = @client_id
                  AND log_date = @log_date;
                """;

            Add(lookup, "@client_id", SqlDbType.UniqueIdentifier, clientId);
            Add(lookup, "@log_date", SqlDbType.Date, logDate.ToDateTime(TimeOnly.MinValue));

            var value = await lookup.ExecuteScalarAsync(cancellationToken);
            if (value is Guid existing)
                return existing;
        }

        var id = Guid.NewGuid();

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT nutrition.DailyLogs
            (
                daily_log_id,
                client_id,
                log_date,
                time_zone
            )
            VALUES
            (
                @id,
                @client_id,
                @log_date,
                @time_zone
            );
            """;

        Add(insert, "@id", SqlDbType.UniqueIdentifier, id);
        Add(insert, "@client_id", SqlDbType.UniqueIdentifier, clientId);
        Add(insert, "@log_date", SqlDbType.Date, logDate.ToDateTime(TimeOnly.MinValue));
        Add(insert, "@time_zone", SqlDbType.NVarChar, timeZone, 100);

        await insert.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    private static async Task<int> GetNextItemOrderAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid dailyLogId,
        string mealSlotCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(MAX(item_order), 0) + 1
            FROM nutrition.DailyLogItems
            WHERE daily_log_id = @daily_log_id
              AND meal_slot_code = @meal_slot_code;
            """;

        Add(command, "@daily_log_id", SqlDbType.UniqueIdentifier, dailyLogId);
        Add(command, "@meal_slot_code", SqlDbType.VarChar, mealSlotCode, 30);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    private static async Task ValidateOwnedReferenceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid clientId,
        NutritionFoodReference food,
        CancellationToken cancellationToken)
    {
        if (food.CustomFoodId.HasValue)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(*)
                FROM nutrition.CustomFoods
                WHERE custom_food_id = @id
                  AND client_id = @client_id
                  AND is_active = 1;
                """;

            Add(command, "@id", SqlDbType.UniqueIdentifier, food.CustomFoodId.Value);
            Add(command, "@client_id", SqlDbType.UniqueIdentifier, clientId);

            var count =
                Convert.ToInt32(
                    await command.ExecuteScalarAsync(cancellationToken));

            if (count != 1)
                throw new InvalidOperationException(
                    "Custom food is not available for this client.");
        }

        if (food.RecipeId.HasValue)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(*)
                FROM nutrition.Recipes
                WHERE recipe_id = @id
                  AND client_id = @client_id
                  AND is_active = 1;
                """;

            Add(command, "@id", SqlDbType.UniqueIdentifier, food.RecipeId.Value);
            Add(command, "@client_id", SqlDbType.UniqueIdentifier, clientId);

            var count =
                Convert.ToInt32(
                    await command.ExecuteScalarAsync(cancellationToken));

            if (count != 1)
                throw new InvalidOperationException(
                    "Recipe is not available for this client.");
        }
    }

    private static async Task RecalculateDailyTotalsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid dailyLogId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            IF NOT EXISTS (
                SELECT 1
                FROM nutrition.DailyLogItems
                WHERE daily_log_id = @daily_log_id
            )
            BEGIN
                DELETE FROM nutrition.DailyTotals
                WHERE daily_log_id = @daily_log_id;
                RETURN;
            END;

            DECLARE
                @energy decimal(12,4),
                @protein decimal(12,4),
                @carbs decimal(12,4),
                @fat decimal(12,4),
                @fiber decimal(12,4),
                @sugar decimal(12,4),
                @sodium decimal(12,4),
                @sat_fat decimal(12,4),
                @item_count int,
                @complete_count int;

            SELECT
                @energy = SUM(COALESCE(energy_kcal, 0)),
                @protein = SUM(COALESCE(protein_g, 0)),
                @carbs = SUM(COALESCE(carbohydrate_g, 0)),
                @fat = SUM(COALESCE(fat_g, 0)),
                @fiber =
                    CASE
                        WHEN COUNT(fiber_g) = COUNT(*)
                        THEN SUM(fiber_g)
                        ELSE NULL
                    END,
                @sugar =
                    CASE
                        WHEN COUNT(sugar_g) = COUNT(*)
                        THEN SUM(sugar_g)
                        ELSE NULL
                    END,
                @sodium =
                    CASE
                        WHEN COUNT(sodium_mg) = COUNT(*)
                        THEN SUM(sodium_mg)
                        ELSE NULL
                    END,
                @sat_fat =
                    CASE
                        WHEN COUNT(saturated_fat_g) = COUNT(*)
                        THEN SUM(saturated_fat_g)
                        ELSE NULL
                    END,
                @item_count = COUNT(*),
                @complete_count = SUM(
                    CASE
                        WHEN energy_kcal IS NOT NULL
                         AND protein_g IS NOT NULL
                         AND carbohydrate_g IS NOT NULL
                         AND fat_g IS NOT NULL
                        THEN 1
                        ELSE 0
                    END)
            FROM nutrition.DailyLogItems
            WHERE daily_log_id = @daily_log_id;

            IF EXISTS (
                SELECT 1
                FROM nutrition.DailyTotals
                WHERE daily_log_id = @daily_log_id
            )
            BEGIN
                UPDATE nutrition.DailyTotals
                SET
                    energy_kcal = @energy,
                    protein_g = @protein,
                    carbohydrate_g = @carbs,
                    fat_g = @fat,
                    fiber_g = @fiber,
                    sugar_g = @sugar,
                    sodium_mg = @sodium,
                    saturated_fat_g = @sat_fat,
                    completeness_code =
                        CASE
                            WHEN @item_count = @complete_count
                            THEN 'COMPLETE'
                            ELSE 'PARTIAL'
                        END,
                    calculated_at_utc = SYSUTCDATETIME(),
                    updated_at_utc = SYSUTCDATETIME()
                WHERE daily_log_id = @daily_log_id;
            END
            ELSE
            BEGIN
                INSERT nutrition.DailyTotals
                (
                    daily_total_id,
                    daily_log_id,
                    energy_kcal,
                    protein_g,
                    carbohydrate_g,
                    fat_g,
                    fiber_g,
                    sugar_g,
                    sodium_mg,
                    saturated_fat_g,
                    completeness_code,
                    calculated_at_utc
                )
                VALUES
                (
                    @daily_total_id,
                    @daily_log_id,
                    @energy,
                    @protein,
                    @carbs,
                    @fat,
                    @fiber,
                    @sugar,
                    @sodium,
                    @sat_fat,
                    CASE
                        WHEN @item_count = @complete_count
                        THEN 'COMPLETE'
                        ELSE 'PARTIAL'
                    END,
                    SYSUTCDATETIME()
                );
            END;
            """;

        Add(command, "@daily_log_id", SqlDbType.UniqueIdentifier, dailyLogId);
        Add(command, "@daily_total_id", SqlDbType.UniqueIdentifier, Guid.NewGuid());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string BuildNutrientKey(
        NutrientAmount nutrient,
        int index)
    {
        var key =
            nutrient.SourceNutrientId?.ToString()
            ?? nutrient.SourceNutrientNumber
            ?? nutrient.CanonicalCode
            ?? nutrient.SourceNutrientName;

        key =
            string.IsNullOrWhiteSpace(key)
                ? "nutrient"
                : key.Trim();

        var result = $"{key}:{index}";
        return result.Length <= 100
            ? result
            : result[..100];
    }

    private static decimal? GetNullableDecimal(
        SqlDataReader reader,
        int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : reader.GetDecimal(ordinal);

    private static void Add(
        SqlCommand command,
        string name,
        SqlDbType type,
        object? value,
        int size = 0)
    {
        var parameter =
            size == 0
                ? command.Parameters.Add(name, type)
                : command.Parameters.Add(name, type, size);

        parameter.Value = value ?? DBNull.Value;
    }

    private static void AddDecimal(
        SqlCommand command,
        string name,
        decimal? value,
        byte precision,
        byte scale)
    {
        var parameter =
            command.Parameters.Add(
                name,
                SqlDbType.Decimal);

        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value =
            value.HasValue
                ? value.Value
                : DBNull.Value;
    }
}
