SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH('app.DailyCheckins', 'daily_activity_hours') IS NULL
    BEGIN
        EXEC(N'
            ALTER TABLE app.DailyCheckins
            ADD daily_activity_hours decimal(4,1) NULL;
        ');
    END;

    IF COL_LENGTH('app.DailyCheckins', 'daily_activity_effort_code') IS NULL
    BEGIN
        EXEC(N'
            ALTER TABLE app.DailyCheckins
            ADD daily_activity_effort_code varchar(12) NULL;
        ');
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE name = 'CK_DailyCheckins_DailyActivityHours'
    )
    BEGIN
        EXEC(N'
            ALTER TABLE app.DailyCheckins
            ADD CONSTRAINT CK_DailyCheckins_DailyActivityHours
            CHECK (
                daily_activity_hours IS NULL
                OR daily_activity_hours BETWEEN 0 AND 24
            );
        ');
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE name = 'CK_DailyCheckins_DailyActivityEffort'
    )
    BEGIN
        EXEC(N'
            ALTER TABLE app.DailyCheckins
            ADD CONSTRAINT CK_DailyCheckins_DailyActivityEffort
            CHECK (
                daily_activity_effort_code IS NULL
                OR daily_activity_effort_code IN (''LIGHT'',''MODERATE'',''HARD'')
            );
        ');
    END;

    COMMIT TRANSACTION;

    PRINT 'PASS: Daily Activity columns are available.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
