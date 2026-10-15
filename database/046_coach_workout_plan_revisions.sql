SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('app.CoachWorkoutPlanRevisions','U') IS NULL
BEGIN
    CREATE TABLE app.CoachWorkoutPlanRevisions (
        coach_workout_plan_revision_id uniqueidentifier NOT NULL PRIMARY KEY,
        client_id uniqueidentifier NOT NULL,
        program_id uniqueidentifier NOT NULL,
        coach_user_id uniqueidentifier NOT NULL,
        workout_date date NOT NULL,
        reason nvarchar(1000) NOT NULL,
        before_json nvarchar(max) NOT NULL,
        after_json nvarchar(max) NOT NULL,
        created_at_utc datetime2 NOT NULL,
        CONSTRAINT FK_CoachWorkoutRevision_Client FOREIGN KEY(client_id) REFERENCES app.Clients(client_id),
        CONSTRAINT FK_CoachWorkoutRevision_Program FOREIGN KEY(program_id) REFERENCES app.Programs(program_id),
        CONSTRAINT FK_CoachWorkoutRevision_Author FOREIGN KEY(coach_user_id) REFERENCES app.UserAccounts(user_id),
        CONSTRAINT CK_CoachWorkoutRevision_Json CHECK(ISJSON(before_json)=1 AND ISJSON(after_json)=1)
    );
    CREATE INDEX IX_CoachWorkoutRevision_Client ON app.CoachWorkoutPlanRevisions(client_id,program_id,created_at_utc DESC);
END;
COMMIT TRANSACTION;
