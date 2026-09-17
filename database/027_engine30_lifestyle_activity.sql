SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT 'Engine 3.0 Migration 027 - Lifestyle Activity';
PRINT '----------------------------------------------';

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(
        'app.Clients',
        'LifestyleActivityCode'
    ) IS NULL
    BEGIN
        PRINT 'Adding app.Clients.LifestyleActivityCode...';

        ALTER TABLE app.Clients
        ADD LifestyleActivityCode varchar(20) NULL;

        ALTER TABLE app.Clients
        ADD CONSTRAINT CK_Clients_LifestyleActivityCode
        CHECK
        (
            LifestyleActivityCode IS NULL
            OR LifestyleActivityCode IN
            (
                'MINIMAL',
                'LIGHT',
                'MODERATE',
                'VERY_ACTIVE'
            )
        );

        PRINT 'Added LifestyleActivityCode.';
    END
    ELSE
    BEGIN
        PRINT 'LifestyleActivityCode already exists - no ALTER performed.';
    END;

    COMMIT TRANSACTION;

    PRINT 'PASS: Migration 027 completed.';
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
    c.is_nullable AS IsNullable
FROM sys.columns c
INNER JOIN sys.types t
    ON c.user_type_id = t.user_type_id
WHERE
    c.object_id = OBJECT_ID(N'app.Clients')
    AND c.name = 'LifestyleActivityCode';
GO


SELECT
    cc.name AS ConstraintName,
    cc.definition AS ConstraintDefinition
FROM sys.check_constraints cc
WHERE
    cc.parent_object_id = OBJECT_ID(N'app.Clients')
    AND cc.name = 'CK_Clients_LifestyleActivityCode';
GO
