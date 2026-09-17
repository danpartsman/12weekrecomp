SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
GO

PRINT 'Engine 3.0 Migration 028 - Habitual Activity Profile';
PRINT '--------------------------------------------------';
GO

/* Ensure the client-level recurring lifestyle classification exists. */
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

/* Create the occupational-role history table with the EF snake_case layout. */
IF OBJECT_ID(N'app.ClientOccupationalActivities', N'U') IS NULL
BEGIN
    CREATE TABLE [app].[ClientOccupationalActivities]
    (
        [client_occupational_activity_id] uniqueidentifier NOT NULL,
        [client_id] uniqueidentifier NOT NULL,
        [occupation_name] nvarchar(100) NOT NULL,
        [occupation_activity_code] varchar(40) NOT NULL,
        [typical_hours_per_week] decimal(5,1) NOT NULL,
        [is_primary] bit NOT NULL CONSTRAINT [DF_ClientOccupationalActivities_is_primary] DEFAULT (0),
        [effective_from] date NOT NULL,
        [effective_to] date NULL,
        [row_version] rowversion NOT NULL,
        [created_at_utc] datetime2(7) NOT NULL CONSTRAINT [DF_ClientOccupationalActivities_created_at_utc] DEFAULT (SYSUTCDATETIME()),
        [updated_at_utc] datetime2(7) NOT NULL CONSTRAINT [DF_ClientOccupationalActivities_updated_at_utc] DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT [PK_ClientOccupationalActivities]
            PRIMARY KEY ([client_occupational_activity_id]),

        CONSTRAINT [FK_ClientOccupationalActivities_Clients]
            FOREIGN KEY ([client_id])
            REFERENCES [app].[Clients]([client_id]),

        CONSTRAINT [CK_ClientOccupationalActivities_hours]
            CHECK ([typical_hours_per_week] >= 0 AND [typical_hours_per_week] <= 168),

        CONSTRAINT [CK_ClientOccupationalActivities_dates]
            CHECK ([effective_to] IS NULL OR [effective_to] >= [effective_from])
    );

    PRINT 'CREATED: app.ClientOccupationalActivities';
END;
GO

/* Repair the older PascalCase prototype layout if it exists. */
IF OBJECT_ID(N'app.ClientOccupationalActivities', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'app.ClientOccupationalActivities', N'client_occupational_activity_id') IS NULL
       AND COL_LENGTH(N'app.ClientOccupationalActivities', N'ClientOccupationalActivityId') IS NOT NULL
        EXEC sp_rename N'app.ClientOccupationalActivities.ClientOccupationalActivityId', N'client_occupational_activity_id', N'COLUMN';

    IF COL_LENGTH(N'app.ClientOccupationalActivities', N'client_id') IS NULL
       AND COL_LENGTH(N'app.ClientOccupationalActivities', N'ClientId') IS NOT NULL
        EXEC sp_rename N'app.ClientOccupationalActivities.ClientId', N'client_id', N'COLUMN';

    IF COL_LENGTH(N'app.ClientOccupationalActivities', N'occupation_name') IS NULL
       AND COL_LENGTH(N'app.ClientOccupationalActivities', N'OccupationName') IS NOT NULL
        EXEC sp_rename N'app.ClientOccupationalActivities.OccupationName', N'occupation_name', N'COLUMN';

    IF COL_LENGTH(N'app.ClientOccupationalActivities', N'occupation_activity_code') IS NULL
       AND COL_LENGTH(N'app.ClientOccupationalActivities', N'OccupationActivityCode') IS NOT NULL
        EXEC sp_rename N'app.ClientOccupationalActivities.OccupationActivityCode', N'occupation_activity_code', N'COLUMN';

    IF COL_LENGTH(N'app.ClientOccupationalActivities', N'typical_hours_per_week') IS NULL
       AND COL_LENGTH(N'app.ClientOccupationalActivities', N'TypicalHoursPerWeek') IS NOT NULL
        EXEC sp_rename N'app.ClientOccupationalActivities.TypicalHoursPerWeek', N'typical_hours_per_week', N'COLUMN';

    IF COL_LENGTH(N'app.ClientOccupationalActivities', N'is_primary') IS NULL
       AND COL_LENGTH(N'app.ClientOccupationalActivities', N'IsPrimary') IS NOT NULL
        EXEC sp_rename N'app.ClientOccupationalActivities.IsPrimary', N'is_primary', N'COLUMN';

    IF COL_LENGTH(N'app.ClientOccupationalActivities', N'effective_from') IS NULL
       AND COL_LENGTH(N'app.ClientOccupationalActivities', N'EffectiveFrom') IS NOT NULL
        EXEC sp_rename N'app.ClientOccupationalActivities.EffectiveFrom', N'effective_from', N'COLUMN';

    IF COL_LENGTH(N'app.ClientOccupationalActivities', N'effective_to') IS NULL
       AND COL_LENGTH(N'app.ClientOccupationalActivities', N'EffectiveTo') IS NOT NULL
        EXEC sp_rename N'app.ClientOccupationalActivities.EffectiveTo', N'effective_to', N'COLUMN';

    IF COL_LENGTH(N'app.ClientOccupationalActivities', N'row_version') IS NULL
       AND COL_LENGTH(N'app.ClientOccupationalActivities', N'RowVersion') IS NOT NULL
        EXEC sp_rename N'app.ClientOccupationalActivities.RowVersion', N'row_version', N'COLUMN';

    IF COL_LENGTH(N'app.ClientOccupationalActivities', N'created_at_utc') IS NULL
       AND COL_LENGTH(N'app.ClientOccupationalActivities', N'CreatedAtUtc') IS NOT NULL
        EXEC sp_rename N'app.ClientOccupationalActivities.CreatedAtUtc', N'created_at_utc', N'COLUMN';

    IF COL_LENGTH(N'app.ClientOccupationalActivities', N'updated_at_utc') IS NULL
       AND COL_LENGTH(N'app.ClientOccupationalActivities', N'UpdatedAtUtc') IS NOT NULL
        EXEC sp_rename N'app.ClientOccupationalActivities.UpdatedAtUtc', N'updated_at_utc', N'COLUMN';
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'app.ClientOccupationalActivities')
      AND [name] = N'IX_ClientOccupationalActivities_Client_EffectiveFrom'
)
BEGIN
    CREATE INDEX [IX_ClientOccupationalActivities_Client_EffectiveFrom]
        ON [app].[ClientOccupationalActivities]([client_id], [effective_from]);
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'app.ClientOccupationalActivities')
      AND [name] = N'IX_ClientOccupationalActivities_Client_ActivityCode'
)
BEGIN
    CREATE INDEX [IX_ClientOccupationalActivities_Client_ActivityCode]
        ON [app].[ClientOccupationalActivities]([client_id], [occupation_activity_code]);
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'app.ClientOccupationalActivities')
      AND [name] = N'UX_ClientOccupationalActivities_CurrentPrimary'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ClientOccupationalActivities_CurrentPrimary]
        ON [app].[ClientOccupationalActivities]([client_id])
        WHERE [is_primary] = 1 AND [effective_to] IS NULL;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'recomp-test') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE, DELETE
        ON OBJECT::[app].[ClientOccupationalActivities]
        TO [recomp-test];

    GRANT SELECT, UPDATE
        ON OBJECT::[app].[Clients]
        TO [recomp-test];
END;
GO

SELECT
    CASE WHEN COL_LENGTH(N'app.Clients', N'lifestyle_activity_code') IS NULL THEN 'MISSING' ELSE 'READY' END AS LifestyleActivityColumn,
    CASE WHEN OBJECT_ID(N'app.ClientOccupationalActivities', N'U') IS NULL THEN 'MISSING' ELSE 'READY' END AS OccupationalActivityTable,
    CASE WHEN COL_LENGTH(N'app.ClientOccupationalActivities', N'client_occupational_activity_id') IS NULL THEN 'MISSING' ELSE 'READY' END AS OccupationalSnakeCaseLayout;
GO

PRINT 'PASS: Engine 3.0 habitual activity profile schema is ready.';
GO
