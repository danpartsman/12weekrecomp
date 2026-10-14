SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('app.CoachPrescriptionRuleVersions','U') IS NULL
BEGIN
    CREATE TABLE app.CoachPrescriptionRuleVersions (
        coach_prescription_rule_version_id uniqueidentifier NOT NULL PRIMARY KEY,
        client_id uniqueidentifier NOT NULL,
        program_id uniqueidentifier NOT NULL,
        coach_user_id uniqueidentifier NOT NULL,
        effective_week date NOT NULL,
        is_enabled bit NOT NULL,
        rules_json nvarchar(max) NOT NULL,
        reason nvarchar(1000) NOT NULL,
        preview_json nvarchar(max) NOT NULL,
        created_at_utc datetime2 NOT NULL,
        CONSTRAINT FK_CoachRules_Client FOREIGN KEY(client_id) REFERENCES app.Clients(client_id),
        CONSTRAINT FK_CoachRules_Program FOREIGN KEY(program_id) REFERENCES app.Programs(program_id),
        CONSTRAINT FK_CoachRules_Author FOREIGN KEY(coach_user_id) REFERENCES app.UserAccounts(user_id),
        CONSTRAINT CK_CoachRules_Json CHECK(ISJSON(rules_json)=1 AND ISJSON(preview_json)=1),
        CONSTRAINT CK_CoachRules_Week CHECK(DATEDIFF(day,CONVERT(date,'19000101',112),effective_week)%7=0)
    );
    CREATE INDEX IX_CoachRules_ClientWeek ON app.CoachPrescriptionRuleVersions(client_id,program_id,effective_week DESC,created_at_utc DESC);
END;
COMMIT TRANSACTION;
