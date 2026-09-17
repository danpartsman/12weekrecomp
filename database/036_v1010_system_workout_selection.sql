SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'app.PlannedWorkouts', N'U') IS NULL
        THROW 51060, 'Required table app.PlannedWorkouts does not exist.', 1;

    IF OBJECT_ID(N'app.WorkoutSessions', N'U') IS NULL
        THROW 51061, 'Required table app.WorkoutSessions does not exist.', 1;

    IF COL_LENGTH(N'app.PlannedWorkouts', N'system_workout_code') IS NULL
    BEGIN
        ALTER TABLE app.PlannedWorkouts
            ADD system_workout_code varchar(80) NULL;
    END;

    IF COL_LENGTH(N'app.PlannedWorkouts', N'planned_duration_min') IS NULL
    BEGIN
        ALTER TABLE app.PlannedWorkouts
            ADD planned_duration_min decimal(8,2) NULL;
    END;

    IF COL_LENGTH(N'app.PlannedWorkouts', N'planned_rpe') IS NULL
    BEGIN
        ALTER TABLE app.PlannedWorkouts
            ADD planned_rpe decimal(3,1) NULL;
    END;

    IF COL_LENGTH(N'app.WorkoutSessions', N'system_workout_code') IS NULL
    BEGIN
        ALTER TABLE app.WorkoutSessions
            ADD system_workout_code varchar(80) NULL;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
