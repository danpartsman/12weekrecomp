SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'app.UserAccounts', N'U') IS NULL
    THROW 51200, 'B2.35.18 safety stop: this is not a valid 12WR database.', 1;

IF OBJECT_ID(N'app.ExerciseCatalogSyncSettings', N'U') IS NULL
    THROW 51201, 'B2.35.18 safety stop: app.ExerciseCatalogSyncSettings is missing.', 1;

DECLARE @BeforeState nvarchar(20) =
    CASE
        WHEN COL_LENGTH(N'app.ExerciseCatalogSyncSettings', N'last_dataset_sha_256') IS NULL
            THEN N'MISSING'
        ELSE N'PRESENT'
    END;

PRINT N'DATABASE=' + DB_NAME();
PRINT N'TABLE=app.ExerciseCatalogSyncSettings';
PRINT N'COLUMN_BEFORE=' + @BeforeState;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'app.ExerciseCatalogSyncSettings', N'last_dataset_sha_256') IS NULL
    BEGIN
        ALTER TABLE app.ExerciseCatalogSyncSettings
            ADD last_dataset_sha_256 varchar(64) NULL;

        PRINT N'ACTION=ADDED_last_dataset_sha_256';
    END
    ELSE
    BEGIN
        PRINT N'ACTION=ALREADY_PRESENT';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns AS c
        INNER JOIN sys.types AS t
            ON c.user_type_id = t.user_type_id
        WHERE c.object_id = OBJECT_ID(N'app.ExerciseCatalogSyncSettings')
          AND c.name = N'last_dataset_sha_256'
          AND t.name = N'varchar'
          AND c.max_length = 64
          AND c.is_nullable = 1
    )
        THROW 51202, 'B2.35.18 verification failed: last_dataset_sha_256 is not varchar(64) NULL.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM app.ExerciseCatalogSyncSettings
        WHERE settings_key = N'DEFAULT'
    )
        THROW 51203, 'B2.35.18 verification failed: DEFAULT settings row is missing.', 1;

    /*
       Dynamic SQL intentionally validates the same EF-shaped query only AFTER
       the ALTER TABLE has completed, avoiding SQL Server same-batch name binding.
    */
    EXEC sys.sp_executesql N'
        SELECT TOP(2)
            [e].[exercise_catalog_sync_setting_id],
            [e].[active_provider_code],
            [e].[auto_sync_enabled],
            [e].[created_at_utc],
            [e].[last_checked_at_utc],
            [e].[last_dataset_sha_256],
            [e].[last_error],
            [e].[last_provider_etag],
            [e].[last_provider_version],
            [e].[last_successful_sync_at_utc],
            [e].[media_provider_code],
            [e].[refresh_interval_days],
            [e].[settings_key],
            [e].[sync_lease_id],
            [e].[sync_lease_until_utc],
            [e].[updated_at_utc],
            [e].[updated_by_user_id]
        FROM [app].[ExerciseCatalogSyncSettings] AS [e]
        WHERE [e].[settings_key] = N''DEFAULT'';
    ';

    COMMIT TRANSACTION;

    PRINT N'COLUMN_AFTER=PRESENT';
    PRINT N'COLUMN_TYPE=varchar(64) NULL';
    PRINT N'EF_SHAPED_QUERY=PASS';
    PRINT N'B2_35_18_SCHEMA_GREEN';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;