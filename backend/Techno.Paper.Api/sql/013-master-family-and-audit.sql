-- Explicit deployment to the approved development business database only.
-- Does not repair legacy values, assign missing sites, or change existing primary keys.
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 10000;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource='webpoc-simple-masters-011',
    @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
IF @lock<0 THROW 50000,'Could not acquire master deployment lock.',1;
IF DB_NAME()<>'EcoTech_main' OR OBJECT_ID('webpoc.MasterRegistration','U') IS NULL
    THROW 50000,'Deploy 011 to the approved development database first.',1;

IF COL_LENGTH('webpoc.MasterRegistration','CaptureAuditValues') IS NULL
    ALTER TABLE webpoc.MasterRegistration ADD CaptureAuditValues bit NOT NULL
        CONSTRAINT DF_MasterRegistration_CaptureAuditValues DEFAULT 1;
IF COL_LENGTH('webpoc.MasterDefinition','Scope') IS NULL
    ALTER TABLE webpoc.MasterDefinition ADD Scope varchar(10) NOT NULL
        CONSTRAINT DF_MasterDefinition_Scope DEFAULT 'global'
        CONSTRAINT CK_MasterDefinition_Scope CHECK(Scope IN('global','site'));
IF COL_LENGTH('webpoc.MasterField','MaxLength') IS NULL
    ALTER TABLE webpoc.MasterField ADD MaxLength int NULL
        CONSTRAINT CK_MasterField_MaxLength CHECK(MaxLength>0 AND MaxLength<=4000);
IF COL_LENGTH('webpoc.MasterCommand','RecordId')=6
    ALTER TABLE webpoc.MasterCommand ALTER COLUMN RecordId varchar(10) NOT NULL;
IF COL_LENGTH('webpoc.MasterAudit','RecordId')=6
    ALTER TABLE webpoc.MasterAudit ALTER COLUMN RecordId varchar(10) NOT NULL;
IF COL_LENGTH('webpoc.MasterAudit','CompanyCode') IS NULL
    ALTER TABLE webpoc.MasterAudit ADD CompanyCode varchar(10) NULL,SiteCode varchar(2) NULL,
        MetadataRevision int NULL,ValuesCaptured bit NOT NULL
        CONSTRAINT DF_MasterAudit_ValuesCaptured DEFAULT 1;

DECLARE @masters TABLE(Id varchar(60),TableName sysname,KeyColumn sysname,NameColumn sysname,
    Title nvarchar(100),Label nvarchar(100),NameLength int,Scope varchar(10),MenuName varchar(100));
INSERT INTO @masters VALUES
('varity','VarityMast','Code','Name','Varity Master','Varity Name',25,'global','MnuVarityMaster'),
('godowns','GodownMast','GodCode','GodName','Godown/Location Master','Godown Name',20,'site','MnuGodownMaster'),
('departments','DepartmentMast','Code','Name','Department Master','Department Name',35,'site','MnuDepartmentMaster'),
('classifications','ClassificationMast','Code','Name','Item Classification','Classification Name',30,'global','MnuItemClassification'),
('item-makes','ItemMakeMast','Code','Name','Item Make Master','Make Name',35,'global','MnuItemMakeMaster'),
('quality-specifications','SpecificationsMast','Code','Name','Quality Specification Master','Specification',35,'global','MnuQltSpeciMaster'),
('rejection-reasons','RejectionReasonMast','Code','Reason','Rejection Reason Master','Rejection Reason',50,'site','MnuRejectionReasonMaster'),
('deductions','DeductionMast','Code','Name','Deduction Master','Deduction',35,'global','MnuDeductionMaster'),
('machines','MachineMast','MachineCode','Name','Machine Master','Machine Master',35,'global','MnuMachineMaster');
DECLARE @id varchar(60),@table sysname,@key sysname,@name sysname,@title nvarchar(100),
    @label nvarchar(100),@length int,@scope varchar(10),@menu varchar(100),@sql nvarchar(max);
DECLARE masters CURSOR LOCAL FAST_FORWARD FOR SELECT * FROM @masters;
OPEN masters;
FETCH NEXT FROM masters INTO @id,@table,@key,@name,@title,@label,@length,@scope,@menu;
WHILE @@FETCH_STATUS=0
BEGIN
    IF OBJECT_ID('dbo.'+@table,'U') IS NULL OR COL_LENGTH('dbo.'+@table,@name)<@length
        THROW 50000,'A required master table or description shape is incompatible.',1;
    IF @scope='site' AND COL_LENGTH('dbo.'+@table,'Site_Code')<>2
        THROW 50000,'The master site column is incompatible.',1;
    SET @sql=N'IF EXISTS(SELECT '+CASE WHEN @scope='site' THEN N'Site_Code,' ELSE N'' END+
        QUOTENAME(@name)+N' FROM dbo.'+QUOTENAME(@table)+N' WHERE '+QUOTENAME(@name)+N' IS NOT NULL'+
        CASE WHEN @scope='site' THEN N' AND Site_Code IS NOT NULL' ELSE N'' END+
        N' GROUP BY '+CASE WHEN @scope='site' THEN N'Site_Code,' ELSE N'' END+QUOTENAME(@name)+
        N' HAVING COUNT(*)>1) THROW 50000,''Duplicate master descriptions require owner review; no data was repaired.'',1;';
    EXEC sys.sp_executesql @sql;
    IF COL_LENGTH('dbo.'+@table,'WebPocVersion') IS NULL
    BEGIN
        SET @sql=N'ALTER TABLE dbo.'+QUOTENAME(@table)+N' ADD WebPocVersion rowversion;';
        EXEC sys.sp_executesql @sql;
    END;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.'+@table)
                  AND name='UX_webpoc_'+@table+'_Name')
    BEGIN
        SET @sql=N'CREATE UNIQUE INDEX '+QUOTENAME('UX_webpoc_'+@table+'_Name')+
            N' ON dbo.'+QUOTENAME(@table)+N'('+CASE WHEN @scope='site' THEN N'Site_Code,' ELSE N'' END+
            QUOTENAME(@name)+N') WHERE '+QUOTENAME(@name)+N' IS NOT NULL'+
            CASE WHEN @scope='site' THEN N' AND Site_Code IS NOT NULL' ELSE N'' END+N';';
        EXEC sys.sp_executesql @sql;
    END;
    IF NOT EXISTS(SELECT 1 FROM webpoc.MasterRegistration WHERE Id=@id)
    BEGIN
        INSERT INTO webpoc.MasterRegistration(Id) VALUES(@id);
        -- Dynamic SQL avoids same-batch compilation of newly added metadata columns.
        EXEC sys.sp_executesql N'
            INSERT INTO webpoc.MasterDefinition(MasterId,Revision,Title,TableName,KeyColumn,NameColumn,
                MenuModule,MenuName,Handler,PublishedBy,Scope)
            VALUES(@id,1,@title,@table,@key,@name,''Master'',@menu,''single-field'',SUSER_SNAME(),@scope);
            INSERT INTO webpoc.MasterField(MasterId,Revision,ColumnName,Label,Position,Required,MaxLength)
            VALUES(@id,1,@name,@label,1,1,@length);',
            N'@id varchar(60),@title nvarchar(100),@table sysname,@key sysname,@name sysname,@menu varchar(100),@scope varchar(10),@label nvarchar(100),@length int',
            @id,@title,@table,@key,@name,@menu,@scope,@label,@length;
        UPDATE webpoc.MasterRegistration SET ActiveRevision=1 WHERE Id=@id;
    END;
    FETCH NEXT FROM masters INTO @id,@table,@key,@name,@title,@label,@length,@scope,@menu;
END;
CLOSE masters;
DEALLOCATE masters;
COMMIT;
