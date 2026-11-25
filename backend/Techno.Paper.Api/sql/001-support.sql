-- Explicit deployment step. Never invoked by application startup.
-- No legacy table changes, credential changes or existing business-record mutations.
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 5000;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock = sys.sp_getapplock @Resource=N'webpoc-support-v1',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;
IF @lock < 0 THROW 50000, 'Could not acquire deployment lock.', 1;
IF DB_NAME() NOT IN ('EcoTech_Comp','EcoTech_main')
    THROW 50000, 'Unapproved POC development database target.', 1;
IF SCHEMA_ID('webpoc') IS NULL EXEC(N'CREATE SCHEMA webpoc');
IF DB_NAME()='EcoTech_Comp' AND OBJECT_ID('webpoc.Session','U') IS NULL
    CREATE TABLE webpoc.Session(
        Id varchar(64) NOT NULL PRIMARY KEY,
        Username varchar(10) NOT NULL,
        ExpiresAt datetime2 NOT NULL);
IF DB_NAME()='EcoTech_main'
BEGIN
    IF OBJECT_ID('webpoc.Field','U') IS NULL
        CREATE TABLE webpoc.Field(
            Section varchar(10) NOT NULL,
            ColumnName sysname NOT NULL,
            Label nvarchar(100) NOT NULL,
            Permission varchar(1) NOT NULL,
            CONSTRAINT PK_webpoc_Field PRIMARY KEY(Section,ColumnName));
    IF OBJECT_ID('webpoc.Command','U') IS NULL
        CREATE TABLE webpoc.Command(
            CommandKey varchar(64) NOT NULL PRIMARY KEY,
            Username varchar(10) NOT NULL,
            PayloadHash varchar(64) NOT NULL,
            DocId varchar(21) NOT NULL);
    IF OBJECT_ID('webpoc.Audit','U') IS NULL
        CREATE TABLE webpoc.Audit(
            Id bigint IDENTITY NOT NULL PRIMARY KEY,
            Username varchar(10) NOT NULL,
            Operation varchar(30) NOT NULL,
            RecordId nvarchar(128) NOT NULL,
            OccurredAt datetime2 NOT NULL CONSTRAINT DF_webpoc_Audit_Time DEFAULT SYSUTCDATETIME());
END;
COMMIT;
