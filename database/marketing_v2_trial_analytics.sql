SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT '12WR Marketing v2 / free-trial migration';

IF SCHEMA_ID(N'app') IS NULL
    THROW 52000, 'Expected [app] schema was not found.', 1;

IF OBJECT_ID(N'app.UserAccounts', N'U') IS NULL
    THROW 52001, 'Expected app.UserAccounts was not found.', 1;

IF OBJECT_ID(N'app.PromoCodes', N'U') IS NULL
    THROW 52002, 'Expected app.PromoCodes was not found. Existing entitlement infrastructure is required.', 1;

DECLARE @required TABLE(column_name sysname);
INSERT @required(column_name)
VALUES
(N'promo_code_id'),
(N'promotion_name'),
(N'code'),
(N'status_code'),
(N'access_duration_days'),
(N'max_redemptions'),
(N'redemption_count'),
(N'redeem_by_utc'),
(N'created_by_user_id'),
(N'notes'),
(N'created_at_utc'),
(N'updated_at_utc');

IF EXISTS
(
    SELECT 1
    FROM @required r
    WHERE COL_LENGTH(N'app.PromoCodes', r.column_name) IS NULL
)
    THROW 52003, 'app.PromoCodes shape does not match the expected Recomp entitlement schema.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'app.MarketingExperimentEvents', N'U') IS NULL
    BEGIN
        CREATE TABLE app.MarketingExperimentEvents
        (
            marketing_experiment_event_id UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT PK_MarketingExperimentEvents PRIMARY KEY
                CONSTRAINT DF_MarketingExperimentEvents_Id DEFAULT NEWSEQUENTIALID(),
            anonymous_session_id VARCHAR(64) NOT NULL,
            variant_code CHAR(1) NOT NULL,
            event_type VARCHAR(32) NOT NULL,
            page_path NVARCHAR(200) NOT NULL,
            created_at_utc DATETIME2(3) NOT NULL
                CONSTRAINT DF_MarketingExperimentEvents_Created DEFAULT SYSUTCDATETIME(),

            CONSTRAINT CK_MarketingExperimentEvents_Variant
                CHECK (variant_code IN ('A','B','C','D','U')),

            CONSTRAINT CK_MarketingExperimentEvents_Event
                CHECK (event_type IN
                (
                    'WELCOME_VIEW',
                    'CLICK_START_FREE',
                    'CLICK_EVIDENCE',
                    'EVIDENCE_VIEW',
                    'EVIDENCE_START_FREE'
                ))
        );

        CREATE INDEX IX_MarketingExperimentEvents_VariantEventDate
            ON app.MarketingExperimentEvents(variant_code, event_type, created_at_utc);

        CREATE INDEX IX_MarketingExperimentEvents_Session
            ON app.MarketingExperimentEvents(anonymous_session_id, created_at_utc);
    END;

    DECLARE @trialCode NVARCHAR(64) = N'FREETRIAL7';

    IF EXISTS (SELECT 1 FROM app.PromoCodes WHERE code = @trialCode)
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM app.PromoCodes
            WHERE code = @trialCode
              AND
              (
                  access_duration_days <> 7
                  OR status_code <> N'ACTIVE'
                  OR max_redemptions < 1000
              )
        )
        BEGIN
            THROW 52004, 'FREETRIAL7 already exists but does not match the required 7-day active reusable trial definition. Nothing changed.', 1;
        END;
    END
    ELSE
    BEGIN
        DECLARE @createdBy UNIQUEIDENTIFIER;

        SELECT TOP (1)
            @createdBy = user_id
        FROM app.UserAccounts
        WHERE account_status_code = N'ACTIVE'
          AND email_normalized IN
          (
              N'dlandon@lockedontech.onmicrosoft.com',
              N'dlandon@lockedontech.com'
          )
        ORDER BY
            CASE
                WHEN email_normalized = N'dlandon@lockedontech.onmicrosoft.com' THEN 0
                ELSE 1
            END;

        IF @createdBy IS NULL
        BEGIN
            SELECT TOP (1)
                @createdBy = user_id
            FROM app.UserAccounts
            WHERE account_status_code = N'ACTIVE'
              AND external_subject NOT LIKE N'demo:%'
            ORDER BY created_at_utc;
        END;

        IF @createdBy IS NULL
            THROW 52005, 'No active non-demo user exists to own the FREETRIAL7 promotion row.', 1;

        INSERT app.PromoCodes
        (
            promo_code_id,
            promotion_name,
            code,
            status_code,
            access_duration_days,
            max_redemptions,
            redemption_count,
            redeem_by_utc,
            created_by_user_id,
            notes,
            created_at_utc,
            updated_at_utc
        )
        VALUES
        (
            NEWID(),
            N'7-Day Free Trial',
            @trialCode,
            N'ACTIVE',
            7,
            1000000,
            0,
            NULL,
            @createdBy,
            N'Public TEST funnel: seven days free; no payment information collected before trial.',
            SYSUTCDATETIME(),
            SYSUTCDATETIME()
        );
    END;

    COMMIT TRANSACTION;

    PRINT 'PASS: MarketingExperimentEvents ready.';
    PRINT 'PASS: FREETRIAL7 ready.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT
    code,
    promotion_name,
    status_code,
    access_duration_days,
    max_redemptions,
    redemption_count
FROM app.PromoCodes
WHERE code = N'FREETRIAL7';
