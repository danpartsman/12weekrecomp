SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -------------------------------------------------------------------------
    -- v1008-A: Global Workout Catalog foundation
    -------------------------------------------------------------------------

    IF COL_LENGTH('config.WorkoutTypeDefinitions', 'workout_category_code') IS NULL
    BEGIN
        ALTER TABLE config.WorkoutTypeDefinitions
        ADD workout_category_code varchar(30) NULL;
    END;

    IF COL_LENGTH('config.WorkoutTypeDefinitions', 'is_selectable') IS NULL
    BEGIN
        ALTER TABLE config.WorkoutTypeDefinitions
        ADD is_selectable bit NULL;
    END;

    IF COL_LENGTH('config.WorkoutTypeDefinitions', 'display_order') IS NULL
    BEGIN
        ALTER TABLE config.WorkoutTypeDefinitions
        ADD display_order smallint NULL;
    END;

    IF COL_LENGTH('config.WorkoutTypeDefinitions', 'description') IS NULL
    BEGIN
        ALTER TABLE config.WorkoutTypeDefinitions
        ADD description nvarchar(1000) NULL;
    END;

    IF COL_LENGTH('config.WorkoutTypeDefinitions', 'instructions') IS NULL
    BEGIN
        ALTER TABLE config.WorkoutTypeDefinitions
        ADD instructions nvarchar(max) NULL;
    END;

    IF COL_LENGTH('config.WorkoutTypeDefinitions', 'overview_video_url') IS NULL
    BEGIN
        ALTER TABLE config.WorkoutTypeDefinitions
        ADD overview_video_url nvarchar(1000) NULL;
    END;

    -------------------------------------------------------------------------
    -- Backfill authoritative workout category.
    -------------------------------------------------------------------------

    EXEC sys.sp_executesql N'
        UPDATE config.WorkoutTypeDefinitions
        SET workout_category_code =
            CASE
                WHEN UPPER(weekly_role_code) IN (''HEAVY'', ''HEAVY_GENERIC'')
                     OR counts_dedicated_heavy = 1
                    THEN ''MAJOR_STRENGTH''

                WHEN UPPER(weekly_role_code) = ''LIGHT_STRENGTH''
                     OR counts_light_strength = 1
                    THEN ''ACCESSORY_STRENGTH''

                WHEN UPPER(weekly_role_code) IN (''CONDITIONING'', ''RECOVERY_CARDIO'')
                     OR counts_primary_conditioning = 1
                    THEN ''CONDITIONING_CARDIO''

                WHEN UPPER(weekly_role_code) IN (''RECOVERY'', ''OTHER_LOAD'')
                     OR counts_recovery = 1
                    THEN ''REST_RECOVERY_OTHER''

                ELSE ''REST_RECOVERY_OTHER''
            END
        WHERE workout_category_code IS NULL
           OR workout_category_code NOT IN
              (
                  ''MAJOR_STRENGTH'',
                  ''ACCESSORY_STRENGTH'',
                  ''CONDITIONING_CARDIO'',
                  ''REST_RECOVERY_OTHER''
              );
    ';

    -------------------------------------------------------------------------
    -- Preserve current selection behavior.
    -------------------------------------------------------------------------

    EXEC sys.sp_executesql N'
        UPDATE config.WorkoutTypeDefinitions
        SET is_selectable =
            CASE
                WHEN is_active = 1 THEN 1
                ELSE 0
            END
        WHERE is_selectable IS NULL;
    ';

    EXEC sys.sp_executesql N'
        UPDATE config.WorkoutTypeDefinitions
        SET display_order = 100
        WHERE display_order IS NULL;
    ';

    -------------------------------------------------------------------------
    -- Required catalog fields after backfill.
    -------------------------------------------------------------------------

    EXEC sys.sp_executesql N'
        ALTER TABLE config.WorkoutTypeDefinitions
        ALTER COLUMN workout_category_code varchar(30) NOT NULL;
    ';

    EXEC sys.sp_executesql N'
        ALTER TABLE config.WorkoutTypeDefinitions
        ALTER COLUMN is_selectable bit NOT NULL;
    ';

    EXEC sys.sp_executesql N'
        ALTER TABLE config.WorkoutTypeDefinitions
        ALTER COLUMN display_order smallint NOT NULL;
    ';

    -------------------------------------------------------------------------
    -- Defaults for administrator-created definitions.
    -------------------------------------------------------------------------

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON c.object_id = dc.parent_object_id
           AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id =
              OBJECT_ID('config.WorkoutTypeDefinitions')
          AND c.name = 'is_selectable'
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE config.WorkoutTypeDefinitions
            ADD CONSTRAINT DF_WorkoutTypeDefinitions_IsSelectable
                DEFAULT (1) FOR is_selectable;
        ';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON c.object_id = dc.parent_object_id
           AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id =
              OBJECT_ID('config.WorkoutTypeDefinitions')
          AND c.name = 'display_order'
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE config.WorkoutTypeDefinitions
            ADD CONSTRAINT DF_WorkoutTypeDefinitions_DisplayOrder
                DEFAULT (100) FOR display_order;
        ';
    END;

    -------------------------------------------------------------------------
    -- Protect the four-category Engine contract.
    -------------------------------------------------------------------------

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id =
              OBJECT_ID('config.WorkoutTypeDefinitions')
          AND name =
              'CK_WorkoutTypeDefinitions_WorkoutCategoryCode'
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE config.WorkoutTypeDefinitions
            WITH CHECK ADD CONSTRAINT
                CK_WorkoutTypeDefinitions_WorkoutCategoryCode
            CHECK
            (
                workout_category_code IN
                (
                    ''MAJOR_STRENGTH'',
                    ''ACCESSORY_STRENGTH'',
                    ''CONDITIONING_CARDIO'',
                    ''REST_RECOVERY_OTHER''
                )
            );

            ALTER TABLE config.WorkoutTypeDefinitions
            CHECK CONSTRAINT
                CK_WorkoutTypeDefinitions_WorkoutCategoryCode;
        ';
    END;

    COMMIT TRANSACTION;

    -------------------------------------------------------------------------
    -- Verification
    -------------------------------------------------------------------------

    EXEC sys.sp_executesql N'
        SELECT
            cv.version_code,
            w.workout_type_code,
            w.display_name,
            w.weekly_role_code,
            w.workout_category_code,
            w.fuel_score,
            w.push_load,
            w.pull_load,
            w.leg_load,
            w.cardio_load,
            w.is_active,
            w.is_selectable,
            w.display_order
        FROM config.WorkoutTypeDefinitions w
        INNER JOIN config.ConfigVersions cv
            ON cv.config_version_id = w.config_version_id
        ORDER BY
            cv.version_code,
            w.workout_category_code,
            w.display_order,
            w.display_name;
    ';

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
