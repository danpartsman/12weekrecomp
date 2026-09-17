SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'app.DetailedWorkoutExercises', N'U') IS NULL
        THROW 51000, 'Required table app.DetailedWorkoutExercises does not exist.', 1;

    IF OBJECT_ID(N'app.ExerciseCatalogItems', N'U') IS NULL
        THROW 51001, 'Required table app.ExerciseCatalogItems does not exist.', 1;

    IF COL_LENGTH(N'app.DetailedWorkoutExercises', N'max_weight') IS NULL
    BEGIN
        ALTER TABLE app.DetailedWorkoutExercises
            ADD max_weight decimal(10,2) NULL;
    END;

    IF COL_LENGTH(N'app.DetailedWorkoutExercises', N'load_unit_code') IS NULL
    BEGIN
        ALTER TABLE app.DetailedWorkoutExercises
            ADD load_unit_code varchar(10) NULL;
    END;

    IF OBJECT_ID(N'app.ClientExercisePreferences', N'U') IS NULL
    BEGIN
        CREATE TABLE app.ClientExercisePreferences
        (
            client_exercise_preference_id uniqueidentifier NOT NULL
                CONSTRAINT PK_ClientExercisePreferences PRIMARY KEY,
            client_id uniqueidentifier NOT NULL,
            exercise_catalog_item_id uniqueidentifier NOT NULL,
            is_favorite bit NOT NULL
                CONSTRAINT DF_ClientExercisePreferences_IsFavorite DEFAULT (0),
            default_sets smallint NOT NULL
                CONSTRAINT DF_ClientExercisePreferences_DefaultSets DEFAULT (3),
            default_reps smallint NOT NULL
                CONSTRAINT DF_ClientExercisePreferences_DefaultReps DEFAULT (10),
            default_max_weight decimal(10,2) NULL,
            load_unit_code varchar(10) NOT NULL
                CONSTRAINT DF_ClientExercisePreferences_LoadUnit DEFAULT ('LB'),
            created_at_utc datetime2(3) NOT NULL,
            updated_at_utc datetime2(3) NOT NULL,

            CONSTRAINT FK_ClientExercisePreferences_ExerciseCatalogItems
                FOREIGN KEY (exercise_catalog_item_id)
                REFERENCES app.ExerciseCatalogItems(exercise_catalog_item_id)
                ON DELETE CASCADE,

            CONSTRAINT CK_ClientExercisePreferences_DefaultSets
                CHECK (default_sets BETWEEN 1 AND 20),

            CONSTRAINT CK_ClientExercisePreferences_DefaultReps
                CHECK (default_reps BETWEEN 1 AND 500),

            CONSTRAINT CK_ClientExercisePreferences_MaxWeight
                CHECK (
                    default_max_weight IS NULL
                    OR default_max_weight BETWEEN 0 AND 10000),

            CONSTRAINT CK_ClientExercisePreferences_LoadUnit
                CHECK (load_unit_code IN ('LB', 'KG'))
        );

        CREATE UNIQUE INDEX UX_ClientExercisePreferences_ClientExercise
            ON app.ClientExercisePreferences
            (
                client_id,
                exercise_catalog_item_id
            );

        CREATE INDEX IX_ClientExercisePreferences_ClientFavorite
            ON app.ClientExercisePreferences
            (
                client_id,
                is_favorite
            );
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.database_principals
        WHERE name = N'recomp-test'
    )
    BEGIN
        GRANT SELECT, INSERT, UPDATE, DELETE
            ON OBJECT::app.ClientExercisePreferences
            TO [recomp-test];
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
