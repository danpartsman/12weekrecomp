SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.schemas
    WHERE name = N'app'
)
BEGIN
    EXEC(N'CREATE SCHEMA app AUTHORIZATION dbo;');
END;

IF OBJECT_ID(N'app.LaunchInterests', N'U') IS NULL
BEGIN
    CREATE TABLE app.LaunchInterests
    (
        LaunchInterestId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_LaunchInterests PRIMARY KEY,

        Name NVARCHAR(100) NOT NULL,
        Email NVARCHAR(320) NOT NULL,
        EmailNormalized NVARCHAR(320) NOT NULL,

        InterestCode NVARCHAR(32) NOT NULL
            CONSTRAINT CK_LaunchInterests_InterestCode
            CHECK (InterestCode IN (N'FREE_UPDATES', N'FOUNDERS_RATE')),

        FirstSubmittedUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_LaunchInterests_FirstSubmittedUtc
            DEFAULT SYSUTCDATETIME(),

        LastSubmittedUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_LaunchInterests_LastSubmittedUtc
            DEFAULT SYSUTCDATETIME(),

        SubmissionCount INT NOT NULL
            CONSTRAINT DF_LaunchInterests_SubmissionCount
            DEFAULT (1),

        CONSTRAINT CK_LaunchInterests_SubmissionCount
            CHECK (SubmissionCount >= 1)
    );

    CREATE UNIQUE INDEX UX_LaunchInterests_EmailNormalized
        ON app.LaunchInterests(EmailNormalized);
END;

COMMIT TRANSACTION;
