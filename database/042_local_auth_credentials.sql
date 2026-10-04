SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'app.LocalAuthCredentials', N'U') IS NULL
BEGIN
    CREATE TABLE app.LocalAuthCredentials
    (
        UserId uniqueidentifier NOT NULL,
        PasswordHash nvarchar(512) NOT NULL,
        GlobalRoleCode varchar(20) NOT NULL
            CONSTRAINT DF_LocalAuthCredentials_GlobalRoleCode DEFAULT ('CLIENT'),
        FailedSignInCount int NOT NULL
            CONSTRAINT DF_LocalAuthCredentials_FailedSignInCount DEFAULT (0),
        LockoutEndUtc datetime2(3) NULL,
        LastSignInAtUtc datetime2(3) NULL,
        CreatedAtUtc datetime2(3) NOT NULL,
        UpdatedAtUtc datetime2(3) NOT NULL,
        CONSTRAINT PK_LocalAuthCredentials PRIMARY KEY (UserId),
        CONSTRAINT FK_LocalAuthCredentials_UserAccounts
            FOREIGN KEY (UserId) REFERENCES app.UserAccounts(UserId)
    );
END;
