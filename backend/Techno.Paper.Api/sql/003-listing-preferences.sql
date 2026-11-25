-- Explicit, additive deployment to the approved development control database.
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 5000;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock = sys.sp_getapplock @Resource=N'webpoc-listing-preferences-v1',
    @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;
IF @lock < 0 THROW 50000, 'Could not acquire deployment lock.', 1;
IF DB_NAME() <> 'EcoTech_Comp'
    THROW 50000, 'Unapproved preference database target.', 1;
IF SCHEMA_ID('webpoc') IS NULL
    THROW 50000, 'Deploy 001-support.sql first.', 1;
IF OBJECT_ID('webpoc.ListingPreference','U') IS NULL
    CREATE TABLE webpoc.ListingPreference(
        Username nvarchar(10) NOT NULL,
        Company nvarchar(20) NOT NULL,
        ListingKey nvarchar(64) NOT NULL,
        Settings nvarchar(4000) NOT NULL,
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_webpoc_ListingPreference_Time DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_webpoc_ListingPreference PRIMARY KEY(Username,Company,ListingKey));
COMMIT;
