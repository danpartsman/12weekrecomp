SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @ConfigId uniqueidentifier;
DECLARE @StatusCode varchar(20);
DECLARE @CurrentValue decimal(18,6);

SELECT
    @ConfigId = config_version_id,
    @StatusCode = status_code
FROM config.ConfigVersions
WHERE version_code = 'CFG-1008.0';

IF @ConfigId IS NULL
    THROW 51030, 'CFG-1008.0 does not exist.', 1;

IF @StatusCode <> 'DRAFT'
    THROW 51031, 'CFG-1008.0 is not DRAFT and cannot be modified.', 1;

SELECT @CurrentValue = decimal_value
FROM config.GlobalSettings
WHERE config_version_id = @ConfigId
  AND setting_code = 'ENGINE21_CATEGORY_BASE_CARB_MULT_REST';

IF @CurrentValue IS NULL
    THROW 51032, 'Rest carbohydrate multiplier setting does not exist.', 1;

IF @CurrentValue = 0.900000
BEGIN
    UPDATE config.GlobalSettings
    SET
        decimal_value = 0.800000,
        description = 'Engine 2.1 approved base-rest carbohydrate multiplier for Rest, Recovery and Other days.'
    WHERE config_version_id = @ConfigId
      AND setting_code = 'ENGINE21_CATEGORY_BASE_CARB_MULT_REST';
END
ELSE IF @CurrentValue <> 0.800000
BEGIN
    THROW 51033, 'Unexpected Rest carbohydrate multiplier value.', 1;
END;

COMMIT TRANSACTION;
