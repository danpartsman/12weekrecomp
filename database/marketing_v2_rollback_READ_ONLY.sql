SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT '12WR Marketing v2 rollback helper - REVIEW BEFORE RUNNING';

IF OBJECT_ID(N'app.PromoCodes', N'U') IS NOT NULL
AND EXISTS
(
    SELECT 1
    FROM app.PromoCodes
    WHERE code = N'FREETRIAL7'
      AND redemption_count = 0
)
BEGIN
    PRINT 'FREETRIAL7 can be removed because it has never been redeemed.';
END
ELSE
BEGIN
    PRINT 'FREETRIAL7 is absent or has redemptions. Do not delete it automatically.';
END;

IF OBJECT_ID(N'app.MarketingExperimentEvents', N'U') IS NOT NULL
BEGIN
    SELECT COUNT(*) AS marketing_event_rows
    FROM app.MarketingExperimentEvents;
END;

PRINT 'This helper intentionally performs no destructive action.';
