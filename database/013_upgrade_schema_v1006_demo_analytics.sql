SET XACT_ABORT ON;
BEGIN TRANSACTION;

PRINT 'Applying v1006 demo analytics upgrade...';

------------------------------------------------------------
-- DemoWorkspaces: first-touch acquisition + coarse location
------------------------------------------------------------

IF COL_LENGTH('app.DemoWorkspaces', 'utm_source') IS NULL
    ALTER TABLE app.DemoWorkspaces ADD utm_source NVARCHAR(100) NULL;

IF COL_LENGTH('app.DemoWorkspaces', 'utm_medium') IS NULL
    ALTER TABLE app.DemoWorkspaces ADD utm_medium NVARCHAR(100) NULL;

IF COL_LENGTH('app.DemoWorkspaces', 'utm_campaign') IS NULL
    ALTER TABLE app.DemoWorkspaces ADD utm_campaign NVARCHAR(150) NULL;

IF COL_LENGTH('app.DemoWorkspaces', 'utm_content') IS NULL
    ALTER TABLE app.DemoWorkspaces ADD utm_content NVARCHAR(150) NULL;

IF COL_LENGTH('app.DemoWorkspaces', 'referrer_host') IS NULL
    ALTER TABLE app.DemoWorkspaces ADD referrer_host NVARCHAR(255) NULL;

IF COL_LENGTH('app.DemoWorkspaces', 'referrer_path') IS NULL
    ALTER TABLE app.DemoWorkspaces ADD referrer_path NVARCHAR(500) NULL;

IF COL_LENGTH('app.DemoWorkspaces', 'landing_path') IS NULL
    ALTER TABLE app.DemoWorkspaces ADD landing_path NVARCHAR(500) NULL;

IF COL_LENGTH('app.DemoWorkspaces', 'country_code') IS NULL
    ALTER TABLE app.DemoWorkspaces ADD country_code NVARCHAR(2) NULL;

IF COL_LENGTH('app.DemoWorkspaces', 'region_name') IS NULL
    ALTER TABLE app.DemoWorkspaces ADD region_name NVARCHAR(100) NULL;

IF COL_LENGTH('app.DemoWorkspaces', 'city_name') IS NULL
    ALTER TABLE app.DemoWorkspaces ADD city_name NVARCHAR(100) NULL;

IF COL_LENGTH('app.DemoWorkspaces', 'demo_completed_at_utc') IS NULL
    ALTER TABLE app.DemoWorkspaces ADD demo_completed_at_utc DATETIME2(7) NULL;


------------------------------------------------------------
-- DemoSessions: browser/device/request + engagement
------------------------------------------------------------

IF COL_LENGTH('app.DemoSessions', 'ip_address') IS NULL
    ALTER TABLE app.DemoSessions ADD ip_address NVARCHAR(64) NULL;

IF COL_LENGTH('app.DemoSessions', 'device_type_code') IS NULL
    ALTER TABLE app.DemoSessions ADD device_type_code NVARCHAR(16) NULL;

IF COL_LENGTH('app.DemoSessions', 'operating_system') IS NULL
    ALTER TABLE app.DemoSessions ADD operating_system NVARCHAR(64) NULL;

IF COL_LENGTH('app.DemoSessions', 'browser_name') IS NULL
    ALTER TABLE app.DemoSessions ADD browser_name NVARCHAR(64) NULL;

IF COL_LENGTH('app.DemoSessions', 'user_agent') IS NULL
    ALTER TABLE app.DemoSessions ADD user_agent NVARCHAR(1000) NULL;

IF COL_LENGTH('app.DemoSessions', 'page_view_count') IS NULL
BEGIN
    ALTER TABLE app.DemoSessions
        ADD page_view_count INT NOT NULL
            CONSTRAINT DF_DemoSessions_PageViewCount DEFAULT (0);
END;

IF COL_LENGTH('app.DemoSessions', 'last_page_path') IS NULL
    ALTER TABLE app.DemoSessions ADD last_page_path NVARCHAR(500) NULL;


------------------------------------------------------------
-- Useful lightweight analytics indexes
------------------------------------------------------------

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_DemoWorkspaces_UtmSource_CreatedAt'
      AND object_id = OBJECT_ID('app.DemoWorkspaces')
)
BEGIN
    CREATE INDEX IX_DemoWorkspaces_UtmSource_CreatedAt
        ON app.DemoWorkspaces(utm_source, created_at_utc);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_DemoSessions_DeviceType_CreatedAt'
      AND object_id = OBJECT_ID('app.DemoSessions')
)
BEGIN
    CREATE INDEX IX_DemoSessions_DeviceType_CreatedAt
        ON app.DemoSessions(device_type_code, created_at_utc);
END;

COMMIT TRANSACTION;

PRINT 'PASS: v1006 demo analytics schema upgrade applied.';
