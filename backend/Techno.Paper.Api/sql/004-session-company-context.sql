-- Explicit deployment; existing sessions must choose a workspace after this upgrade.
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 5000;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock = sys.sp_getapplock @Resource=N'webpoc-session-context-v1',
    @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;
IF @lock < 0 THROW 50000, 'Could not acquire deployment lock.', 1;
IF DB_NAME() <> 'EcoTech_Comp' OR OBJECT_ID('webpoc.Session','U') IS NULL
    THROW 50000, 'Deploy support schema in the approved control database first.', 1;
IF COL_LENGTH('webpoc.Session','Company') IS NULL ALTER TABLE webpoc.Session ADD Company varchar(2) NULL;
IF COL_LENGTH('webpoc.Session','Site') IS NULL ALTER TABLE webpoc.Session ADD Site varchar(2) NULL;
IF COL_LENGTH('webpoc.Session','ContextVersion') IS NULL ALTER TABLE webpoc.Session ADD ContextVersion varchar(32) NULL;
COMMIT;
