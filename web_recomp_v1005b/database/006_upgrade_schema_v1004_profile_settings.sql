SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

/*
    Web Recomp v1004 profile/settings foundation.

    Additive only. v1003 can continue running against the same database because
    it ignores these columns. v1004 uses them for application-facing contact,
    preferred display name, and theme preference while Entra remains the source
    of truth for sign-in identity and credentials.
*/

IF COL_LENGTH('app.UserAccounts', 'preferred_display_name') IS NULL
BEGIN
    ALTER TABLE app.UserAccounts ADD preferred_display_name nvarchar(100) NULL;
END;
GO

IF COL_LENGTH('app.UserAccounts', 'contact_email') IS NULL
BEGIN
    ALTER TABLE app.UserAccounts ADD contact_email nvarchar(320) NULL;
END;
GO

IF COL_LENGTH('app.UserAccounts', 'theme_code') IS NULL
BEGIN
    ALTER TABLE app.UserAccounts
        ADD theme_code varchar(20) NOT NULL
            CONSTRAINT DF_UserAccounts_Theme DEFAULT 'SYSTEM';
END;
GO

UPDATE app.UserAccounts
SET preferred_display_name = COALESCE(preferred_display_name, display_name),
    contact_email = COALESCE(contact_email, email_normalized),
    theme_code = CASE WHEN theme_code IN ('SYSTEM','LIGHT','DARK') THEN theme_code ELSE 'SYSTEM' END
WHERE preferred_display_name IS NULL
   OR contact_email IS NULL
   OR theme_code NOT IN ('SYSTEM','LIGHT','DARK');
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = 'CK_UserAccounts_ThemeCode'
)
BEGIN
    ALTER TABLE app.UserAccounts
        ADD CONSTRAINT CK_UserAccounts_ThemeCode
        CHECK (theme_code IN ('SYSTEM','LIGHT','DARK'));
END;
GO

PRINT 'v1004 profile/settings schema foundation applied successfully.';
GO
