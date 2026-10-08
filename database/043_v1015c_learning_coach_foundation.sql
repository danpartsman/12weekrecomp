/*
    v1015c Learning + Coach foundation.
    Apply before deploying v1015c application code.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('app.LearningCoachPreferences', 'U') IS NULL
BEGIN
    CREATE TABLE app.LearningCoachPreferences
    (
        user_id uniqueidentifier NOT NULL,
        preferred_coach_code varchar(30) NOT NULL
            CONSTRAINT DF_LearningCoachPreferences_coach DEFAULT ('MARISOL'),
        created_at_utc datetime2 NOT NULL,
        updated_at_utc datetime2 NOT NULL,
        CONSTRAINT PK_LearningCoachPreferences PRIMARY KEY (user_id),
        CONSTRAINT FK_LearningCoachPreferences_UserAccounts
            FOREIGN KEY (user_id) REFERENCES app.UserAccounts(user_id) ON DELETE CASCADE
    );
END;

IF OBJECT_ID('app.LearningSeriesProgress', 'U') IS NULL
BEGIN
    CREATE TABLE app.LearningSeriesProgress
    (
        learning_series_progress_id uniqueidentifier NOT NULL,
        user_id uniqueidentifier NOT NULL,
        series_code varchar(60) NOT NULL,
        current_module_number int NOT NULL CONSTRAINT DF_LearningSeriesProgress_current DEFAULT (1),
        highest_completed_module_number int NOT NULL CONSTRAINT DF_LearningSeriesProgress_highest DEFAULT (0),
        status_code varchar(20) NOT NULL CONSTRAINT DF_LearningSeriesProgress_status DEFAULT ('ACTIVE'),
        last_shown_at_utc datetime2 NULL,
        last_completed_at_utc datetime2 NULL,
        created_at_utc datetime2 NOT NULL,
        updated_at_utc datetime2 NOT NULL,
        CONSTRAINT PK_LearningSeriesProgress PRIMARY KEY (learning_series_progress_id),
        CONSTRAINT FK_LearningSeriesProgress_UserAccounts
            FOREIGN KEY (user_id) REFERENCES app.UserAccounts(user_id) ON DELETE CASCADE,
        CONSTRAINT UQ_LearningSeriesProgress_UserSeries UNIQUE (user_id, series_code)
    );
END;

IF OBJECT_ID('app.LearningInteractions', 'U') IS NULL
BEGIN
    CREATE TABLE app.LearningInteractions
    (
        learning_interaction_id uniqueidentifier NOT NULL,
        user_id uniqueidentifier NOT NULL,
        content_code varchar(100) NOT NULL,
        series_code varchar(60) NOT NULL,
        surface_code varchar(30) NOT NULL,
        interaction_code varchar(30) NOT NULL,
        module_number int NOT NULL,
        target_url nvarchar(1000) NULL,
        occurred_at_utc datetime2 NOT NULL,
        CONSTRAINT PK_LearningInteractions PRIMARY KEY (learning_interaction_id),
        CONSTRAINT FK_LearningInteractions_UserAccounts
            FOREIGN KEY (user_id) REFERENCES app.UserAccounts(user_id) ON DELETE CASCADE
    );

    CREATE INDEX IX_LearningInteractions_UserOccurred
        ON app.LearningInteractions(user_id, occurred_at_utc);
END;

COMMIT TRANSACTION;
