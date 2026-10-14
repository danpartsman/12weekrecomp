-- Coach Center: additive schema, isolated relationships, no changes to existing client ownership.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('app.CoachProfiles', 'U') IS NULL
BEGIN
    CREATE TABLE app.CoachProfiles (
        user_id uniqueidentifier NOT NULL PRIMARY KEY,
        display_name nvarchar(100) NOT NULL,
        bio nvarchar(2000) NULL,
        specialties nvarchar(500) NULL,
        updated_at_utc datetime2 NOT NULL,
        CONSTRAINT FK_CoachProfiles_User FOREIGN KEY (user_id) REFERENCES app.UserAccounts(user_id)
    );
END;
IF OBJECT_ID('app.CoachClientRelationships', 'U') IS NULL
BEGIN
    CREATE TABLE app.CoachClientRelationships (
        coach_client_relationship_id uniqueidentifier NOT NULL PRIMARY KEY,
        coach_user_id uniqueidentifier NOT NULL,
        client_id uniqueidentifier NULL,
        status_code nvarchar(20) NOT NULL,
        invite_token_hash nvarchar(64) NULL,
        invite_expires_at_utc datetime2 NOT NULL,
        created_at_utc datetime2 NOT NULL,
        accepted_at_utc datetime2 NULL,
        revoked_at_utc datetime2 NULL,
        CONSTRAINT FK_CoachRelationships_User FOREIGN KEY (coach_user_id) REFERENCES app.UserAccounts(user_id),
        CONSTRAINT FK_CoachRelationships_Client FOREIGN KEY (client_id) REFERENCES app.Clients(client_id),
        CONSTRAINT CK_CoachRelationships_State CHECK (
            (status_code = 'INVITED' AND client_id IS NULL AND invite_token_hash IS NOT NULL AND revoked_at_utc IS NULL)
            OR (status_code = 'ACTIVE' AND client_id IS NOT NULL AND invite_token_hash IS NULL AND accepted_at_utc IS NOT NULL AND revoked_at_utc IS NULL)
            OR (status_code = 'REVOKED' AND revoked_at_utc IS NOT NULL))
    );
    CREATE UNIQUE INDEX UX_CoachRelationships_Token ON app.CoachClientRelationships(invite_token_hash) WHERE invite_token_hash IS NOT NULL;
    CREATE UNIQUE INDEX UX_CoachRelationships_Active ON app.CoachClientRelationships(coach_user_id, client_id) WHERE status_code = 'ACTIVE';
    CREATE INDEX IX_CoachRelationships_Client ON app.CoachClientRelationships(client_id, status_code);
END;
COMMIT TRANSACTION;
