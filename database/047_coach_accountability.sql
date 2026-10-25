SET XACT_ABORT ON;
IF @ExpectedDatabase IS NULL OR DB_NAME() <> @ExpectedDatabase OR @ExpectedDatabase NOT IN (N'sqldb-recomp',N'sqldb-recomp-live')
    THROW 50001, 'Accountability rollout requires an explicitly verified 12WR database.', 1;
BEGIN TRANSACTION;
IF OBJECT_ID(N'app.CoachAccountabilityAgreements',N'U') IS NULL
BEGIN
 CREATE TABLE app.CoachAccountabilityAgreements (
  client_id uniqueidentifier NOT NULL PRIMARY KEY,
  coach_user_id uniqueidentifier NOT NULL,
  state_json nvarchar(max) NOT NULL,
  terms_json nvarchar(max) NOT NULL,
  terms_hash nvarchar(64) NOT NULL,
  revision int NOT NULL,
  terms_version int NOT NULL,
  created_at_utc datetime2 NOT NULL,
  updated_at_utc datetime2 NOT NULL,
  CONSTRAINT FK_Accountability_Client FOREIGN KEY(client_id) REFERENCES app.Clients(client_id),
  CONSTRAINT FK_Accountability_Coach FOREIGN KEY(coach_user_id) REFERENCES app.UserAccounts(user_id),
  CONSTRAINT CK_Accountability_Json CHECK(ISJSON(state_json)=1 AND ISJSON(terms_json)=1),
  CONSTRAINT CK_Accountability_Version CHECK(revision>0 AND terms_version>0)
 );
END;
IF OBJECT_ID(N'app.CoachAccountabilityApprovals',N'U') IS NULL
BEGIN
 CREATE TABLE app.CoachAccountabilityApprovals (
  approval_id uniqueidentifier NOT NULL PRIMARY KEY,
  client_id uniqueidentifier NOT NULL,
  coach_user_id uniqueidentifier NOT NULL,
  owner_user_id uniqueidentifier NOT NULL,
  terms_version int NOT NULL,
  terms_hash nvarchar(64) NOT NULL,
  terms_json nvarchar(max) NOT NULL,
  signed_by nvarchar(320) NOT NULL,
  approved_at_utc datetime2 NOT NULL,
  CONSTRAINT FK_AccountabilityApproval_Client FOREIGN KEY(client_id) REFERENCES app.CoachAccountabilityAgreements(client_id),
  CONSTRAINT FK_AccountabilityApproval_Coach FOREIGN KEY(coach_user_id) REFERENCES app.UserAccounts(user_id),
  CONSTRAINT FK_AccountabilityApproval_Owner FOREIGN KEY(owner_user_id) REFERENCES app.UserAccounts(user_id),
  CONSTRAINT CK_AccountabilityApproval_Json CHECK(ISJSON(terms_json)=1)
 );
 CREATE UNIQUE INDEX UX_AccountabilityApproval_Version ON app.CoachAccountabilityApprovals(client_id,owner_user_id,terms_version,terms_hash);
END;
IF OBJECT_ID(N'app.CoachAccountabilityEvents',N'U') IS NULL
BEGIN
 CREATE TABLE app.CoachAccountabilityEvents (
  event_id uniqueidentifier NOT NULL PRIMARY KEY,
  client_id uniqueidentifier NOT NULL,
  actor_user_id uniqueidentifier NOT NULL,
  kind nvarchar(30) NOT NULL,
  revision int NOT NULL,
  terms_version int NOT NULL,
  payload_json nvarchar(max) NOT NULL,
  created_at_utc datetime2 NOT NULL,
  CONSTRAINT FK_AccountabilityEvent_Client FOREIGN KEY(client_id) REFERENCES app.CoachAccountabilityAgreements(client_id),
  CONSTRAINT FK_AccountabilityEvent_Actor FOREIGN KEY(actor_user_id) REFERENCES app.UserAccounts(user_id),
  CONSTRAINT CK_AccountabilityEvent_Json CHECK(ISJSON(payload_json)=1),
  CONSTRAINT CK_AccountabilityEvent_Kind CHECK(kind IN ('CONFIG','RESULT','APPROVE'))
 );
 CREATE UNIQUE INDEX UX_AccountabilityEvent_Revision ON app.CoachAccountabilityEvents(client_id,revision);
END;
GRANT SELECT, INSERT, UPDATE ON OBJECT::app.CoachAccountabilityAgreements TO web_recomp_app;
GRANT SELECT, INSERT ON OBJECT::app.CoachAccountabilityApprovals TO web_recomp_app;
GRANT SELECT, INSERT ON OBJECT::app.CoachAccountabilityEvents TO web_recomp_app;
DENY DELETE ON OBJECT::app.CoachAccountabilityAgreements TO web_recomp_app;
DENY UPDATE, DELETE ON OBJECT::app.CoachAccountabilityApprovals TO web_recomp_app;
DENY UPDATE, DELETE ON OBJECT::app.CoachAccountabilityEvents TO web_recomp_app;
COMMIT TRANSACTION;
SELECT N'ACCOUNTABILITY_SCHEMA_READY' AS Verification,
 (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'app') AND name IN (N'CoachAccountabilityAgreements',N'CoachAccountabilityApprovals',N'CoachAccountabilityEvents')) AS TablesPresent;
-- Exercise storage only in TEST. Production migrations never insert sample financial records.
IF @ExpectedDatabase = N'sqldb-recomp'
BEGIN
DECLARE @client uniqueidentifier, @coach uniqueidentifier, @owner uniqueidentifier;
SELECT TOP(1) @client=r.client_id,@coach=r.coach_user_id,@owner=a.user_id
FROM app.CoachClientRelationships r
JOIN app.UserClientAccess a ON a.client_id=r.client_id AND a.access_role_code='OWNER' AND a.revoked_at_utc IS NULL
JOIN app.Clients c ON c.client_id=r.client_id AND c.client_status_code='ACTIVE'
WHERE r.status_code='ACTIVE' AND r.revoked_at_utc IS NULL
ORDER BY r.client_id;
IF @client IS NULL THROW 50002, 'An active coached TEST client is required for storage verification.', 1;
DECLARE @approval uniqueidentifier=NEWID(),@event uniqueidentifier=NEWID(),@revision int;
BEGIN TRY
 BEGIN TRANSACTION;
 IF NOT EXISTS(SELECT 1 FROM app.CoachAccountabilityAgreements WHERE client_id=@client)
  INSERT app.CoachAccountabilityAgreements(client_id,coach_user_id,state_json,terms_json,terms_hash,revision,terms_version,created_at_utc,updated_at_utc)
  VALUES(@client,@coach,N'{}',N'{}',REPLICATE(N'0',64),1,1,SYSUTCDATETIME(),SYSUTCDATETIME());
 UPDATE app.CoachAccountabilityAgreements SET revision=revision+1 WHERE client_id=@client;
 SELECT @revision=revision FROM app.CoachAccountabilityAgreements WHERE client_id=@client;
 INSERT app.CoachAccountabilityEvents(event_id,client_id,actor_user_id,kind,revision,terms_version,payload_json,created_at_utc)
 VALUES(@event,@client,@coach,N'CONFIG',@revision,1,N'{"verification":"rollback"}',SYSUTCDATETIME());
 INSERT app.CoachAccountabilityApprovals(approval_id,client_id,coach_user_id,owner_user_id,terms_version,terms_hash,terms_json,signed_by,approved_at_utc)
 VALUES(@approval,@client,@coach,@owner,2147483647,CONVERT(nvarchar(64),HASHBYTES('SHA2_256',CONVERT(nvarchar(36),@approval)),2),N'{}',N'TEST rollback verification',SYSUTCDATETIME());
 IF NOT EXISTS(SELECT 1 FROM app.CoachAccountabilityEvents WHERE event_id=@event)
    OR NOT EXISTS(SELECT 1 FROM app.CoachAccountabilityApprovals WHERE approval_id=@approval)
    THROW 50003, 'Accountability storage verification failed.', 1;
 ROLLBACK TRANSACTION;
 IF EXISTS(SELECT 1 FROM app.CoachAccountabilityEvents WHERE event_id=@event)
    OR EXISTS(SELECT 1 FROM app.CoachAccountabilityApprovals WHERE approval_id=@approval)
    THROW 50004, 'Accountability verification did not roll back.', 1;
 SELECT N'ACCOUNTABILITY_STORAGE_ROLLBACK_VERIFIED' AS Verification;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;

END;
