-- Data exposure boundary only. Terms and lookup definitions remain in the ERP tables.
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 5000;
BEGIN TRANSACTION;

DECLARE @result int;
EXEC @result = sys.sp_getapplock @Resource = 'webpoc:016-voucher-term-lookup-permissions',
    @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000;
IF @result < 0 THROW 50016, 'Could not acquire the term lookup permissions deployment lock.', 1;

IF DATABASE_PRINCIPAL_ID(N'webpoc-term-sales-orders') IS NULL
    CREATE ROLE [webpoc-term-sales-orders] AUTHORIZATION dbo;

GRANT SELECT ON OBJECT::dbo.ItemMast (Code, Name) TO [webpoc-term-sales-orders];

COMMIT TRANSACTION;
