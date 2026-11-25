-- Master.FrmEnviro: reviewed, site-specific settings. No existing values are changed.
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
EXEC @lock=sys.sp_getapplock @Resource='webpoc-simple-masters-011',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
IF @lock<0 THROW 50000,'Could not acquire settings deployment lock.',1;
IF DB_NAME()<>'EcoTech_main' OR COL_LENGTH('webpoc.MasterField','MaxLength') IS NULL
    THROW 50000,'Deploy 013 in the approved development database first.',1;
IF EXISTS(SELECT Site_Code FROM dbo.Enviro WHERE Site_Code IS NOT NULL GROUP BY Site_Code HAVING COUNT(*)>1)
    THROW 50000,'Duplicate environment sites require owner review; no data was repaired.',1;
IF COL_LENGTH('dbo.Enviro','WebPocVersion') IS NULL ALTER TABLE dbo.Enviro ADD WebPocVersion rowversion;
IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Enviro') AND name='WebPocVersion' AND system_type_id=189)
    THROW 50000,'Environment concurrency column is incompatible.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Enviro') AND name='UX_webpoc_Enviro_Site')
    CREATE UNIQUE INDEX UX_webpoc_Enviro_Site ON dbo.Enviro(Site_Code) WHERE Site_Code IS NOT NULL;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_MasterDefinition_Handler')
    ALTER TABLE webpoc.MasterDefinition DROP CONSTRAINT CK_MasterDefinition_Handler;
ALTER TABLE webpoc.MasterDefinition ADD CONSTRAINT CK_MasterDefinition_Handler CHECK(Handler IN('single-field','environment'));

IF OBJECT_ID('webpoc.EnvironmentFieldLayout','U') IS NULL
CREATE TABLE webpoc.EnvironmentFieldLayout(
 MasterId varchar(60) NOT NULL,Revision int NOT NULL,ColumnName sysname NOT NULL,
 ControlName varchar(100) NOT NULL,TabName varchar(20) NOT NULL,GroupName nvarchar(100) NOT NULL,
 LookupId varchar(60) NULL,ChoiceSet varchar(30) NULL,Multiple bit NOT NULL,
 IntegerDigits int NULL,FractionDigits int NULL,Maximum int NULL,
 PRIMARY KEY(MasterId,Revision,ColumnName),
 FOREIGN KEY(MasterId,Revision,ColumnName) REFERENCES webpoc.MasterField(MasterId,Revision,ColumnName),
 FOREIGN KEY(LookupId) REFERENCES webpoc.LookupDefinition(Id),
 CHECK(TabName IN('Gate','Sales','Excise','Purchase','CostSheet','Stores')),
 CHECK(LookupId IS NULL OR ChoiceSet IS NULL),
 CHECK(Multiple=0 OR LookupId IS NOT NULL));
IF OBJECT_ID('webpoc.EnvironmentChoice','U') IS NULL
CREATE TABLE webpoc.EnvironmentChoice(
 ChoiceSet varchar(30) NOT NULL,Code varchar(100) NOT NULL,Label nvarchar(100) NOT NULL,
 Position int NOT NULL,Selectable bit NOT NULL DEFAULT 1,
 PRIMARY KEY(ChoiceSet,Code),UNIQUE(ChoiceSet,Position));

DECLARE @lookups TABLE(Id varchar(60),Title nvarchar(100),ViewName sysname,Query nvarchar(max),Caption nvarchar(100),Width int);
INSERT @lookups VALUES
('env-city','Select billing site','EnvironmentCitySource',N'SELECT CityCode AS Code,CityName AS Name FROM dbo.City','Name',200),
('env-unit','Select weight unit','EnvironmentUnitSource',N'SELECT Unit AS Code,Unit AS Name FROM dbo.UnitMast','Name',60),
('env-accounts','Select account','EnvironmentAccountSource',N'SELECT SubCode AS Code,Name,ManualCode FROM dbo.SubGroup','Name',200),
('env-groups','Select account groups','EnvironmentGroupSource',N'SELECT GroupCode AS Code,GroupName AS Name FROM dbo.AcGroup','GroupName',200),
('env-designations','Select designations','EnvironmentDesignationSource',N'SELECT Code,Name FROM dbo.DesignationMast','Designation Name',300),
('env-open-rate-vouchers','Select open-rate vouchers','EnvironmentVoucherSource',N'SELECT V_Type AS Code,Description AS Name FROM dbo.Voucher_Type WHERE Category IN (''PORD'',''PORDR'')','Voucher Description',300),
('env-rate-categories','Select last-rate categories','EnvironmentCategorySource',N'SELECT DISTINCT Category AS Code,Category AS Name FROM dbo.Voucher_Type WHERE Category IS NOT NULL','Category Name',300);
DECLARE @lookup varchar(60),@title nvarchar(100),@view sysname,@query nvarchar(max),@caption nvarchar(100),@width int;
DECLARE lookup_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT * FROM @lookups;
OPEN lookup_cursor; FETCH NEXT FROM lookup_cursor INTO @lookup,@title,@view,@query,@caption,@width;
WHILE @@FETCH_STATUS=0
BEGIN
    SET @query=N'CREATE OR ALTER VIEW webpoc.'+QUOTENAME(@view)+N' AS '+@query;
    EXEC sys.sp_executesql @query;
    IF NOT EXISTS(SELECT 1 FROM webpoc.LookupDefinition WHERE Id=@lookup)
    BEGIN
        INSERT webpoc.LookupDefinition(Id,Resource,Title,SourceView,KeyColumn,ScopeMode,ScopeColumn,LabelSeparator,PageSize)
        VALUES(@lookup,'environment-settings',@title,@view,'Code','none',NULL,'',25);
        INSERT webpoc.LookupColumn(LookupId,ColumnName,Caption,Position,Width,Visible,Searchable,LabelOrder)
        VALUES(@lookup,'Name',@caption,1,@width,1,1,1),(@lookup,'Code','ERP code',3,60,0,0,NULL);
        IF @lookup='env-accounts'
            INSERT webpoc.LookupColumn(LookupId,ColumnName,Caption,Position,Width,Visible,Searchable,LabelOrder)
            VALUES(@lookup,'ManualCode','Code',2,100,1,1,NULL);
    END;
    FETCH NEXT FROM lookup_cursor INTO @lookup,@title,@view,@query,@caption,@width;
END;
CLOSE lookup_cursor; DEALLOCATE lookup_cursor;

IF NOT EXISTS(SELECT 1 FROM webpoc.EnvironmentChoice)
INSERT webpoc.EnvironmentChoice(ChoiceSet,Code,Label,Position,Selectable) VALUES
('yes-no','Y','Yes',1,1),('yes-no','N','No',2,1),
('auto-manual','A','Auto',1,1),('auto-manual','M','Manual',2,1),
('expression','*','*',1,1),('expression','/','/',2,1),
('credit-for','P','Party',1,1),('credit-for','D','Distributor',2,1),
('price-for','P','Party',1,1),('price-for','D','Distributor',2,1),('price-for','B','Both',3,1),
('limit-check','S','Stop',1,1),('limit-check','W','Warning Only',2,1),('limit-check','N','None',3,1),
('temporary-credit','D','Day Wise',1,1),('temporary-credit','B','Bill Wise',2,1),
('email-criteria','A','Auto',1,1),('email-criteria','M','Manual',2,1),('email-criteria','W','Warning (existing value)',3,0),
('impurity','([Found %] - [Accept %]) * 0.01','([Found %] - [Accept %]) * 0.01',1,1),
('impurity','1 - ((100.0 - [Found %]) / (100.0 - [Accept %]))','1 - ((100.0 - [Found %]) / (100.0 - [Accept %]))',2,1);

DECLARE @fields TABLE(ColumnName sysname,Label nvarchar(100),ControlName varchar(100),TabName varchar(20),GroupName nvarchar(100),
 Required bit,LookupId varchar(60),ChoiceSet varchar(30),Multiple bit,Digits int,Decimals int,Maximum int,Position int IDENTITY);
INSERT @fields VALUES
('WGTEXEPath','EXE Path','TXTWgtexepath','Gate','Weightment',1,NULL,NULL,0,NULL,NULL,NULL),
('WGTFilePath','Data File Path','TXTWgtFilepath','Gate','Weightment',1,NULL,NULL,0,NULL,NULL,NULL),
('WeightConvRate','Weight Conv. Rate','TxtWeightConv','Gate','Weightment',1,NULL,NULL,0,10,0,NULL),
('WeightExpression','Expression','TxtExpression','Gate','Weightment',1,NULL,'expression',0,NULL,NULL,NULL),
('WeightUnit','Unit','TxtUnit','Gate','Weightment',1,'env-unit',NULL,0,NULL,NULL,NULL),
('MDIBGFilePath','BG Image Path','TxtBGImage','Gate','Appearance',0,NULL,NULL,0,NULL,NULL,NULL),
('NegativeStk','Allow Negative Stock','TxtNegativeStk','Gate','Entry rules',1,NULL,'yes-no',0,NULL,NULL,NULL),
('GT_Party_Needed','Party Needed (Gate Entry)','TxtPartyNeeded','Gate','Entry rules',1,NULL,'yes-no',0,NULL,NULL,NULL),
('InchKG','Reel Calculation Formula 1 Inch = (KG.)','TxtInchKG','Sales','Reel calculation',1,NULL,NULL,0,NULL,NULL,NULL),
('CMKG','Reel Calculation Formula 1 CM. = (KG.)','TxtCMKG','Sales','Reel calculation',1,NULL,NULL,0,NULL,NULL,NULL),
('SINVNotificationNo','Sale Invoice Notification No.','TxtSINVNotiNo','Sales','Invoice',0,NULL,NULL,0,NULL,NULL,NULL),
('MaintainReelNo','Maintain Reel No. (Sale Invoice)','TxtMaintainReelNo','Sales','Invoice',1,NULL,'yes-no',0,NULL,NULL,NULL),
('BillingSite','Billing Site','TxtBillingSite','Sales','Invoice',1,'env-city',NULL,0,NULL,NULL,NULL),
('SheetConversion','Sheet Conversion','TxtSheetConv','Sales','Reel calculation',1,NULL,NULL,0,10,2,NULL),
('RimWTRoff','Round Off Rim Wt To (Digits)','Txtroffrimwt','Sales','Reel calculation',0,NULL,NULL,0,1,0,9),
('ReelNoGenerate','Generate Reel No.','TxtReelNoGen','Sales','Invoice',1,NULL,'auto-manual',0,NULL,NULL,NULL),
('PriceApplicableYN','Price List Applicable','TxtPriceApplicableYN','Sales','Pricing',1,NULL,'yes-no',0,NULL,NULL,NULL),
('SINVRateEditable','Rate Editable (Sale Invoice)','TxtRateEditableYN','Sales','Pricing',1,NULL,'yes-no',0,NULL,NULL,NULL),
('SL_OrdPricePick','Price List Affected (Auto) For','TxtPriceListAffectFor','Sales','Pricing',0,NULL,'price-for',0,NULL,NULL,NULL),
('EmailApplicable','E-Mail Applicable','TxtEmailApplicable','Sales','Communication',1,NULL,'yes-no',0,NULL,NULL,NULL),
('SMSApplicable','SMS Applicable','TxtSMSApplicable','Sales','Communication',1,NULL,'yes-no',0,NULL,NULL,NULL),
('BookNoYN','Book / Serial No. (Sale Invoice)','TxtBookNo','Sales','Invoice',1,NULL,'yes-no',0,NULL,NULL,NULL),
('Sl_Email_Criteria','Email Sending Option (Invoice)','TxtMailCriteria','Sales','Communication',0,NULL,'email-criteria',0,NULL,NULL,NULL),
('SL_Credit_CheckFor','Credit Limit Check','TxtPartyOrDist','Sales','Credit and policy',1,NULL,'credit-for',0,NULL,NULL,NULL),
('SL_Credit_Check','Credit Limit Check: Warning Or Stop','TxtWarnOrStop','Sales','Credit and policy',1,NULL,'limit-check',0,NULL,NULL,NULL),
('SL_Tempo_CrLimit_Type','Tempo.Credit Limit Type','TxtTempoCreditLimiyType','Sales','Credit and policy',0,NULL,'temporary-credit',0,NULL,NULL,NULL),
('SL_Policy_Check','Policy Limit Check','TxtPolicyCheck','Sales','Credit and policy',1,NULL,'limit-check',0,NULL,NULL,NULL),
('SL_ReelInvoiceAgainstFresh','Reel Invoice Stopped Against Fresh','TxtRellInvoiceAgainstFresh','Sales','Invoice',1,NULL,'yes-no',0,NULL,NULL,NULL),
('SL_DesigForChemist','Designation For Chemist','TxtDesigForChemist','Sales','Production designations',0,'env-designations',NULL,1,NULL,NULL,NULL),
('SL_DesigForIncharge','Designation For Shift Incharge','TxtDesigForShifIncharge','Sales','Production designations',0,'env-designations',NULL,1,NULL,NULL,NULL),
('SL_DesigForOperator','Designation For Operator','TxtDesigForOperator','Sales','Production designations',0,'env-designations',NULL,1,NULL,NULL,NULL),
('Sl_Invoice_Kanta_Diff_Excess','Allow Invoice & Kanta Wt. Difference + (KG.)','TxtAllowInvoice_KantaWtExcess','Sales','Weight tolerance',0,NULL,NULL,0,10,0,NULL),
('Sl_Invoice_Kanta_Diff_Shortage','Allow Invoice & Kanta Wt. Difference - (KG.)','TxtAllowInvoice_KantaWtShortage','Sales','Weight tolerance',0,NULL,NULL,0,10,0,NULL),
('Sl_Order_Weight_round_to_places','Order WT. Round Off Upto','TxtOrderWtRoundupto','Sales','Reel calculation',0,NULL,NULL,0,4,0,6),
('ExcisePaybleAc','Excise Account','TxtExciseAccount','Excise','Account(s) For Second Half Of Capital',1,'env-accounts',NULL,0,NULL,NULL,NULL),
('CessPaybleAc','Cess Account','TxtCessAccount','Excise','Account(s) For Second Half Of Capital',1,'env-accounts',NULL,0,NULL,NULL,NULL),
('ECessPaybleAc','EduCess Account','TxtEcessAccount','Excise','Account(s) For Second Half Of Capital',1,'env-accounts',NULL,0,NULL,NULL,NULL),
('SHCessPaybleAc','SHCess Account','TxtSHCessAccount','Excise','Account(s) For Second Half Of Capital',1,'env-accounts',NULL,0,NULL,NULL,NULL),
('InputExciseAc','Excise Account','TxtIExciseAc','Excise','Excise Posting For Input',1,'env-accounts',NULL,0,NULL,NULL,NULL),
('InputCessAc','Cess Account','TxtICessAc','Excise','Excise Posting For Input',1,'env-accounts',NULL,0,NULL,NULL,NULL),
('InputEduCessAc','EduCess Account','TxtIEducessAc','Excise','Excise Posting For Input',1,'env-accounts',NULL,0,NULL,NULL,NULL),
('InputSHCessAc','SHCess Account','TxtIShcess','Excise','Excise Posting For Input',1,'env-accounts',NULL,0,NULL,NULL,NULL),
('CapitalExciseAc','Excise Account','TxtCaExciseAc','Excise','Excise Posting For Capital',1,'env-accounts',NULL,0,NULL,NULL,NULL),
('CapitalCessAc','Cess Account','TxtCaCessAc','Excise','Excise Posting For Capital',1,'env-accounts',NULL,0,NULL,NULL,NULL),
('CapitalEduCessAc','EduCess Account','TxtCaEduCessAc','Excise','Excise Posting For Capital',1,'env-accounts',NULL,0,NULL,NULL,NULL),
('CapitalSHCessAc','SHCess Account','TxtCaSHCessAc','Excise','Excise Posting For Capital',1,'env-accounts',NULL,0,NULL,NULL,NULL),
('PORateEditable','PO Rate Editable','TxtPOrateEditable','Purchase','Purchase rules',1,NULL,'yes-no',0,NULL,NULL,NULL),
('GateMandatory','Gate Entry Mandatory (GRN)','TxtGateMandatory','Purchase','Purchase rules',1,NULL,'yes-no',0,NULL,NULL,NULL),
('PBillApproveYN','Purchase Bill Approved (Auto)','TxtPBillApproved','Purchase','Purchase rules',1,NULL,'yes-no',0,NULL,NULL,NULL),
('UnitEditable','Item Master Unit Editable','TxtItemUnit','Purchase','Purchase rules',1,NULL,'yes-no',0,NULL,NULL,NULL),
('IndentYN','Indent Mandatory (PO)','TxtIndentYN','Purchase','Purchase rules',1,NULL,'yes-no',0,NULL,NULL,NULL),
('PBR_Transporter_Posting','Transporter Posting (PB Raw)','TxtTransporterPostingPBRaw','Purchase','PB Raw',1,NULL,'yes-no',0,NULL,NULL,NULL),
('PBR_Unloader_Posting','Unloader Posting (PB Raw)','TxtUnloaderPostingPBRaw','Purchase','PB Raw',1,NULL,'yes-no',0,NULL,NULL,NULL),
('PBR_Impurity_Formula','Impurity Formula (PB Raw)','TxtImpurityFormula','Purchase','PB Raw',1,NULL,'impurity',0,NULL,NULL,NULL),
('PCForm38_Needed','Form 38 Mandatory in GRN','TxtForm38ManadatoryInGRN','Purchase','Purchase rules',1,NULL,'yes-no',0,NULL,NULL,NULL),
('PR_V_TypeForOpenRate','V_Type For Open Rate (PBRaw)','TxtV_TypeForopenrate','Purchase','Rate updates',0,'env-open-rate-vouchers',NULL,1,NULL,NULL,NULL),
('PU_V_CatForLRateUpdation','Voucher For LRate Updation','TxtLRateUpdation','Purchase','Rate updates',0,'env-rate-categories',NULL,1,NULL,NULL,NULL),
('SalaryAc','Salary Group Account','TxtSalaryAc','CostSheet','Account(s) For Costing Sheet',1,'env-groups',NULL,1,NULL,NULL,NULL),
('FreightAc','Freight Group Account','TxtFreightAc','CostSheet','Account(s) For Costing Sheet',1,'env-groups',NULL,1,NULL,NULL,NULL),
('InterestAc','Interest Account','TxtInterestAc','CostSheet','Account(s) For Costing Sheet',1,'env-groups',NULL,1,NULL,NULL,NULL),
('TaxAc','Tax Account','TxtTaxAc','CostSheet','Account(s) For Costing Sheet',1,'env-groups',NULL,1,NULL,NULL,NULL),
('ST_ISU_DeptMandatory','Department Mandatory (Stock Issue)','TxtST_ISU_DeptMandatory','Stores','Stock issue',1,NULL,'yes-no',0,NULL,NULL,NULL);
IF (SELECT COUNT(*) FROM @fields)<>62 THROW 50000,'Environment field census mismatch.',1;
IF EXISTS(SELECT 1 FROM @fields f LEFT JOIN sys.columns c ON c.object_id=OBJECT_ID('dbo.Enviro') AND c.name=f.ColumnName
          WHERE c.column_id IS NULL OR c.is_identity=1 OR c.is_computed=1 OR c.system_type_id NOT IN(167,62,48))
    THROW 50000,'Environment field schema requires review.',1;
IF NOT EXISTS(SELECT 1 FROM webpoc.MasterRegistration WHERE Id='environment')
BEGIN
    INSERT webpoc.MasterRegistration(Id) VALUES('environment');
    INSERT webpoc.MasterDefinition(MasterId,Revision,Title,TableName,KeyColumn,NameColumn,MenuModule,MenuName,Handler,PublishedBy,Scope)
    VALUES('environment',1,'Environment Setting','Enviro','ID','Site_Code','Master','MnuEnvironmentSetting','environment',SUSER_SNAME(),'site');
    INSERT webpoc.MasterField(MasterId,Revision,ColumnName,Label,Position,Required,MaxLength)
        SELECT 'environment',1,f.ColumnName,f.Label,f.Position,f.Required,
            CASE WHEN c.system_type_id=167 THEN CASE WHEN c.max_length=-1 THEN 4000 ELSE c.max_length END END
        FROM @fields f JOIN sys.columns c ON c.object_id=OBJECT_ID('dbo.Enviro') AND c.name=f.ColumnName;
    INSERT webpoc.EnvironmentFieldLayout(MasterId,Revision,ColumnName,ControlName,TabName,GroupName,LookupId,ChoiceSet,Multiple,IntegerDigits,FractionDigits,Maximum)
        SELECT 'environment',1,ColumnName,ControlName,TabName,GroupName,LookupId,ChoiceSet,Multiple,Digits,Decimals,Maximum FROM @fields;
    UPDATE webpoc.MasterRegistration SET ActiveRevision=1 WHERE Id='environment';
END;
COMMIT;
SELECT @@TRANCOUNT OpenTransactions;
