-- Explicit, additive deployment to the approved development database only.
-- BF/GSM values, keys and existing nullability are not rewritten.
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 10000;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource='webpoc-simple-masters-011',
    @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
IF @lock<0 THROW 50000,'Could not acquire master deployment lock.',1;
IF DB_NAME()<>'EcoTech_main' THROW 50000,'Unapproved master deployment target.',1;
IF OBJECT_ID('webpoc.LookupDefinition','U') IS NULL
    THROW 50000,'Deploy value-help scripts 008, 009 and 010 first.',1;

IF OBJECT_ID('webpoc.MasterRegistration','U') IS NULL
    CREATE TABLE webpoc.MasterRegistration(
        Id varchar(60) NOT NULL CONSTRAINT PK_MasterRegistration PRIMARY KEY,
        ActiveRevision int NULL,
        Enabled bit NOT NULL CONSTRAINT DF_MasterRegistration_Enabled DEFAULT 1);
IF OBJECT_ID('webpoc.MasterDefinition','U') IS NULL
    CREATE TABLE webpoc.MasterDefinition(
        MasterId varchar(60) NOT NULL,
        Revision int NOT NULL,
        Title nvarchar(100) NOT NULL,
        TableName sysname NOT NULL,
        KeyColumn sysname NOT NULL,
        NameColumn sysname NOT NULL,
        MenuModule varchar(30) NOT NULL,
        MenuName varchar(100) NOT NULL,
        Handler varchar(30) NOT NULL,
        PublishedBy varchar(128) NOT NULL,
        PublishedAt datetime2 NOT NULL CONSTRAINT DF_MasterDefinition_Time DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MasterDefinition PRIMARY KEY(MasterId,Revision),
        CONSTRAINT FK_MasterDefinition_Registration FOREIGN KEY(MasterId) REFERENCES webpoc.MasterRegistration(Id),
        CONSTRAINT CK_MasterDefinition_Revision CHECK(Revision>0),
        CONSTRAINT CK_MasterDefinition_Handler CHECK(Handler='single-field'));
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_MasterRegistration_ActiveRevision'
              AND parent_object_id=OBJECT_ID('webpoc.MasterRegistration'))
    ALTER TABLE webpoc.MasterRegistration ADD CONSTRAINT FK_MasterRegistration_ActiveRevision
        FOREIGN KEY(Id,ActiveRevision) REFERENCES webpoc.MasterDefinition(MasterId,Revision);
IF OBJECT_ID('webpoc.MasterField','U') IS NULL
    CREATE TABLE webpoc.MasterField(
        MasterId varchar(60) NOT NULL,
        Revision int NOT NULL,
        ColumnName sysname NOT NULL,
        Label nvarchar(100) NOT NULL,
        Position int NOT NULL,
        Required bit NOT NULL,
        DefaultValue nvarchar(4000) NULL,
        CONSTRAINT PK_MasterField PRIMARY KEY(MasterId,Revision,ColumnName),
        CONSTRAINT UQ_MasterField_Position UNIQUE(MasterId,Revision,Position),
        CONSTRAINT FK_MasterField_Definition FOREIGN KEY(MasterId,Revision)
            REFERENCES webpoc.MasterDefinition(MasterId,Revision));
IF OBJECT_ID('webpoc.LookupMaster','U') IS NULL
    CREATE TABLE webpoc.LookupMaster(
        LookupId varchar(60) NOT NULL CONSTRAINT PK_LookupMaster PRIMARY KEY,
        MasterId varchar(60) NOT NULL,
        CONSTRAINT FK_LookupMaster_Lookup FOREIGN KEY(LookupId) REFERENCES webpoc.LookupDefinition(Id),
        CONSTRAINT FK_LookupMaster_Master FOREIGN KEY(MasterId) REFERENCES webpoc.MasterRegistration(Id));
IF OBJECT_ID('webpoc.MasterCommand','U') IS NULL
    CREATE TABLE webpoc.MasterCommand(
        CommandKey varchar(64) NOT NULL CONSTRAINT PK_MasterCommand PRIMARY KEY,
        MasterId varchar(60) NOT NULL,
        RecordId varchar(6) NOT NULL,
        PayloadHash varchar(64) NOT NULL,
        ResponseJson nvarchar(max) NOT NULL,
        CONSTRAINT FK_MasterCommand_Master FOREIGN KEY(MasterId) REFERENCES webpoc.MasterRegistration(Id));
IF OBJECT_ID('webpoc.MasterAudit','U') IS NULL
BEGIN
    CREATE TABLE webpoc.MasterAudit(
        Id bigint IDENTITY NOT NULL CONSTRAINT PK_MasterAudit PRIMARY KEY,
        MasterId varchar(60) NOT NULL,
        RecordId varchar(6) NOT NULL,
        Username varchar(10) NOT NULL,
        Operation varchar(10) NOT NULL,
        BeforeJson nvarchar(max) NULL,
        AfterJson nvarchar(max) NULL,
        OccurredAt datetime2 NOT NULL CONSTRAINT DF_MasterAudit_Time DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_MasterAudit_Master FOREIGN KEY(MasterId) REFERENCES webpoc.MasterRegistration(Id),
        CONSTRAINT CK_MasterAudit_Operation CHECK(Operation IN ('create','edit','delete')));
    CREATE INDEX IX_MasterAudit_Record ON webpoc.MasterAudit(MasterId,RecordId,Id)
        INCLUDE(Username,Operation);
END;

DECLARE @masters TABLE(Id varchar(60),TableName sysname,Title nvarchar(100),MenuName varchar(100));
INSERT INTO @masters VALUES
    ('bf','BFMast','BF Master','MnuBFMaster'),('gsm','GSMMast','GSM Master','MnuGSMMaster');
DECLARE @id varchar(60),@table sysname,@title nvarchar(100),@menu varchar(100),@sql nvarchar(max);
DECLARE masters CURSOR LOCAL FAST_FORWARD FOR SELECT Id,TableName,Title,MenuName FROM @masters;
OPEN masters;
FETCH NEXT FROM masters INTO @id,@table,@title,@menu;
WHILE @@FETCH_STATUS=0
BEGIN
    IF OBJECT_ID('dbo.'+@table,'U') IS NULL THROW 50000,'A required legacy master table is missing.',1;
    IF COL_LENGTH('dbo.'+@table,'WebPocVersion') IS NULL
    BEGIN
        SET @sql=N'ALTER TABLE dbo.'+QUOTENAME(@table)+N' ADD WebPocVersion rowversion;';
        EXEC sys.sp_executesql @sql;
    END;
    IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.'+@table)
                  AND name='   AND system_type_id=189)
        THROW 50000,'Master concurrency column has an incompatible definition.',1;
    -- Enforce the existing Windows duplicate-name rule for Windows and web writers alike.
    SET @sql=N'IF EXISTS(SELECT Name FROM dbo.'+QUOTENAME(@table)+
        N' WHERE Name IS NOT NULL GROUP BY Name HAVING COUNT(*)>1)
        THROW 50000,''Existing duplicate master names require owner review; no data was repaired.'',1;';
    EXEC sys.sp_executesql @sql;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.'+@table)
                  AND name='UX_webpoc_'+@table+'_Name')
    BEGIN
        SET @sql=N'CREATE UNIQUE INDEX '+QUOTENAME('UX_webpoc_'+@table+'_Name')+
            N' ON dbo.'+QUOTENAME(@table)+N'(Name) WHERE Name IS NOT NULL;';
        EXEC sys.sp_executesql @sql;
    END;
    IF NOT EXISTS(SELECT 1 FROM webpoc.MasterRegistration WHERE Id=@id)
    BEGIN
        INSERT INTO webpoc.MasterRegistration(Id) VALUES(@id);
        INSERT INTO webpoc.MasterDefinition(MasterId,Revision,Title,TableName,KeyColumn,NameColumn,
            MenuModule,MenuName,Handler,PublishedBy)
        VALUES(@id,1,@title,@table,'Code','Name','Master',@menu,'single-field',SUSER_SNAME());
        INSERT INTO webpoc.MasterField(MasterId,Revision,ColumnName,Label,Position,Required)
        VALUES(@id,1,'Name',CASE @id WHEN 'bf' THEN 'BF Name' ELSE 'GSM Name' END,1,1);
        UPDATE webpoc.MasterRegistration SET ActiveRevision=1 WHERE Id=@id;
    END;
    IF EXISTS(SELECT 1 FROM webpoc.LookupMaster WHERE LookupId=@id AND MasterId<>@id)
        THROW 50000,'Conflicting lookup-to-master binding requires review.',1;
    IF NOT EXISTS(SELECT 1 FROM webpoc.LookupMaster WHERE LookupId=@id)
        INSERT INTO webpoc.LookupMaster(LookupId,MasterId) VALUES(@id,@id);
    FETCH NEXT FROM masters INTO @id,@table,@title,@menu;
END;
CLOSE masters;
DEALLOCATE masters;
COMMIT;
