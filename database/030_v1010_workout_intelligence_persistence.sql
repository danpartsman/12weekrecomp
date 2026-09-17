SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
GO

PRINT '12WR TEST v1010 Migration 030 - Workout Intelligence Persistence';
PRINT '--------------------------------------------------------------';
GO

IF OBJECT_ID(N'app.ExerciseCatalogImports', N'U') IS NULL
BEGIN
    CREATE TABLE [app].[ExerciseCatalogImports]
    (
        [exercise_catalog_import_id] uniqueidentifier NOT NULL,
        [source_code] varchar(40) NOT NULL,
        [license_code] varchar(40) NOT NULL,
        [source_version] nvarchar(200) NULL,
        [manifest_sha256] varchar(64) NULL,
        [captured_at_utc] datetime2(7) NOT NULL,
        [exercise_count] int NOT NULL,
        [created_at_utc] datetime2(7) NOT NULL CONSTRAINT [DF_ExerciseCatalogImports_created_at_utc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ExerciseCatalogImports] PRIMARY KEY ([exercise_catalog_import_id])
    );
END;
GO

IF OBJECT_ID(N'app.ExerciseCatalogItems', N'U') IS NULL
BEGIN
    CREATE TABLE [app].[ExerciseCatalogItems]
    (
        [exercise_catalog_item_id] uniqueidentifier NOT NULL,
        [source_code] varchar(40) NOT NULL,
        [source_external_id] nvarchar(100) NOT NULL,
        [name] nvarchar(200) NOT NULL,
        [measurement_type] varchar(30) NOT NULL,
        [difficulty_level] varchar(50) NULL,
        [force_type] varchar(30) NULL,
        [mechanics] varchar(30) NULL,
        [category] varchar(60) NULL,
        [instructions_json] nvarchar(max) NULL,
        [is_active] bit NOT NULL CONSTRAINT [DF_ExerciseCatalogItems_is_active] DEFAULT (1),
        [last_import_id] uniqueidentifier NULL,
        [created_at_utc] datetime2(7) NOT NULL CONSTRAINT [DF_ExerciseCatalogItems_created_at_utc] DEFAULT (SYSUTCDATETIME()),
        [updated_at_utc] datetime2(7) NOT NULL CONSTRAINT [DF_ExerciseCatalogItems_updated_at_utc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ExerciseCatalogItems] PRIMARY KEY ([exercise_catalog_item_id]),
        CONSTRAINT [FK_ExerciseCatalogItems_LastImport]
            FOREIGN KEY ([last_import_id])
            REFERENCES [app].[ExerciseCatalogImports]([exercise_catalog_import_id])
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'app.ExerciseCatalogItems') AND [name] = N'UX_ExerciseCatalogItems_SourceExternalId')
BEGIN
    CREATE UNIQUE INDEX [UX_ExerciseCatalogItems_SourceExternalId]
        ON [app].[ExerciseCatalogItems]([source_code], [source_external_id]);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'app.ExerciseCatalogItems') AND [name] = N'IX_ExerciseCatalogItems_ActiveName')
BEGIN
    CREATE INDEX [IX_ExerciseCatalogItems_ActiveName]
        ON [app].[ExerciseCatalogItems]([is_active], [name]);
END;
GO

IF OBJECT_ID(N'app.ExerciseMuscleTargets', N'U') IS NULL
BEGIN
    CREATE TABLE [app].[ExerciseMuscleTargets]
    (
        [exercise_muscle_target_id] uniqueidentifier NOT NULL,
        [exercise_catalog_item_id] uniqueidentifier NOT NULL,
        [external_id] nvarchar(100) NOT NULL,
        [muscle_slug] varchar(100) NOT NULL,
        [muscle_name] nvarchar(150) NOT NULL,
        [role_code] varchar(30) NOT NULL,
        [display_order] smallint NOT NULL,
        CONSTRAINT [PK_ExerciseMuscleTargets] PRIMARY KEY ([exercise_muscle_target_id]),
        CONSTRAINT [FK_ExerciseMuscleTargets_Exercise]
            FOREIGN KEY ([exercise_catalog_item_id])
            REFERENCES [app].[ExerciseCatalogItems]([exercise_catalog_item_id])
            ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'app.ExerciseMuscleTargets') AND [name] = N'IX_ExerciseMuscleTargets_Exercise')
BEGIN
    CREATE INDEX [IX_ExerciseMuscleTargets_Exercise]
        ON [app].[ExerciseMuscleTargets]([exercise_catalog_item_id], [display_order]);
END;
GO

IF OBJECT_ID(N'app.ExerciseEquipmentRequirements', N'U') IS NULL
BEGIN
    CREATE TABLE [app].[ExerciseEquipmentRequirements]
    (
        [exercise_equipment_requirement_id] uniqueidentifier NOT NULL,
        [exercise_catalog_item_id] uniqueidentifier NOT NULL,
        [external_id] nvarchar(100) NOT NULL,
        [equipment_name] nvarchar(150) NOT NULL,
        [equipment_type] varchar(80) NOT NULL,
        [usage_type] varchar(80) NULL,
        [num_items] int NULL,
        [display_order] smallint NOT NULL,
        CONSTRAINT [PK_ExerciseEquipmentRequirements] PRIMARY KEY ([exercise_equipment_requirement_id]),
        CONSTRAINT [FK_ExerciseEquipmentRequirements_Exercise]
            FOREIGN KEY ([exercise_catalog_item_id])
            REFERENCES [app].[ExerciseCatalogItems]([exercise_catalog_item_id])
            ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'app.ExerciseEquipmentRequirements') AND [name] = N'IX_ExerciseEquipmentRequirements_Exercise')
BEGIN
    CREATE INDEX [IX_ExerciseEquipmentRequirements_Exercise]
        ON [app].[ExerciseEquipmentRequirements]([exercise_catalog_item_id], [display_order]);
END;
GO

IF OBJECT_ID(N'app.DetailedWorkoutExercises', N'U') IS NULL
BEGIN
    CREATE TABLE [app].[DetailedWorkoutExercises]
    (
        [detailed_workout_exercise_id] uniqueidentifier NOT NULL,
        [workout_session_id] uniqueidentifier NOT NULL,
        [exercise_catalog_item_id] uniqueidentifier NOT NULL,
        [exercise_order] smallint NOT NULL,
        [exercise_name_snapshot] nvarchar(200) NOT NULL,
        [mechanics_snapshot] varchar(30) NULL,
        [notes] nvarchar(1000) NULL,
        [created_at_utc] datetime2(7) NOT NULL CONSTRAINT [DF_DetailedWorkoutExercises_created_at_utc] DEFAULT (SYSUTCDATETIME()),
        [updated_at_utc] datetime2(7) NOT NULL CONSTRAINT [DF_DetailedWorkoutExercises_updated_at_utc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_DetailedWorkoutExercises] PRIMARY KEY ([detailed_workout_exercise_id]),
        CONSTRAINT [FK_DetailedWorkoutExercises_WorkoutSession]
            FOREIGN KEY ([workout_session_id])
            REFERENCES [app].[WorkoutSessions]([workout_session_id])
            ON DELETE CASCADE,
        CONSTRAINT [FK_DetailedWorkoutExercises_Exercise]
            FOREIGN KEY ([exercise_catalog_item_id])
            REFERENCES [app].[ExerciseCatalogItems]([exercise_catalog_item_id])
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'app.DetailedWorkoutExercises') AND [name] = N'UX_DetailedWorkoutExercises_SessionOrder')
BEGIN
    CREATE UNIQUE INDEX [UX_DetailedWorkoutExercises_SessionOrder]
        ON [app].[DetailedWorkoutExercises]([workout_session_id], [exercise_order]);
END;
GO

IF OBJECT_ID(N'app.DetailedWorkoutSets', N'U') IS NULL
BEGIN
    CREATE TABLE [app].[DetailedWorkoutSets]
    (
        [detailed_workout_set_id] uniqueidentifier NOT NULL,
        [detailed_workout_exercise_id] uniqueidentifier NOT NULL,
        [set_order] smallint NOT NULL,
        [reps] smallint NULL,
        [load] decimal(10,2) NULL,
        [load_unit_code] varchar(10) NOT NULL CONSTRAINT [DF_DetailedWorkoutSets_load_unit_code] DEFAULT ('LB'),
        [rpe] decimal(3,1) NULL,
        [rir] decimal(3,1) NULL,
        [rest_seconds] int NULL,
        [duration_seconds] int NULL,
        [is_warmup] bit NOT NULL CONSTRAINT [DF_DetailedWorkoutSets_is_warmup] DEFAULT (0),
        [notes] nvarchar(500) NULL,
        [created_at_utc] datetime2(7) NOT NULL CONSTRAINT [DF_DetailedWorkoutSets_created_at_utc] DEFAULT (SYSUTCDATETIME()),
        [updated_at_utc] datetime2(7) NOT NULL CONSTRAINT [DF_DetailedWorkoutSets_updated_at_utc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_DetailedWorkoutSets] PRIMARY KEY ([detailed_workout_set_id]),
        CONSTRAINT [FK_DetailedWorkoutSets_Exercise]
            FOREIGN KEY ([detailed_workout_exercise_id])
            REFERENCES [app].[DetailedWorkoutExercises]([detailed_workout_exercise_id])
            ON DELETE CASCADE,
        CONSTRAINT [CK_DetailedWorkoutSets_reps] CHECK ([reps] IS NULL OR [reps] >= 0),
        CONSTRAINT [CK_DetailedWorkoutSets_load] CHECK ([load] IS NULL OR [load] >= 0),
        CONSTRAINT [CK_DetailedWorkoutSets_rpe] CHECK ([rpe] IS NULL OR ([rpe] >= 0 AND [rpe] <= 10)),
        CONSTRAINT [CK_DetailedWorkoutSets_rir] CHECK ([rir] IS NULL OR ([rir] >= 0 AND [rir] <= 10)),
        CONSTRAINT [CK_DetailedWorkoutSets_rest] CHECK ([rest_seconds] IS NULL OR [rest_seconds] >= 0),
        CONSTRAINT [CK_DetailedWorkoutSets_duration] CHECK ([duration_seconds] IS NULL OR [duration_seconds] >= 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'app.DetailedWorkoutSets') AND [name] = N'UX_DetailedWorkoutSets_ExerciseOrder')
BEGIN
    CREATE UNIQUE INDEX [UX_DetailedWorkoutSets_ExerciseOrder]
        ON [app].[DetailedWorkoutSets]([detailed_workout_exercise_id], [set_order]);
END;
GO

IF DATABASE_PRINCIPAL_ID(N'recomp-test') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[app].[ExerciseCatalogImports] TO [recomp-test];
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[app].[ExerciseCatalogItems] TO [recomp-test];
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[app].[ExerciseMuscleTargets] TO [recomp-test];
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[app].[ExerciseEquipmentRequirements] TO [recomp-test];
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[app].[DetailedWorkoutExercises] TO [recomp-test];
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[app].[DetailedWorkoutSets] TO [recomp-test];
END;
GO

SELECT
    CASE WHEN OBJECT_ID(N'app.ExerciseCatalogItems', N'U') IS NULL THEN 'MISSING' ELSE 'READY' END AS ExerciseCatalogItems,
    CASE WHEN OBJECT_ID(N'app.ExerciseMuscleTargets', N'U') IS NULL THEN 'MISSING' ELSE 'READY' END AS ExerciseMuscleTargets,
    CASE WHEN OBJECT_ID(N'app.ExerciseEquipmentRequirements', N'U') IS NULL THEN 'MISSING' ELSE 'READY' END AS ExerciseEquipmentRequirements,
    CASE WHEN OBJECT_ID(N'app.DetailedWorkoutExercises', N'U') IS NULL THEN 'MISSING' ELSE 'READY' END AS DetailedWorkoutExercises,
    CASE WHEN OBJECT_ID(N'app.DetailedWorkoutSets', N'U') IS NULL THEN 'MISSING' ELSE 'READY' END AS DetailedWorkoutSets;
GO

PRINT 'PASS: Workout Intelligence persistence schema is ready.';
GO
