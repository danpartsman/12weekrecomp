SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
GO

PRINT '12WR TEST v1010 Migration 031 - Catalog Provider Sync';
PRINT '-----------------------------------------------------';
GO

IF OBJECT_ID(N'app.ExerciseCatalogSyncSettings', N'U') IS NULL
BEGIN
    CREATE TABLE [app].[ExerciseCatalogSyncSettings]
    (
        [exercise_catalog_sync_setting_id] uniqueidentifier NOT NULL,
        [settings_key] varchar(32) NOT NULL,
        [active_provider_code] varchar(40) NOT NULL,
        [media_provider_code] varchar(40) NOT NULL,
        [auto_sync_enabled] bit NOT NULL CONSTRAINT [DF_ExerciseCatalogSyncSettings_auto_sync_enabled] DEFAULT (0),
        [refresh_interval_days] int NOT NULL CONSTRAINT [DF_ExerciseCatalogSyncSettings_refresh_interval_days] DEFAULT (7),
        [last_checked_at_utc] datetime2(7) NULL,
        [last_successful_sync_at_utc] datetime2(7) NULL,
        [last_dataset_sha256] varchar(64) NULL,
        [last_provider_version] nvarchar(200) NULL,
        [last_provider_etag] nvarchar(300) NULL,
        [last_error] nvarchar(4000) NULL,
        [sync_lease_id] uniqueidentifier NULL,
        [sync_lease_until_utc] datetime2(7) NULL,
        [updated_by_user_id] uniqueidentifier NULL,
        [created_at_utc] datetime2(7) NOT NULL CONSTRAINT [DF_ExerciseCatalogSyncSettings_created_at_utc] DEFAULT (SYSUTCDATETIME()),
        [updated_at_utc] datetime2(7) NOT NULL CONSTRAINT [DF_ExerciseCatalogSyncSettings_updated_at_utc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ExerciseCatalogSyncSettings] PRIMARY KEY ([exercise_catalog_sync_setting_id]),
        CONSTRAINT [CK_ExerciseCatalogSyncSettings_refresh_interval_days]
            CHECK ([refresh_interval_days] IN (1, 3, 7, 14, 30)),
        CONSTRAINT [FK_ExerciseCatalogSyncSettings_UpdatedBy]
            FOREIGN KEY ([updated_by_user_id])
            REFERENCES [app].[UserAccounts]([user_id])
    );
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'app.ExerciseCatalogSyncSettings')
      AND [name] = N'UX_ExerciseCatalogSyncSettings_SettingsKey')
BEGIN
    CREATE UNIQUE INDEX [UX_ExerciseCatalogSyncSettings_SettingsKey]
        ON [app].[ExerciseCatalogSyncSettings]([settings_key]);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM [app].[ExerciseCatalogSyncSettings]
    WHERE [settings_key] = 'DEFAULT')
BEGIN
    INSERT INTO [app].[ExerciseCatalogSyncSettings]
    (
        [exercise_catalog_sync_setting_id],
        [settings_key],
        [active_provider_code],
        [media_provider_code],
        [auto_sync_enabled],
        [refresh_interval_days],
        [created_at_utc],
        [updated_at_utc]
    )
    VALUES
    (
        NEWID(),
        'DEFAULT',
        'KINETIC',
        'NONE',
        0,
        7,
        SYSUTCDATETIME(),
        SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'app.ExerciseCatalogSyncRuns', N'U') IS NULL
BEGIN
    CREATE TABLE [app].[ExerciseCatalogSyncRuns]
    (
        [exercise_catalog_sync_run_id] uniqueidentifier NOT NULL,
        [provider_code] varchar(40) NOT NULL,
        [trigger_code] varchar(20) NOT NULL,
        [status_code] varchar(20) NOT NULL,
        [started_at_utc] datetime2(7) NOT NULL,
        [completed_at_utc] datetime2(7) NULL,
        [dataset_sha256] varchar(64) NULL,
        [source_version] nvarchar(200) NULL,
        [added_count] int NULL,
        [updated_count] int NULL,
        [deactivated_count] int NULL,
        [active_count] int NULL,
        [error_message] nvarchar(4000) NULL,
        CONSTRAINT [PK_ExerciseCatalogSyncRuns] PRIMARY KEY ([exercise_catalog_sync_run_id])
    );
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'app.ExerciseCatalogSyncRuns')
      AND [name] = N'IX_ExerciseCatalogSyncRuns_StartedAtUtc')
BEGIN
    CREATE INDEX [IX_ExerciseCatalogSyncRuns_StartedAtUtc]
        ON [app].[ExerciseCatalogSyncRuns]([started_at_utc] DESC);
END;
GO

IF OBJECT_ID(N'app.ExerciseMediaAssets', N'U') IS NULL
BEGIN
    CREATE TABLE [app].[ExerciseMediaAssets]
    (
        [exercise_media_asset_id] uniqueidentifier NOT NULL,
        [exercise_catalog_item_id] uniqueidentifier NOT NULL,
        [provider_code] varchar(40) NOT NULL,
        [source_external_id] nvarchar(200) NOT NULL,
        [media_type_code] varchar(30) NOT NULL,
        [media_url] nvarchar(2000) NULL,
        [local_path] nvarchar(1000) NULL,
        [license_code] nvarchar(80) NULL,
        [attribution] nvarchar(1000) NULL,
        [display_order] smallint NOT NULL CONSTRAINT [DF_ExerciseMediaAssets_display_order] DEFAULT (0),
        [is_active] bit NOT NULL CONSTRAINT [DF_ExerciseMediaAssets_is_active] DEFAULT (1),
        [last_seen_sync_run_id] uniqueidentifier NULL,
        [created_at_utc] datetime2(7) NOT NULL CONSTRAINT [DF_ExerciseMediaAssets_created_at_utc] DEFAULT (SYSUTCDATETIME()),
        [updated_at_utc] datetime2(7) NOT NULL CONSTRAINT [DF_ExerciseMediaAssets_updated_at_utc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ExerciseMediaAssets] PRIMARY KEY ([exercise_media_asset_id]),
        CONSTRAINT [FK_ExerciseMediaAssets_Exercise]
            FOREIGN KEY ([exercise_catalog_item_id])
            REFERENCES [app].[ExerciseCatalogItems]([exercise_catalog_item_id])
            ON DELETE CASCADE,
        CONSTRAINT [FK_ExerciseMediaAssets_LastSyncRun]
            FOREIGN KEY ([last_seen_sync_run_id])
            REFERENCES [app].[ExerciseCatalogSyncRuns]([exercise_catalog_sync_run_id])
    );
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'app.ExerciseMediaAssets')
      AND [name] = N'UX_ExerciseMediaAssets_ProviderExternalId')
BEGIN
    CREATE UNIQUE INDEX [UX_ExerciseMediaAssets_ProviderExternalId]
        ON [app].[ExerciseMediaAssets]
        ([exercise_catalog_item_id], [provider_code], [source_external_id]);
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'app.ExerciseMediaAssets')
      AND [name] = N'IX_ExerciseMediaAssets_ActiveExercise')
BEGIN
    CREATE INDEX [IX_ExerciseMediaAssets_ActiveExercise]
        ON [app].[ExerciseMediaAssets]([exercise_catalog_item_id], [is_active], [display_order]);
END;
GO

IF DATABASE_PRINCIPAL_ID(N'recomp-test') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[app].[ExerciseCatalogSyncSettings] TO [recomp-test];
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[app].[ExerciseCatalogSyncRuns] TO [recomp-test];
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[app].[ExerciseMediaAssets] TO [recomp-test];
END;
GO

SELECT
    CASE WHEN OBJECT_ID(N'app.ExerciseCatalogSyncSettings', N'U') IS NULL THEN 'MISSING' ELSE 'READY' END AS ExerciseCatalogSyncSettings,
    CASE WHEN OBJECT_ID(N'app.ExerciseCatalogSyncRuns', N'U') IS NULL THEN 'MISSING' ELSE 'READY' END AS ExerciseCatalogSyncRuns,
    CASE WHEN OBJECT_ID(N'app.ExerciseMediaAssets', N'U') IS NULL THEN 'MISSING' ELSE 'READY' END AS ExerciseMediaAssets;
GO

PRINT 'PASS: Catalog provider/sync schema is ready.';
GO
