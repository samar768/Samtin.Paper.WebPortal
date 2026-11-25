-- Reviewed configuration example. Choose one registered master and desired policy.
-- Only affects subsequent portal commands; does not erase history or capture Windows writes.
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 10000;
BEGIN TRANSACTION;
IF DB_NAME()<>'EcoTech_main' OR COL_LENGTH('webpoc.MasterRegistration','CaptureAuditValues') IS NULL
    THROW 50000,'Deploy 013 to the approved development database first.',1;
DECLARE @MasterId varchar(60)='bf',@CaptureValues bit=1;
UPDATE webpoc.MasterRegistration SET CaptureAuditValues=@CaptureValues WHERE Id=@MasterId AND Enabled=1;
IF @@ROWCOUNT<>1 THROW 50000,'Exactly one enabled master must be selected.',1;
SELECT Id,CaptureAuditValues FROM webpoc.MasterRegistration WHERE Id=@MasterId;
COMMIT;
