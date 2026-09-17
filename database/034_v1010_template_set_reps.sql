SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'app.WorkoutTemplateExercises', N'U') IS NULL
BEGIN
    THROW 51000, 'app.WorkoutTemplateExercises does not exist.', 1;
END;

IF COL_LENGTH(N'app.WorkoutTemplateExercises', N'set_reps_json') IS NULL
BEGIN
    ALTER TABLE app.WorkoutTemplateExercises
        ADD set_reps_json nvarchar(max) NULL;
END;

COMMIT TRANSACTION;
