SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH('app.LaunchInterests', 'EmailStatus') IS NULL
BEGIN
    ALTER TABLE app.LaunchInterests
        ADD EmailStatus NVARCHAR(32) NOT NULL
            CONSTRAINT DF_LaunchInterests_EmailStatus DEFAULT ('ACTIVE') WITH VALUES;
END;

IF COL_LENGTH('app.LaunchInterests', 'UnsubscribedUtc') IS NULL
    ALTER TABLE app.LaunchInterests ADD UnsubscribedUtc DATETIME2(0) NULL;

IF COL_LENGTH('app.LaunchInterests', 'SignupSource') IS NULL
BEGIN
    ALTER TABLE app.LaunchInterests
        ADD SignupSource NVARCHAR(64) NOT NULL
            CONSTRAINT DF_LaunchInterests_SignupSource DEFAULT ('PRELAUNCH_WEB') WITH VALUES;
END;

IF COL_LENGTH('app.LaunchInterests', 'ConsentVersion') IS NULL
BEGIN
    ALTER TABLE app.LaunchInterests
        ADD ConsentVersion NVARCHAR(64) NOT NULL
            CONSTRAINT DF_LaunchInterests_ConsentVersion DEFAULT ('PRELAUNCH_V1') WITH VALUES;
END;

IF COL_LENGTH('app.LaunchInterests', 'UnsubscribeToken') IS NULL
    ALTER TABLE app.LaunchInterests ADD UnsubscribeToken UNIQUEIDENTIFIER NULL;
GO

UPDATE app.LaunchInterests
SET UnsubscribeToken = NEWID()
WHERE UnsubscribeToken IS NULL;

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('app.LaunchInterests')
      AND name = 'UnsubscribeToken'
      AND is_nullable = 1
)
    ALTER TABLE app.LaunchInterests ALTER COLUMN UnsubscribeToken UNIQUEIDENTIFIER NOT NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('app.LaunchInterests')
      AND name = 'UX_LaunchInterests_UnsubscribeToken'
)
    CREATE UNIQUE INDEX UX_LaunchInterests_UnsubscribeToken
        ON app.LaunchInterests(UnsubscribeToken);

IF COL_LENGTH('app.LaunchInterests', 'WelcomeEmailStatus') IS NULL
BEGIN
    ALTER TABLE app.LaunchInterests
        ADD WelcomeEmailStatus NVARCHAR(32) NOT NULL
            CONSTRAINT DF_LaunchInterests_WelcomeEmailStatus DEFAULT ('NOT_SENT') WITH VALUES;
END;

IF COL_LENGTH('app.LaunchInterests', 'WelcomeEmailSentUtc') IS NULL
    ALTER TABLE app.LaunchInterests ADD WelcomeEmailSentUtc DATETIME2(0) NULL;

IF COL_LENGTH('app.LaunchInterests', 'WelcomeEmailLastError') IS NULL
    ALTER TABLE app.LaunchInterests ADD WelcomeEmailLastError NVARCHAR(2000) NULL;
GO

IF OBJECT_ID('app.EmailBroadcasts', 'U') IS NULL
BEGIN
    CREATE TABLE app.EmailBroadcasts
    (
        EmailBroadcastId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailBroadcasts PRIMARY KEY,
        Subject NVARCHAR(150) NOT NULL,
        MessageText NVARCHAR(MAX) NOT NULL,
        AudienceCode NVARCHAR(32) NOT NULL,
        StatusCode NVARCHAR(32) NOT NULL,
        RecipientCount INT NOT NULL,
        SentCount INT NOT NULL,
        FailedCount INT NOT NULL,
        CreatedBy NVARCHAR(320) NULL,
        CreatedAtUtc DATETIME2(0) NOT NULL,
        CompletedAtUtc DATETIME2(0) NULL
    );
END;

IF OBJECT_ID('app.EmailDeliveries', 'U') IS NULL
BEGIN
    CREATE TABLE app.EmailDeliveries
    (
        EmailDeliveryId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailDeliveries PRIMARY KEY,
        EmailBroadcastId BIGINT NOT NULL,
        LaunchInterestId BIGINT NOT NULL,
        RecipientEmail NVARCHAR(320) NOT NULL,
        Subject NVARCHAR(150) NOT NULL,
        StatusCode NVARCHAR(32) NOT NULL,
        AttemptedAtUtc DATETIME2(0) NOT NULL,
        SentAtUtc DATETIME2(0) NULL,
        ErrorMessage NVARCHAR(2000) NULL,
        CONSTRAINT FK_EmailDeliveries_Broadcast FOREIGN KEY (EmailBroadcastId)
            REFERENCES app.EmailBroadcasts(EmailBroadcastId),
        CONSTRAINT FK_EmailDeliveries_LaunchInterest FOREIGN KEY (LaunchInterestId)
            REFERENCES app.LaunchInterests(LaunchInterestId)
    );

    CREATE INDEX IX_EmailDeliveries_Broadcast
        ON app.EmailDeliveries(EmailBroadcastId, StatusCode);
END;

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'recomp-live')
BEGIN
    GRANT SELECT, INSERT, UPDATE ON OBJECT::app.LaunchInterests TO [recomp-live];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::app.EmailBroadcasts TO [recomp-live];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::app.EmailDeliveries TO [recomp-live];
END;
GO

SELECT
    COUNT(*) AS ExistingLaunchInterests,
    SUM(CASE WHEN EmailStatus = 'ACTIVE' THEN 1 ELSE 0 END) AS ActiveSubscribers
FROM app.LaunchInterests;
