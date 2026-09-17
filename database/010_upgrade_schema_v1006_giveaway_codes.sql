SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT 'Starting v1006 giveaway-code schema migration...';

IF SCHEMA_ID(N'app') IS NULL
    THROW 51000, 'Expected schema [app] does not exist.', 1;

IF OBJECT_ID(N'app.UserAccounts', N'U') IS NULL
    THROW 51001, 'Expected table [app].[UserAccounts] does not exist.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    -------------------------------------------------------------------------
    -- GiveawayCodeBatches
    -------------------------------------------------------------------------
    IF OBJECT_ID(N'app.GiveawayCodeBatches', N'U') IS NULL
    BEGIN
        CREATE TABLE app.GiveawayCodeBatches
        (
            giveaway_code_batch_id UNIQUEIDENTIFIER NOT NULL,
            batch_name NVARCHAR(120) NULL,
            quantity INT NOT NULL,
            access_duration_days INT NOT NULL,
            redeem_by_utc DATETIME2(7) NULL,
            created_by_user_id UNIQUEIDENTIFIER NOT NULL,
            notes NVARCHAR(1000) NULL,
            created_at_utc DATETIME2(7) NOT NULL,

            CONSTRAINT PK_GiveawayCodeBatches
                PRIMARY KEY (giveaway_code_batch_id),

            CONSTRAINT FK_GiveawayCodeBatches_CreatedBy
                FOREIGN KEY (created_by_user_id)
                REFERENCES app.UserAccounts(user_id),

            CONSTRAINT CK_GiveawayCodeBatches_quantity
                CHECK (quantity BETWEEN 1 AND 1000),

            CONSTRAINT CK_GiveawayCodeBatches_duration
                CHECK (access_duration_days BETWEEN 1 AND 365)
        );
    END;

    -------------------------------------------------------------------------
    -- GiveawayCodes
    -------------------------------------------------------------------------
    IF OBJECT_ID(N'app.GiveawayCodes', N'U') IS NULL
    BEGIN
        CREATE TABLE app.GiveawayCodes
        (
            giveaway_code_id UNIQUEIDENTIFIER NOT NULL,
            giveaway_code_batch_id UNIQUEIDENTIFIER NOT NULL,
            code NVARCHAR(64) NOT NULL,
            status_code NVARCHAR(16) NOT NULL
                CONSTRAINT DF_GiveawayCodes_status DEFAULT (N'AVAILABLE'),
            access_duration_days INT NOT NULL,
            redeem_by_utc DATETIME2(7) NULL,

            redeemed_by_user_id UNIQUEIDENTIFIER NULL,
            redeemed_at_utc DATETIME2(7) NULL,
            activated_at_utc DATETIME2(7) NULL,
            access_expires_at_utc DATETIME2(7) NULL,

            revoked_at_utc DATETIME2(7) NULL,
            revoked_by_user_id UNIQUEIDENTIFIER NULL,
            created_at_utc DATETIME2(7) NOT NULL,
            updated_at_utc DATETIME2(7) NOT NULL,
            row_version ROWVERSION NOT NULL,

            CONSTRAINT PK_GiveawayCodes
                PRIMARY KEY (giveaway_code_id),

            CONSTRAINT FK_GiveawayCodes_Batch
                FOREIGN KEY (giveaway_code_batch_id)
                REFERENCES app.GiveawayCodeBatches(giveaway_code_batch_id),

            CONSTRAINT FK_GiveawayCodes_RedeemedBy
                FOREIGN KEY (redeemed_by_user_id)
                REFERENCES app.UserAccounts(user_id),

            CONSTRAINT FK_GiveawayCodes_RevokedBy
                FOREIGN KEY (revoked_by_user_id)
                REFERENCES app.UserAccounts(user_id),

            CONSTRAINT CK_GiveawayCodes_status
                CHECK (status_code IN
                    (N'AVAILABLE', N'REDEEMED', N'EXPIRED', N'REVOKED')),

            CONSTRAINT CK_GiveawayCodes_duration
                CHECK (access_duration_days BETWEEN 1 AND 365)
        );
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.GiveawayCodes')
          AND name = N'UX_GiveawayCodes_code'
    )
    BEGIN
        CREATE UNIQUE INDEX UX_GiveawayCodes_code
            ON app.GiveawayCodes(code);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.GiveawayCodes')
          AND name = N'IX_GiveawayCodes_status_redeem_by'
    )
    BEGIN
        CREATE INDEX IX_GiveawayCodes_status_redeem_by
            ON app.GiveawayCodes(status_code, redeem_by_utc);
    END;

    -------------------------------------------------------------------------
    -- UserEntitlements
    -------------------------------------------------------------------------
    IF OBJECT_ID(N'app.UserEntitlements', N'U') IS NULL
    BEGIN
        CREATE TABLE app.UserEntitlements
        (
            user_entitlement_id UNIQUEIDENTIFIER NOT NULL,
            user_id UNIQUEIDENTIFIER NOT NULL,

            entitlement_type_code NVARCHAR(32) NOT NULL,
            source_type_code NVARCHAR(32) NOT NULL,
            source_id UNIQUEIDENTIFIER NULL,

            starts_at_utc DATETIME2(7) NOT NULL,
            ends_at_utc DATETIME2(7) NULL,
            revoked_at_utc DATETIME2(7) NULL,

            created_at_utc DATETIME2(7) NOT NULL,
            updated_at_utc DATETIME2(7) NOT NULL,
            row_version ROWVERSION NOT NULL,

            CONSTRAINT PK_UserEntitlements
                PRIMARY KEY (user_entitlement_id),

            CONSTRAINT FK_UserEntitlements_User
                FOREIGN KEY (user_id)
                REFERENCES app.UserAccounts(user_id),

            CONSTRAINT CK_UserEntitlements_type
                CHECK (entitlement_type_code IN (N'FULL_ACCESS')),

            CONSTRAINT CK_UserEntitlements_source
                CHECK (source_type_code IN
                    (N'GIVEAWAY_CODE', N'ADMIN_GRANT', N'SUBSCRIPTION')),

            CONSTRAINT CK_UserEntitlements_dates
                CHECK (ends_at_utc IS NULL OR ends_at_utc > starts_at_utc)
        );
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.UserEntitlements')
          AND name = N'IX_UserEntitlements_user_dates'
    )
    BEGIN
        CREATE INDEX IX_UserEntitlements_user_dates
            ON app.UserEntitlements
            (
                user_id,
                starts_at_utc,
                ends_at_utc,
                revoked_at_utc
            );
    END;

    COMMIT TRANSACTION;

    PRINT 'PASS: v1006 giveaway-code schema applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
