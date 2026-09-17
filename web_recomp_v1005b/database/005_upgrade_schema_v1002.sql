SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

/*
    Web Recomp v1002 provenance foundation.

    Purpose:
      - preserve the existing v1001 dated/history model;
      - add enough source metadata for future Health Connect / HealthKit /
        vendor integrations without changing the core tables later;
      - provide an external-record key that future imports can use for
        idempotent synchronization instead of creating duplicates.

    This migration is additive and safe to run more than once.
*/

IF COL_LENGTH('app.Measurements', 'source_record_id') IS NULL
BEGIN
    ALTER TABLE app.Measurements
        ADD source_record_id nvarchar(200) NULL;
END;
GO

IF COL_LENGTH('app.WorkoutSessions', 'entry_source_code') IS NULL
BEGIN
    ALTER TABLE app.WorkoutSessions
        ADD entry_source_code varchar(30) NOT NULL
            CONSTRAINT DF_WorkoutSessions_EntrySource DEFAULT 'MANUAL';
END;
GO

IF COL_LENGTH('app.WorkoutSessions', 'source_record_id') IS NULL
BEGIN
    ALTER TABLE app.WorkoutSessions
        ADD source_record_id nvarchar(200) NULL;
END;
GO

IF COL_LENGTH('app.NutritionDays', 'source_record_id') IS NULL
BEGIN
    ALTER TABLE app.NutritionDays
        ADD source_record_id nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_Measurements_SourceRecord'
      AND object_id = OBJECT_ID('app.Measurements')
)
BEGIN
    CREATE UNIQUE INDEX UX_Measurements_SourceRecord
        ON app.Measurements(client_id, measurement_source_code, source_record_id)
        WHERE source_record_id IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_WorkoutSessions_SourceRecord'
      AND object_id = OBJECT_ID('app.WorkoutSessions')
)
BEGIN
    CREATE UNIQUE INDEX UX_WorkoutSessions_SourceRecord
        ON app.WorkoutSessions(client_id, entry_source_code, source_record_id)
        WHERE source_record_id IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_NutritionDays_SourceRecord'
      AND object_id = OBJECT_ID('app.NutritionDays')
)
BEGIN
    CREATE UNIQUE INDEX UX_NutritionDays_SourceRecord
        ON app.NutritionDays(client_id, entry_source_code, source_record_id)
        WHERE source_record_id IS NOT NULL;
END;
GO

PRINT 'v1002 provenance foundation applied successfully.';
GO
