SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT 'Starting v1006 Contact Us schema migration...';

IF SCHEMA_ID(N'app') IS NULL
    THROW 51000, 'Expected schema [app] does not exist.', 1;

IF OBJECT_ID(N'app.UserAccounts', N'U') IS NULL
    THROW 51001, 'Expected table [app].[UserAccounts] does not exist.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    -------------------------------------------------------------------------
    -- ContactSettings
    --
    -- One logical settings row for the public Contact Us feature.
    -------------------------------------------------------------------------
    IF OBJECT_ID(N'app.ContactSettings', N'U') IS NULL
    BEGIN
        CREATE TABLE app.ContactSettings
        (
            contact_settings_id UNIQUEIDENTIFIER NOT NULL,
            settings_key NVARCHAR(32) NOT NULL
                CONSTRAINT DF_ContactSettings_settings_key
                DEFAULT (N'DEFAULT'),

            destination_email NVARCHAR(320) NULL,
            contact_enabled BIT NOT NULL
                CONSTRAINT DF_ContactSettings_enabled
                DEFAULT (1),

            updated_by_user_id UNIQUEIDENTIFIER NULL,

            created_at_utc DATETIME2(7) NOT NULL,
            updated_at_utc DATETIME2(7) NOT NULL,

            row_version ROWVERSION NOT NULL,

            CONSTRAINT PK_ContactSettings
                PRIMARY KEY (contact_settings_id),

            CONSTRAINT FK_ContactSettings_UpdatedBy
                FOREIGN KEY (updated_by_user_id)
                REFERENCES app.UserAccounts(user_id),

            CONSTRAINT CK_ContactSettings_settings_key
                CHECK (settings_key = N'DEFAULT')
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.ContactSettings')
          AND name = N'UX_ContactSettings_settings_key'
    )
    BEGIN
        CREATE UNIQUE INDEX UX_ContactSettings_settings_key
            ON app.ContactSettings(settings_key);
    END;

    -------------------------------------------------------------------------
    -- ContactMessages
    -------------------------------------------------------------------------
    IF OBJECT_ID(N'app.ContactMessages', N'U') IS NULL
    BEGIN
        CREATE TABLE app.ContactMessages
        (
            contact_message_id UNIQUEIDENTIFIER NOT NULL,

            sender_name NVARCHAR(120) NOT NULL,
            sender_email NVARCHAR(320) NOT NULL,
            subject NVARCHAR(200) NOT NULL,
            message NVARCHAR(MAX) NOT NULL,

            status_code NVARCHAR(16) NOT NULL
                CONSTRAINT DF_ContactMessages_status
                DEFAULT (N'NEW'),

            submitted_at_utc DATETIME2(7) NOT NULL,

            read_at_utc DATETIME2(7) NULL,
            read_by_user_id UNIQUEIDENTIFIER NULL,

            archived_at_utc DATETIME2(7) NULL,
            archived_by_user_id UNIQUEIDENTIFIER NULL,

            email_notification_status NVARCHAR(24) NOT NULL
                CONSTRAINT DF_ContactMessages_email_status
                DEFAULT (N'NOT_CONFIGURED'),

            email_sent_at_utc DATETIME2(7) NULL,
            email_error NVARCHAR(2000) NULL,

            row_version ROWVERSION NOT NULL,

            CONSTRAINT PK_ContactMessages
                PRIMARY KEY (contact_message_id),

            CONSTRAINT FK_ContactMessages_ReadBy
                FOREIGN KEY (read_by_user_id)
                REFERENCES app.UserAccounts(user_id),

            CONSTRAINT FK_ContactMessages_ArchivedBy
                FOREIGN KEY (archived_by_user_id)
                REFERENCES app.UserAccounts(user_id),

            CONSTRAINT CK_ContactMessages_status
                CHECK
                (
                    status_code IN
                    (
                        N'NEW',
                        N'READ',
                        N'ARCHIVED'
                    )
                ),

            CONSTRAINT CK_ContactMessages_email_status
                CHECK
                (
                    email_notification_status IN
                    (
                        N'NOT_CONFIGURED',
                        N'PENDING',
                        N'SENT',
                        N'FAILED'
                    )
                )
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.ContactMessages')
          AND name = N'IX_ContactMessages_status_submitted'
    )
    BEGIN
        CREATE INDEX IX_ContactMessages_status_submitted
            ON app.ContactMessages
            (
                status_code,
                submitted_at_utc DESC
            );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.ContactMessages')
          AND name = N'IX_ContactMessages_email_status'
    )
    BEGIN
        CREATE INDEX IX_ContactMessages_email_status
            ON app.ContactMessages
            (
                email_notification_status,
                submitted_at_utc
            );
    END;

    COMMIT TRANSACTION;

    PRINT 'PASS: v1006 Contact Us schema applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
