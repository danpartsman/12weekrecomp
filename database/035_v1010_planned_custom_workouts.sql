SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'app.PlannedWorkouts', N'U') IS NULL
        THROW 51035, 'Required table app.PlannedWorkouts does not exist.', 1;

    IF OBJECT_ID(N'app.WorkoutTemplates', N'U') IS NULL
        THROW 51036, 'Required table app.WorkoutTemplates does not exist.', 1;

    IF COL_LENGTH(N'app.PlannedWorkouts', N'workout_template_id') IS NULL
    BEGIN
        ALTER TABLE app.PlannedWorkouts
            ADD workout_template_id uniqueidentifier NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = N'FK_PlannedWorkouts_WorkoutTemplates'
    )
    BEGIN
        ALTER TABLE app.PlannedWorkouts
            ADD CONSTRAINT FK_PlannedWorkouts_WorkoutTemplates
                FOREIGN KEY (workout_template_id)
                REFERENCES app.WorkoutTemplates(workout_template_id)
                ON DELETE SET NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.PlannedWorkouts')
          AND name = N'IX_PlannedWorkouts_WorkoutTemplate'
    )
    BEGIN
        CREATE INDEX IX_PlannedWorkouts_WorkoutTemplate
            ON app.PlannedWorkouts(workout_template_id);
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
