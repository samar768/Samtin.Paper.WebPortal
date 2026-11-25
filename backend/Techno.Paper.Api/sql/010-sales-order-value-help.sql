-- Upgrade the 11 existing Sales Order lookup fields. Deploy after 009.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 10000;
BEGIN TRAN;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource='webpoc-value-help-008',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
IF @lock<0 THROW 50000,'Could not acquire Sales lookup deployment lock.',1;
IF OBJECT_ID('webpoc.LookupColumn') IS NULL OR NOT EXISTS(
 SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('webpoc.LookupPartySource') AND name='DISCity')
 THROW 50000,'Deploy 008 and 009 before the Sales lookup rollout.',1;

-- Windows item help uses a 40px Grain column. Only the support-table width range changes.
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('webpoc.LookupColumn')
 AND name='CK_Lookup_ColumnWidth')
BEGIN
 DECLARE @widthConstraint sysname;
 SELECT @widthConstraint=name FROM sys.check_constraints
 WHERE parent_object_id=OBJECT_ID('webpoc.LookupColumn')
 AND parent_column_id=COLUMNPROPERTY(OBJECT_ID('webpoc.LookupColumn'),'Width','ColumnId');
 IF @widthConstraint IS NULL THROW 50000,'Review the existing lookup width constraint.',1;
 DECLARE @ddl nvarchar(max)=N'ALTER TABLE webpoc.LookupColumn DROP CONSTRAINT '+QUOTENAME(@widthConstraint);
 EXEC sys.sp_executesql @ddl;
 ALTER TABLE webpoc.LookupColumn ADD CONSTRAINT CK_Lookup_ColumnWidth CHECK(Width BETWEEN 40 AND 400);
END;

EXEC sys.sp_executesql N'
CREATE OR ALTER VIEW webpoc.LookupSalesItemSource AS
 SELECT i.Code,i.Name,i.ManualCode,i.SKU,v.Name AS Varity,g.Name AS GSM,
        i.SizeLength,i.SizeWidth,b.Name AS BFName,i.Grain,i.SiteList
 FROM dbo.ItemMast i
 LEFT JOIN dbo.VarityMast v ON v.Code=i.Varity
 LEFT JOIN dbo.GSMMast g ON g.Code=i.GSM
 LEFT JOIN dbo.BFMast b ON b.Code=i.BFCode
 WHERE i.ItemType=''FM'';';
EXEC sys.sp_executesql N'
CREATE OR ALTER VIEW webpoc.LookupSalesFormSource AS
 SELECT Form_Code AS Code,Form_Desc AS Name,Tax_per AS TaxPer FROM dbo.TaxForms;';
EXEC sys.sp_executesql N'
CREATE OR ALTER VIEW webpoc.LookupSalesBfSource AS SELECT Code,Name FROM dbo.BFMast;';
EXEC sys.sp_executesql N'
CREATE OR ALTER VIEW webpoc.LookupSalesGsmSource AS SELECT Code,Name FROM dbo.GSMMast;';
EXEC sys.sp_executesql N'
CREATE OR ALTER VIEW webpoc.LookupSalesOrderTypeSource AS
 SELECT CAST(''R'' AS varchar(1)) AS Code,CAST(''Paper'' AS varchar(5)) AS Name
 UNION ALL SELECT ''S'',''Sheet'' UNION ALL SELECT ''O'',''Other'';';
EXEC sys.sp_executesql N'
CREATE OR ALTER VIEW webpoc.LookupSalesYesNoSource AS
 SELECT CAST(''Y'' AS varchar(1)) AS Code,CAST(''Yes'' AS varchar(3)) AS Name
 UNION ALL SELECT ''N'',''No'';';
EXEC sys.sp_executesql N'
CREATE OR ALTER VIEW webpoc.LookupSalesSizeUnitSource AS
 SELECT CAST(''CM'' AS varchar(4)) AS Code,CAST(''CM'' AS varchar(4)) AS Name
 UNION ALL SELECT ''MM'',''MM'' UNION ALL SELECT ''INCH'',''Inch'';';
EXEC sys.sp_executesql N'
CREATE OR ALTER VIEW webpoc.LookupSalesGrainSource AS
 SELECT CAST(''L'' AS varchar(1)) AS Code,CAST(''Long'' AS varchar(5)) AS Name
 UNION ALL SELECT ''S'',''Short'';';

DECLARE @definitions TABLE(Id varchar(60),Title nvarchar(100),SourceView sysname,ScopeMode varchar(10),ScopeColumn sysname NULL);
INSERT @definitions VALUES
 ('consignees',N'Select consignee','LookupPartySource','list','SiteList'),
 ('delivery-parties',N'Select delivery party','LookupPartySource','list','SiteList'),
 ('items',N'Select finished product','LookupSalesItemSource','list','SiteList'),
 ('forms',N'Select against form','LookupSalesFormSource','none',NULL),
 ('bf',N'Select BF','LookupSalesBfSource','none',NULL),
 ('gsm',N'Select GSM','LookupSalesGsmSource','none',NULL),
 ('order-types',N'Select order type','LookupSalesOrderTypeSource','none',NULL),
 ('yes-no',N'Select excisable','LookupSalesYesNoSource','none',NULL),
 ('size-units',N'Select size unit','LookupSalesSizeUnitSource','none',NULL),
 ('grain',N'Select grain','LookupSalesGrainSource','none',NULL);
DECLARE @added TABLE(Id varchar(60) PRIMARY KEY);
INSERT webpoc.LookupDefinition(Id,Resource,Title,SourceView,KeyColumn,ScopeMode,ScopeColumn,PageSize)
OUTPUT inserted.Id INTO @added
SELECT d.Id,'sales-orders',d.Title,d.SourceView,'Code',d.ScopeMode,d.ScopeColumn,25
FROM @definitions d WHERE NOT EXISTS(SELECT 1 FROM webpoc.LookupDefinition WHERE Id=d.Id);

-- New party definitions inherit the already reviewed Buyer presentation.
INSERT webpoc.LookupColumn(LookupId,ColumnName,Caption,Position,Width,Visible,Searchable,LabelOrder)
SELECT a.Id,c.ColumnName,c.Caption,c.Position,c.Width,c.Visible,c.Searchable,c.LabelOrder
FROM @added a JOIN @definitions d ON d.Id=a.Id AND d.SourceView='LookupPartySource'
CROSS JOIN webpoc.LookupColumn c WHERE c.LookupId='parties';

DECLARE @columns TABLE(LookupId varchar(60),ColumnName sysname,Caption nvarchar(100),Position int,Width int,LabelOrder int NULL);
INSERT @columns VALUES
 ('items','Name',N'Name',1,200,1),('items','ManualCode',N'Code',2,175,NULL),
 ('items','SKU',N'SKU',3,80,NULL),('items','Varity',N'Varity',4,140,NULL),
 ('items','GSM',N'GSM',5,100,NULL),('items','SizeLength',N'Length',6,100,NULL),
 ('items','SizeWidth',N'Width',7,100,NULL),('items','BFName',N'BFName',8,60,NULL),
 ('items','Grain',N'Grain',9,40,NULL),
 ('forms','Name',N'Name',1,200,1),('forms','TaxPer',N'Per',2,100,NULL),
 ('bf','Name',N'Name',1,100,1),('gsm','Name',N'Name',1,100,1),
 ('order-types','Name',N'Name',1,100,1),('yes-no','Name',N'Name',1,100,1),
 ('size-units','Name',N'Unit',1,100,1),('grain','Name',N'Grain',1,100,1);
INSERT webpoc.LookupColumn(LookupId,ColumnName,Caption,Position,Width,Visible,Searchable,LabelOrder)
SELECT c.LookupId,c.ColumnName,c.Caption,c.Position,c.Width,1,1,c.LabelOrder
FROM @columns c JOIN @added a ON a.Id=c.LookupId;
INSERT webpoc.LookupColumn(LookupId,ColumnName,Caption,Position,Width,Visible,Searchable,LabelOrder)
SELECT a.Id,'Code',N'ERP code',0,60,0,1,NULL FROM @added a
JOIN @definitions d ON d.Id=a.Id AND d.SourceView<>'LookupPartySource';

DECLARE @bindings TABLE(Section varchar(20),FieldKey sysname,LookupId varchar(60));
INSERT @bindings VALUES
 ('header','PartyCode','parties'),('header','Consignee','consignees'),('header','DelAt','delivery-parties'),
 ('header','FormCode','forms'),('header','OrderType','order-types'),('header','Exciseable','yes-no'),
 ('lines','ItemCode','items'),('lines','BF','bf'),('lines','GSM','gsm'),('lines','SizeUnit','size-units'),('lines','Grain','grain');
IF EXISTS(SELECT 1 FROM @bindings b JOIN webpoc.LookupField f ON f.Resource='sales-orders'
 AND f.Section=b.Section AND f.FieldKey=b.FieldKey WHERE f.LookupId<>b.LookupId)
 THROW 50000,'Review existing Sales field bindings before replacing a customized lookup.',1;
INSERT webpoc.LookupField(Resource,Section,FieldKey,LookupId)
SELECT 'sales-orders',b.Section,b.FieldKey,b.LookupId FROM @bindings b
WHERE NOT EXISTS(SELECT 1 FROM webpoc.LookupField f WHERE f.Resource='sales-orders' AND f.Section=b.Section AND f.FieldKey=b.FieldKey);
COMMIT;
SELECT @@TRANCOUNT AS OpenTransactions;
