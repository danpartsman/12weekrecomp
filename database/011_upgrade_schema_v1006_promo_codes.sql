SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT 'Starting v1006 promotion-code schema migration...';

IF SCHEMA_ID(N'app') IS NULL
    THROW 51000, 'Expected schema [app] does not exist.', 1;

IF OBJECT_ID(N'app.UserAccounts', N'U') IS NULL
    THROW 51001, 'Expected table [app].[UserAccounts] does not exist.', 1;

IF OBJECT_ID(N'app.UserEntitlements', N'U') IS NULL
    THROW 51002, 'Expected table [app].[UserEntitlements] does not exist.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    -------------------------------------------------------------------------
    -- Allow promotion codes to create normal full-access entitlements.
    -------------------------------------------------------------------------
    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'app.UserEntitlements')
          AND name = N'CK_UserEntitlements_source'
    )
    BEGIN
        ALTER TABLE app.UserEntitlements
            DROP CONSTRAINT CK_UserEntitlements_source;
    END;

    ALTER TABLE app.UserEntitlements
        ADD CONSTRAINT CK_UserEntitlements_source
        CHECK
        (
            source_type_code IN
            (
                N'GIVEAWAY_CODE',
                N'PROMO_CODE',
                N'ADMIN_GRANT',
                N'SUBSCRIPTION'
            )
        );

    -------------------------------------------------------------------------
    -- PromoCodes
    --
    -- Example:
    -- Promotion Name: Today for Tomorrow Campaign
    -- Code:           TODAY4TOMORROW
    -- Access:         30 days
    -- Max Uses:       1,000,000
    -- Redeem By:      optional
    -------------------------------------------------------------------------
    IF OBJECT_ID(N'app.PromoCodes', N'U') IS NULL
    BEGIN
        CREATE TABLE app.PromoCodes
        (
            promo_code_id UNIQUEIDENTIFIER NOT NULL,

            promotion_name NVARCHAR(120) NOT NULL,
            code NVARCHAR(64) NOT NULL,

            status_code NVARCHAR(16) NOT NULL
                CONSTRAINT DF_PromoCodes_status
                DEFAULT (N'ACTIVE'),

            access_duration_days INT NOT NULL,
            max_redemptions INT NOT NULL,

            redemption_count INT NOT NULL
                CONSTRAINT DF_PromoCodes_redemption_count
                DEFAULT (0),

            redeem_by_utc DATETIME2(7) NULL,

            created_by_user_id UNIQUEIDENTIFIER NOT NULL,
            notes NVARCHAR(1000) NULL,

            created_at_utc DATETIME2(7) NOT NULL,
            updated_at_utc DATETIME2(7) NOT NULL,

            row_version ROWVERSION NOT NULL,

            CONSTRAINT PK_PromoCodes
                PRIMARY KEY (promo_code_id),

            CONSTRAINT FK_PromoCodes_CreatedBy
                FOREIGN KEY (created_by_user_id)
                REFERENCES app.UserAccounts(user_id),

            CONSTRAINT CK_PromoCodes_status
                CHECK
                (
                    status_code IN
                    (
                        N'ACTIVE',
                        N'DISABLED',
                        N'EXPIRED',
                        N'EXHAUSTED'
                    )
                ),

            CONSTRAINT CK_PromoCodes_duration
                CHECK
                (
                    access_duration_days
                    BETWEEN 1 AND 365
                ),

            CONSTRAINT CK_PromoCodes_max_redemptions
                CHECK
                (
                    max_redemptions
                    BETWEEN 1 AND 1000000
                ),

            CONSTRAINT CK_PromoCodes_redemption_count
                CHECK
                (
                    redemption_count >= 0
                    AND redemption_count <= max_redemptions
                )
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.PromoCodes')
          AND name = N'UX_PromoCodes_code'
    )
    BEGIN
        CREATE UNIQUE INDEX UX_PromoCodes_code
            ON app.PromoCodes(code);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.PromoCodes')
          AND name = N'IX_PromoCodes_status_redeem_by'
    )
    BEGIN
        CREATE INDEX IX_PromoCodes_status_redeem_by
            ON app.PromoCodes
            (
                status_code,
                redeem_by_utc
            );
    END;

    -------------------------------------------------------------------------
    -- PromoCodeRedemptions
    --
    -- One row per user redemption. The unique index below prevents one
    -- person from repeatedly redeeming the same promotion.
    -------------------------------------------------------------------------
    IF OBJECT_ID(N'app.PromoCodeRedemptions', N'U') IS NULL
    BEGIN
        CREATE TABLE app.PromoCodeRedemptions
        (
            promo_code_redemption_id UNIQUEIDENTIFIER NOT NULL,
            promo_code_id UNIQUEIDENTIFIER NOT NULL,
            user_id UNIQUEIDENTIFIER NOT NULL,
            user_entitlement_id UNIQUEIDENTIFIER NOT NULL,
            redeemed_at_utc DATETIME2(7) NOT NULL,

            CONSTRAINT PK_PromoCodeRedemptions
                PRIMARY KEY (promo_code_redemption_id),

            CONSTRAINT FK_PromoCodeRedemptions_PromoCode
                FOREIGN KEY (promo_code_id)
                REFERENCES app.PromoCodes(promo_code_id),

            CONSTRAINT FK_PromoCodeRedemptions_User
                FOREIGN KEY (user_id)
                REFERENCES app.UserAccounts(user_id),

            CONSTRAINT FK_PromoCodeRedemptions_Entitlement
                FOREIGN KEY (user_entitlement_id)
                REFERENCES app.UserEntitlements(user_entitlement_id)
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'app.PromoCodeRedemptions')
          AND name = N'UX_PromoCodeRedemptions_promo_user'
    )
    BEGIN
        CREATE UNIQUE INDEX UX_PromoCodeRedemptions_promo_user
            ON app.PromoCodeRedemptions
            (
                promo_code_id,
                user_id
            );
    END;

    COMMIT TRANSACTION;

    PRINT 'PASS: v1006 promotion-code schema applied.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
