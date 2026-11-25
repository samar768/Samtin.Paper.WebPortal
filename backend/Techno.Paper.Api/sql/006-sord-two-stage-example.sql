-- Optional configuration example, NOT part of normal deployment.
-- Preview by default. Change @Apply to 1 only after reviewing SA/SUPER membership.
-- Existing requests retain their stage snapshots; this affects future submissions only.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET LOCK_TIMEOUT 10000;
DECLARE @Apply bit=0;
IF DB_NAME()<>'EcoTech_main' THROW 50000,'Use the approved development transaction database.',1;
IF @Apply=0
BEGIN
 SELECT 'PREVIEW ONLY: SA sales review, then SUPER finance review; no changes made.' AS Mode;
 SELECT f.Name,st.Step,st.Name AS Stage,g.Name AS RecipientGroup
 FROM webpoc.erpApprovalFlow f JOIN webpoc.erpApprovalFlowStage st ON st.FlowId=f.Id
 JOIN webpoc.erpTaskGroup g ON g.Code=st.GroupCode
 WHERE f.Company='26' AND f.Site='HO' AND f.VoucherType='SORD' ORDER BY st.Step;
 RETURN;
END;
BEGIN TRAN;
DECLARE @flow uniqueidentifier;
SELECT @flow=Id FROM webpoc.erpApprovalFlow WITH(XLOCK,HOLDLOCK)
 WHERE Company='26' AND Site='HO' AND VoucherType='SORD';
IF @flow IS NULL THROW 50000,'Deploy 005-tasks-and-approvals.sql first.',1;
IF (SELECT COUNT(*) FROM EcoTech_Comp.dbo.UserMast WHERE User_Name IN ('SA','SUPER'))<>2
 THROW 50000,'Both configured ERP users must exist.',1;
IF EXISTS(SELECT 1 FROM webpoc.erpTaskGroup WHERE Code IN ('SORD_REVIEW_26_HO','SORD_FINANCE_26_HO')
 AND (Company<>'26' OR Site<>'HO' OR Active<>1))
 THROW 50000,'An example group code conflicts with existing configuration.',1;
INSERT webpoc.erpTaskGroup(Code,Company,Site,Name)
SELECT v.Code,'26','HO',v.Name FROM (VALUES
 ('SORD_REVIEW_26_HO',N'Sales review'),('SORD_FINANCE_26_HO',N'Finance review')) v(Code,Name)
WHERE NOT EXISTS(SELECT 1 FROM webpoc.erpTaskGroup g WHERE g.Code=v.Code);
INSERT webpoc.erpTaskGroupMember(GroupCode,Username)
SELECT v.* FROM (VALUES ('SORD_REVIEW_26_HO','SA'),('SORD_FINANCE_26_HO','SUPER')) v(GroupCode,Username)
WHERE NOT EXISTS(SELECT 1 FROM webpoc.erpTaskGroupMember m WHERE m.GroupCode=v.GroupCode AND m.Username=v.Username);
INSERT webpoc.erpTaskTypeGroup(TypeCode,GroupCode,CanPublish,CanAct)
SELECT 'SORD_APPROVAL',Code,0,1 FROM webpoc.erpTaskGroup g
WHERE Code IN ('SORD_REVIEW_26_HO','SORD_FINANCE_26_HO') AND NOT EXISTS(
 SELECT 1 FROM webpoc.erpTaskTypeGroup p WHERE p.TypeCode='SORD_APPROVAL' AND p.GroupCode=g.Code);
UPDATE webpoc.erpApprovalFlow SET AllowSelf=1,AllowRepeat=0 WHERE Id=@flow;
DELETE webpoc.erpApprovalFlowStage WHERE FlowId=@flow;
INSERT webpoc.erpApprovalFlowStage(FlowId,Step,Name,GroupCode)
VALUES(@flow,1,N'Sales review','SORD_REVIEW_26_HO'),(@flow,2,N'Finance review','SORD_FINANCE_26_HO');
COMMIT;
SELECT @@TRANCOUNT AS OpenTransactions;
