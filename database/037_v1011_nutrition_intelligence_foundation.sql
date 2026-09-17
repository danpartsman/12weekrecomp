SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'app.Clients', N'U') IS NULL
        THROW 51110, 'Required table app.Clients does not exist.', 1;

    IF SCHEMA_ID(N'nutrition') IS NULL
        EXEC(N'CREATE SCHEMA nutrition AUTHORIZATION dbo;');

    IF OBJECT_ID(N'nutrition.Providers', N'U') IS NULL
    BEGIN
        CREATE TABLE nutrition.Providers
        (
            provider_id uniqueidentifier NOT NULL,
            provider_code varchar(40) NOT NULL,
            display_name nvarchar(120) NOT NULL,
            cache_policy_code varchar(30) NOT NULL,
            license_code varchar(60) NULL,
            requires_attribution bit NOT NULL
                CONSTRAINT DF_NutritionProviders_RequiresAttribution DEFAULT (0),
            terms_review_required bit NOT NULL
                CONSTRAINT DF_NutritionProviders_TermsReviewRequired DEFAULT (0),
            is_enabled bit NOT NULL
                CONSTRAINT DF_NutritionProviders_IsEnabled DEFAULT (0),
            created_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionProviders_CreatedAt DEFAULT (SYSUTCDATETIME()),
            updated_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionProviders_UpdatedAt DEFAULT (SYSUTCDATETIME()),
            row_version rowversion NOT NULL,
            CONSTRAINT PK_NutritionProviders
                PRIMARY KEY CLUSTERED (provider_id),
            CONSTRAINT CK_NutritionProviders_CachePolicy
                CHECK (cache_policy_code IN ('PERSISTENT', 'REFERENCE_ONLY', 'SESSION_ONLY'))
        );

        CREATE UNIQUE INDEX UX_NutritionProviders_ProviderCode
            ON nutrition.Providers(provider_code);
    END;

    IF OBJECT_ID(N'nutrition.ExternalFoods', N'U') IS NULL
    BEGIN
        CREATE TABLE nutrition.ExternalFoods
        (
            external_food_id uniqueidentifier NOT NULL,
            provider_id uniqueidentifier NOT NULL,
            source_food_id nvarchar(120) NOT NULL,
            data_type nvarchar(80) NULL,
            description nvarchar(300) NULL,
            brand_owner nvarchar(200) NULL,
            gtin_upc varchar(32) NULL,
            serving_size decimal(12,4) NULL,
            serving_size_unit varchar(30) NULL,
            household_serving_text nvarchar(200) NULL,
            cache_policy_snapshot_code varchar(30) NOT NULL,
            cache_status_code varchar(30) NOT NULL,
            source_version nvarchar(200) NULL,
            source_etag nvarchar(200) NULL,
            source_payload_sha256 char(64) NULL,
            source_updated_at_utc datetime2(3) NULL,
            cached_at_utc datetime2(3) NULL,
            cache_expires_at_utc datetime2(3) NULL,
            last_accessed_at_utc datetime2(3) NULL,
            raw_source_json nvarchar(max) NULL,
            is_active bit NOT NULL
                CONSTRAINT DF_NutritionExternalFoods_IsActive DEFAULT (1),
            created_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionExternalFoods_CreatedAt DEFAULT (SYSUTCDATETIME()),
            updated_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionExternalFoods_UpdatedAt DEFAULT (SYSUTCDATETIME()),
            row_version rowversion NOT NULL,
            CONSTRAINT PK_NutritionExternalFoods
                PRIMARY KEY CLUSTERED (external_food_id),
            CONSTRAINT FK_NutritionExternalFoods_Provider
                FOREIGN KEY (provider_id)
                REFERENCES nutrition.Providers(provider_id),
            CONSTRAINT CK_NutritionExternalFoods_CachePolicy
                CHECK (cache_policy_snapshot_code IN ('PERSISTENT', 'REFERENCE_ONLY', 'SESSION_ONLY')),
            CONSTRAINT CK_NutritionExternalFoods_CacheStatus
                CHECK (cache_status_code IN ('REFERENCE', 'CACHED', 'STALE', 'EXPIRED'))
        );

        CREATE UNIQUE INDEX UX_NutritionExternalFoods_ProviderSource
            ON nutrition.ExternalFoods(provider_id, source_food_id);

        CREATE INDEX IX_NutritionExternalFoods_GtinUpc
            ON nutrition.ExternalFoods(gtin_upc)
            WHERE gtin_upc IS NOT NULL;

        CREATE INDEX IX_NutritionExternalFoods_ProviderDescription
            ON nutrition.ExternalFoods(provider_id, description);
    END;

    IF OBJECT_ID(N'nutrition.ExternalFoodNutrients', N'U') IS NULL
    BEGIN
        CREATE TABLE nutrition.ExternalFoodNutrients
        (
            external_food_nutrient_id uniqueidentifier NOT NULL,
            external_food_id uniqueidentifier NOT NULL,
            source_nutrient_key nvarchar(100) NOT NULL,
            source_nutrient_id nvarchar(60) NULL,
            source_nutrient_name nvarchar(150) NOT NULL,
            source_nutrient_number nvarchar(40) NULL,
            canonical_code varchar(40) NULL,
            amount decimal(18,6) NULL,
            unit_name varchar(30) NOT NULL,
            created_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionExternalFoodNutrients_CreatedAt DEFAULT (SYSUTCDATETIME()),
            updated_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionExternalFoodNutrients_UpdatedAt DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_NutritionExternalFoodNutrients
                PRIMARY KEY CLUSTERED (external_food_nutrient_id),
            CONSTRAINT FK_NutritionExternalFoodNutrients_Food
                FOREIGN KEY (external_food_id)
                REFERENCES nutrition.ExternalFoods(external_food_id)
                ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX UX_NutritionExternalFoodNutrients_FoodSourceKey
            ON nutrition.ExternalFoodNutrients(external_food_id, source_nutrient_key);

        CREATE INDEX IX_NutritionExternalFoodNutrients_FoodCanonical
            ON nutrition.ExternalFoodNutrients(external_food_id, canonical_code)
            WHERE canonical_code IS NOT NULL;
    END;

    IF OBJECT_ID(N'nutrition.CustomFoods', N'U') IS NULL
    BEGIN
        CREATE TABLE nutrition.CustomFoods
        (
            custom_food_id uniqueidentifier NOT NULL,
            client_id uniqueidentifier NOT NULL,
            food_name nvarchar(200) NOT NULL,
            brand_name nvarchar(160) NULL,
            serving_size decimal(12,4) NOT NULL,
            serving_unit_code varchar(30) NOT NULL,
            household_serving_text nvarchar(200) NULL,
            notes nvarchar(1000) NULL,
            is_active bit NOT NULL
                CONSTRAINT DF_NutritionCustomFoods_IsActive DEFAULT (1),
            created_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionCustomFoods_CreatedAt DEFAULT (SYSUTCDATETIME()),
            updated_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionCustomFoods_UpdatedAt DEFAULT (SYSUTCDATETIME()),
            row_version rowversion NOT NULL,
            CONSTRAINT PK_NutritionCustomFoods
                PRIMARY KEY CLUSTERED (custom_food_id),
            CONSTRAINT FK_NutritionCustomFoods_Client
                FOREIGN KEY (client_id)
                REFERENCES app.Clients(client_id),
            CONSTRAINT CK_NutritionCustomFoods_ServingSize
                CHECK (serving_size > 0)
        );

        CREATE INDEX IX_NutritionCustomFoods_ClientActiveName
            ON nutrition.CustomFoods(client_id, is_active, food_name);
    END;

    IF OBJECT_ID(N'nutrition.CustomFoodNutrients', N'U') IS NULL
    BEGIN
        CREATE TABLE nutrition.CustomFoodNutrients
        (
            custom_food_nutrient_id uniqueidentifier NOT NULL,
            custom_food_id uniqueidentifier NOT NULL,
            canonical_code varchar(40) NOT NULL,
            amount decimal(18,6) NOT NULL,
            unit_name varchar(30) NOT NULL,
            created_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionCustomFoodNutrients_CreatedAt DEFAULT (SYSUTCDATETIME()),
            updated_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionCustomFoodNutrients_UpdatedAt DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_NutritionCustomFoodNutrients
                PRIMARY KEY CLUSTERED (custom_food_nutrient_id),
            CONSTRAINT FK_NutritionCustomFoodNutrients_CustomFood
                FOREIGN KEY (custom_food_id)
                REFERENCES nutrition.CustomFoods(custom_food_id)
                ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX UX_NutritionCustomFoodNutrients_FoodCanonical
            ON nutrition.CustomFoodNutrients(custom_food_id, canonical_code);
    END;

    IF OBJECT_ID(N'nutrition.Recipes', N'U') IS NULL
    BEGIN
        CREATE TABLE nutrition.Recipes
        (
            recipe_id uniqueidentifier NOT NULL,
            client_id uniqueidentifier NOT NULL,
            recipe_name nvarchar(200) NOT NULL,
            yield_servings decimal(12,4) NOT NULL,
            notes nvarchar(2000) NULL,
            is_active bit NOT NULL
                CONSTRAINT DF_NutritionRecipes_IsActive DEFAULT (1),
            created_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionRecipes_CreatedAt DEFAULT (SYSUTCDATETIME()),
            updated_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionRecipes_UpdatedAt DEFAULT (SYSUTCDATETIME()),
            row_version rowversion NOT NULL,
            CONSTRAINT PK_NutritionRecipes
                PRIMARY KEY CLUSTERED (recipe_id),
            CONSTRAINT FK_NutritionRecipes_Client
                FOREIGN KEY (client_id)
                REFERENCES app.Clients(client_id),
            CONSTRAINT CK_NutritionRecipes_YieldServings
                CHECK (yield_servings > 0)
        );

        CREATE INDEX IX_NutritionRecipes_ClientActiveName
            ON nutrition.Recipes(client_id, is_active, recipe_name);
    END;

    IF OBJECT_ID(N'nutrition.RecipeItems', N'U') IS NULL
    BEGIN
        CREATE TABLE nutrition.RecipeItems
        (
            recipe_item_id uniqueidentifier NOT NULL,
            recipe_id uniqueidentifier NOT NULL,
            item_order smallint NOT NULL,
            external_food_id uniqueidentifier NULL,
            custom_food_id uniqueidentifier NULL,
            quantity decimal(12,4) NOT NULL,
            quantity_unit_code varchar(30) NOT NULL,
            gram_weight decimal(12,4) NULL,
            notes nvarchar(500) NULL,
            created_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionRecipeItems_CreatedAt DEFAULT (SYSUTCDATETIME()),
            updated_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionRecipeItems_UpdatedAt DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_NutritionRecipeItems
                PRIMARY KEY CLUSTERED (recipe_item_id),
            CONSTRAINT FK_NutritionRecipeItems_Recipe
                FOREIGN KEY (recipe_id)
                REFERENCES nutrition.Recipes(recipe_id)
                ON DELETE CASCADE,
            CONSTRAINT FK_NutritionRecipeItems_ExternalFood
                FOREIGN KEY (external_food_id)
                REFERENCES nutrition.ExternalFoods(external_food_id),
            CONSTRAINT FK_NutritionRecipeItems_CustomFood
                FOREIGN KEY (custom_food_id)
                REFERENCES nutrition.CustomFoods(custom_food_id),
            CONSTRAINT CK_NutritionRecipeItems_OneFoodReference
                CHECK (
                    (CASE WHEN external_food_id IS NULL THEN 0 ELSE 1 END) +
                    (CASE WHEN custom_food_id IS NULL THEN 0 ELSE 1 END) = 1
                ),
            CONSTRAINT CK_NutritionRecipeItems_Quantity
                CHECK (quantity > 0),
            CONSTRAINT CK_NutritionRecipeItems_GramWeight
                CHECK (gram_weight IS NULL OR gram_weight > 0)
        );

        CREATE UNIQUE INDEX UX_NutritionRecipeItems_RecipeOrder
            ON nutrition.RecipeItems(recipe_id, item_order);
    END;

    IF OBJECT_ID(N'nutrition.SavedMeals', N'U') IS NULL
    BEGIN
        CREATE TABLE nutrition.SavedMeals
        (
            saved_meal_id uniqueidentifier NOT NULL,
            client_id uniqueidentifier NOT NULL,
            meal_name nvarchar(200) NOT NULL,
            meal_type_code varchar(30) NULL,
            notes nvarchar(2000) NULL,
            is_active bit NOT NULL
                CONSTRAINT DF_NutritionSavedMeals_IsActive DEFAULT (1),
            created_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionSavedMeals_CreatedAt DEFAULT (SYSUTCDATETIME()),
            updated_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionSavedMeals_UpdatedAt DEFAULT (SYSUTCDATETIME()),
            row_version rowversion NOT NULL,
            CONSTRAINT PK_NutritionSavedMeals
                PRIMARY KEY CLUSTERED (saved_meal_id),
            CONSTRAINT FK_NutritionSavedMeals_Client
                FOREIGN KEY (client_id)
                REFERENCES app.Clients(client_id)
        );

        CREATE INDEX IX_NutritionSavedMeals_ClientActiveName
            ON nutrition.SavedMeals(client_id, is_active, meal_name);
    END;

    IF OBJECT_ID(N'nutrition.SavedMealItems', N'U') IS NULL
    BEGIN
        CREATE TABLE nutrition.SavedMealItems
        (
            saved_meal_item_id uniqueidentifier NOT NULL,
            saved_meal_id uniqueidentifier NOT NULL,
            item_order smallint NOT NULL,
            external_food_id uniqueidentifier NULL,
            custom_food_id uniqueidentifier NULL,
            recipe_id uniqueidentifier NULL,
            quantity decimal(12,4) NOT NULL,
            quantity_unit_code varchar(30) NOT NULL,
            gram_weight decimal(12,4) NULL,
            created_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionSavedMealItems_CreatedAt DEFAULT (SYSUTCDATETIME()),
            updated_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionSavedMealItems_UpdatedAt DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_NutritionSavedMealItems
                PRIMARY KEY CLUSTERED (saved_meal_item_id),
            CONSTRAINT FK_NutritionSavedMealItems_Meal
                FOREIGN KEY (saved_meal_id)
                REFERENCES nutrition.SavedMeals(saved_meal_id)
                ON DELETE CASCADE,
            CONSTRAINT FK_NutritionSavedMealItems_ExternalFood
                FOREIGN KEY (external_food_id)
                REFERENCES nutrition.ExternalFoods(external_food_id),
            CONSTRAINT FK_NutritionSavedMealItems_CustomFood
                FOREIGN KEY (custom_food_id)
                REFERENCES nutrition.CustomFoods(custom_food_id),
            CONSTRAINT FK_NutritionSavedMealItems_Recipe
                FOREIGN KEY (recipe_id)
                REFERENCES nutrition.Recipes(recipe_id),
            CONSTRAINT CK_NutritionSavedMealItems_OneFoodReference
                CHECK (
                    (CASE WHEN external_food_id IS NULL THEN 0 ELSE 1 END) +
                    (CASE WHEN custom_food_id IS NULL THEN 0 ELSE 1 END) +
                    (CASE WHEN recipe_id IS NULL THEN 0 ELSE 1 END) = 1
                ),
            CONSTRAINT CK_NutritionSavedMealItems_Quantity
                CHECK (quantity > 0),
            CONSTRAINT CK_NutritionSavedMealItems_GramWeight
                CHECK (gram_weight IS NULL OR gram_weight > 0)
        );

        CREATE UNIQUE INDEX UX_NutritionSavedMealItems_MealOrder
            ON nutrition.SavedMealItems(saved_meal_id, item_order);
    END;

    IF OBJECT_ID(N'nutrition.FoodPreferences', N'U') IS NULL
    BEGIN
        CREATE TABLE nutrition.FoodPreferences
        (
            food_preference_id uniqueidentifier NOT NULL,
            client_id uniqueidentifier NOT NULL,
            external_food_id uniqueidentifier NULL,
            custom_food_id uniqueidentifier NULL,
            recipe_id uniqueidentifier NULL,
            is_favorite bit NOT NULL
                CONSTRAINT DF_NutritionFoodPreferences_IsFavorite DEFAULT (0),
            use_count int NOT NULL
                CONSTRAINT DF_NutritionFoodPreferences_UseCount DEFAULT (0),
            last_used_at_utc datetime2(3) NULL,
            created_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionFoodPreferences_CreatedAt DEFAULT (SYSUTCDATETIME()),
            updated_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionFoodPreferences_UpdatedAt DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_NutritionFoodPreferences
                PRIMARY KEY CLUSTERED (food_preference_id),
            CONSTRAINT FK_NutritionFoodPreferences_Client
                FOREIGN KEY (client_id)
                REFERENCES app.Clients(client_id),
            CONSTRAINT FK_NutritionFoodPreferences_ExternalFood
                FOREIGN KEY (external_food_id)
                REFERENCES nutrition.ExternalFoods(external_food_id),
            CONSTRAINT FK_NutritionFoodPreferences_CustomFood
                FOREIGN KEY (custom_food_id)
                REFERENCES nutrition.CustomFoods(custom_food_id),
            CONSTRAINT FK_NutritionFoodPreferences_Recipe
                FOREIGN KEY (recipe_id)
                REFERENCES nutrition.Recipes(recipe_id),
            CONSTRAINT CK_NutritionFoodPreferences_OneFoodReference
                CHECK (
                    (CASE WHEN external_food_id IS NULL THEN 0 ELSE 1 END) +
                    (CASE WHEN custom_food_id IS NULL THEN 0 ELSE 1 END) +
                    (CASE WHEN recipe_id IS NULL THEN 0 ELSE 1 END) = 1
                ),
            CONSTRAINT CK_NutritionFoodPreferences_UseCount
                CHECK (use_count >= 0)
        );

        CREATE UNIQUE INDEX UX_NutritionFoodPreferences_ClientExternal
            ON nutrition.FoodPreferences(client_id, external_food_id)
            WHERE external_food_id IS NOT NULL;

        CREATE UNIQUE INDEX UX_NutritionFoodPreferences_ClientCustom
            ON nutrition.FoodPreferences(client_id, custom_food_id)
            WHERE custom_food_id IS NOT NULL;

        CREATE UNIQUE INDEX UX_NutritionFoodPreferences_ClientRecipe
            ON nutrition.FoodPreferences(client_id, recipe_id)
            WHERE recipe_id IS NOT NULL;

        CREATE INDEX IX_NutritionFoodPreferences_ClientRecent
            ON nutrition.FoodPreferences(client_id, last_used_at_utc);
    END;

    IF OBJECT_ID(N'nutrition.DailyLogs', N'U') IS NULL
    BEGIN
        CREATE TABLE nutrition.DailyLogs
        (
            daily_log_id uniqueidentifier NOT NULL,
            client_id uniqueidentifier NOT NULL,
            log_date date NOT NULL,
            time_zone nvarchar(100) NULL,
            notes nvarchar(2000) NULL,
            created_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionDailyLogs_CreatedAt DEFAULT (SYSUTCDATETIME()),
            updated_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionDailyLogs_UpdatedAt DEFAULT (SYSUTCDATETIME()),
            row_version rowversion NOT NULL,
            CONSTRAINT PK_NutritionDailyLogs
                PRIMARY KEY CLUSTERED (daily_log_id),
            CONSTRAINT FK_NutritionDailyLogs_Client
                FOREIGN KEY (client_id)
                REFERENCES app.Clients(client_id)
        );

        CREATE UNIQUE INDEX UX_NutritionDailyLogs_ClientDate
            ON nutrition.DailyLogs(client_id, log_date);
    END;

    IF OBJECT_ID(N'nutrition.DailyLogItems', N'U') IS NULL
    BEGIN
        CREATE TABLE nutrition.DailyLogItems
        (
            daily_log_item_id uniqueidentifier NOT NULL,
            daily_log_id uniqueidentifier NOT NULL,
            meal_slot_code varchar(30) NOT NULL,
            item_order smallint NOT NULL,
            external_food_id uniqueidentifier NULL,
            custom_food_id uniqueidentifier NULL,
            recipe_id uniqueidentifier NULL,
            source_saved_meal_id uniqueidentifier NULL,
            description_snapshot nvarchar(300) NOT NULL,
            quantity decimal(12,4) NOT NULL,
            quantity_unit_code varchar(30) NOT NULL,
            gram_weight decimal(12,4) NULL,
            nutrient_snapshot_policy_code varchar(30) NOT NULL,
            energy_kcal decimal(12,4) NULL,
            protein_g decimal(12,4) NULL,
            carbohydrate_g decimal(12,4) NULL,
            fat_g decimal(12,4) NULL,
            fiber_g decimal(12,4) NULL,
            sugar_g decimal(12,4) NULL,
            sodium_mg decimal(12,4) NULL,
            saturated_fat_g decimal(12,4) NULL,
            created_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionDailyLogItems_CreatedAt DEFAULT (SYSUTCDATETIME()),
            updated_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionDailyLogItems_UpdatedAt DEFAULT (SYSUTCDATETIME()),
            row_version rowversion NOT NULL,
            CONSTRAINT PK_NutritionDailyLogItems
                PRIMARY KEY CLUSTERED (daily_log_item_id),
            CONSTRAINT FK_NutritionDailyLogItems_Log
                FOREIGN KEY (daily_log_id)
                REFERENCES nutrition.DailyLogs(daily_log_id)
                ON DELETE CASCADE,
            CONSTRAINT FK_NutritionDailyLogItems_ExternalFood
                FOREIGN KEY (external_food_id)
                REFERENCES nutrition.ExternalFoods(external_food_id),
            CONSTRAINT FK_NutritionDailyLogItems_CustomFood
                FOREIGN KEY (custom_food_id)
                REFERENCES nutrition.CustomFoods(custom_food_id),
            CONSTRAINT FK_NutritionDailyLogItems_Recipe
                FOREIGN KEY (recipe_id)
                REFERENCES nutrition.Recipes(recipe_id),
            CONSTRAINT FK_NutritionDailyLogItems_SourceSavedMeal
                FOREIGN KEY (source_saved_meal_id)
                REFERENCES nutrition.SavedMeals(saved_meal_id),
            CONSTRAINT CK_NutritionDailyLogItems_OneFoodReference
                CHECK (
                    (CASE WHEN external_food_id IS NULL THEN 0 ELSE 1 END) +
                    (CASE WHEN custom_food_id IS NULL THEN 0 ELSE 1 END) +
                    (CASE WHEN recipe_id IS NULL THEN 0 ELSE 1 END) = 1
                ),
            CONSTRAINT CK_NutritionDailyLogItems_Quantity
                CHECK (quantity > 0),
            CONSTRAINT CK_NutritionDailyLogItems_GramWeight
                CHECK (gram_weight IS NULL OR gram_weight > 0),
            CONSTRAINT CK_NutritionDailyLogItems_SnapshotPolicy
                CHECK (nutrient_snapshot_policy_code IN ('PERSISTED', 'REFERENCE_ONLY', 'USER_ENTERED'))
        );

        CREATE UNIQUE INDEX UX_NutritionDailyLogItems_LogMealOrder
            ON nutrition.DailyLogItems(daily_log_id, meal_slot_code, item_order);

        CREATE INDEX IX_NutritionDailyLogItems_ExternalFood
            ON nutrition.DailyLogItems(external_food_id)
            WHERE external_food_id IS NOT NULL;
    END;

    IF OBJECT_ID(N'nutrition.DailyTotals', N'U') IS NULL
    BEGIN
        CREATE TABLE nutrition.DailyTotals
        (
            daily_total_id uniqueidentifier NOT NULL,
            daily_log_id uniqueidentifier NOT NULL,
            energy_kcal decimal(12,4) NOT NULL,
            protein_g decimal(12,4) NOT NULL,
            carbohydrate_g decimal(12,4) NOT NULL,
            fat_g decimal(12,4) NOT NULL,
            fiber_g decimal(12,4) NULL,
            sugar_g decimal(12,4) NULL,
            sodium_mg decimal(12,4) NULL,
            saturated_fat_g decimal(12,4) NULL,
            completeness_code varchar(30) NOT NULL,
            calculated_at_utc datetime2(3) NOT NULL,
            created_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionDailyTotals_CreatedAt DEFAULT (SYSUTCDATETIME()),
            updated_at_utc datetime2(3) NOT NULL
                CONSTRAINT DF_NutritionDailyTotals_UpdatedAt DEFAULT (SYSUTCDATETIME()),
            row_version rowversion NOT NULL,
            CONSTRAINT PK_NutritionDailyTotals
                PRIMARY KEY CLUSTERED (daily_total_id),
            CONSTRAINT FK_NutritionDailyTotals_Log
                FOREIGN KEY (daily_log_id)
                REFERENCES nutrition.DailyLogs(daily_log_id)
                ON DELETE CASCADE,
            CONSTRAINT CK_NutritionDailyTotals_Completeness
                CHECK (completeness_code IN ('COMPLETE', 'PARTIAL', 'REFERENCE_ONLY'))
        );

        CREATE UNIQUE INDEX UX_NutritionDailyTotals_Log
            ON nutrition.DailyTotals(daily_log_id);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM nutrition.Providers
        WHERE provider_code = 'USDA_FDC'
    )
    BEGIN
        INSERT nutrition.Providers
        (
            provider_id,
            provider_code,
            display_name,
            cache_policy_code,
            license_code,
            requires_attribution,
            terms_review_required,
            is_enabled
        )
        VALUES
        (
            NEWID(),
            'USDA_FDC',
            N'USDA FoodData Central',
            'PERSISTENT',
            'CC0',
            0,
            0,
            1
        );
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM nutrition.Providers
        WHERE provider_code = 'FATSECRET'
    )
    BEGIN
        INSERT nutrition.Providers
        (
            provider_id,
            provider_code,
            display_name,
            cache_policy_code,
            license_code,
            requires_attribution,
            terms_review_required,
            is_enabled
        )
        VALUES
        (
            NEWID(),
            'FATSECRET',
            N'FatSecret Platform API',
            'REFERENCE_ONLY',
            'PROVIDER_TERMS',
            1,
            1,
            0
        );
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
