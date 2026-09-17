SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'__WEB_APP_NAME__')
BEGIN
    CREATE USER [__WEB_APP_NAME__] FROM EXTERNAL PROVIDER;
END;

IF DATABASE_PRINCIPAL_ID(N'web_recomp_app') IS NULL
BEGIN
    CREATE ROLE web_recomp_app;
    GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::app TO web_recomp_app;
    GRANT SELECT ON SCHEMA::config TO web_recomp_app;
    GRANT INSERT ON SCHEMA::audit TO web_recomp_app;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.database_role_members drm
    JOIN sys.database_principals rolep ON rolep.principal_id = drm.role_principal_id
    JOIN sys.database_principals memberp ON memberp.principal_id = drm.member_principal_id
    WHERE rolep.name = N'web_recomp_app'
      AND memberp.name = N'__WEB_APP_NAME__'
)
BEGIN
    ALTER ROLE web_recomp_app ADD MEMBER [__WEB_APP_NAME__];
END;
