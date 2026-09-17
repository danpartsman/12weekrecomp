SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
GO
/*
    Engine 3.0 runtime prerequisite.

    Client.LifestyleActivityCode exists in the current application model,
    but some test database revisions do not yet contain the physical column.
*/
IF COL_LENGTH(N'app.Clients', N'lifestyle_activity_code') IS NULL
BEGIN
    ALTER TABLE [app].[Clients]
    ADD [lifestyle_activity_code] varchar(20) NULL;

    PRINT 'ADDED: app.Clients.lifestyle_activity_code';
END
ELSE
BEGIN
    PRINT 'EXISTS: app.Clients.lifestyle_activity_code';
END;
GO


/*
    Validate the existing WorkoutTypeDefinitions key before creating
    the Engine 3.0 version table.
*/
IF OBJECT_ID(N'config.WorkoutTypeDefinitions', N'U') IS NULL
BEGIN
    THROW 51001,
        'config.WorkoutTypeDefinitions does not exist.',
        1;
END;
GO

IF COL_LENGTH(
       N'config.WorkoutTypeDefinitions',
       N'workout_type_definition_id'
   ) IS NULL
BEGIN
    THROW 51002,
        'config.WorkoutTypeDefinitions.workout_type_definition_id does not exist.',
        1;
END;
GO


/*
    Versioned Engine 3.0 administrator workout profiles.
*/
IF OBJECT_ID(
       N'config.Engine30WorkoutProfileVersions',
       N'U'
   ) IS NULL
BEGIN

    CREATE TABLE [config].[Engine30WorkoutProfileVersions]
    (
        [engine_30_workout_profile_version_id]
            uniqueidentifier NOT NULL,

        [workout_type_definition_id]
            uniqueidentifier NOT NULL,

        [version_number]
            int NOT NULL,

        [version_code]
            nvarchar(80) NOT NULL,

        [is_baseline]
            bit NOT NULL
            CONSTRAINT [DF_E30WorkoutProfile_IsBaseline]
            DEFAULT (0),

        [is_active]
            bit NOT NULL
            CONSTRAINT [DF_E30WorkoutProfile_IsActive]
            DEFAULT (0),

        [profile_json]
            nvarchar(max) NOT NULL,

        [restored_from_profile_version_id]
            uniqueidentifier NULL,

        [change_notes]
            nvarchar(1000) NULL,

        [created_by]
            nvarchar(256) NULL,

        [created_at_utc]
            datetime2(7) NOT NULL
            CONSTRAINT [DF_E30WorkoutProfile_CreatedAtUtc]
            DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT [PK_Engine30WorkoutProfileVersions]
            PRIMARY KEY
            ([engine_30_workout_profile_version_id]),

        CONSTRAINT [FK_E30WorkoutProfile_WorkoutType]
            FOREIGN KEY
            ([workout_type_definition_id])
            REFERENCES
            [config].[WorkoutTypeDefinitions]
            ([workout_type_definition_id])
            ON DELETE CASCADE
    );

    PRINT 'CREATED: config.Engine30WorkoutProfileVersions';

END
ELSE
BEGIN

    /*
        If this exists but does NOT have the snake_case ID column,
        stop instead of silently corrupting anything.
    */
    IF COL_LENGTH(
           N'config.Engine30WorkoutProfileVersions',
           N'engine_30_workout_profile_version_id'
       ) IS NULL
    BEGIN
        THROW 51003,
            'Engine30WorkoutProfileVersions exists with an incompatible column layout.',
            1;
    END;

    PRINT 'EXISTS: config.Engine30WorkoutProfileVersions';

END;
GO


IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE
        [name] =
            N'UX_Engine30WorkoutProfileVersions_Workout_Version'
        AND
        [object_id] =
            OBJECT_ID(
                N'config.Engine30WorkoutProfileVersions'
            )
)
BEGIN

    CREATE UNIQUE INDEX
        [UX_Engine30WorkoutProfileVersions_Workout_Version]
    ON
        [config].[Engine30WorkoutProfileVersions]
    (
        [workout_type_definition_id],
        [version_number]
    );

END;
GO


IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE
        [name] =
            N'IX_Engine30WorkoutProfileVersions_Active'
        AND
        [object_id] =
            OBJECT_ID(
                N'config.Engine30WorkoutProfileVersions'
            )
)
BEGIN

    CREATE INDEX
        [IX_Engine30WorkoutProfileVersions_Active]
    ON
        [config].[Engine30WorkoutProfileVersions]
    (
        [workout_type_definition_id],
        [is_active]
    )
    WHERE
        [is_active] = 1;

END;
GO


IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE
        [name] =
            N'IX_Engine30WorkoutProfileVersions_CreatedAtUtc'
        AND
        [object_id] =
            OBJECT_ID(
                N'config.Engine30WorkoutProfileVersions'
            )
)
BEGIN

    CREATE INDEX
        [IX_Engine30WorkoutProfileVersions_CreatedAtUtc]
    ON
        [config].[Engine30WorkoutProfileVersions]
    (
        [created_at_utc] DESC
    );

END;
GO


PRINT 'PASS: Engine 3.0 runtime database schema is aligned.';
GO

