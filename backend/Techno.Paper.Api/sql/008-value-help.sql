-- Explicit deployment to the approved EcoTech_main development database.
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
IF @lock<0 THROW 50000,'Could not acquire value-help deployment lock.',1;
IF SCHEMA_ID('webpoc') IS NULL THROW 50000,'Deploy 001-support.sql first.',1;

IF OBJECT_ID('webpoc.LookupDefinition') IS NULL
CREATE TABLE webpoc.LookupDefinition(
 Id varchar(60) NOT NULL PRIMARY KEY,
 Resource varchar(60) NOT NULL,
 Title nvarchar(100) NOT NULL,
 SourceView sysname NOT NULL,
 KeyColumn sysname NOT NULL,
 ScopeMode varchar(10) NOT NULL CHECK(ScopeMode IN ('list','code','none')),
 ScopeColumn sysname NULL,
 LabelSeparator nvarchar(16) NOT NULL DEFAULT N' - ',
 PageSize int NOT NULL CHECK(PageSize BETWEEN 10 AND 50),
 Enabled bit NOT NULL DEFAULT 1,
 CONSTRAINT CK_Lookup_Scope CHECK(
   (ScopeMode='none' AND ScopeColumn IS NULL) OR (ScopeMode IN ('list','code') AND ScopeColumn IS NOT NULL)));
IF OBJECT_ID('webpoc.LookupColumn') IS NULL
CREATE TABLE webpoc.LookupColumn(
 LookupId varchar(60) NOT NULL REFERENCES webpoc.LookupDefinition(Id),
 ColumnName sysname NOT NULL,
 Caption nvarchar(100) NOT NULL,
 Position int NOT NULL,
 Width int NOT NULL CHECK(Width BETWEEN 60 AND 400),
 Visible bit NOT NULL,
 Searchable bit NOT NULL,
 LabelOrder int NULL,
 PRIMARY KEY(LookupId,ColumnName),
 UNIQUE(LookupId,Position));
IF OBJECT_ID('webpoc.LookupField') IS NULL
CREATE TABLE webpoc.LookupField(
 Resource varchar(60) NOT NULL,
 Section varchar(20) NOT NULL,
 FieldKey sysname NOT NULL,
 LookupId varchar(60) NOT NULL REFERENCES webpoc.LookupDefinition(Id),
 PRIMARY KEY(Resource,Section,FieldKey));

EXEC sys.sp_executesql N'
CREATE OR ALTER VIEW webpoc.LookupPartySource AS
 SELECT s.SubCode AS Code,s.Name,s.ManualCode,c.CityName AS City,s.SiteList
 FROM dbo.SubGroup s LEFT JOIN dbo.City c ON c.CityCode=s.CityCode;';

-- Initial configuration is inserted once; reruns preserve reviewed configuration edits.
IF NOT EXISTS(SELECT 1 FROM webpoc.LookupDefinition WHERE Id='parties')
BEGIN
 INSERT webpoc.LookupDefinition(Id,Resource,Title,SourceView,KeyColumn,ScopeMode,ScopeColumn,LabelSeparator,PageSize)
 VALUES('parties','sales-orders',N'Select buyer','LookupPartySource','Code','list','SiteList',N' - ',25);
 INSERT webpoc.LookupColumn(LookupId,ColumnName,Caption,Position,Width,Visible,Searchable,LabelOrder)
 VALUES('parties','Name',N'Party name',1,280,1,1,1),
       ('parties','ManualCode',N'Business code',2,130,1,1,NULL),
       ('parties','City',N'City',3,140,1,1,NULL),
       ('parties','Code',N'ERP code',4,110,1,1,NULL);
 INSERT webpoc.LookupField(Resource,Section,FieldKey,LookupId)
 VALUES('sales-orders','header','PartyCode','parties');
END;
COMMIT;
SELECT @@TRANCOUNT AS OpenTransactions;
