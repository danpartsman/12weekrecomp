SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT 'Engine 3.0 Migration 026 - Occupational Activity Foundation';
PRINT '----------------------------------------------------------';

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(
        N'app.ClientOccupationalActivities',
        N'U'
    ) IS NULL
    BEGIN
        PRINT 'Creating app.ClientOccupationalActivities...';

        CREATE TABLE app.ClientOccupationalActivities
        (
            ClientOccupationalActivityId uniqueidentifier NOT NULL,

            ClientId uniqueidentifier NOT NULL,

            OccupationName nvarchar(100) NOT NULL,

            OccupationActivityCode varchar(40) NOT NULL,

            TypicalHoursPerWeek decimal(5,1) NOT NULL,

            IsPrimary bit NOT NULL
                CONSTRAINT DF_ClientOccupationalActivities_IsPrimary
                DEFAULT (0),

            EffectiveFrom date NOT NULL,

            EffectiveTo date NULL,

            RowVersion rowversion NOT NULL,

            CreatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_ClientOccupationalActivities_CreatedAtUtc
                DEFAULT (SYSUTCDATETIME()),

            UpdatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_ClientOccupationalActivities_UpdatedAtUtc
                DEFAULT (SYSUTCDATETIME()),

            CONSTRAINT PK_ClientOccupationalActivities
                PRIMARY KEY (ClientOccupationalActivityId),

            CONSTRAINT FK_ClientOccupationalActivities_Clients
                FOREIGN KEY (ClientId)
                REFERENCES app.Clients(ClientId),

            CONSTRAINT CK_ClientOccupationalActivities_Hours
                CHECK
                (
                    TypicalHoursPerWeek >= 0
                    AND TypicalHoursPerWeek <= 168
                ),

            CONSTRAINT CK_ClientOccupationalActivities_EffectiveDates
                CHECK
                (
                    EffectiveTo IS NULL
                    OR EffectiveTo >= EffectiveFrom
                )
        );

        CREATE INDEX IX_ClientOccupationalActivities_Client_EffectiveFrom
            ON app.ClientOccupationalActivities
            (
                ClientId,
                EffectiveFrom
            );

        CREATE INDEX IX_ClientOccupationalActivities_Client_ActivityCode
            ON app.ClientOccupationalActivities
            (
                ClientId,
                OccupationActivityCode
            );

        /*
            A client may have several active occupational roles,
            but only one can be marked as the current primary role.

            IsPrimary is for UI/display semantics only.
            It does NOT give that role extra calorie weighting.
        */
        CREATE UNIQUE INDEX UX_ClientOccupationalActivities_CurrentPrimary
            ON app.ClientOccupationalActivities(ClientId)
            WHERE
                IsPrimary = 1
                AND EffectiveTo IS NULL;

        PRINT 'Created app.ClientOccupationalActivities.';
    END
    ELSE
    BEGIN
        PRINT 'app.ClientOccupationalActivities already exists - no create performed.';
    END;

    COMMIT TRANSACTION;

    PRINT 'PASS: Migration 026 completed.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO


PRINT '';
PRINT 'Verification';
PRINT '------------';

SELECT
    c.name AS ColumnName,
    t.name AS SqlType,
    c.max_length AS MaxLength,
    c.precision AS PrecisionValue,
    c.scale AS ScaleValue,
    c.is_nullable AS IsNullable
FROM sys.columns c
INNER JOIN sys.types t
    ON c.user_type_id = t.user_type_id
WHERE c.object_id =
    OBJECT_ID(N'app.ClientOccupationalActivities')
ORDER BY c.column_id;
GO


SELECT
    i.name AS IndexName,
    i.is_unique AS IsUnique,
    i.filter_definition AS FilterDefinition
FROM sys.indexes i
WHERE i.object_id =
    OBJECT_ID(N'app.ClientOccupationalActivities')
    AND i.name IS NOT NULL
ORDER BY i.name;
GO
