-- Explicitly approved nullable POC demonstration columns. Keep data; do not drop on rollback.
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 5000;
BEGIN TRANSACTION;
IF DB_NAME()<>'EcoTech_main' THROW 50000, 'Unapproved POC development database target.', 1;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'webpoc-demo-v1',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;
IF @lock<0 THROW 50000, 'Could not acquire deployment lock.', 1;
IF COL_LENGTH('dbo.SOrd','WebPocReference') IS NULL
    ALTER TABLE dbo.SOrd ADD WebPocReference nvarchar(60) NULL;
IF COL_LENGTH('dbo.SOrdDet','WebPocMeasure') IS NULL
    ALTER TABLE dbo.SOrdDet ADD WebPocMeasure decimal(12,3) NULL;
COMMIT;
