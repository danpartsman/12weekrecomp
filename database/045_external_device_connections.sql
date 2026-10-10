SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'app.DeviceProviderSettings', N'U') IS NULL
BEGIN
 CREATE TABLE app.DeviceProviderSettings (
 provider_code nvarchar(40) NOT NULL PRIMARY KEY,
 enabled bit NOT NULL, automatic_sync_enabled bit NOT NULL,
 sync_interval_minutes int NOT NULL CHECK(sync_interval_minutes BETWEEN 15 AND 1440),
 history_days int NOT NULL CHECK(history_days BETWEEN 1 AND 90),
 app_version nvarchar(32) NOT NULL, body_composition_enabled bit NOT NULL,
 skeletal_muscle_unit nvarchar(16) NOT NULL CHECK(skeletal_muscle_unit IN ('PERCENT','LB','KG')),
 body_water_unit nvarchar(16) NOT NULL CHECK(body_water_unit IN ('PERCENT','LB','KG')),
 updated_at_utc datetime2 NOT NULL);
END;
IF NOT EXISTS (SELECT 1 FROM app.DeviceProviderSettings WHERE provider_code=N'FITDAYS')
 INSERT INTO app.DeviceProviderSettings VALUES(N'FITDAYS',1,1,15,30,N'1.21.0',1,N'PERCENT',N'PERCENT',SYSUTCDATETIME());
IF OBJECT_ID(N'app.DeviceConnections', N'U') IS NULL
BEGIN
 CREATE TABLE app.DeviceConnections (
 device_connection_id uniqueidentifier NOT NULL PRIMARY KEY,
 user_id uniqueidentifier NOT NULL REFERENCES app.UserAccounts(user_id),
 client_id uniqueidentifier NOT NULL REFERENCES app.Clients(client_id),
 provider_code nvarchar(40) NOT NULL REFERENCES app.DeviceProviderSettings(provider_code),
 region nvarchar(8) NOT NULL, person_id bigint NOT NULL, person_name nvarchar(200) NOT NULL,
 protected_credential nvarchar(max) NULL, automatic_sync bit NOT NULL, status_code nvarchar(24) NOT NULL,
 last_attempt_at_utc datetime2 NULL, last_success_at_utc datetime2 NULL,
 sync_lease_until_utc datetime2 NULL, sync_lease_id uniqueidentifier NULL,
 last_message nvarchar(600) NULL, created_at_utc datetime2 NOT NULL, updated_at_utc datetime2 NOT NULL,
 CONSTRAINT UX_DeviceConnections_OwnerProvider UNIQUE(user_id,client_id,provider_code));
END;
COMMIT;
