SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT 'Starting v1006 demo/invitation schema migration...';

IF SCHEMA_ID(N'app') IS NULL
    THROW 51000, 'Expected schema [app] does not exist. Verify that this is the Recomp database.', 1;

IF OBJECT_ID(N'app.UserAccounts', N'U') IS NULL
    THROW 51001, 'Expected table [app].[UserAccounts] does not exist. Apply the existing Recomp baseline schema first.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    -------------------------------------------------------------------------
    -- DemoInvites
    -------------------------------------------------------------------------
    IF OBJECT_ID(N'app.DemoInvites', N'U') IS NULL
    BEGIN
        CREATE TABLE app.DemoInvites
        (
            demo_invite_id UNIQUEIDENTIFIER NOT NULL,
            invite_code NVARCHAR(96) NOT NULL,
            invite_type_code NVARCHAR(16) NOT NULL,
            guided_scenario_code NVARCHAR(64) NULL,
            status_code NVARCHAR(16) NOT NULL
                CONSTRAINT DF_DemoInvites_status_code DEFAULT (N'ACTIVE'),
            expires_at_utc DATETIME2(7) NULL,
            max_redemptions INT NULL,
            redemption_count INT NOT NULL
                CONSTRAINT DF_DemoInvites_redemption_count DEFAULT (0),
            workspace_lifetime_days INT NOT NULL
                CONSTRAINT DF_DemoInvites_workspace_lifetime_days DEFAULT (60),
            inactivity_timeout_days INT NOT NULL
                CONSTRAINT DF_DemoInvites_inactivity_timeout_days DEFAULT (30),
            session_lifetime_days INT NOT NULL
                CONSTRAINT DF_DemoInvites_session_lifetime_days DEFAULT (30),
            allow_conversion BIT NOT NULL
                CONSTRAINT DF_DemoInvites_allow_conversion DEFAULT (1),
            created_by_user_id UNIQUEIDENTIFIER NULL,
            notes NVARCHAR(1000) NULL,
            row_version ROWVERSION NOT NULL,
            created_at_utc DATETIME2(7) NOT NULL,
            updated_at_utc DATETIME2(7) NOT NULL,

            CONSTRAINT PK_DemoInvites
                PRIMARY KEY (demo_invite_id),

            CONSTRAINT FK_DemoInvites_UserAccounts_CreatedBy
                FOREIGN KEY (created_by_user_id)
                REFERENCES app.UserAccounts(user_id),

            CONSTRAINT CK_DemoInvites_invite_type_code
                CHECK (invite_type_code IN (N'GUIDED', N'FRESH')),

            CONSTRAINT CK_DemoInvites_status_code
                CHECK (status_code IN (N'ACTIVE', N'DISABLED', N'EXPIRED')),

            CONSTRAINT CK_DemoInvites_max_redemptions
                CHECK (max_redemptions IS NULL OR max_redemptions > 0),

            CONSTRAINT CK_DemoInvites_redemption_count
                CHECK (redemption_count >= 0),

            CONSTRAINT CK_DemoInvites_workspace_lifetime_days
                CHECK (workspace_lifetime_days BETWEEN 1 AND 365),

            CONSTRAINT CK_DemoInvites_inactivity_timeout_days
                CHECK (inactivity_timeout_days BETWEEN 1 AND 365),

            CONSTRAINT CK_DemoInvites_session_lifetime_days
                CHECK (session_lifetime_days BETWEEN 1 AND 365)
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.DemoInvites')
          AND name = N'UX_DemoInvites_invite_code'
    )
    BEGIN
        CREATE UNIQUE INDEX UX_DemoInvites_invite_code
            ON app.DemoInvites(invite_code);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.DemoInvites')
          AND name = N'IX_DemoInvites_status_expires'
    )
    BEGIN
        CREATE INDEX IX_DemoInvites_status_expires
            ON app.DemoInvites(status_code, expires_at_utc);
    END;

    -------------------------------------------------------------------------
    -- DemoWorkspaces
    -------------------------------------------------------------------------
    IF OBJECT_ID(N'app.DemoWorkspaces', N'U') IS NULL
    BEGIN
        CREATE TABLE app.DemoWorkspaces
        (
            demo_workspace_id UNIQUEIDENTIFIER NOT NULL,
            demo_invite_id UNIQUEIDENTIFIER NOT NULL,
            user_id UNIQUEIDENTIFIER NOT NULL,
            demo_type_code NVARCHAR(16) NOT NULL,
            workspace_status_code NVARCHAR(16) NOT NULL
                CONSTRAINT DF_DemoWorkspaces_workspace_status_code DEFAULT (N'ACTIVE'),
            expires_at_utc DATETIME2(7) NOT NULL,
            inactivity_timeout_days INT NOT NULL
                CONSTRAINT DF_DemoWorkspaces_inactivity_timeout_days DEFAULT (30),
            session_lifetime_days INT NOT NULL
                CONSTRAINT DF_DemoWorkspaces_session_lifetime_days DEFAULT (30),
            last_activity_at_utc DATETIME2(7) NOT NULL,
            recovery_code_hash NVARCHAR(128) NULL,
            recovery_code_created_at_utc DATETIME2(7) NULL,
            converted_to_user_id UNIQUEIDENTIFIER NULL,
            converted_at_utc DATETIME2(7) NULL,
            row_version ROWVERSION NOT NULL,
            created_at_utc DATETIME2(7) NOT NULL,
            updated_at_utc DATETIME2(7) NOT NULL,

            CONSTRAINT PK_DemoWorkspaces
                PRIMARY KEY (demo_workspace_id),

            CONSTRAINT FK_DemoWorkspaces_DemoInvites
                FOREIGN KEY (demo_invite_id)
                REFERENCES app.DemoInvites(demo_invite_id),

            CONSTRAINT FK_DemoWorkspaces_UserAccounts
                FOREIGN KEY (user_id)
                REFERENCES app.UserAccounts(user_id),

            CONSTRAINT FK_DemoWorkspaces_UserAccounts_ConvertedTo
                FOREIGN KEY (converted_to_user_id)
                REFERENCES app.UserAccounts(user_id),

            CONSTRAINT CK_DemoWorkspaces_demo_type_code
                CHECK (demo_type_code IN (N'GUIDED', N'FRESH')),

            CONSTRAINT CK_DemoWorkspaces_workspace_status_code
                CHECK (workspace_status_code IN
                    (N'ACTIVE', N'CONVERTED', N'EXPIRED', N'DELETED')),

            CONSTRAINT CK_DemoWorkspaces_inactivity_timeout_days
                CHECK (inactivity_timeout_days BETWEEN 1 AND 365),

            CONSTRAINT CK_DemoWorkspaces_session_lifetime_days
                CHECK (session_lifetime_days BETWEEN 1 AND 365)
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.DemoWorkspaces')
          AND name = N'UX_DemoWorkspaces_user_id'
    )
    BEGIN
        CREATE UNIQUE INDEX UX_DemoWorkspaces_user_id
            ON app.DemoWorkspaces(user_id);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.DemoWorkspaces')
          AND name = N'IX_DemoWorkspaces_status_activity'
    )
    BEGIN
        CREATE INDEX IX_DemoWorkspaces_status_activity
            ON app.DemoWorkspaces(workspace_status_code, last_activity_at_utc);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.DemoWorkspaces')
          AND name = N'IX_DemoWorkspaces_invite_status'
    )
    BEGIN
        CREATE INDEX IX_DemoWorkspaces_invite_status
            ON app.DemoWorkspaces(demo_invite_id, workspace_status_code);
    END;

    -------------------------------------------------------------------------
    -- DemoSessions
    -------------------------------------------------------------------------
    IF OBJECT_ID(N'app.DemoSessions', N'U') IS NULL
    BEGIN
        CREATE TABLE app.DemoSessions
        (
            demo_session_id UNIQUEIDENTIFIER NOT NULL,
            demo_workspace_id UNIQUEIDENTIFIER NOT NULL,
            session_token_hash NVARCHAR(128) NOT NULL,
            created_at_utc DATETIME2(7) NOT NULL,
            last_seen_at_utc DATETIME2(7) NOT NULL,
            expires_at_utc DATETIME2(7) NOT NULL,
            revoked_at_utc DATETIME2(7) NULL,

            CONSTRAINT PK_DemoSessions
                PRIMARY KEY (demo_session_id),

            CONSTRAINT FK_DemoSessions_DemoWorkspaces
                FOREIGN KEY (demo_workspace_id)
                REFERENCES app.DemoWorkspaces(demo_workspace_id)
                ON DELETE CASCADE
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.DemoSessions')
          AND name = N'UX_DemoSessions_session_token_hash'
    )
    BEGIN
        CREATE UNIQUE INDEX UX_DemoSessions_session_token_hash
            ON app.DemoSessions(session_token_hash);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.DemoSessions')
          AND name = N'IX_DemoSessions_workspace_active'
    )
    BEGIN
        CREATE INDEX IX_DemoSessions_workspace_active
            ON app.DemoSessions
            (
                demo_workspace_id,
                revoked_at_utc,
                expires_at_utc
            );
    END;

    COMMIT TRANSACTION;

    PRINT 'PASS: v1006 demo/invitation schema applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
