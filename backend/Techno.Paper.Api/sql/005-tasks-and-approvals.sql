-- Deploy explicitly to the approved EcoTech_main development database, never on startup.
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
EXEC @lock=sys.sp_getapplock @Resource='webpoc-task-schema-005',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
IF @lock<0 THROW 50000,'Could not acquire task deployment lock.',1;
IF SCHEMA_ID('webpoc') IS NULL THROW 50000,'Deploy 001-support.sql first.',1;

IF OBJECT_ID('webpoc.erpTaskType') IS NULL
CREATE TABLE webpoc.erpTaskType(
 Code varchar(40) NOT NULL PRIMARY KEY, Name nvarchar(100) NOT NULL,
 Category varchar(20) NOT NULL CHECK(Category IN ('Memo','Note','Notification','Approval')),
 OpenMode varchar(20) NOT NULL CHECK(OpenMode IN ('Dialog','Transaction')),
 EntityType varchar(40) NULL, ManualPublish bit NOT NULL DEFAULT 0);
IF OBJECT_ID('webpoc.erpTaskGroup') IS NULL
CREATE TABLE webpoc.erpTaskGroup(
 Code varchar(40) NOT NULL PRIMARY KEY, Company varchar(2) NOT NULL, Site varchar(2) NOT NULL,
 Name nvarchar(100) NOT NULL, Active bit NOT NULL DEFAULT 1);
IF OBJECT_ID('webpoc.erpTaskGroupMember') IS NULL
CREATE TABLE webpoc.erpTaskGroupMember(
 GroupCode varchar(40) NOT NULL REFERENCES webpoc.erpTaskGroup(Code), Username varchar(10) NOT NULL,
 PRIMARY KEY(GroupCode,Username));
IF OBJECT_ID('webpoc.erpTaskTypeGroup') IS NULL
CREATE TABLE webpoc.erpTaskTypeGroup(
 TypeCode varchar(40) NOT NULL REFERENCES webpoc.erpTaskType(Code),
 GroupCode varchar(40) NOT NULL REFERENCES webpoc.erpTaskGroup(Code),
 CanPublish bit NOT NULL DEFAULT 0, CanAct bit NOT NULL DEFAULT 0, PRIMARY KEY(TypeCode,GroupCode));
IF OBJECT_ID('webpoc.erpApprovalFlow') IS NULL
CREATE TABLE webpoc.erpApprovalFlow(
 Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
 Company varchar(2) NOT NULL, Site varchar(2) NOT NULL, VoucherType varchar(5) NOT NULL,
 Name nvarchar(100) NOT NULL, Enabled bit NOT NULL DEFAULT 1,
 ApprovalType varchar(40) NOT NULL REFERENCES webpoc.erpTaskType(Code),
 CreatedType varchar(40) NOT NULL REFERENCES webpoc.erpTaskType(Code),
 ResultType varchar(40) NOT NULL REFERENCES webpoc.erpTaskType(Code),
 AllowSelf bit NOT NULL DEFAULT 0, AllowRepeat bit NOT NULL DEFAULT 0,
 UNIQUE(Company,Site,VoucherType));
IF OBJECT_ID('webpoc.erpApprovalFlowStage') IS NULL
CREATE TABLE webpoc.erpApprovalFlowStage(
 FlowId uniqueidentifier NOT NULL REFERENCES webpoc.erpApprovalFlow(Id),
 Step int NOT NULL CHECK(Step BETWEEN 1 AND 20), Name nvarchar(100) NOT NULL,
 GroupCode varchar(40) NOT NULL REFERENCES webpoc.erpTaskGroup(Code), PRIMARY KEY(FlowId,Step));
IF OBJECT_ID('webpoc.erpApprovalRequest') IS NULL
CREATE TABLE webpoc.erpApprovalRequest(
 Id uniqueidentifier NOT NULL PRIMARY KEY, Revision bigint IDENTITY NOT NULL UNIQUE,
 Company varchar(2) NOT NULL, Prefix varchar(5) NOT NULL,
 Site varchar(2) NOT NULL, VoucherType varchar(5) NOT NULL, DocId varchar(21) COLLATE Latin1_General_CI_AI NOT NULL,
 SourceVersion varchar(64) NOT NULL, FlowId uniqueidentifier NOT NULL REFERENCES webpoc.erpApprovalFlow(Id),
 ApprovalType varchar(40) NOT NULL REFERENCES webpoc.erpTaskType(Code),
 ResultType varchar(40) NOT NULL REFERENCES webpoc.erpTaskType(Code),
 CreatedBy varchar(10) NOT NULL, State varchar(16) NOT NULL CHECK(State IN ('Pending','Approved','Rejected','Cancelled')),
 AllowSelf bit NOT NULL, AllowRepeat bit NOT NULL, CreatedAt datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET(),
 Version rowversion NOT NULL);
IF OBJECT_ID('webpoc.erpApprovalRequestStage') IS NULL
CREATE TABLE webpoc.erpApprovalRequestStage(
 RequestId uniqueidentifier NOT NULL REFERENCES webpoc.erpApprovalRequest(Id),
 Step int NOT NULL, Name nvarchar(100) NOT NULL, GroupCode varchar(40) NOT NULL REFERENCES webpoc.erpTaskGroup(Code),
 State varchar(16) NOT NULL CHECK(State IN ('Waiting','Active','Approved','Rejected','Cancelled')),
 Actor varchar(10) NULL, Comment nvarchar(2000) NULL, ActedAt datetimeoffset NULL,
 PRIMARY KEY(RequestId,Step));
IF OBJECT_ID('webpoc.erpTaskInstance') IS NULL
CREATE TABLE webpoc.erpTaskInstance(
 Id uniqueidentifier NOT NULL PRIMARY KEY, Company varchar(2) NOT NULL, Prefix varchar(5) NOT NULL,
 Site varchar(2) NOT NULL, TypeCode varchar(40) NOT NULL REFERENCES webpoc.erpTaskType(Code),
 Title nvarchar(200) NOT NULL, Body nvarchar(2000) NOT NULL,
 State varchar(16) NOT NULL CHECK(State IN ('Open','Completed','Rejected','Cancelled')),
 SourceDocId varchar(21) COLLATE Latin1_General_CI_AI NULL,
 RequestId uniqueidentifier NULL REFERENCES webpoc.erpApprovalRequest(Id), Step int NULL,
 EventKey varchar(140) NOT NULL, CreatedBy varchar(10) NOT NULL,
 CreatedAt datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET(),
 UNIQUE(Company,Prefix,Site,EventKey));
IF OBJECT_ID('webpoc.erpTaskAudience') IS NULL
CREATE TABLE webpoc.erpTaskAudience(
 Id bigint IDENTITY NOT NULL PRIMARY KEY, TaskId uniqueidentifier NOT NULL REFERENCES webpoc.erpTaskInstance(Id),
 GroupCode varchar(40) NULL REFERENCES webpoc.erpTaskGroup(Code), Username varchar(10) NULL,
 CHECK((GroupCode IS NULL AND Username IS NOT NULL) OR (GroupCode IS NOT NULL AND Username IS NULL)));
IF OBJECT_ID('webpoc.erpTaskReceipt') IS NULL
CREATE TABLE webpoc.erpTaskReceipt(
 TaskId uniqueidentifier NOT NULL REFERENCES webpoc.erpTaskInstance(Id), Username varchar(10) NOT NULL,
 ReadAt datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET(), PRIMARY KEY(TaskId,Username));
IF OBJECT_ID('webpoc.erpTaskHistory') IS NULL
CREATE TABLE webpoc.erpTaskHistory(
 Id bigint IDENTITY NOT NULL PRIMARY KEY, TaskId uniqueidentifier NOT NULL REFERENCES webpoc.erpTaskInstance(Id),
 Actor varchar(10) NOT NULL, Action varchar(30) NOT NULL, Comment nvarchar(2000) NULL,
 CreatedAt datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET());
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('webpoc.erpTaskInstance') AND name='IX_Task_Inbox')
 CREATE INDEX IX_Task_Inbox ON webpoc.erpTaskInstance(Company,Prefix,Site,CreatedAt DESC,Id) INCLUDE(TypeCode,State);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('webpoc.erpTaskAudience') AND name='IX_Task_Audience')
 CREATE INDEX IX_Task_Audience ON webpoc.erpTaskAudience(TaskId) INCLUDE(GroupCode,Username);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('webpoc.erpApprovalRequest') AND name='IX_Approval_Document')
 CREATE INDEX IX_Approval_Document ON webpoc.erpApprovalRequest(Company,Prefix,Site,DocId,CreatedAt DESC);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('webpoc.erpApprovalRequest') AND name='UX_Approval_Pending')
 CREATE UNIQUE INDEX UX_Approval_Pending ON webpoc.erpApprovalRequest(Company,Prefix,Site,DocId) WHERE State='Pending';
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('webpoc.erpTaskGroupMember') AND name='IX_Task_Membership')
 CREATE INDEX IX_Task_Membership ON webpoc.erpTaskGroupMember(Username,GroupCode);

INSERT webpoc.erpTaskType(Code,Name,Category,OpenMode,EntityType,ManualPublish)
SELECT v.* FROM (VALUES
 ('MEMO',N'Memo','Memo','Dialog',NULL,1),('NOTE',N'Note','Note','Dialog',NULL,1),
 ('SORD_CREATED',N'Sales order created','Notification','Transaction','SalesOrder',0),
 ('SORD_APPROVAL',N'Sales order approval','Approval','Transaction','SalesOrder',0),
 ('SORD_RESULT',N'Sales order decision','Notification','Transaction','SalesOrder',0)
) v(Code,Name,Category,OpenMode,EntityType,ManualPublish)
WHERE NOT EXISTS(SELECT 1 FROM webpoc.erpTaskType t WHERE t.Code=v.Code);
IF NOT EXISTS(SELECT 1 FROM webpoc.erpTaskGroup WHERE Code='SALES_APPROVERS_26_HO')
 INSERT webpoc.erpTaskGroup(Code,Company,Site,Name) VALUES('SALES_APPROVERS_26_HO','26','HO',N'Sales order approvers');
INSERT webpoc.erpTaskGroupMember(GroupCode,Username)
SELECT 'SALES_APPROVERS_26_HO',u.User_Name FROM EcoTech_Comp.dbo.UserMast u
WHERE u.User_Name IN ('SA','SUPER') AND NOT EXISTS(SELECT 1 FROM webpoc.erpTaskGroupMember m
 WHERE m.GroupCode='SALES_APPROVERS_26_HO' AND m.Username=u.User_Name COLLATE DATABASE_DEFAULT);
INSERT webpoc.erpTaskTypeGroup(TypeCode,GroupCode,CanPublish,CanAct)
SELECT Code,'SALES_APPROVERS_26_HO',ManualPublish,CASE WHEN Category='Approval' THEN 1 ELSE 0 END
FROM webpoc.erpTaskType t WHERE NOT EXISTS(SELECT 1 FROM webpoc.erpTaskTypeGroup g
 WHERE g.TypeCode=t.Code AND g.GroupCode='SALES_APPROVERS_26_HO');
IF NOT EXISTS(SELECT 1 FROM webpoc.erpApprovalFlow WHERE Company='26' AND Site='HO' AND VoucherType='SORD')
BEGIN
 DECLARE @flow uniqueidentifier=NEWID();
 INSERT webpoc.erpApprovalFlow(Id,Company,Site,VoucherType,Name,AllowSelf,AllowRepeat,ApprovalType,CreatedType,ResultType)
 VALUES(@flow,'26','HO','SORD',N'Sales order approval (POC)',1,1,'SORD_APPROVAL','SORD_CREATED','SORD_RESULT');
 INSERT webpoc.erpApprovalFlowStage(FlowId,Step,Name,GroupCode)
 VALUES(@flow,1,N'Sales approval','SALES_APPROVERS_26_HO');
END;
COMMIT;
SELECT @@TRANCOUNT AS OpenTransactions;
