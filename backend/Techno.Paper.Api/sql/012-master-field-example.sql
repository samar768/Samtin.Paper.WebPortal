-- OPTIONAL, reviewed example only. Not part of the default BF/GSM deployment.
-- Demonstrates adding BF.WebRemark without a React/API deployment.
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 10000;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource='webpoc-simple-masters-011',
    @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
IF @lock<0 THROW 50000,'Could not acquire master deployment lock.',1;
IF DB_NAME()<>'EcoTech_main' OR COL_LENGTH('webpoc.MasterField','MaxLength') IS NULL
    THROW 50000,'Deploy 011 and 013 to the approved development database first.',1;
DECLARE @active int,@next int;
SELECT @active=ActiveRevision FROM webpoc.MasterRegistration WITH(UPDLOCK,HOLDLOCK) WHERE Id='bf' AND Enabled=1;
IF @active IS NULL THROW 50000,'BF has no active reviewed definition.',1;
IF COL_LENGTH('dbo.BFMast','WebRemark') IS NULL
    ALTER TABLE dbo.BFMast ADD WebRemark nvarchar(200) NULL;
IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.BFMast')
              AND name='WebRemark' AND system_type_id=231 AND max_length=400 AND is_nullable=1
              AND is_computed=0 AND is_identity=0)
    THROW 50000,'Existing WebRemark has an incompatible definition.',1;
IF NOT EXISTS(SELECT 1 FROM webpoc.MasterField WHERE MasterId='bf' AND Revision=@active AND ColumnName='WebRemark')
BEGIN
    SELECT @next=MAX(Revision)+1 FROM webpoc.MasterDefinition WITH(UPDLOCK,HOLDLOCK) WHERE MasterId='bf';
    INSERT INTO webpoc.MasterDefinition(MasterId,Revision,Title,TableName,KeyColumn,NameColumn,MenuModule,MenuName,Handler,PublishedBy,Scope)
        SELECT MasterId,@next,Title,TableName,KeyColumn,NameColumn,MenuModule,MenuName,Handler,SUSER_SNAME(),Scope
        FROM webpoc.MasterDefinition WHERE MasterId='bf' AND Revision=@active;
    INSERT INTO webpoc.MasterField(MasterId,Revision,ColumnName,Label,Position,Required,DefaultValue,MaxLength)
        SELECT MasterId,@next,ColumnName,Label,Position,Required,DefaultValue,MaxLength
        FROM webpoc.MasterField WHERE MasterId='bf' AND Revision=@active;
    DECLARE @position int;
    SELECT @position=MAX(Position)+1 FROM webpoc.MasterField WHERE MasterId='bf' AND Revision=@next;
    INSERT INTO webpoc.MasterField(MasterId,Revision,ColumnName,Label,Position,Required)
        VALUES('bf',@next,'WebRemark','Remark',@position,0);
    UPDATE webpoc.MasterRegistration SET ActiveRevision=@next WHERE Id='bf';
END;
COMMIT;
