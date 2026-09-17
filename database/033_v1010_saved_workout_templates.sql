SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'app.WorkoutSessions', N'U') IS NULL
        THROW 51000, 'Required table app.WorkoutSessions does not exist.', 1;

    IF OBJECT_ID(N'app.ExerciseCatalogItems', N'U') IS NULL
        THROW 51001, 'Required table app.ExerciseCatalogItems does not exist.', 1;

    IF COL_LENGTH(N'app.WorkoutSessions', N'workout_name') IS NULL
    BEGIN
        ALTER TABLE app.WorkoutSessions
            ADD workout_name nvarchar(120) NULL;
    END;

    IF COL_LENGTH(N'app.WorkoutSessions', N'workout_template_id') IS NULL
    BEGIN
        ALTER TABLE app.WorkoutSessions
            ADD workout_template_id uniqueidentifier NULL;
    END;

    IF OBJECT_ID(N'app.WorkoutTemplates', N'U') IS NULL
    BEGIN
        CREATE TABLE app.WorkoutTemplates
        (
            workout_template_id uniqueidentifier NOT NULL
                CONSTRAINT PK_WorkoutTemplates PRIMARY KEY,
            client_id uniqueidentifier NOT NULL,
            template_name nvarchar(120) NOT NULL,
            workout_type_code varchar(50) NOT NULL,
            default_duration_min decimal(8,2) NOT NULL,
            notes nvarchar(1000) NULL,
            created_at_utc datetime2(3) NOT NULL,
            updated_at_utc datetime2(3) NOT NULL,

            CONSTRAINT CK_WorkoutTemplates_Duration
                CHECK (default_duration_min >= 0 AND default_duration_min <= 1440)
        );

        CREATE UNIQUE INDEX UX_WorkoutTemplates_ClientName
            ON app.WorkoutTemplates(client_id, template_name);

        CREATE INDEX IX_WorkoutTemplates_Client
            ON app.WorkoutTemplates(client_id);
    END;

    IF OBJECT_ID(N'app.WorkoutTemplateExercises', N'U') IS NULL
    BEGIN
        CREATE TABLE app.WorkoutTemplateExercises
        (
            workout_template_exercise_id uniqueidentifier NOT NULL
                CONSTRAINT PK_WorkoutTemplateExercises PRIMARY KEY,
            workout_template_id uniqueidentifier NOT NULL,
            exercise_catalog_item_id uniqueidentifier NOT NULL,
            exercise_order smallint NOT NULL,
            exercise_name_snapshot nvarchar(200) NOT NULL,
            mechanics_snapshot varchar(30) NULL,
            sets smallint NOT NULL,
            reps smallint NOT NULL,
            max_weight decimal(10,2) NULL,
            load_unit_code varchar(10) NOT NULL,
            notes nvarchar(1000) NULL,
            created_at_utc datetime2(3) NOT NULL,
            updated_at_utc datetime2(3) NOT NULL,

            CONSTRAINT FK_WorkoutTemplateExercises_WorkoutTemplates
                FOREIGN KEY (workout_template_id)
                REFERENCES app.WorkoutTemplates(workout_template_id)
                ON DELETE CASCADE,

            CONSTRAINT FK_WorkoutTemplateExercises_ExerciseCatalogItems
                FOREIGN KEY (exercise_catalog_item_id)
                REFERENCES app.ExerciseCatalogItems(exercise_catalog_item_id),

            CONSTRAINT CK_WorkoutTemplateExercises_Order
                CHECK (exercise_order >= 1),

            CONSTRAINT CK_WorkoutTemplateExercises_Sets
                CHECK (sets >= 1 AND sets <= 20),

            CONSTRAINT CK_WorkoutTemplateExercises_Reps
                CHECK (reps >= 1 AND reps <= 500),

            CONSTRAINT CK_WorkoutTemplateExercises_MaxWeight
                CHECK (max_weight IS NULL OR (max_weight >= 0 AND max_weight <= 10000)),

            CONSTRAINT CK_WorkoutTemplateExercises_LoadUnit
                CHECK (load_unit_code IN ('LB', 'KG'))
        );

        CREATE UNIQUE INDEX UX_WorkoutTemplateExercises_TemplateOrder
            ON app.WorkoutTemplateExercises(workout_template_id, exercise_order);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = N'FK_WorkoutSessions_WorkoutTemplates'
    )
    BEGIN
        ALTER TABLE app.WorkoutSessions
            ADD CONSTRAINT FK_WorkoutSessions_WorkoutTemplates
                FOREIGN KEY (workout_template_id)
                REFERENCES app.WorkoutTemplates(workout_template_id)
                ON DELETE SET NULL;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.database_principals
        WHERE name = N'recomp-test'
    )
    BEGIN
        GRANT SELECT, INSERT, UPDATE, DELETE
            ON OBJECT::app.WorkoutTemplates
            TO [recomp-test];

        GRANT SELECT, INSERT, UPDATE, DELETE
            ON OBJECT::app.WorkoutTemplateExercises
            TO [recomp-test];
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
