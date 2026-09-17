SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'app.WorkoutTemplateExercises', N'U') IS NULL
        THROW 51000, 'app.WorkoutTemplateExercises does not exist.', 1;

    IF COL_LENGTH('app.WorkoutTemplateExercises', 'sets_prescription') IS NULL
    BEGIN
        ALTER TABLE app.WorkoutTemplateExercises
            ADD sets_prescription nvarchar(100) NULL;
    END;

    IF COL_LENGTH('app.WorkoutTemplateExercises', 'reps_prescription') IS NULL
    BEGIN
        ALTER TABLE app.WorkoutTemplateExercises
            ADD reps_prescription nvarchar(100) NULL;
    END;

    IF COL_LENGTH('app.WorkoutTemplateExercises', 'weight_prescription') IS NULL
    BEGIN
        ALTER TABLE app.WorkoutTemplateExercises
            ADD weight_prescription nvarchar(100) NULL;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;