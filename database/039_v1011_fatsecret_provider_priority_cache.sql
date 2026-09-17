SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF DB_NAME() <> N'sqldb-recomp'
        THROW 51200, 'Safety stop: wrong database.', 1;

    IF OBJECT_ID(N'nutrition.Providers', N'U') IS NULL
        THROW 51201, 'nutrition.Providers is missing.', 1;

    IF OBJECT_ID(N'nutrition.ExternalFoods', N'U') IS NULL
        THROW 51202, 'nutrition.ExternalFoods is missing.', 1;

    IF OBJECT_ID(N'nutrition.DailyLogItems', N'U') IS NULL
        THROW 51203, 'nutrition.DailyLogItems is missing.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM nutrition.Providers
        WHERE provider_code = 'FATSECRET'
    )
        THROW 51204, 'FATSECRET provider row is missing.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM nutrition.Providers
        WHERE provider_code = 'USDA_FDC'
    )
        THROW 51205, 'USDA_FDC provider row is missing.', 1;

    IF COL_LENGTH(
        N'nutrition.Providers',
        N'search_priority') IS NULL
    BEGIN
        ALTER TABLE nutrition.Providers
            ADD search_priority smallint NOT NULL
                CONSTRAINT DF_NutritionProviders_SearchPriority
                DEFAULT (100) WITH VALUES;
    END;

    IF COL_LENGTH(
        N'nutrition.Providers',
        N'cache_ttl_minutes') IS NULL
    BEGIN
        ALTER TABLE nutrition.Providers
            ADD cache_ttl_minutes int NULL;
    END;

    IF COL_LENGTH(
        N'nutrition.DailyLogItems',
        N'source_serving_id') IS NULL
    BEGIN
        ALTER TABLE nutrition.DailyLogItems
            ADD source_serving_id nvarchar(120) NULL;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id =
            OBJECT_ID(N'nutrition.Providers')
          AND name =
            N'CK_NutritionProviders_CachePolicy'
    )
    BEGIN
        ALTER TABLE nutrition.Providers
            DROP CONSTRAINT CK_NutritionProviders_CachePolicy;
    END;

    ALTER TABLE nutrition.Providers
        ADD CONSTRAINT CK_NutritionProviders_CachePolicy
        CHECK
        (
            cache_policy_code IN
            (
                'PERSISTENT',
                'TEMPORARY',
                'REFERENCE_ONLY',
                'SESSION_ONLY'
            )
        );

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id =
            OBJECT_ID(N'nutrition.ExternalFoods')
          AND name =
            N'CK_NutritionExternalFoods_CachePolicy'
    )
    BEGIN
        ALTER TABLE nutrition.ExternalFoods
            DROP CONSTRAINT CK_NutritionExternalFoods_CachePolicy;
    END;

    ALTER TABLE nutrition.ExternalFoods
        ADD CONSTRAINT CK_NutritionExternalFoods_CachePolicy
        CHECK
        (
            cache_policy_snapshot_code IN
            (
                'PERSISTENT',
                'TEMPORARY',
                'REFERENCE_ONLY',
                'SESSION_ONLY'
            )
        );

    EXEC sys.sp_executesql N'
        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.check_constraints
            WHERE parent_object_id =
                OBJECT_ID(N''nutrition.Providers'')
              AND name =
                N''CK_NutritionProviders_SearchPriority''
        )
        BEGIN
            ALTER TABLE nutrition.Providers
                ADD CONSTRAINT CK_NutritionProviders_SearchPriority
                CHECK
                (
                    search_priority >= 1
                    AND search_priority <= 32767
                );
        END;
    ';

    EXEC sys.sp_executesql N'
        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.check_constraints
            WHERE parent_object_id =
                OBJECT_ID(N''nutrition.Providers'')
              AND name =
                N''CK_NutritionProviders_CacheTtl''
        )
        BEGIN
            ALTER TABLE nutrition.Providers
                ADD CONSTRAINT CK_NutritionProviders_CacheTtl
                CHECK
                (
                    cache_ttl_minutes IS NULL
                    OR cache_ttl_minutes > 0
                );
        END;
    ';

    EXEC sys.sp_executesql N'
        UPDATE nutrition.Providers
        SET
            display_name = N''FatSecret Premier'',
            cache_policy_code = ''TEMPORARY'',
            requires_attribution = 1,
            terms_review_required = 0,
            is_enabled = 1,
            search_priority = 10,
            cache_ttl_minutes = 1440,
            updated_at_utc = SYSUTCDATETIME()
        WHERE provider_code = ''FATSECRET'';
    ';

    EXEC sys.sp_executesql N'
        UPDATE nutrition.Providers
        SET
            display_name = N''USDA FoodData Central'',
            cache_policy_code = ''PERSISTENT'',
            requires_attribution = 0,
            terms_review_required = 0,
            is_enabled = 1,
            search_priority = 20,
            cache_ttl_minutes = NULL,
            updated_at_utc = SYSUTCDATETIME()
        WHERE provider_code = ''USDA_FDC'';
    ';

    EXEC sys.sp_executesql N'
        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE object_id =
                OBJECT_ID(N''nutrition.Providers'')
              AND name =
                N''IX_NutritionProviders_SearchPriority''
        )
        BEGIN
            CREATE INDEX IX_NutritionProviders_SearchPriority
                ON nutrition.Providers(
                    is_enabled,
                    search_priority,
                    provider_code);
        END;
    ';

    EXEC sys.sp_executesql N'
        IF
        (
            SELECT COUNT(*)
            FROM nutrition.Providers
            WHERE provider_code IN
                (''FATSECRET'', ''USDA_FDC'')
              AND is_enabled = 1
        ) <> 2
            THROW 51206, ''Provider enablement verification failed.'', 1;
    ';

    EXEC sys.sp_executesql N'
        DECLARE @fat smallint;
        DECLARE @usda smallint;

        SELECT @fat = search_priority
        FROM nutrition.Providers
        WHERE provider_code = ''FATSECRET'';

        SELECT @usda = search_priority
        FROM nutrition.Providers
        WHERE provider_code = ''USDA_FDC'';

        IF @fat IS NULL OR @usda IS NULL OR @fat >= @usda
            THROW 51207, ''FatSecret is not configured ahead of USDA.'', 1;
    ';

    EXEC sys.sp_executesql N'
        DECLARE @ttl int;

        SELECT @ttl = cache_ttl_minutes
        FROM nutrition.Providers
        WHERE provider_code = ''FATSECRET'';

        IF @ttl <> 1440
            THROW 51208, ''FatSecret cache TTL verification failed.'', 1;
    ';

    IF EXISTS
    (
        SELECT 1
        FROM nutrition.ExternalFoods ef
        INNER JOIN nutrition.Providers p
            ON p.provider_id = ef.provider_id
        WHERE p.provider_code = 'FATSECRET'
    )
        THROW 51209, 'Unexpected pre-existing FatSecret cache rows detected.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;