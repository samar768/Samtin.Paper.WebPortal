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
EXEC @lock=sys.sp_getapplock @Resource='webpoc-task-schema-007',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
IF @lock<0 THROW 50000,'Could not acquire reversal deployment lock.',1;
IF OBJECT_ID('webpoc.erpTaskTypeGroup') IS NULL THROW 50000,'Deploy 005-tasks-and-approvals.sql first.',1;
IF COL_LENGTH('webpoc.erpTaskTypeGroup','CanReverse') IS NULL
BEGIN
 ALTER TABLE webpoc.erpTaskTypeGroup ADD CanReverse bit NOT NULL CONSTRAINT DF_TaskTypeGroup_CanReverse DEFAULT 0;
 -- Seed only when introducing this permission; reruns must not restore revoked grants.
 EXEC sys.sp_executesql N'UPDATE webpoc.erpTaskTypeGroup SET CanReverse=1 WHERE TypeCode=''SORD_APPROVAL'' AND GroupCode=''SALES_APPROVERS_26_HO'';';
END;
IF OBJECT_ID('webpoc.erpApprovalReversal') IS NULL
CREATE TABLE webpoc.erpApprovalReversal(
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 Company varchar(2) NOT NULL, Prefix varchar(5) NOT NULL, Site varchar(2) NOT NULL,
 VoucherType varchar(5) NOT NULL, DocId varchar(21) COLLATE Latin1_General_CI_AI NOT NULL,
 RequestId uniqueidentifier NULL REFERENCES webpoc.erpApprovalRequest(Id),
 SourceVersion varchar(64) NOT NULL, PreviousApprovedBy varchar(10) NOT NULL,
 Actor varchar(10) NOT NULL, Reason nvarchar(2000) NOT NULL,
 CreatedAt datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET());
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('webpoc.erpApprovalReversal') AND name='IX_Reversal_Document')
 CREATE INDEX IX_Reversal_Document ON webpoc.erpApprovalReversal(Company,Prefix,Site,DocId,CreatedAt DESC);
COMMIT;
SELECT @@TRANCOUNT AS OpenTransactions;
