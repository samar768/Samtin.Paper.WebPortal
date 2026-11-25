-- Explicit Buyer presentation update; deploy after 008-value-help.sql.
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
IF @lock<0 THROW 50000,'Could not acquire Buyer layout deployment lock.',1;
IF OBJECT_ID('webpoc.LookupColumn') IS NULL THROW 50000,'Deploy 008-value-help.sql first.',1;
IF NOT EXISTS(
 SELECT 1 FROM webpoc.LookupDefinition
 WHERE Id='parties' AND Resource='sales-orders' AND SourceView='LookupPartySource' AND KeyColumn='Code')
 THROW 50000,'Review the Buyer source configuration before applying the Windows layout.',1;

EXEC sys.sp_executesql N'
CREATE OR ALTER VIEW webpoc.LookupPartySource AS
 SELECT s.SubCode AS Code,s.Name,s.ManualCode,ISNULL(c.CityName,'''') AS City,s.SiteList,
        ISNULL(s.Add1,'''')+ISNULL(s.Add2,'''') AS Address,
        ISNULL(distributor.Name,'''') AS DName,ISNULL(distributorCity.CityName,'''') AS DISCity
 FROM dbo.SubGroup s
 LEFT JOIN dbo.City c ON c.CityCode=s.CityCode
 LEFT JOIN dbo.SubGroup distributor ON distributor.SubCode=s.Distributor
 LEFT JOIN dbo.City distributorCity ON distributorCity.CityCode=distributor.CityCode;';

-- Reapplying this reviewed migration deliberately restores the Windows Buyer layout.
DELETE webpoc.LookupColumn WHERE LookupId='parties';
INSERT webpoc.LookupColumn(LookupId,ColumnName,Caption,Position,Width,Visible,Searchable,LabelOrder)
VALUES('parties','Code',N'ERP code',0,60,0,0,NULL),
      ('parties','Name',N'Party Name',1,180,1,1,1),
      ('parties','ManualCode',N'Code',2,70,1,1,NULL),
      ('parties','Address',N'Address',3,250,1,1,NULL),
      ('parties','City',N'City',4,100,1,1,NULL),
      ('parties','DName',N'Distributor',5,200,1,1,NULL),
      ('parties','DISCity',N'D.City',6,100,1,1,NULL);
COMMIT;
SELECT @@TRANCOUNT AS OpenTransactions;
